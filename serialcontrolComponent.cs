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

        public SerialControlComponent()
          : base("Serial Control", "SerialControl",
              "Send GCode commands over serial port", "crft", "Control")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Port", "P", "Serial port name (e.g., COM3 or /dev/cu.usbserial)", GH_ParamAccess.item, "");
            pManager.AddIntegerParameter("Baud Rate", "B", "Baud rate (e.g., 115200)", GH_ParamAccess.item, 115200);
            pManager.AddBooleanParameter("Connect", "C", "Connect to printer", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Command", "Cmd", "GCode command to send", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Send", "S", "Send command", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Clear", "Clr", "Clear response log", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Response", "Res", "Response log messages", GH_ParamAccess.list);
            pManager.AddTextParameter("PortEvent", "Evt", "Last event on port", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string portName = "";
            int baudRate = 115200;
            bool connect = false;
            string command = "";
            bool send = false;
            bool clear = false;

            DA.GetData("Port", ref portName);
            DA.GetData("Baud Rate", ref baudRate);
            DA.GetData("Connect", ref connect);
            DA.GetData("Command", ref command);
            DA.GetData("Send", ref send);
            DA.GetData("Clear", ref clear);

            if (clear && !_lastClear)
            {
                _responseLog.Clear();
                _lastEvent = "Cleared responses";
            }
            _lastClear = clear;

            if (connect && !_lastConnect)
            {
                try
                {
                    // Initialize cross-platform serial port
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        _serialPort = new WindowsSerialPort(portName, baudRate);
                    else
                    {
                        string device = portName.StartsWith("/") ? portName : Path.Combine("/dev", portName);
                        _serialPort = new UnixSerialPort(device, baudRate);
                    }
                    _responseLog.Clear();
                    _serialPort.DataReceived += data =>
                    {
                        _responseLog.Add(data);
                        _lastEvent = $"Received: {data}";
                    };
                    _serialPort.Open();
                    _serialPort.ClearBuffers();
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
            }
            _lastConnect = connect;

            if (send && !_lastSend)
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    try
                    {
                        _responseLog.Clear();
                        _serialPort.ClearBuffers();
                        _serialPort.WriteLine(command);
                        _lastEvent = $"Sent: {command}";
                        // Allow time for response
                        Thread.Sleep(200);
                    }
                    catch (Exception ex)
                    {
                        _lastEvent = $"Error: {ex.Message}";
                    }
                }
                else
                {
                    _lastEvent = "Port not connected";
                }
            }
            _lastSend = send;

            DA.SetDataList("Response", _responseLog);
            DA.SetData("PortEvent", _lastEvent);
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
