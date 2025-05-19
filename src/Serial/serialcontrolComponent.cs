using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using crft;
using Rhino.Geometry;
using System.Globalization;
using System.Drawing;
using Rhino.UI;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;
using Rhino;

namespace crft
{
    public class SerialControlComponent : GH_Component
    {
        private ISerialPort _serialPort;
        private bool _lastConnect;
        private bool _lastReset;
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
        // Toolpath segment lists for preview
        private List<Curve> _execTransSegs = new List<Curve>();
        private List<Curve> _execExtrusionSegs = new List<Curve>();
        private List<Curve> _unexecTransSegs = new List<Curve>();
        private List<Curve> _unexecExtrusionSegs = new List<Curve>();
        // External editor state for G-code
        private bool _isEditing;
        private string _editTempDir;
        private string _editInPath;
        private string _editOutPath;
        private CancellationTokenSource _editCancelSource;
        private Task _editMonitorTask;
        // Points and indices for the unexecuted path being edited
        private List<Tuple<int, Point3d>> _editPathPts;
        private string _bboxMinArg;
        private string _bboxMaxArg;
        // Last loaded G-code commands (for preview)
        private List<string> _lastCommandsList = new List<string>();
        // Printer bounding box dimensions (X, Y, Z)
        private Vector3d _printerBBoxDims = new Vector3d(220, 220, 250);
        // Show bounding box toggle
        private bool _showBBox = false;
        // Home position (where G28 ends)
        private Point3d _homePosition = new Point3d(0, 0, 0);
        // Number of sample points along entire path for preview visualization
        private int _samplesPerSegment = 0;

        /// <summary>
        /// Sample total number of points evenly along an entire sequence of straight-line segments.
        /// </summary>
        private static List<Point3d> EvenlySampleCurves(IEnumerable<Curve> segments, int totalSamples)
        {
            var curves = segments.ToList();
            if (totalSamples <= 0 || curves.Count == 0)
                return new List<Point3d>();
            var lengths = curves.Select(c => c.GetLength()).ToList();
            double totalLength = lengths.Sum();
            var result = new List<Point3d>();
            for (int i = 1; i <= totalSamples; i++)
            {
                double target = totalLength * i / (totalSamples + 1);
                double acc = 0;
                for (int j = 0; j < curves.Count; j++)
                {
                    var len = lengths[j];
                    if (acc + len >= target)
                    {
                        double rem = target - acc;
                        double tNorm = len > 0 ? rem / len : 0;
                        var pt = curves[j].PointAtNormalizedLength(tNorm);
                        result.Add(pt);
                        break;
                    }
                    acc += len;
                }
            }
            return result;
        }

        /// <summary>
        /// Sample total number of points evenly along an entire sequence of Line segments.
        /// </summary>
        private static List<Point3d> EvenlySampleLines(IEnumerable<Line> lines, int totalSamples)
        {
            var curves = lines.Select(l => new LineCurve(l)).Cast<Curve>();
            return EvenlySampleCurves(curves, totalSamples);
        }

