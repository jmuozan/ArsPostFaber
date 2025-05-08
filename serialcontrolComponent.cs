using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using crft;

namespace crft
{
    public class SerialControlComponent : GH_Component
    {
        private ISerialPort _serialPort;
        private bool _lastConnect;
        private bool _lastSend;
        private bool _lastClear;
        private readonly List<string> _responseLog = new List<string>();
        private string _lastEvent;
        private PortParam _portParam;
        // Playback state: true if streaming is active (playing), false if paused
        private bool _isPlaying;
        // Streaming print internals
        private List<string> _printCommands = new List<string>();
        private int _currentLineIndex = 0;
        private Thread _printThread;
        private AutoResetEvent _ackEvent;
        // Current status/response for UI
        private string _status;

        public SerialControlComponent()
          : base("Serial Control", "SerialControl",
              "Send GCode commands over serial port", "crft", "Control")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // Port settings
            pManager.AddTextParameter("Port", "P", "Serial port name (e.g., COM3 or /dev/cu.usbserial)", GH_ParamAccess.item, "");
            pManager.AddIntegerParameter("Baud Rate", "B", "Baud rate (e.g., 115200)", GH_ParamAccess.item, 115200);
            // Connect/disconnect toggle
            pManager.AddBooleanParameter("Connect", "C", "Connect to printer", GH_ParamAccess.item, false);
            // G-code commands to stream (one per list item)
            pManager.AddTextParameter("Command", "Cmd", "G-code commands to send sequentially", GH_ParamAccess.list);
            // Allow missing command list; connect button first then stream later
            pManager[3].Optional = true;
            // Start streaming when toggled
            pManager.AddBooleanParameter("Send", "S", "Start/stop streaming commands", GH_ParamAccess.item, false);
            // Clear status
            pManager.AddBooleanParameter("Clear", "Clr", "Clear status message", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Current status or executing command
            pManager.AddTextParameter("Response", "Res", "Current status or command executing", GH_ParamAccess.item);
            // Raw port event messages (e.g., received data)
            pManager.AddTextParameter("PortEvent", "Evt", "Last event on port", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Input variables
            string portName = string.Empty;
            int baudRate = 115200;
            bool connect = false;
            var commandsList = new List<string>();
            bool send = false;
            bool clear = false;

            DA.GetData("Port", ref portName);
            DA.GetData("Baud Rate", ref baudRate);
            DA.GetData("Connect", ref connect);
            // Get list of commands (one string per G-code line)
            DA.GetDataList("Command", commandsList);
            DA.GetData("Send", ref send);
            DA.GetData("Clear", ref clear);

            // Clear status
            if (clear && !_lastClear)
            {
                _status = string.Empty;
                _lastEvent = "Status cleared";
            }
            _lastClear = clear;

            if (connect && !_lastConnect)
            {
                try
                {
                    // Initialize cross-platform serial port
                    // Windows: use SerialPortStream (RJCP)
                    // Unix-like (including macOS): use UnixSerialPort (stty + FileStream)
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _serialPort = new WindowsSerialPort(portName, baudRate);
                    }
                    else
                    {
                        // On Unix/macOS use file-stream implementation
                        string device = portName.StartsWith("/") ? portName : Path.Combine("/dev", portName);
                        _serialPort = new UnixSerialPort(device, baudRate);
                    }
                    _responseLog.Clear();
                    // Handle incoming data and signal acknowledgments
                    _serialPort.DataReceived += data =>
                    {
                        // Always log incoming data
                        _responseLog.Add(data);
                        var trimmed = data.TrimStart();
                        // Update last event only for non-busy messages
                        if (!trimmed.StartsWith("echo:busy", StringComparison.OrdinalIgnoreCase))
                        {
                            _lastEvent = $"Received: {data}";
                        }
                        // Signal OK acknowledgment if waiting
                        if (_ackEvent != null && trimmed.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
                        {
                            _ackEvent.Set();
                        }
                    };
                    _serialPort.Open();
                    // Clear any stray data and send initial handshake to reset line numbers
                    _serialPort.ClearBuffers();
                    _serialPort.WriteLine("M110 N0");  // Reset line numbering
                    Thread.Sleep(100);                  // Wait for printer to process
                    _lastEvent = $"Connected to {portName}";
                }
                catch (Exception ex)
                {
                    _lastEvent = $"Connection error: {ex.Message}";
                }
            }
            else if (!connect && _lastConnect)
            {
                if (_serialPort != null)
                {
                    try { _serialPort.Close(); } catch { }
                    _serialPort = null;
                }
                _lastEvent = "Disconnected";
                _status = _lastEvent;
                // Stop any ongoing print stream
                if (_printThread != null)
                {
                    _isPlaying = false;
                    try { _printThread.Join(500); } catch { }
                    _printThread = null;
                    _currentLineIndex = 0;
                    _printCommands.Clear();
                }
            }
            _lastConnect = connect;

            // Start streaming on send toggle
            if (send && !_lastSend)
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    if (commandsList.Count > 0)
                    {
                        _printCommands = new List<string>(commandsList);
                        _currentLineIndex = 0;
                        _ackEvent = new AutoResetEvent(false);
                        _status = $"Started printing {_printCommands.Count} lines";
                        _printThread = new Thread(PrintLoop) { IsBackground = true };
                        _printThread.Start();
                    }
                    else
                    {
                        _status = "No commands to print";
                    }
                }
                else
                {
                    _status = "Port not connected";
                }
            }
            _lastSend = send;

