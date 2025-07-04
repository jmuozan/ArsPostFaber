using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Eto.Forms;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Linq;
using QRCoder;
using Eto.Drawing;
using Newtonsoft.Json;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using RJCP.IO.Ports;
using System.Runtime.InteropServices;

namespace crft
{
    public class Draw2DComponent : GH_Component
    {
        private bool _startLast = false;
        private bool _serverStarted = false;
        private int _port;
        private HttpListener _listener;
        private Task _serverTask;
        // store each submission (with its strokes & color)
        // store normalized strokes per submission
        private class SubmissionRecord { public List<List<PointDto>> strokes; public string color; }
        private readonly List<SubmissionRecord> _submissions = new List<SubmissionRecord>();
        private int _lastProcessedSubmissionIndex = 0;
        private List<Curve> _inputCurves;
        private double _bedX;
        private double _bedY;
        // G-code buffer and playback indices
        private List<string> _gcodeLines = new List<string>();
        private int _currentGcodeLineIndex = 0;

        // Serial control fields
        private ISerialPort _serialPort;
        private bool _printing = false;

        public Draw2DComponent()
          : base("Draw2D", "Draw2D", "Interactive 2D drawing component", "crft", "Draw")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Start", "S", "Activate drawing session", GH_ParamAccess.item, false);
            pManager.AddCurveParameter("Input Curves", "C", "Curves to display", GH_ParamAccess.list);
            pManager.AddNumberParameter("Bed Size X", "X", "Bed size X dimension", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Bed Size Y", "Y", "Bed size Y dimension", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Lift Height", "H", "Height to lift pen for travel moves", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Z Down", "Z", "Height to lower pen for drawing", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Feed Rate", "F", "Feed rate for drawing moves (mm/min)", GH_ParamAccess.item, 1000.0);
            pManager.AddNumberParameter("Travel Rate", "T", "Feed rate for travel moves (mm/min)", GH_ParamAccess.item, 3000.0);
            pManager.AddNumberParameter("Max Segment Length", "R", "Maximum length (mm) of each G-code segment for path smoothing", GH_ParamAccess.item, 1.0);
            // Allow drawing without supplying input curves
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Server URL", "URL", "URL for drawing connection", GH_ParamAccess.item);
            pManager.AddCurveParameter("Output Curves", "C", "Input and drawn curves per submission", GH_ParamAccess.tree);
            pManager.AddTextParameter("G-Code", "G", "Generated G-Code commands for all submissions", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool start = false;
            DA.GetData(0, ref start);

            var rawCurves = new List<GeometryBase>();
            DA.GetDataList(1, rawCurves);
            var curves = rawCurves.OfType<Curve>().ToList();
            double bedX = 0, bedY = 0;
            DA.GetData(2, ref bedX);
            DA.GetData(3, ref bedY);
            double liftHeight = 1.0, zDown = 0.0, feedRate = 1000.0, travelRate = 3000.0;
            DA.GetData(4, ref liftHeight);
            DA.GetData(5, ref zDown);
            DA.GetData(6, ref feedRate);
            DA.GetData(7, ref travelRate);
            // Maximum length per segment for path smoothing
            double maxSegmentLength = 1.0;
            DA.GetData(8, ref maxSegmentLength);

            if (start && !_startLast)
            {
                _inputCurves = curves.Select(c => (Curve)c.Duplicate()).ToList();
                _bedX = bedX;
                _bedY = bedY;
                // clear any previous submissions
                _submissions.Clear();
                StartServer();
                _startLast = true;
                DA.SetData(0, GetServerUrl());
                DA.SetData(2, string.Empty);
                return;
            }
            if (!start && _startLast)
            {
                StopServer();
                Reset();
                _startLast = false;
                DA.SetData(2, string.Empty);
                return;
            }
            _startLast = start;

            // Output server URL
            DA.SetData(0, _serverStarted ? GetServerUrl() : string.Empty);
            // Debug: show submission count
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Submissions: {_submissions.Count}");
            // Output strokes grouped per submission as a data tree
            {
                var tree = new Grasshopper.DataTree<Curve>();
                double tol = Math.Min(_bedX, _bedY) * 1e-6;
                for (int i = 0; i < _submissions.Count; i++)
                {
                    var sub = _submissions[i];
                    var path = new Grasshopper.Kernel.Data.GH_Path(i);
                    foreach (var stroke in sub.strokes)
                    {
                        if (stroke.Count < 2) continue;
                        // build and clean point list
                        var rawPts = stroke.Select(p => new Point3d(p.x * _bedX, p.y * _bedY, 0)).ToList();
                        var cleanPts = new List<Point3d>();
                        Point3d lastPt = rawPts[0];
                        cleanPts.Add(lastPt);
                        foreach (var pt in rawPts.Skip(1))
                        {
                            if (pt.DistanceTo(lastPt) > tol)
                            {
                                cleanPts.Add(pt);
                                lastPt = pt;
                            }
                        }
                        if (cleanPts.Count < 2) continue;
                        var poly = new PolylineCurve(cleanPts);
                        if (poly.IsValid)
                            tree.Add(poly, path);
                    }
                }
                DA.SetDataTree(1, tree);
            }
            // Generate G-Code for all submissions incrementally
            string gcodeText = string.Empty;
            if (_serverStarted)
            {
                
                // Append commands for new strokes only
                for (int i = _lastProcessedSubmissionIndex; i < _submissions.Count; i++)
                {
                    var submission = _submissions[i];
                    foreach (var stroke in submission.strokes)
                    {
                        if (stroke.Count < 1) continue;
                        // Convert normalized points to machine coordinates
                        var rawPts = stroke.Select(p => new Point3d(p.x * _bedX, p.y * _bedY, 0)).ToList();
                        if (rawPts.Count < 1) continue;
                        // Densify path for smoother motion
                        // Densify path for smoother motion using user-specified max segment length
                        double maxLen = maxSegmentLength;
                        var pts = new List<Point3d> { rawPts[0] };
                        for (int j = 1; j < rawPts.Count; j++)
                        {
                            var prev = rawPts[j - 1];
                            var curr = rawPts[j];
                            double dist = prev.DistanceTo(curr);
                            if (dist > maxLen)
                            {
                                int segments = (int)Math.Floor(dist / maxLen);
                                for (int k = 1; k <= segments; k++)
                                {
                                    double t = (double)k / (segments + 1);
                                    pts.Add(new Point3d(
                                        prev.X + (curr.X - prev.X) * t,
                                        prev.Y + (curr.Y - prev.Y) * t,
                                        0));
                                }
                            }
                            pts.Add(curr);
                        }
                        // Ensure pen is lifted before travel
                        _gcodeLines.Add(string.Format(CultureInfo.InvariantCulture, "G1 Z{0:0.###} F{1:0.###}", liftHeight, travelRate));
                        // Travel to stroke start
                        var startPt = pts[0];
                        _gcodeLines.Add(string.Format(CultureInfo.InvariantCulture, "G1 X{0:0.###} Y{1:0.###} F{2:0.###}", startPt.X, startPt.Y, travelRate));
                        // Pen down
                        _gcodeLines.Add(string.Format(CultureInfo.InvariantCulture, "G1 Z{0:0.###}", zDown));
                        // Draw stroke with finer segments
                        foreach (var pt in pts.Skip(1))
                            _gcodeLines.Add(string.Format(CultureInfo.InvariantCulture, "G1 X{0:0.###} Y{1:0.###} F{2:0.###}", pt.X, pt.Y, feedRate));
                        // Pen up at end
                        _gcodeLines.Add(string.Format(CultureInfo.InvariantCulture, "G1 Z{0:0.###}", liftHeight));
                    }
                }
                _lastProcessedSubmissionIndex = _submissions.Count;
                gcodeText = string.Join("\n", _gcodeLines);
            }
            DA.SetData(2, gcodeText);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("BDEF6789-ABCD-4EFA-B123-4567890ABCDE");

        private string GetServerUrl()
        {
            var ip = GetLocalIPAddress();
            return $"http://{ip}:{_port}/";
        }

        private void StartServer()
        {
            _port = GetFreePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{_port}/");
            var ip = GetLocalIPAddress();
            _listener.Prefixes.Add($"http://{ip}:{_port}/");
            try
            {
            _listener.Start();
            _serverStarted = true;
            var url = GetServerUrl();
            Application.Instance.Invoke(() =>
            {
                ShowQrCode(url);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Draw2D server listening at {url}");
            });
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to start server: {ex.Message}");
                return;
            }

                // clear previous submissions
                _submissions.Clear();

            _serverTask = Task.Run(() =>
            {
                while (_listener.IsListening)
                {
                    try
                    {
                        var ctx = _listener.GetContext();
                        var req = ctx.Request;
                        var resp = ctx.Response;
                        // Handle stroke upload
                        if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/upload")
                        {
                            using var reader = new StreamReader(req.InputStream);
                            var json = reader.ReadToEnd();
                            var data = JsonConvert.DeserializeObject<UploadData>(json);
                            // Normalize into submission record
                            var submission = new SubmissionRecord { strokes = new List<List<PointDto>>(), color = data.color };
                            foreach (var stroke in data.strokes)
                            {
                                var normPts = new List<PointDto>();
                                foreach (var pt in stroke.points)
                                {
                                    double nx = pt.x / data.width;
                                    double ny = (data.height - pt.y) / data.height;
                                    normPts.Add(new PointDto { x = nx, y = ny });
                                }
                                if (normPts.Count > 1)
                                    submission.strokes.Add(normPts);
                            }
                            _submissions.Add(submission);
                            resp.StatusCode = 200;
                            resp.Close();
                            Application.Instance.Invoke(() => ExpireSolution(true));
                            }
                            // Serve serial ports JSON
                            else if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/ports")
                            {
                                // Gather serial ports from multiple sources
                                var portsList = new List<string>();
                                // Try RJCP SerialPortStream
                                try { portsList.AddRange(RJCP.IO.Ports.SerialPortStream.GetPortNames()); } catch { }
                                // Try System.IO.Ports
                                try { portsList.AddRange(System.IO.Ports.SerialPort.GetPortNames()); } catch { }
                                // Fallback for Unix/macOS if still empty
                                if (portsList.Count == 0 && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                                {
                                    var dev = "/dev";
                                    var patterns = new[] { "tty.*", "cu.*", "ttyUSB*", "ttyACM*" };
                                    foreach (var pat in patterns)
                                    {
                                        try
                                        {
                                            var files = Directory.GetFiles(dev, pat);
                                            portsList.AddRange(files.Select(f => Path.GetFileName(f)));
                                        }
                                        catch { }
                                    }
                                }
                                var ports = portsList.Distinct().ToArray();
                                var respDataP = JsonConvert.SerializeObject(new { ports });
                                var bufP = Encoding.UTF8.GetBytes(respDataP);
                                resp.ContentType = "application/json";
                                resp.ContentEncoding = Encoding.UTF8;
                                resp.ContentLength64 = bufP.Length;
                                resp.OutputStream.Write(bufP, 0, bufP.Length);
                                resp.OutputStream.Close();
                            }
                            // Handle connect request
                            else if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/connect")
                            {
                                using var reader2 = new StreamReader(req.InputStream);
                                var body2 = reader2.ReadToEnd();
                                var connData = JsonConvert.DeserializeObject<ConnectData>(body2);
                                if (_serialPort != null && _serialPort.IsOpen)
                                {
                                    resp.StatusCode = 200;
                                    resp.Close();
                                }
                                else
                                {
                                    try
                                    {
                                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                                            _serialPort = new WindowsSerialPort(connData.port, connData.baud);
                                        else
                                            _serialPort = new UnixSerialPort(
                                                connData.port.StartsWith("/") ? connData.port : Path.Combine("/dev", connData.port),
                                                connData.baud);
                                        _serialPort.Open();
                                        _serialPort.ClearBuffers();
                                        // Home axes on connection
                                        _serialPort.WriteLine("G28");
                                        resp.StatusCode = 200;
                                    }
                                    catch
                                    {
                                        resp.StatusCode = 500;
                                    }
                                    resp.Close();
                                }
                            }
                            // Handle disconnect request
                            else if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/disconnect")
                            {
                                try
                                {
                                    if (_serialPort != null)
                                    {
                                        _serialPort.ClearBuffers();
                                        _serialPort.Close();
                                        _serialPort = null;
                                    }
                                    resp.StatusCode = 200;
                                }
                                catch
                                {
                                    resp.StatusCode = 500;
                                }
                                resp.Close();
                            }
                            // Handle play request
                            else if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/play")
                            {
                                _printing = true;
                                resp.StatusCode = 200;
                                resp.Close();
                            }
                            // Handle pause request: pause server-side printing
                            else if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/pause")
                            {
                                _printing = false;
                                resp.StatusCode = 200;
                                resp.Close();
                            }
                            // Serve status JSON
                            else if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/status")
                            {
                                var statusObj = new { printing = _printing, current = _currentGcodeLineIndex, total = _gcodeLines.Count };
                                var statusJson = JsonConvert.SerializeObject(statusObj);
                                var bufS = Encoding.UTF8.GetBytes(statusJson);
                                resp.ContentType = "application/json";
                                resp.ContentEncoding = Encoding.UTF8;
                                resp.ContentLength64 = bufS.Length;
                                resp.OutputStream.Write(bufS, 0, bufS.Length);
                                resp.OutputStream.Close();
                            }
                            // Serve strokes JSON for polling
                        else if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/strokes")
                        {
                            // Flatten submissions into strokes array
                            var flat = new List<object>();
                            foreach (var sub in _submissions)
                            {
                                foreach (var stroke in sub.strokes)
                                    flat.Add(new { points = stroke, color = sub.color });
                            }
                            var respData = JsonConvert.SerializeObject(new { strokes = flat });
                            var buf = Encoding.UTF8.GetBytes(respData);
                            resp.ContentType = "application/json";
                            resp.ContentEncoding = Encoding.UTF8;
                            resp.ContentLength64 = buf.Length;
                            resp.OutputStream.Write(buf, 0, buf.Length);
                            resp.OutputStream.Close();
                        }
                        else if (req.HttpMethod == "POST" && req.Url.AbsolutePath == "/home")
                        {
                            if (_serialPort != null && _serialPort.IsOpen && !_printing)
                            {
                                _serialPort.WriteLine("G28");
                                resp.StatusCode = 200;
                            }
                            else
                            {
                                resp.StatusCode = 400; // Bad request if not connected or printing
                            }
                            resp.Close();
                        }
                        // Serve dynamic HTML page
                        else
                        {
                            var html = BuildHtml();
                            var buf = Encoding.UTF8.GetBytes(html);
                            resp.ContentType = "text/html";
                            resp.ContentEncoding = Encoding.UTF8;
                            resp.ContentLength64 = buf.Length;
                            resp.OutputStream.Write(buf, 0, buf.Length);
                            resp.OutputStream.Close();
                        }
                    }
                    catch { break; }
                }
            });
            // start background print loop
            Task.Run(async () =>
            {
                while (_serverStarted)
                {
                    if (_serialPort != null && _printing && _currentGcodeLineIndex < _gcodeLines.Count)
                    {
                        _serialPort.WriteLine(_gcodeLines[_currentGcodeLineIndex]);
                        _currentGcodeLineIndex++;
                    }
                    await Task.Delay(100);
                }
            });
        }

        private void StopServer()
        {
            try { if (_listener?.IsListening == true) _listener.Stop(); } catch { }
            _serverStarted = false;
        }

        private void Reset()
        {
            // clear stored submissions and input curves
            _submissions.Clear();
            _inputCurves = null;
            _serverStarted = false;
            // Clear G-code buffer and indices
            _gcodeLines.Clear();
            _lastProcessedSubmissionIndex = 0;
            _currentGcodeLineIndex = 0;
        }

        private string BuildHtml()
        {
            // Serialize initial curves
            var initCurves = new List<List<PointDto>>();
            foreach (var c in _inputCurves)
            {
                c.DivideByCount(50, true, out var pts);
                initCurves.Add(pts.Select(p => new PointDto { x = p.X, y = p.Y }).ToList());
            }
            // Assign each user a distinct color
            var rand = new Random();
            var clientColor = "#" + rand.Next(0x1000000).ToString("X6");
            var initData = new { bedX = _bedX, bedY = _bedY, curves = initCurves, clientColor };
            var json = JsonConvert.SerializeObject(initData);
            // HTML + JS template
            var template = @"<!DOCTYPE html>
<html lang='en'>
<head>
  <meta charset='UTF-8'>
  <meta name='viewport' content='width=device-width, initial-scale=1.0'>
  <title>Draw2D Canvas</title>
  <style>
    html, body { margin:0; padding:0; width:100%; height:100%; background:#000; color:#fff; font-family:-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; }
    body { display:flex; flex-direction:column; align-items:center; justify-content:flex-start; padding:10px; box-sizing:border-box; }
    #canvas { background:#fff; border:none; border-radius:0; touch-action:none; display:block; }
    .controls { margin-top:10px; display:flex; flex-wrap:wrap; gap:10px; width:100%; max-width:800px; justify-content:center; }
    button, select { padding:10px 20px; background:#000; color:#fff; border:none; border-radius:0; cursor:pointer; font-size:1em; }
    button:hover, select:hover { opacity:0.8; }
    #serialStatus { margin-left:8px; font-weight:bold; color:#fff; }
    @media (max-width: 600px) {
      .controls { flex-direction:column; align-items:stretch; }
      button, select { width:100%; }
    }
  </style>
</head>
<body>
  <canvas id='canvas'></canvas>
  <div class='controls'>
    <button id='clearBtn'>Clear</button>
    <button id='submitBtn'>Submit</button>
    <select id='portSelect'></select>
    <button id='connBtn'>Connect</button>
    <button id='playBtn'>Play</button>
    <button id='homeBtn'>Home</button>
    <span id='serialStatus'>Disconnected</span>
  </div>
  <script>
    const initData = %%DATA%%;
    let existingStrokes = [];
    const canvas = document.getElementById('canvas');
    const ctx = canvas.getContext('2d');
    const clearBtn = document.getElementById('clearBtn');
    const submitBtn = document.getElementById('submitBtn');
    const clientColor = initData.clientColor;
    let isDrawing = false, strokes = [], currentStroke = null;
    // Add camera capture button and image processing pipeline
    const imageBtn = document.createElement('button');
    imageBtn.id = 'imageBtn';
    imageBtn.textContent = 'Capture Image';
    const fileInput = document.createElement('input');
    fileInput.type = 'file'; fileInput.accept = 'image/*'; fileInput.capture = 'environment'; fileInput.style.display = 'none';
    const controlsDiv = document.querySelector('.controls');
    controlsDiv.appendChild(imageBtn); controlsDiv.appendChild(fileInput);
    const originalCanvas = document.createElement('canvas');
    const originalCtx = originalCanvas.getContext('2d');
    imageBtn.addEventListener('click', () => fileInput.click());
    fileInput.addEventListener('change', e => {
      const file = e.target.files[0]; if (!file) return;
      const reader = new FileReader();
      reader.onload = ev => {
        const img = new Image(); img.onload = () => {
          const maxW = canvas.width, maxH = canvas.height;
          const ratio = Math.min(maxW / img.width, maxH / img.height);
          const w2 = Math.floor(img.width * ratio), h2 = Math.floor(img.height * ratio);
          originalCanvas.width = w2; originalCanvas.height = h2;
          originalCtx.drawImage(img, 0, 0, w2, h2);
          const imageData = originalCtx.getImageData(0, 0, w2, h2);
          const gray = toGrayscale(imageData);
          const edges = detectEdges(gray, 100);
          const contours = findContours(edges);
          const simplified = simplifyContours(contours, 3, 50);
          strokes = []; existingStrokes = [];
          ctx.clearRect(0, 0, canvas.width, canvas.height);
          drawBase();
          simplified.forEach(contour => {
            const pts = contour.map(p => ({ x: p.x * canvas.width / w2, y: p.y * canvas.height / h2 }));
            strokes.push({ points: pts });
          }); drawAll();
          fetch('/upload', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify({ strokes, width: canvas.width, height: canvas.height, color: clientColor }) });
          strokes = [];
        }; img.src = ev.target.result;
      };
      reader.readAsDataURL(file);
    });
    // Utility functions for image processing
    function toGrayscale(imageData) {
      const data = imageData.data; const grayData = new Uint8ClampedArray(imageData.width * imageData.height);
      for (let i = 0; i < data.length; i += 4) {
        const g = data[i] * 0.299 + data[i+1] * 0.587 + data[i+2] * 0.114;
        grayData[i/4] = g;
      } return { data: grayData, width: imageData.width, height: imageData.height };
    }
    function detectEdges(grayData, threshold) {
      const { data, width, height } = grayData; const edges = new Uint8ClampedArray(width * height);
      for (let y = 1; y < height - 1; y++) {
        for (let x = 1; x < width - 1; x++) {
          const idx = y * width + x;
          const sobelX = -data[(y-1)*width + (x-1)] + data[(y-1)*width + (x+1)] +
                         -2*data[y*width + (x-1)] + 2*data[y*width + (x+1)] +
                         -data[(y+1)*width + (x-1)] + data[(y+1)*width + (x+1)];
          const sobelY = -data[(y-1)*width + (x-1)] - 2*data[(y-1)*width + x] - data[(y-1)*width + (x+1)] +
                         data[(y+1)*width + (x-1)] + 2*data[(y+1)*width + x] + data[(y+1)*width + (x+1)];
          edges[idx] = Math.sqrt(sobelX*sobelX + sobelY*sobelY) > threshold ? 255 : 0;
        }
      } return { data: edges, width, height };
    }
    function findContours(edges) {
      const { data, width, height } = edges; const visited = Array(width*height).fill(false), contours = [];
      for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
        const idx = y*width + x;
        if (data[idx] === 255 && !visited[idx]) {
          const contour = [], stack = [{x,y}];
          while (stack.length) {
            const { x:cx, y:cy } = stack.pop(), cidx = cy*width + cx;
            if (cx<0||cx>=width||cy<0||cy>=height||visited[cidx]||data[cidx]!==255) continue;
            visited[cidx]=true; contour.push({x:cx,y:cy});
            for (let dy=-1;dy<=1;dy++) for (let dx=-1;dx<=1;dx++) if(dx||dy) stack.push({x:cx+dx,y:cy+dy});
          }
          if (contour.length>10) contours.push(contour);
        }
      }
      return contours;
    }
    function simplifyContours(contours, eps, minSize) {
      return contours.filter(c=>c.length>=minSize).map(c=>douglasPeucker(c,eps)).filter(c=>c.length>=3);
    }
    function douglasPeucker(pts, eps) {
      if (pts.length<=2) return pts;
      let dmax=0, idx=0; const start=pts[0], end=pts[pts.length-1];
      for (let i=1;i<pts.length-1;i++){const d=pointToLineDistance(pts[i],start,end); if(d> dmax){dmax=d;idx=i;}}
      if (dmax>eps) {
        const l=douglasPeucker(pts.slice(0,idx+1),eps), r=douglasPeucker(pts.slice(idx),eps);
        return l.slice(0,-1).concat(r);
      }
      return [start,end];
    }
    function pointToLineDistance(p,s,e) {
      const A=p.x-s.x, B=p.y-s.y, C=e.x-s.x, D=e.y-s.y;
      const dot=A*C+B*D, lenSq=C*C+D*D, param=lenSq?dot/lenSq:-1;
      const xx=param<0?s.x:param>1?e.x:s.x+param*C;
      const yy=param<0?s.y:param>1?e.y:s.y+param*D;
      const dx=p.x-xx, dy=p.y-yy;
      return Math.sqrt(dx*dx+dy*dy);
    }

    function adjustCanvas() {
      const margin = 40;
      const maxW = window.innerWidth - margin;
      const maxH = window.innerHeight - margin - 80;
      const ratio = initData.bedX / initData.bedY;
      let w = maxW, h = w / ratio;
      if (h > maxH) { h = maxH; w = h * ratio; }
      canvas.width = w; canvas.height = h;
    }

    function drawBase() {
      const w = canvas.width, h = canvas.height;
      ctx.clearRect(0,0,w,h);
      ctx.strokeStyle = '#888'; ctx.lineWidth = 1;
      ctx.strokeRect(0,0,w,h);
      ctx.strokeStyle = '#000'; ctx.lineWidth = 2;
      initData.curves.forEach(curve => {
        ctx.beginPath();
        curve.forEach((p,i) => {
          const x = p.x * w / initData.bedX;
          const y = h - (p.y * h / initData.bedY);
          i===0? ctx.moveTo(x,y) : ctx.lineTo(x,y);
        });
        ctx.stroke();
      });
    }

    function drawExisting() {
      existingStrokes.forEach(st => {
        ctx.strokeStyle = st.color; ctx.lineWidth = 2;
        ctx.beginPath();
        st.points.forEach((p,i) => {
          const x = p.x * canvas.width;
          const y = (1 - p.y) * canvas.height;
          i===0? ctx.moveTo(x,y) : ctx.lineTo(x,y);
        });
        ctx.stroke();
      });
    }

    function drawLocal() {
      ctx.strokeStyle = clientColor; ctx.lineWidth = 3;
      strokes.forEach(st => {
        ctx.beginPath();
        st.points.forEach((pt,i) => {
          i===0? ctx.moveTo(pt.x,pt.y) : ctx.lineTo(pt.x,pt.y);
        });
        ctx.stroke();
      });
    }

    function drawAll() { drawBase(); drawExisting(); drawLocal(); }

    canvas.addEventListener('mousedown', e => {
      isDrawing = true;
      const r = canvas.getBoundingClientRect();
      const x = e.clientX - r.left, y = e.clientY - r.top;
      currentStroke = { points:[{x,y}] };
      strokes.push(currentStroke);
    });
    canvas.addEventListener('mousemove', e => {
      if (!isDrawing) return;
      const r = canvas.getBoundingClientRect();
      const x = e.clientX - r.left, y = e.clientY - r.top;
      currentStroke.points.push({x,y});
      ctx.strokeStyle = clientColor;
      ctx.beginPath();
      const prev = currentStroke.points[currentStroke.points.length-2];
      ctx.moveTo(prev.x,prev.y);
      ctx.lineTo(x,y);
      ctx.stroke();
    });
    ['mouseup','mouseout'].forEach(ev => canvas.addEventListener(ev,()=>isDrawing=false));
    // Touch events for mobile drawing
    canvas.addEventListener('touchstart', e => {
      e.preventDefault();
      const t = e.touches[0];
      const r = canvas.getBoundingClientRect();
      const x = t.clientX - r.left, y = t.clientY - r.top;
      isDrawing = true;
      currentStroke = { points:[{x,y}] };
      strokes.push(currentStroke);
    });
    canvas.addEventListener('touchmove', e => {
      e.preventDefault();
      if (!isDrawing) return;
      const t = e.touches[0];
      const r = canvas.getBoundingClientRect();
      const x = t.clientX - r.left, y = t.clientY - r.top;
      currentStroke.points.push({x,y});
      ctx.strokeStyle = clientColor;
      ctx.beginPath();
      const prev = currentStroke.points[currentStroke.points.length-2];
      ctx.moveTo(prev.x,prev.y);
      ctx.lineTo(x,y);
      ctx.stroke();
    });
    canvas.addEventListener('touchend', e => {
      e.preventDefault();
      isDrawing = false;
    });

    clearBtn.addEventListener('click', ()=>{ strokes=[]; drawAll(); });
    submitBtn.addEventListener('click', ()=>{
      fetch('/upload',{
        method: 'POST', headers:{'Content-Type':'application/json'},
        body: JSON.stringify({ strokes, width:canvas.width, height:canvas.height, color: clientColor })
      }).catch(e => alert('Submit error: ' + e));
      // clear local strokes but keep the canvas active
      strokes = [];
    });

    function pollStrokes() {
      if (localPaused) return;
      fetch('/strokes').then(r=>r.json()).then(d=>{ existingStrokes = d.strokes; drawAll(); });
    }
    // Serial port UI handlers
    function loadPorts() {
      fetch('/ports')
        .then(r => r.json())
        .then(d => {
          const sel = document.getElementById('portSelect');
          sel.innerHTML = '';
          d.ports.forEach(p => {
            const opt = document.createElement('option');
            opt.value = p; opt.textContent = p;
            sel.appendChild(opt);
          });
        });
    }
    const connBtn = document.getElementById('connBtn');
    const playBtn = document.getElementById('playBtn');
    const serialStatus = document.getElementById('serialStatus');
    const homeBtn = document.getElementById('homeBtn');
    let connected = false;
    let localPaused = false;
    connBtn.addEventListener('click', () => {
      const sel = document.getElementById('portSelect');
      if (!connected) {
        fetch('/connect', {
            method: 'POST',
            headers: {'Content-Type':'application/json'},
            body: JSON.stringify({ port: sel.value, baud: 115200 })
          })
          .then(() => { connected = true; connBtn.textContent = 'Disconnect'; serialStatus.textContent = 'Connected'; })
          .catch(() => alert('Connect failed'));
      } else {
        fetch('/disconnect', { method: 'POST' })
          .then(() => { connected = false; connBtn.textContent = 'Connect'; serialStatus.textContent = 'Disconnected'; })
          .catch(() => alert('Disconnect failed'));
      }
    });
    playBtn.addEventListener('click', () => {
      if (!connected) return alert('Connect first');
      if (playBtn.textContent === 'Play') {
        localPaused = false;
        const sel = document.getElementById('portSelect');
        // Reconnect silently to flush buffers and ensure printer readiness
        fetch('/disconnect', { method: 'POST' })
          .then(() => fetch('/connect', {
            method: 'POST',
            headers: {'Content-Type':'application/json'},
            body: JSON.stringify({ port: sel.value, baud: 115200 })
          }))
          .then(() => fetch('/play', { method: 'POST' }))
          .then(() => { playBtn.textContent = 'Pause'; serialStatus.textContent = 'Printing'; })
          .catch(() => alert('Play failed'));
      } else {
        // pause printing on server and locally
        fetch('/pause', { method: 'POST' })
          .then(() => {
            localPaused = true;
            playBtn.textContent = 'Play';
            serialStatus.textContent = 'Paused';
          })
          .catch(() => alert('Pause failed'));
      }
    });
    homeBtn.addEventListener('click', () => {
      if (!connected) return alert('Connect first');
      fetch('/home', { method: 'POST' })
          .then(() => { serialStatus.textContent = 'Homing...'; })
          .catch(() => alert('Home failed'));
    });
    function pollStatus() {
      if (localPaused) return;
      fetch('/status')
        .then(r => r.json())
        .then(d => {
          serialStatus.textContent = d.printing ? `Printing (${d.current}/${d.total})` : (connected ? 'Paused' : 'Disconnected');
          playBtn.textContent = d.printing ? 'Pause' : 'Play';
        });
    }
    window.addEventListener('load', () => {
      adjustCanvas(); loadPorts();
      // initial polls
      pollStrokes(); pollStatus();
      // periodic polling
      setInterval(() => { if (!localPaused) pollStrokes(); }, 2000);
      setInterval(pollStatus, 2000);
    });
    window.addEventListener('resize', ()=>{ adjustCanvas(); drawAll(); });
  </script>
</body>
</html>";
            return template.Replace("%%DATA%%", json);
        }

        private int GetFreePort()
        {
            var listener = TcpListener.Create(0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private string GetLocalIPAddress()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint end) return end.Address.ToString();
            }
            catch { }
            try
            {
                var addrs = Dns.GetHostAddresses(Dns.GetHostName());
                foreach (var a in addrs) if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) return a.ToString();
            }
            catch { }
            return "localhost";
        }

        private void ShowQrCode(string url)
        {
            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var pngQr = new PngByteQRCode(qrData);
            var qrBytes = pngQr.GetGraphic(20);
            using var ms = new MemoryStream(qrBytes);
            var bmp = new Bitmap(ms);
            var imageView = new ImageView { Image = bmp, Size = bmp.Size };
            var label = new Label { Text = url, Wrap = WrapMode.Word, TextAlignment = TextAlignment.Center };
            var layout = new DynamicLayout { DefaultSpacing = new Size(5,5), Padding = new Padding(10) };
            layout.AddCentered(imageView);
            layout.AddCentered(label);
            var form = new Form
            {
                Title = "Scan to Draw",
                ClientSize = new Size(bmp.Width+20, bmp.Height+60),
                Content = layout,
                Resizable = false
            };
            form.Show();
        }

        private class PointDto { public double x; public double y; }
        private class Stroke { public List<PointDto> points; }
        private class UploadData { public List<Stroke> strokes; public double width; public double height; public string color; }
        // Data contract for port connection requests
        private class ConnectData { public string port; public int baud; }
    }
}