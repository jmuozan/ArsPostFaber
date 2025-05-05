using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;

namespace crft
{
    public class SerialControlComponent : GH_Component
    {
        private SerialPort serialPort;
        private Thread communicationThread;
        private bool isRunning = false;
        private bool isPaused = false;
        private string lastPortName = string.Empty;
        private bool lastConnect = false;
        private string lastEvent = string.Empty;
        private bool prevHome = false;
        private Queue<string> commandQueue = new Queue<string>();
        private List<string> responseLog = new List<string>();
        private int maxLogEntries = 100;

        public SerialControlComponent()
          : base("Serial Control", "SerialControl",
              "Control 3D printer through serial connection",
              "crft", "Control")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Port", "P", "COM port (e.g., COM3 on Windows or /dev/ttyUSB0 on Mac/Linux)", GH_ParamAccess.item, "COM1");
            pManager.AddIntegerParameter("Baud Rate", "B", "Baud rate (e.g., 115200)", GH_ParamAccess.item, 115200);
            pManager.AddBooleanParameter("Connect", "C", "Connect to printer", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Start", "S", "Start printing", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Pause", "Pause", "Pause printing", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Resume", "R", "Resume printing", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Stop", "Stop", "Stop printing", GH_ParamAccess.item, false);
            pManager.AddTextParameter("G-Code", "G", "G-code commands to send", GH_ParamAccess.list);
            // Allow missing G-Code input without error
            pManager[7].Optional = true;
            pManager.AddTextParameter("Command", "Cmd", "Single command to send", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Send Command", "Send", "Send the single command", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Home", "H", "Home all axes (G28)", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("Connected", "Con", "True if connected to printer", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Printing", "Print", "True if currently printing", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Paused", "Psd", "True if printing is paused", GH_ParamAccess.item);
            pManager.AddTextParameter("Response", "Res", "Response from printer", GH_ParamAccess.list);
            pManager.AddTextParameter("Status", "Sts", "Current status information", GH_ParamAccess.item);
            pManager.AddTextParameter("PortEvent", "Evt", "Last serial port event (sent/received)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Input parameters
            string portName = "";
            int baudRate = 115200;
            bool connect = false;
            bool start = false;
            bool pause = false;
            bool resume = false;
            bool stop = false;
            List<string> gcode = new List<string>();
            string command = "";
            bool sendCommand = false;
            bool home = false;

            // Get input data
            DA.GetData("Port", ref portName);
            DA.GetData("Baud Rate", ref baudRate);
            DA.GetData("Connect", ref connect);
            DA.GetData("Start", ref start);
            DA.GetData("Pause", ref pause);
            DA.GetData("Resume", ref resume);
            DA.GetData("Stop", ref stop);
            DA.GetDataList("G-Code", gcode);
            DA.GetData("Command", ref command);
            DA.GetData("Send Command", ref sendCommand);
            DA.GetData("Home", ref home);

            // Check connection status
            bool isConnected = (serialPort != null && serialPort.IsOpen);

            // Create status message
            string status = isConnected ? $"Connected to {portName} at {baudRate} baud" : "Not connected";
            if (isRunning) status += isPaused ? " (Paused)" : " (Printing)";

            // Handle connection/disconnection
            if (connect && !isConnected)
            {
                try
                {
                    // Close any existing connection
                    if (serialPort != null && serialPort.IsOpen)
                    {
                        serialPort.Close();
                    }

                    // Create new serial connection
                    serialPort = new SerialPort(portName, baudRate)
                    {
                        ReadTimeout = 500,
                        WriteTimeout = 500,
                        DtrEnable = true,
                        RtsEnable = true
                    };

                    serialPort.DataReceived += SerialPort_DataReceived;
                    serialPort.Open();

                    // Wait for printer to initialize
                    Thread.Sleep(2000);

                    // Start communication thread
                    isRunning = false;
                    isPaused = false;
                    communicationThread = new Thread(CommunicationLoop);
                    communicationThread.Start();

                    AddLogEntry("Connected to printer");
                    status = $"Connected to {portName} at {baudRate} baud";
                }
                catch (Exception ex)
                {
                    AddLogEntry($"Connection error: {ex.Message}");
                    status = $"Connection error: {ex.Message}";
                }
            }
            else if (!connect && isConnected)
            {
                try
                {
                    // Stop printing and communication thread
                    isRunning = false;
                    isPaused = false;

                    // Wait for thread to terminate
                    if (communicationThread != null && communicationThread.IsAlive)
                    {
                        communicationThread.Join(2000);
                        if (communicationThread.IsAlive)
                        {
                            communicationThread.Abort();
                        }
                    }

                    // Close serial connection
                    serialPort.Close();
                    AddLogEntry("Disconnected from printer");
                    status = "Not connected";
                }
                catch (Exception ex)
                {
                    AddLogEntry($"Disconnection error: {ex.Message}");
                    status = $"Disconnection error: {ex.Message}";
                }
            }

            // Handle printing controls
            if (isConnected)
            {
                // Start printing
                if (start && !isRunning)
                {
                    if (gcode.Count > 0)
                    {
                        // Clear queue and add all G-code commands
                        lock (commandQueue)
                        {
                            commandQueue.Clear();
                            foreach (string line in gcode)
                            {
                                string trimmedLine = line.Trim();
                                if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith(";"))
                                {
                                    commandQueue.Enqueue(trimmedLine);
                                }
                            }
                        }

                        isRunning = true;
                        isPaused = false;
                        AddLogEntry("Print started");
                        status = "Printing";
                    }
                    else
                    {
                        AddLogEntry("Cannot start: No G-code provided");
                        status = "Error: No G-code provided";
                    }
                }

                // Pause printing
                if (pause && isRunning && !isPaused)
                {
                    isPaused = true;
                    SendImmediate("M226"); // Pause command (varies by firmware)
                    AddLogEntry("Print paused");
                    status = "Paused";
                }

                // Resume printing
                if (resume && isRunning && isPaused)
                {
                    isPaused = false;
                    SendImmediate("M118"); // Resume command (varies by firmware)
                    AddLogEntry("Print resumed");
                    status = "Printing";
                }

                // Stop printing
                if (stop && isRunning)
                {
                    isRunning = false;
                    isPaused = false;
                    SendImmediate("M112"); // Emergency stop
                    Thread.Sleep(100);
                    SendImmediate("M410"); // Quick stop
                    lock (commandQueue)
                    {
                        commandQueue.Clear();
                    }
                    AddLogEntry("Print stopped");
                    status = "Connected but idle";
                }

                // Send single command
                if (sendCommand && !string.IsNullOrEmpty(command))
                {
                    SendImmediate(command);
                    AddLogEntry($"Sent command: {command}");
                }
            }

            // Set outputs
            // Handle direct home command on rising edge
            if (home && !prevHome && isConnected)
            {
                SendImmediate("G28");
                AddLogEntry("Sent home command (G28)");
            }
            prevHome = home;
            
            DA.SetData("Connected", isConnected);
            DA.SetData("Printing", isRunning && !isPaused);
            DA.SetData("Paused", isRunning && isPaused);
            DA.SetDataList("Response", responseLog);
            DA.SetData("Status", status);
            // Output last serial port event (sent/received)
            DA.SetData("PortEvent", lastEvent);
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (serialPort != null && serialPort.IsOpen)
                {
                    string data = serialPort.ReadLine();
                    AddLogEntry($"< {data}");
                }
            }
            catch (Exception ex)
            {
                AddLogEntry($"Read error: {ex.Message}");
            }
        }

