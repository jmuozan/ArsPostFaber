using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Eto.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using System.Linq;
using Rhino.Geometry;
using Newtonsoft.Json.Linq;
using QRCoder;
using Eto.Drawing;

namespace crft
{
    /// <summary>
    /// Component that hosts a web canvas for drawing 2D curves and imports them into Grasshopper.
    /// </summary>
    public class Draw2DComponent : GH_Component
    {
        private bool _startLast = false;
        private bool _serverStarted = false;
        private bool _dataReceived = false;
        private int _port;
        private HttpListener _listener;
        private Task _serverTask;
        private Dictionary<string, List<List<Point3d>>> _strokesByClient = new Dictionary<string, List<List<Point3d>>>();
        private Dictionary<string, string> _clientColors = new Dictionary<string, string>();
        private List<Curve> _inputCurves;
        private double _originX, _originY, _canvasWorldWidth, _canvasWorldHeight;

        public Draw2DComponent()
          : base("Draw 2D", "Draw2D",
              "Hosts a web-based 2D drawing canvas and outputs drawn curves.",
              "crft", "Draw")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Start", "S", "Activate web drawing session", GH_ParamAccess.item, false);
            pManager.AddCurveParameter("Input Curves", "C", "Optional curves to preload on canvas", GH_ParamAccess.list);
            pManager[1].Optional = true;
            pManager.AddNumberParameter("Bed X", "BX", "Bed width in X direction (world units), overrides input curves mapping", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Bed Y", "BY", "Bed depth in Y direction (world units), overrides input curves mapping", GH_ParamAccess.item, 0.0);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("URL", "URL", "URL to open the drawing canvas", GH_ParamAccess.item);
            pManager.AddCurveParameter("Curves", "C", "Drawn curves as 2D polylines", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool start = false;
            DA.GetData(0, ref start);
            var inputCurves = new List<Curve>();
            DA.GetDataList(1, inputCurves);
            double bedX = 0, bedY = 0;
            DA.GetData(2, ref bedX);
            DA.GetData(3, ref bedY);

            // Start session on rising edge
            if (start && !_startLast)
            {
                // determine mapping via bed size or input curves
                if (bedX > 0 && bedY > 0)
                {
                    // Use bed dimensions for canvas mapping; keep input curves for preload
                    _inputCurves = inputCurves;
                    _originX = 0; _originY = 0;
                    _canvasWorldWidth = bedX; _canvasWorldHeight = bedY;
                }
                else if (inputCurves.Count > 0)
                {
                    _inputCurves = inputCurves;
                    bool first = true;
                    BoundingBox bbox = default;
                    foreach (var c in _inputCurves)
                    {
                        var bb = c.GetBoundingBox(true);
                        if (first) { bbox = bb; first = false; }
                        else bbox.Union(bb);
                    }
                    _originX = bbox.Min.X; _originY = bbox.Min.Y;
                    _canvasWorldWidth = bbox.Max.X - bbox.Min.X; if (_canvasWorldWidth <= 0) _canvasWorldWidth = 1;
                    _canvasWorldHeight = bbox.Max.Y - bbox.Min.Y; if (_canvasWorldHeight <= 0) _canvasWorldHeight = 1;
                }
                else
                {
                    _inputCurves = null;
                    _originX = 0; _originY = 0;
                    _canvasWorldWidth = 1; _canvasWorldHeight = 1;
                }
                StartServer();
                _startLast = true;
                DA.SetData(0, GetServerUrl());
                // output bed border and input curves immediately
                var initCurves = new List<Curve>();
                if (_canvasWorldWidth > 0 && _canvasWorldHeight > 0)
                {
                    var pts = new List<Point3d>
                    {
                        new Point3d(_originX, _originY, 0),
                        new Point3d(_originX + _canvasWorldWidth, _originY, 0),
                        new Point3d(_originX + _canvasWorldWidth, _originY + _canvasWorldHeight, 0),
                        new Point3d(_originX, _originY + _canvasWorldHeight, 0),
                        new Point3d(_originX, _originY, 0)
                    };
                    initCurves.Add(new PolylineCurve(pts));
                }
                if (_inputCurves != null)
                    initCurves.AddRange(_inputCurves);
                DA.SetDataList(1, initCurves);
                return;
            }
            // Stop session on falling edge
            if (!start && _startLast)
            {
                StopServer();
                ResetFlags();
                _startLast = false;
                return;
            }
            _startLast = start;

            // Output server URL
            DA.SetData(0, _serverStarted ? GetServerUrl() : string.Empty);
            // Build output data tree with branches: 0=bed, 1=input, 2+ users
            var tree = new GH_Structure<GH_Curve>();
            // Branch 0: bed boundary
            if (_canvasWorldWidth > 0 && _canvasWorldHeight > 0)
            {
                var pts = new List<Point3d>
                {
                    new Point3d(_originX, _originY, 0),
                    new Point3d(_originX + _canvasWorldWidth, _originY, 0),
                    new Point3d(_originX + _canvasWorldWidth, _originY + _canvasWorldHeight, 0),
                    new Point3d(_originX, _originY + _canvasWorldHeight, 0),
                    new Point3d(_originX, _originY, 0)
                };
                var border = new PolylineCurve(pts);
                tree.Append(new GH_Curve(border), new GH_Path(0));
            }
            // Branch 1: input curves
            if (_inputCurves != null)
                tree.AppendRange(_inputCurves.Select(c => new GH_Curve(c)), new GH_Path(1));
            // Branches 2+: user strokes
            int userIndex = 2;
            lock (_strokesByClient)
            {
                foreach (var kv in _strokesByClient)
                {
                    var userCurves = new List<Curve>();
                    foreach (var stroke in kv.Value)
                        if (stroke.Count > 1)
                            userCurves.Add(new PolylineCurve(stroke));
                    tree.AppendRange(userCurves.Select(c => new GH_Curve(c)), new GH_Path(userIndex));
                    userIndex++;
                }
            }
            DA.SetDataTree(1, tree);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("D1E2F3A4-5678-90AB-CDEF-1234567890AB");

        private void StartServer()
        {
            _port = GetFreePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{_port}/");
            _listener.Prefixes.Add($"http://{GetLocalIPAddress()}:{_port}/");
            // reset per-session strokes and colors
            _strokesByClient = new Dictionary<string, List<List<Point3d>>>();
            _clientColors = new Dictionary<string, string>();
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to start Draw2D server: {ex.Message}");
                return;
            }
            _serverTask = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch { break; }
                    var req = ctx.Request;
                    var resp = ctx.Response;
                    if (req.Url.AbsolutePath == "/upload" && req.HttpMethod == "POST")
                    {
                        // Multi-user stroke upload
                        var clientId = req.QueryString["client"] ?? "_default";
                        var colorParam = req.QueryString["color"];
                        if (!string.IsNullOrEmpty(colorParam))
                            lock (_clientColors)
                                _clientColors[clientId] = colorParam;
                        string json;
                        using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                            json = await reader.ReadToEndAsync();
                        try
                        {
                            var arr = JArray.Parse(json);
                            var list = new List<List<Point3d>>();
                            foreach (JArray stroke in arr)
                            {
                                var pts = new List<Point3d>();
                                foreach (var p in stroke)
                                {
                                    double x = (double)p["x"];
                                    double y = (double)p["y"];
                                    pts.Add(new Point3d(x + _originX, y + _originY, 0));
                                }
                                list.Add(pts);
                            }
                            lock (_strokesByClient)
                                _strokesByClient[clientId] = list;
                        }
                        catch { }
                        resp.StatusCode = 200;
                        resp.OutputStream.Close();
                        Application.Instance.Invoke(() => ExpireSolution(true));
                    }
                    else if (req.Url.AbsolutePath == "/data" && req.HttpMethod == "GET")
                    {
                        // Return all clients' strokes as JSON object
                        var root = new JObject();
                        lock (_strokesByClient)
                        {
                            foreach (var kv in _strokesByClient)
                            {
                                var jarr = new JArray();
                                foreach (var stroke in kv.Value)
                                {
                                    var parr = new JArray();
                                    foreach (var pt in stroke)
                                        parr.Add(new JObject(new JProperty("x", pt.X - _originX), new JProperty("y", pt.Y - _originY)));
                                    jarr.Add(parr);
                                }
                                // Include client color
                                var color = _clientColors.ContainsKey(kv.Key) ? _clientColors[kv.Key] : "#000000";
                                root[kv.Key] = new JObject(
                                    new JProperty("color", color),
                                    new JProperty("strokes", jarr)
                                );
                            }
                        }
                        var outBuf = Encoding.UTF8.GetBytes(root.ToString());
                        resp.ContentType = "application/json";
                        resp.ContentLength64 = outBuf.Length;
                        resp.OutputStream.Write(outBuf, 0, outBuf.Length);
                        resp.OutputStream.Close();
                    }
                    else
                    {
                        var html = GenerateHtml();
                        var buf = Encoding.UTF8.GetBytes(html);
                        resp.ContentType = "text/html";
                        resp.ContentEncoding = Encoding.UTF8;
                        resp.ContentLength64 = buf.Length;
                        resp.OutputStream.Write(buf, 0, buf.Length);
                        resp.OutputStream.Close();
                    }
                }
            });
        }

        private void StopServer()
        {
            try { if (_listener?.IsListening == true) _listener.Stop(); }
            catch { }
            _serverStarted = false;
        }

        private void ResetFlags()
        {
            // clear state
            _inputCurves = null;
            _originX = _originY = _canvasWorldWidth = _canvasWorldHeight = 0;
            _strokesByClient.Clear();
            _port = 0;
        }

        // ParseJsonStrokes removed; multi-user handled via /upload and /data endpoints

        private string GenerateHtml()
        {
            // Build preload strokes in world coords relative to origin
            var preloadArr = new JArray();
            if (_inputCurves != null)
            {
                foreach (var c in _inputCurves)
                {
                    var nurbs = c.ToNurbsCurve();
                    var domain = nurbs.Domain;
                        int segments = 200;
                    var ptsArr = new JArray();
                    for (int i = 0; i <= segments; i++)
                    {
                        double t = domain.T0 + (domain.T1 - domain.T0) * i / segments;
                        var p = nurbs.PointAt(t);
                        ptsArr.Add(new JObject(
                            new JProperty("x", p.X - _originX),
                            new JProperty("y", p.Y - _originY)));
                    }
                    preloadArr.Add(ptsArr);
                }
            }
            var preloadJson = preloadArr.ToString(Newtonsoft.Json.Formatting.None);
            return @"<!DOCTYPE html>  
<html>
<head>
  <meta name='viewport' content='width=device-width, initial-scale=1.0'>
  <title>Draw 2D</title>
    <style>
    body {{ margin:0; overflow:hidden; display:flex; justify-content:center; align-items:center; font-family:-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, 'Fira Sans', 'Droid Sans', 'Helvetica Neue', sans-serif; }}
    canvas {{ touch-action:none; display:block; border:2px solid #000; }}
    #submitBtn {{ position:fixed; top:10px; right:10px; z-index:10; padding:8px 12px; font-family:inherit; }}
    #colorIndicator {{ position:fixed; top:10px; left:10px; z-index:10; padding:4px 8px; background-color:rgba(255,255,255,0.8); border:1px solid #000; border-radius:4px; font-family:inherit; }}
  </style>
</head>
<body>
<canvas id='drawCanvas'></canvas>
<button id='submitBtn'>Submit</button>
<div id='colorIndicator'></div>
        <script>
        /* Removed JS block to restore compilation; please re-add drawing logic here */
        // Helper to convert event to canvas coordinates
        function toCanvas(e) {
            const rect = canvas.getBoundingClientRect();
            return {
                x: Math.max(0, Math.min(e.clientX - rect.left, canvas.width)),
                y: Math.max(0, Math.min(e.clientY - rect.top, canvas.height))
            };
        }

        function resizeCanvas() {{
          const vw = window.innerWidth;
          const vh = window.innerHeight;
          const scale = Math.min(vw / WORLD_WIDTH, vh / WORLD_HEIGHT);
          const cw = WORLD_WIDTH * scale;
          const ch = WORLD_HEIGHT * scale;
          canvas.width = cw;
          canvas.height = ch;
          canvas.style.width = cw + 'px';
          canvas.style.height = ch + 'px';
          redraw();
        }}
        window.addEventListener('resize', resizeCanvas);
        resizeCanvas();

        // Poll strokes from server and merge every 500ms
        setInterval(() => {{
          fetch('/data').then(r => r.json()).then(data => {{
            strokesByClient = {{}};
            clientColors = {{}};
            for (let id in data) {{
              let entry = data[id];
              clientColors[id] = entry.color;
              strokesByClient[id] = entry.strokes;
            }}
            redraw();
          }});
        }}, 500);
        // Upload own strokes periodically for live updates
        setInterval(() => {{
          fetch('/upload?client=' + CLIENT_ID + '&color=' + CLIENT_COLOR, {{
            method: 'POST',
            headers: {{ 'Content-Type': 'application/json' }},
            body: JSON.stringify(strokesByClient[CLIENT_ID] || [])
          }});
        }}, 200);

        // Drawing events
        let isDrawing = false;
        let currentPointerId = null;
        canvas.addEventListener('pointerdown', e => {{
          if (isDrawing) return;
          isDrawing = true;
          currentPointerId = e.pointerId;
          canvas.setPointerCapture(currentPointerId);
          currentStroke = [];
          strokesByClient[CLIENT_ID].push(currentStroke);
          addPoint(e);
        }});
        canvas.addEventListener('pointermove', e => {{
          if (!isDrawing || e.pointerId !== currentPointerId) return;
          const p = toCanvas(e);
          ctx.lineTo(p.x, p.y);
          ctx.strokeStyle = CLIENT_COLOR;
          ctx.lineWidth = 3;
          ctx.stroke();
          addPoint(e);
        }});
        function endStroke(e) {{
          if (!isDrawing || e.pointerId !== currentPointerId) return;
          addPoint(e);
          isDrawing = false;
          canvas.releasePointerCapture(currentPointerId);
          currentPointerId = null;
        }}
        canvas.addEventListener('pointerup', endStroke);
        canvas.addEventListener('pointercancel', endStroke);

        function addPoint(e) {{
          const rect = canvas.getBoundingClientRect();
          let px = e.clientX - rect.left;
          let py = e.clientY - rect.top;
          px = Math.max(0, Math.min(px, canvas.width));
          py = Math.max(0, Math.min(py, canvas.height));
          let wx = (px / canvas.width) * WORLD_WIDTH;
          let wy = ((canvas.height - py) / canvas.height) * WORLD_HEIGHT;
          if (currentStroke) {{
            currentStroke.push({{ x: wx, y: wy }});
            // (removed live upload here for smoother drawing)
          }}
        }}

        function redraw() {{
          ctx.clearRect(0, 0, canvas.width, canvas.height);
          // border
          ctx.save(); ctx.lineWidth = 2; ctx.strokeStyle = '#000';
          ctx.strokeRect(1, 1, canvas.width - 2, canvas.height - 2);
          ctx.restore();

          // draw preloaded curves
          ctx.save(); ctx.strokeStyle = '#888'; ctx.lineWidth = 1;
          for (let stroke of PRELOAD_STROKES) {{
            if (stroke.length < 2) continue;
            ctx.beginPath();
            let p0 = stroke[0];
            ctx.moveTo(p0.x / WORLD_WIDTH * canvas.width, (1 - p0.y / WORLD_HEIGHT) * canvas.height);
            for (let pt of stroke) {{
              ctx.lineTo(pt.x / WORLD_WIDTH * canvas.width, (1 - pt.y / WORLD_HEIGHT) * canvas.height);
            }}
            ctx.stroke();
          }}
          ctx.restore();

          // draw each client's strokes in its color
          for (let id in strokesByClient) {{
            let color = id === CLIENT_ID
              ? CLIENT_COLOR
              : (clientColors[id] || '#000000');
            ctx.strokeStyle = color;
            ctx.lineWidth = 3; ctx.lineJoin = 'round'; ctx.lineCap = 'round';
            for (let stroke of strokesByClient[id]) {{
              if (stroke.length < 2) continue;
              ctx.beginPath();
              let p0 = stroke[0];
              ctx.moveTo(p0.x / WORLD_WIDTH * canvas.width, (1 - p0.y / WORLD_HEIGHT) * canvas.height);
              for (let pt of stroke) {{
                ctx.lineTo(pt.x / WORLD_WIDTH * canvas.width, (1 - pt.y / WORLD_HEIGHT) * canvas.height);
              }}
              ctx.stroke();
            }}
          }}
        }}
// simple hash for color
function hashCode(str) {{ return str.split('').reduce((h,c)=>((h<<5)-h)+c.charCodeAt(0),0); }}
document.getElementById('submitBtn').addEventListener('click', () => {{
  fetch('/upload?client='+CLIENT_ID+'&color='+CLIENT_COLOR, {{
    method:'POST', headers:{{'Content-Type':'application/json'}},
    body: JSON.stringify(strokesByClient[CLIENT_ID])
  }})
  .then(() => {{ document.body.innerHTML = '<h2>Submitted</h2>'; }})
  .catch(err => alert('Upload failed:' + err));
}});
</script>
</body>
</html>";
        }

        /// <summary>
        /// Displays a window containing the QR code and URL for mobile connection.
        /// </summary>
        private void ShowQrCode(string url)
        {
            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            var pngQr = new PngByteQRCode(qrData);
            var qrBytes = pngQr.GetGraphic(20);
            using var ms = new MemoryStream(qrBytes);
            var etoBmp = new Bitmap(ms);
            var imageView = new ImageView { Image = etoBmp, Size = etoBmp.Size };
            var label = new Label { Text = url, Wrap = WrapMode.Word, TextAlignment = TextAlignment.Center };
            var layout = new DynamicLayout { DefaultSpacing = new Size(5, 5), Padding = new Padding(10) };
            layout.AddCentered(imageView);
            layout.AddCentered(label);
            var form = new Form
            {
                Title = "Scan QR Code to Connect",
                ClientSize = new Size(etoBmp.Width + 20, etoBmp.Height + 60),
                Content = layout,
                Resizable = false
            };
            form.Location = new Eto.Drawing.Point(0, 0);
            form.Show();
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
                if (socket.LocalEndPoint is IPEndPoint endPoint)
                    return endPoint.Address.ToString();
            }
            catch {}
            try
            {
                var addrs = Dns.GetHostAddresses(Dns.GetHostName());
                foreach (var addr in addrs)
                    if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                        return addr.ToString();
            }
            catch {}
            return "localhost";
        }
        private string GetServerUrl()
        {
            var ip = GetLocalIPAddress();
            return $"http://{ip}:{_port}/";
        }
    }
}