            // Output current status and port event
            DA.SetData("Response", _status);
            DA.SetData("PortEvent", _lastEvent);
        }
        /// <summary>
        /// Add custom UI button under the component for playback control
        /// </summary>
        public override void CreateAttributes()
        {
            // Play/pause toggle button (▶ play, ⏸ pause)
            m_attributes = new ComponentButton(this, () => _isPlaying ? "⏸" : "▶", ToggleForm);
        }
        /// <summary>
        /// Handle button click: toggle playback form (stub)
        /// </summary>
        private void ToggleForm()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                if (!_isPlaying)
                {
                    // Resume printing from current line
                    _status = $"Resumed printing at line {_currentLineIndex + 1}";
                    _lastEvent = _status;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, _status);
                    _isPlaying = true;
                }
                else
                {
                    // Pause printing after current line
                    _status = $"Paused after line {_currentLineIndex}";
                    _lastEvent = _status;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, _status);
                    _isPlaying = false;
                }
                ExpireSolution(true);
            }
        }

        /// <summary>
        /// Background print loop: sends G-code sequentially with pause/resume support.
        /// </summary>
        private void PrintLoop()
        {
            while (_currentLineIndex < _printCommands.Count)
            {
                // Wait until playing
                while (!_isPlaying)
                {
                    Thread.Sleep(100);
                }
                // Stop if port closed
                if (_serialPort == null || !_serialPort.IsOpen)
                    break;
                var cmd = _printCommands[_currentLineIndex];
                try
                {
                    // Update status before sending
                    _status = $"Executing: {cmd}";
                    // Send user command and wait for buffer acknowledgment
                    _serialPort.WriteLine(cmd);
                    _ackEvent.WaitOne();
                    // Send M400 to wait for motion complete
                    _serialPort.WriteLine("M400");
                    _ackEvent.WaitOne();
                }
                catch (Exception ex)
                {
                    _lastEvent = $"Error: {ex.Message}";
                    break;
                }
                _currentLineIndex++;
            }
            // If all commands processed, mark complete
            if (_currentLineIndex >= _printCommands.Count)
            {
                _isPlaying = false;
                _lastEvent = "Print complete";
            }
            _printThread = null;
        }
        
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            // Add port dropdown list and connect to first input
            _portParam = new PortParam();
            document.AddObject(_portParam, false);
            // Position to the left of this component
            var myAttr = this.Attributes;
            if (myAttr != null)
            {
                var b = myAttr.Bounds;
                _portParam.Attributes.Pivot = new System.Drawing.PointF(
                    b.Left - _portParam.Attributes.Bounds.Width - 20,
                    b.Top);
            }
            // Connect port dropdown output to Port input (index 0)
            // Automatically connect PortParam output to this component's Port input
            // Automatically connect PortParam output to this component's Port input
            // Automatically connect PortParam to this component's Port input
            this.Params.Input[0].AddSource(_portParam);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            // Remove port dropdown when component is removed
            if (_portParam != null)
            {
                document.RemoveObject(_portParam, false);
                _portParam = null;
            }
            base.RemovedFromDocument(document);
        }
        
        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("f6b42a3d-5f1e-4e87-a840-2a0d7a8c6e5f");
    }
}