        private void CommunicationLoop()
        {
            while (serialPort != null && serialPort.IsOpen)
            {
                try
                {
                    // Check if we're running and not paused
                    if (isRunning && !isPaused)
                    {
                        string command = null;
                        
                        // Get next command from queue
                        lock (commandQueue)
                        {
                            if (commandQueue.Count > 0)
                            {
                                command = commandQueue.Dequeue();
                            }
                        }

                        // Send command if we have one
                        if (!string.IsNullOrEmpty(command))
                        {
                            SendCommand(command);
                            
                            // Wait for acknowledgment or timeout (adjust based on your printer's response time)
                            Thread.Sleep(100);
                        }
                        else
                        {
                            // No more commands, printing is done
                            isRunning = false;
                            AddLogEntry("Print completed");
                        }
                    }
                    else
                    {
                        // Just a small delay to prevent CPU hogging
                        Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    AddLogEntry($"Communication error: {ex.Message}");
                    Thread.Sleep(1000); // Pause a bit longer after error
                }
            }
        }

        private void SendCommand(string command)
        {
            try
            {
                if (serialPort != null && serialPort.IsOpen)
                {
                    // Log the command
                    AddLogEntry($"> {command}");
                    
                    // Send the command with newline
                    serialPort.WriteLine(command);
                }
            }
            catch (Exception ex)
            {
                AddLogEntry($"Send error: {ex.Message}");
            }
        }

        private void SendImmediate(string command)
        {
            try
            {
                if (serialPort != null && serialPort.IsOpen)
                {
                    // Log the command
                    AddLogEntry($">> {command} (immediate)");
                    
                    // Send the command with newline
                    serialPort.WriteLine(command);
                    
                    // Wait a moment for the command to be processed
                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                AddLogEntry($"Send error: {ex.Message}");
            }
        }

        private void AddLogEntry(string entry)
        {
            // Track last event for output
            lastEvent = entry;
            lock (responseLog)
            {
                responseLog.Add($"[{DateTime.Now:HH:mm:ss}] {entry}");
                // Limit log size
                while (responseLog.Count > maxLogEntries)
                    responseLog.RemoveAt(0);
            }
        }

        // AppendAdditionalMenuItems has been removed. Port selection handled via dropdown parameter and refresh button.
        
        // Static helper to find PortParam connected to this component
        private static bool IsConnected(GH_Component component, out PortParam portParam)
        {
            var sources = component.Params.Input[0].Sources;
            portParam = sources.OfType<PortParam>().FirstOrDefault();
            return portParam != null;
        }

        // Create the port dropdown if none exists
        private static bool CreateIfEmpty(GH_Document document, GH_Component component, string selected = null)
        {
            var inputParam = component.Params.Input[0]; // "Port" parameter
            if (inputParam.SourceCount > 0)
                return false;
            var portParam = new PortParam();
            if (selected != null)
            {
                var selectedItem = new GH_ValueListItem(selected, $"\"{selected}\"");
                selectedItem.Selected = true;
                portParam.ListItems.Add(selectedItem);
            }
            portParam.Update();
            // Position parameter left of component
            var pivot = component.Attributes.Pivot;
            portParam.Attributes.Pivot = new System.Drawing.PointF(pivot.X - 240, pivot.Y - 21);
            document.AddObject(portParam, false);
            inputParam.AddSource(portParam);
            return true;
        }

        // Called when component is added to canvas
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            CreateIfEmpty(document, this);
        }

        // Replace attributes to include a refresh button
        public override void CreateAttributes()
        {
            m_attributes = new ComponentButton(this, "Refresh Ports", RefreshPorts);
        }

        // Refresh the dropdown menu
        private void RefreshPorts()
        {
            if (IsConnected(this, out var portParam))
            {
                portParam.Update();
                portParam.ExpireSolution(true);
            }
        }

        protected override void BeforeSolveInstance()
        {
            // This method is called before SolveInstance
            base.BeforeSolveInstance();
        }

        // Clean up and remove port dropdown when component is removed
        public override void RemovedFromDocument(GH_Document document)
        {
            // Original cleanup
            try
            {
                isRunning = false;
                isPaused = false;
                if (communicationThread != null && communicationThread.IsAlive)
                {
                    communicationThread.Join(2000);
                    if (communicationThread.IsAlive)
                        communicationThread.Abort();
                }
                if (serialPort != null && serialPort.IsOpen)
                    serialPort.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
            // Remove dropdown parameter
            if (IsConnected(this, out var portParam))
                document.RemoveObject(portParam, false);
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                //return Resources.IconForThisComponent;
                return null;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("f6b42a3d-5f1e-4e87-a840-2a0d7a8c6e5f"); }
        }
    }
}