        public SerialControlComponent()
          : base("Serial Control", "SerialControl",
              "Send GCode commands over serial port", "crft", "Serial")
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
            // Reset playback to beginning when toggled
            pManager.AddBooleanParameter("Reset", "R", "Reset playback to beginning", GH_ParamAccess.item, false);
            // Printer bounding box dimensions (X, Y, Z)
            pManager.AddVectorParameter("Bounding Box", "BB", "Printer bounding box dimensions X*Y*Z", GH_ParamAccess.item, new Vector3d(220, 220, 250));
            // Toggle to show printer bounding box in viewport
            pManager.AddBooleanParameter("Show BBox", "SB", "Show printer bounding box", GH_ParamAccess.item, false);
            // Number of sample points along entire path for preview visualization
            pManager.AddIntegerParameter("Samples", "S", "Number of sample points along entire path for preview visualization", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Current status or executing command
            pManager.AddTextParameter("Response", "Res", "Current status or command executing", GH_ParamAccess.item);
            // Raw port event messages (e.g., received data)
            pManager.AddTextParameter("PortEvent", "Evt", "Last event on port", GH_ParamAccess.item);
            // Toolpath curve parsed from G-code commands
            pManager.AddCurveParameter("Path", "Path", "Toolpath curve parsed from G-code commands", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Input variables
            string portName = string.Empty;
            int baudRate = 115200;
            bool connect = false;
            var commandsList = new List<string>();
            bool reset = false;

            DA.GetData("Port", ref portName);
            DA.GetData("Baud Rate", ref baudRate);
            DA.GetData("Connect", ref connect);
            // Get list of commands (one string per G-code line)
            DA.GetDataList("Command", commandsList);
            // Cache commands for preview
            _lastCommandsList = new List<string>(commandsList);
            DA.GetData("Reset", ref reset);
            // Get bounding box inputs
            var bboxDims = _printerBBoxDims;
            var showBBox = _showBBox;
            DA.GetData("Bounding Box", ref bboxDims);
            DA.GetData("Show BBox", ref showBBox);
            _printerBBoxDims = bboxDims;
            _showBBox = showBBox;
            // Get sample points count for segment sampling
            int samplesPerSegment = 0;
            DA.GetData("Samples", ref samplesPerSegment);
            _samplesPerSegment = samplesPerSegment;
            // Determine home position from G28 commands (first occurrence)
            var homeList = (_printCommands != null && _printCommands.Count > 0) ? _printCommands : commandsList;
            var homePos = new Point3d(0, 0, 0);
            foreach (var cmdLine in homeList)
            {
                var s = cmdLine.Trim();
                if (s.StartsWith("G28", StringComparison.OrdinalIgnoreCase))
                {
                    double x = homePos.X, y = homePos.Y, z = homePos.Z;
                    var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        if (p.StartsWith("X", StringComparison.OrdinalIgnoreCase) && double.TryParse(p.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double xv)) x = xv;
                        else if (p.StartsWith("Y", StringComparison.OrdinalIgnoreCase) && double.TryParse(p.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double yv)) y = yv;
                        else if (p.StartsWith("Z", StringComparison.OrdinalIgnoreCase) && double.TryParse(p.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double zv)) z = zv;
                    }
                    homePos = new Point3d(x, y, z);
                    break;
                }
            }
            _homePosition = homePos;


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
                    // Load commands for playback, await play button
                    if (commandsList.Count > 0)
                    {
                        _printCommands = new List<string>(commandsList);
                        _currentLineIndex = 0;
                        _isPlaying = false;
                        _ackEvent = new AutoResetEvent(false);
                        _status = $"Loaded {_printCommands.Count} commands";
                    }
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
            // Handle Reset toggle: reload commands and reset playback state
            if (reset && !_lastReset)
            {
                _printCommands = new List<string>(commandsList);
                _currentLineIndex = 0;
                _isPlaying = false;
                _status = "Reset to beginning";
                _lastEvent = _status;
            }
            _lastReset = reset;


            // Output current status, port event, and toolpath
            DA.SetData("Response", _status);
            DA.SetData("PortEvent", _lastEvent);
            // Parse G-code commands into colored toolpath segments
            _execTransSegs.Clear();
            _execExtrusionSegs.Clear();
            _unexecTransSegs.Clear();
            _unexecExtrusionSegs.Clear();
            var points = new List<Point3d>();
            var currentPoint = new Point3d(0, 0, 0);
            double currentE = 0;
            points.Add(currentPoint);
            for (int i = 0; i < commandsList.Count; i++)
            {
                var s = commandsList[i].Trim();
                if (s.StartsWith("G0", StringComparison.OrdinalIgnoreCase) || s.StartsWith("G1", StringComparison.OrdinalIgnoreCase))
                {
                    double x = currentPoint.X;
                    double y = currentPoint.Y;
                    double z = currentPoint.Z;
                    double newE = currentE;
                    var tokens = s.Split(new[] {' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var token in tokens)
                    {
                        if (token.Length < 2) continue;
                        char prefix = char.ToUpper(token[0]);
                        if (!double.TryParse(token.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                            continue;
                        switch (prefix)
                        {
                            case 'X': x = value; break;
                            case 'Y': y = value; break;
                            case 'Z': z = value; break;
                            case 'E': newE = value; break;
                        }
                    }
                    var nextPoint = new Point3d(x, y, z);
                    var segment = new LineCurve(currentPoint, nextPoint);
                    // Determine if this move is an extrusion (G1 with increasing E)
                    bool isExtrude = s.StartsWith("G1", StringComparison.OrdinalIgnoreCase) && newE > currentE;
                    if (i < _currentLineIndex)
                    {
                        if (isExtrude) _execExtrusionSegs.Add(segment);
                        else _execTransSegs.Add(segment);
                    }
                    else
                    {
                        if (isExtrude) _unexecExtrusionSegs.Add(segment);
                        else _unexecTransSegs.Add(segment);
                    }
                    currentPoint = nextPoint;
                    currentE = newE;
                    points.Add(currentPoint);
                }
            }
            // Output full path as polyline curve
            Curve toolpath = null;
            if (points.Count > 1)
            {
                var polyline = new Polyline(points);
                toolpath = new PolylineCurve(polyline);
            }
            DA.SetData("Path", toolpath);
        }
        /// <summary>
        /// Add custom UI button under the component for playback control
        /// </summary>
        public override void CreateAttributes()
        {
            // Toolbar with Play/Pause and Edit buttons
            // Toolbar with Play/Pause and Edit buttons (Edit disabled)
            m_attributes = new ComponentToolbar(
                this,
                // Play/Pause label and action
                () => _isPlaying ? "⏸" : "▶",
                ToggleForm,
                // Edit label and action (show preview window)
                () => "✎",
                ShowEditDialog
            );
        }
        /// <summary>
        /// Handle button click: toggle playback form (stub)
        /// </summary>
        private void ToggleForm()
        {
            // Toggle play/pause if there are commands to stream
            if (_printCommands != null && _printCommands.Count > 0)
            {
                if (!_isPlaying)
                {
                    // Start or resume printing from current line
                    if (_printThread == null)
                    {
                        if (_ackEvent == null) _ackEvent = new AutoResetEvent(false);
                        _printThread = new Thread(PrintLoop) { IsBackground = true };
                        _printThread.Start();
                    }
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
        /// Launch the mesh-based path editor for unexecuted G-code moves.
        /// </summary>
        private void ShowEditDialog()
        {
            // Preview the G-code path and optional bounding box in a pop-up window
            var commands = _lastCommandsList;
            if (commands == null || commands.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No G-code loaded for preview.");
                return;
            }
            // Segment path into executed and unexecuted moves
            var execTrans = new List<Line>();
            var execExtr = new List<Line>();
            var unexecTrans = new List<Line>();
            var unexecExtr = new List<Line>();
            var currentPoint = new Point3d(0, 0, 0);
            double currentE = 0;
            for (int i = 0; i < commands.Count; i++)
            {
                var s = commands[i].Trim();
                if (s.StartsWith("G0", StringComparison.OrdinalIgnoreCase) || s.StartsWith("G1", StringComparison.OrdinalIgnoreCase))
                {
                    double x = currentPoint.X, y = currentPoint.Y, z = currentPoint.Z;
                    double newE = currentE;
                    var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        if (p.Length < 2) continue;
                        char prefix = char.ToUpper(p[0]);
                        if (!double.TryParse(p.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double val)) continue;
                        switch (prefix)
                        {
                            case 'X': x = val; break;
                            case 'Y': y = val; break;
                            case 'Z': z = val; break;
                            case 'E': newE = val; break;
                        }
                    }
                    var nextPoint = new Point3d(x, y, z);
                    var line = new Line(currentPoint, nextPoint);
                    bool isExtrude = s.StartsWith("G1", StringComparison.OrdinalIgnoreCase) && newE > currentE;
                    if (i < _currentLineIndex)
                    {
                        if (isExtrude) execExtr.Add(line); else execTrans.Add(line);
                    }
                    else
                    {
                        if (isExtrude) unexecExtr.Add(line); else unexecTrans.Add(line);
                    }
                    currentPoint = nextPoint;
                    currentE = newE;
                }
            }
            // Collect segments with colors
            var segs = new List<Tuple<Point3d, Point3d, Color>>();
            foreach (var l in execTrans) segs.Add(Tuple.Create(l.From, l.To, Color.Orange));
            foreach (var l in execExtr) segs.Add(Tuple.Create(l.From, l.To, Color.Orange));
            foreach (var l in unexecTrans) segs.Add(Tuple.Create(l.From, l.To, Color.Blue));
            foreach (var l in unexecExtr) segs.Add(Tuple.Create(l.From, l.To, Color.Red));
            // Include bounding box if enabled
            if (_showBBox)
            {
                var p0 = _homePosition;
                var d = _printerBBoxDims;
                var p1 = new Point3d(p0.X + d.X, p0.Y, p0.Z);
                var p2 = new Point3d(p0.X + d.X, p0.Y + d.Y, p0.Z);
                var p3 = new Point3d(p0.X, p0.Y + d.Y, p0.Z);
                var p4 = new Point3d(p0.X, p0.Y, p0.Z + d.Z);
                var p5 = new Point3d(p0.X + d.X, p0.Y, p0.Z + d.Z);
                var p6 = new Point3d(p0.X + d.X, p0.Y + d.Y, p0.Z + d.Z);
                var p7 = new Point3d(p0.X, p0.Y + d.Y, p0.Z + d.Z);
                var boxLines = new[] {
                    new Line(p0, p1), new Line(p1, p2), new Line(p2, p3), new Line(p3, p0),
                    new Line(p4, p5), new Line(p5, p6), new Line(p6, p7), new Line(p7, p4),
                    new Line(p0, p4), new Line(p1, p5), new Line(p2, p6), new Line(p3, p7)
                };
                foreach (var l in boxLines) segs.Add(Tuple.Create(l.From, l.To, Color.LightBlue));
            }
            // Compute evenly spaced sample points along entire path for edit preview
            List<Point3d> samplePts = null;
            if (_samplesPerSegment > 0)
            {
                var allLines = execTrans.Concat(execExtr).Concat(unexecTrans).Concat(unexecExtr);
                samplePts = EvenlySampleLines(allLines, _samplesPerSegment);
            }
            // Show preview window
            try
            {
#if WINDOWS
                var formWin = new PreviewForm(segs, _homePosition, _printerBBoxDims);
                formWin.Show();
#else
                var form = new PreviewEtoForm(segs, samplePts);
                form.Show();
#endif
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Preview unavailable: {ex.Message}");
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
                    Thread.Sleep(100);
                // Stop if port closed
                if (_serialPort == null || !_serialPort.IsOpen)
                    break;
                var cmd = _printCommands[_currentLineIndex];
                try
                {
                    // Send command and wait for ack and motion completion
                    _status = $"Executing: {cmd}";
                    _serialPort.WriteLine(cmd);
                    _ackEvent.WaitOne();
                    _serialPort.WriteLine("M400");
                    _ackEvent.WaitOne();
                }
                catch (Exception ex)
                {
                    _lastEvent = $"Error: {ex.Message}";
                    break;
                }
                _currentLineIndex++;
                ExpireSolution(true);
            }
            if (_currentLineIndex >= _printCommands.Count)
            {
                _isPlaying = false;
                _lastEvent = "Print complete";
            }
            _printThread = null;
        }

        /// <summary>
        /// Build list of all path points along with their command indices.
        /// Includes executed and unexecuted segments.
        /// </summary>
        private List<Tuple<int, Point3d>> BuildFullPath()
        {
            var result = new List<Tuple<int, Point3d>>();
            Point3d currentPoint = new Point3d(0, 0, 0);
            double currentE = 0;
            for (int i = 0; i < _printCommands.Count; i++)
            {
                var s = _printCommands[i].Trim();
                if (s.StartsWith("G0", StringComparison.OrdinalIgnoreCase) || s.StartsWith("G1", StringComparison.OrdinalIgnoreCase))
                {
                    double x = currentPoint.X, y = currentPoint.Y, z = currentPoint.Z;
                    double newE = currentE;
                    var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var p in parts)
                    {
                        if (p.StartsWith("X", StringComparison.OrdinalIgnoreCase) && double.TryParse(p.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double xv)) x = xv;
                        else if (p.StartsWith("Y", StringComparison.OrdinalIgnoreCase) && double.TryParse(p.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double yv)) y = yv;
                        else if (p.StartsWith("Z", StringComparison.OrdinalIgnoreCase) && double.TryParse(p.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double zv)) z = zv;
                        else if (p.StartsWith("E", StringComparison.OrdinalIgnoreCase) && double.TryParse(p.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out double ev)) newE = ev;
                    }
                    var nextPoint = new Point3d(x, y, z);
                    // Append all segments to full path
                    if (result.Count == 0)
                        result.Add(Tuple.Create(i, currentPoint));
                    result.Add(Tuple.Create(i, nextPoint));
                    currentPoint = nextPoint;
                    currentE = newE;
                }
            }
            return result;
        }

        /// <summary>
        /// Create a thin ribbon mesh by extruding the path points vertically.
        /// </summary>
        private Mesh CreateRibbonMesh(List<Point3d> pts, double thickness)
        {
            var mesh = new Mesh();
            // Add bottom and top vertices
            foreach (var p in pts)
            {
                mesh.Vertices.Add(p);
                mesh.Vertices.Add(new Point3d(p.X, p.Y, p.Z + thickness));
            }
            // Build faces between consecutive pairs
            int count = pts.Count;
            for (int i = 0; i < count - 1; i++)
            {
                int b0 = 2 * i;
                int t0 = b0 + 1;
                int b1 = 2 * (i + 1);
                int t1 = b1 + 1;
                mesh.Faces.AddFace(b0, b1, t1);
                mesh.Faces.AddFace(b0, t1, t0);
            }
            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        /// <summary>
        /// Save a Rhino mesh to an ASCII STL file.
        /// </summary>
        private void SaveMeshToStl(Mesh mesh, string filePath)
        {
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine("solid GH_PATH");
                foreach (var face in mesh.Faces)
                {
                    var v1 = mesh.Vertices[face.A];
                    var v2 = mesh.Vertices[face.B];
                    var v3 = mesh.Vertices[face.C];
                    var normal = Vector3d.CrossProduct(new Vector3d(v2 - v1), new Vector3d(v3 - v1));
                    normal.Unitize();
                    writer.WriteLine($"  facet normal {normal.X.ToString(CultureInfo.InvariantCulture)} {normal.Y.ToString(CultureInfo.InvariantCulture)} {normal.Z.ToString(CultureInfo.InvariantCulture)}");
                    writer.WriteLine("    outer loop");
                    writer.WriteLine($"      vertex {v1.X.ToString(CultureInfo.InvariantCulture)} {v1.Y.ToString(CultureInfo.InvariantCulture)} {v1.Z.ToString(CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"      vertex {v2.X.ToString(CultureInfo.InvariantCulture)} {v2.Y.ToString(CultureInfo.InvariantCulture)} {v2.Z.ToString(CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"      vertex {v3.X.ToString(CultureInfo.InvariantCulture)} {v3.Y.ToString(CultureInfo.InvariantCulture)} {v3.Z.ToString(CultureInfo.InvariantCulture)}");
                    writer.WriteLine("    endloop");
                    writer.WriteLine("  endfacet");
                }
                writer.WriteLine("endsolid GH_PATH");
            }
        }

        /// <summary>
        /// Load an ASCII STL file into a Rhino mesh.
        /// </summary>
        private Mesh LoadMeshFromStl(string filePath)
        {
            var mesh = new Mesh();
            var lines = File.ReadAllLines(filePath);
            var verts = new List<Point3d>();
            foreach (var line in lines)
            {
                var trim = line.Trim();
                if (trim.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trim.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 4
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                        && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                        && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                    {
                        verts.Add(new Point3d(x, y, z));
                        if (verts.Count == 3)
                        {
                            int i1 = mesh.Vertices.Add(verts[0]);
                            int i2 = mesh.Vertices.Add(verts[1]);
                            int i3 = mesh.Vertices.Add(verts[2]);
                            mesh.Faces.AddFace(i1, i2, i3);
                            verts.Clear();
                        }
                    }
                }
            }
            mesh.Normals.ComputeNormals();
            return mesh;
        }

        /// <summary>
        /// Start the external Python-based mesh editor.
        /// </summary>
        private void StartPathEditor()
        {
            try
            {
                // Extract embedded Python scripts
                var scriptDir = _editTempDir;
                Directory.CreateDirectory(scriptDir);
                var asm = Assembly.GetExecutingAssembly();
                var resources = asm.GetManifestResourceNames();
                string[] scriptFiles = new[] { "gh_edit.py", "meshedit.py", "hands.py" };
                foreach (var name in scriptFiles)
                {
                    var res = resources.FirstOrDefault(r => r.EndsWith(name, StringComparison.OrdinalIgnoreCase));
                    if (res != null)
                    {
                        using var rs = asm.GetManifestResourceStream(res);
                        using var fs = File.Create(Path.Combine(scriptDir, name));
                        rs.CopyTo(fs);
                    }
                }
                // Write launcher
                var launcherName = "path_launcher.py";
                var launcher = Path.Combine(scriptDir, launcherName);
                using (var sw = new StreamWriter(launcher))
                {
                    sw.WriteLine("#!/usr/bin/env python3");
                    sw.WriteLine("import sys, os, subprocess");
                    sw.WriteLine("script_dir = os.path.dirname(os.path.realpath(__file__))");
                    sw.WriteLine($"input_path = r'{_editInPath}'");
                    sw.WriteLine($"output_path = r'{_editOutPath}'");
                    sw.WriteLine("script_path = os.path.join(script_dir, 'gh_edit.py')");
                    sw.WriteLine("if not os.path.exists(script_path):");
                    sw.WriteLine("    script_path = os.path.join(script_dir, 'meshedit.py')");
                    sw.WriteLine($"cmd = [sys.executable, script_path, '--input', input_path, '--output', output_path, '--executed', '{_currentLineIndex}', '--bbox_min', '{_bboxMinArg}', '--bbox_max', '{_bboxMaxArg}']");
                    sw.WriteLine("print('Running path editor:', cmd)");
                    sw.WriteLine("sys.exit(subprocess.call(cmd))");
                }
                // Launch via AppleScript (macOS Terminal)
                var apple = $@"
tell application ""Terminal""
    do script ""cd '{scriptDir}' && python3 {launcherName}""
    activate
end tell
";
                var scpt = Path.Combine(Path.GetTempPath(), $"run_pathedit_{Guid.NewGuid()}.scpt");
                File.WriteAllText(scpt, apple);
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = scpt,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                // Monitor output
                _editCancelSource = new CancellationTokenSource();
                _editMonitorTask = Task.Run(() => MonitorEditOutput(_editCancelSource.Token));
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Path editor launched. Toggle Edit button again to cancel.");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error starting path editor: {ex.Message}");
                _isEditing = false;
            }
        }

        /// <summary>
        /// Stop the external mesh editor and import the edited path.
        /// </summary>
        private void StopPathEditor()
        {
            try
            {
                _editCancelSource?.Cancel();
                // Attempt to kill any meshedit processes
                try
                {
                    var kill = new Process();
                    kill.StartInfo.FileName = "pkill";
                    kill.StartInfo.Arguments = "-f meshedit";
                    kill.StartInfo.UseShellExecute = false;
                    kill.Start();
                    kill.WaitForExit(500);
                }
                catch { }
                Thread.Sleep(500);
                if (File.Exists(_editOutPath))
                {
                    var editedMesh = LoadMeshFromStl(_editOutPath);
                    if (editedMesh != null)
                    {
                        var newPts = new List<Point3d>();
                        int count = _editPathPts.Count;
                        for (int i = 0; i < count; i++)
                        {
                            var b = editedMesh.Vertices[2 * i];
                            var t = editedMesh.Vertices[2 * i + 1];
                            newPts.Add(new Point3d((b.X + t.X) / 2, (b.Y + t.Y) / 2, (b.Z + t.Z) / 2));
                        }
                        // Update commands with new coordinates
                        for (int i = 0; i < count; i++)
                        {
                            int idx = _editPathPts[i].Item1;
                            // Only update unexecuted commands
                            if (idx < _currentLineIndex) continue;
                            var orig = _printCommands[idx].Trim();
                            if (orig.StartsWith("G0", StringComparison.OrdinalIgnoreCase) || orig.StartsWith("G1", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = orig.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                                var np = newPts[i];
                                var updated = new List<string> { parts[0], $"X{np.X.ToString(CultureInfo.InvariantCulture)}", $"Y{np.Y.ToString(CultureInfo.InvariantCulture)}", $"Z{np.Z.ToString(CultureInfo.InvariantCulture)}" };
                                foreach (var p in parts.Skip(1))
                                {
                                    if (!p.StartsWith("X", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("Y", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("Z", StringComparison.OrdinalIgnoreCase))
                                        updated.Add(p);
                                }
                                _printCommands[idx] = string.Join(" ", updated);
                            }
                        }
                        ExpireSolution(true);
                    }
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error stopping path editor: {ex.Message}");
            }
            finally
            {
                _isEditing = false;
            }
        }

        /// <summary>
        /// Monitor external editor output file for changes to trigger import.
        /// </summary>
        private void MonitorEditOutput(CancellationToken token)
        {
            try
            {
                DateTime lastMod = DateTime.MinValue;
                while (!token.IsCancellationRequested)
                {
                    if (File.Exists(_editOutPath))
                    {
                        var fi = new FileInfo(_editOutPath);
                        if (fi.LastWriteTime > lastMod && fi.Length > 0)
                        {
                            lastMod = fi.LastWriteTime;
                            Thread.Sleep(100);
                            Rhino.RhinoApp.InvokeOnUiThread(StopPathEditor);
                            break;
                        }
                    }
                    Thread.Sleep(500);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in edit monitor: {ex.Message}");
            }
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
        
        /// <summary>
        /// Custom preview: translation (blue), extrusion (red), executed (orange).
        /// </summary>
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            // Note: skipping base.DrawViewportWires to prevent default Path preview
            // Snapshot lists to avoid enumeration errors
            var execTrans = _execTransSegs.ToArray();
            var execExtr = _execExtrusionSegs.ToArray();
            var unexecTrans = _unexecTransSegs.ToArray();
            var unexecExtr = _unexecExtrusionSegs.ToArray();
            // Executed segments in orange
            foreach (var seg in execTrans) args.Display.DrawCurve(seg, Color.Orange, 3);
            foreach (var seg in execExtr) args.Display.DrawCurve(seg, Color.Orange, 3);
            // Upcoming translation and extrusion
            foreach (var seg in unexecTrans) args.Display.DrawCurve(seg, Color.Blue, 1);
            foreach (var seg in unexecExtr) args.Display.DrawCurve(seg, Color.Red, 1);
            // Draw printer bounding box if enabled
            if (_showBBox)
            {
                // Build box corners based on home position and dimensions
                var p0 = _homePosition;
                var dims = _printerBBoxDims;
                var p1 = new Point3d(p0.X + dims.X, p0.Y, p0.Z);
                var p2 = new Point3d(p0.X + dims.X, p0.Y + dims.Y, p0.Z);
                var p3 = new Point3d(p0.X, p0.Y + dims.Y, p0.Z);
                var p4 = new Point3d(p0.X, p0.Y, p0.Z + dims.Z);
                var p5 = new Point3d(p0.X + dims.X, p0.Y, p0.Z + dims.Z);
                var p6 = new Point3d(p0.X + dims.X, p0.Y + dims.Y, p0.Z + dims.Z);
                var p7 = new Point3d(p0.X, p0.Y + dims.Y, p0.Z + dims.Z);
                // Bottom rectangle
                args.Display.DrawLine(new Line(p0, p1), Color.LightBlue, 1);
                args.Display.DrawLine(new Line(p1, p2), Color.LightBlue, 1);
                args.Display.DrawLine(new Line(p2, p3), Color.LightBlue, 1);
                args.Display.DrawLine(new Line(p3, p0), Color.LightBlue, 1);
                // Top rectangle
                args.Display.DrawLine(new Line(p4, p5), Color.LightBlue, 1);
                args.Display.DrawLine(new Line(p5, p6), Color.LightBlue, 1);
                args.Display.DrawLine(new Line(p6, p7), Color.LightBlue, 1);
                args.Display.DrawLine(new Line(p7, p4), Color.LightBlue, 1);
                // Vertical edges
                args.Display.DrawLine(new Line(p0, p4), Color.LightBlue, 1);
                args.Display.DrawLine(new Line(p1, p5), Color.LightBlue, 1);
                args.Display.DrawLine(new Line(p2, p6), Color.LightBlue, 1);
                args.Display.DrawLine(new Line(p3, p7), Color.LightBlue, 1);
            }
            // Draw sample points if requested (evenly along entire path)
            if (_samplesPerSegment > 0)
            {
                var sampleColor = Color.LimeGreen;
                // collect all segments in order: executed then upcoming
                var allCurves = execTrans.Concat(execExtr).Concat(unexecTrans).Concat(unexecExtr);
                var samples = EvenlySampleCurves(allCurves, _samplesPerSegment);
                foreach (var pt in samples)
                    args.Display.DrawPoint(pt, sampleColor);
            }
        }
        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("f6b42a3d-5f1e-4e87-a840-2a0d7a8c6e5f");
    }
}