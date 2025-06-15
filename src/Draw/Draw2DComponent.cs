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

namespace crft
{
    public class Draw2DComponent : GH_Component
    {
        private bool _startLast = false;
        private bool _serverStarted = false;
        private bool _received = false;
        private int _port;
        private HttpListener _listener;
        private Task _serverTask;
        private readonly List<StrokeRecordNormalized> _serverStrokes = new List<StrokeRecordNormalized>();
        private List<Curve> _inputCurves;
        private double _bedX;
        private double _bedY;
        private List<PolylineCurve> _newCurves;

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
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Server URL", "URL", "URL for drawing connection", GH_ParamAccess.item);
            pManager.AddCurveParameter("Output Curves", "C", "Input and drawn curves", GH_ParamAccess.list);
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

            if (start && !_startLast)
            {
                _inputCurves = curves.Select(c => (Curve)c.Duplicate()).ToList();
                _bedX = bedX;
                _bedY = bedY;
                StartServer();
                _startLast = true;
                DA.SetData(0, GetServerUrl());
                return;
            }
            if (!start && _startLast)
            {
                StopServer();
                Reset();
                _startLast = false;
                return;
            }
            _startLast = start;

            // Output server URL
            DA.SetData(0, _serverStarted ? GetServerUrl() : string.Empty);
            // Always output input curves + any strokes received from web
            if (_serverStarted && _inputCurves != null)
            {
                var outCurves = new List<Curve>();
                // input curves
                outCurves.AddRange(_inputCurves.Select(c => (Curve)c.Duplicate()));
                // strokes from clients
                foreach (var rec in _serverStrokes)
                {
                    var pts = new List<Point3d>();
                    foreach (var p in rec.points)
                    {
                        var x = p.x * _bedX;
                        var y = p.y * _bedY;
                        pts.Add(new Point3d(x, y, 0));
                    }
                    if (pts.Count > 1)
                        outCurves.Add(new PolylineCurve(pts));
                }
                DA.SetDataList(1, outCurves);
            }
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

                // initialize server strokes and start HTML server
                _serverStrokes.Clear();

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
                            // Normalize and store strokes
                            foreach (var stroke in data.strokes)
                            {
                                var normPts = new List<PointDto>();
                                foreach (var pt in stroke.points)
                                {
                                    double nx = pt.x / data.width;
                                    double ny = (data.height - pt.y) / data.height;
                                    normPts.Add(new PointDto { x = nx, y = ny });
                                }
                                _serverStrokes.Add(new StrokeRecordNormalized { points = normPts, color = data.color });
                            }
                            resp.StatusCode = 200;
                            resp.Close();
                            // Trigger Grasshopper to recompute and display new strokes
                            Application.Instance.Invoke(() => ExpireSolution(true));
                        }
                        // Serve strokes JSON for polling
                        else if (req.HttpMethod == "GET" && req.Url.AbsolutePath == "/strokes")
                        {
                            var respData = JsonConvert.SerializeObject(new { strokes = _serverStrokes });
                            var buf = Encoding.UTF8.GetBytes(respData);
                            resp.ContentType = "application/json";
                            resp.ContentEncoding = Encoding.UTF8;
                            resp.ContentLength64 = buf.Length;
                            resp.OutputStream.Write(buf, 0, buf.Length);
                            resp.OutputStream.Close();
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
        }

        private void StopServer()
        {
            try { if (_listener?.IsListening == true) _listener.Stop(); } catch { }
            _serverStarted = false;
        }

        private void Reset()
        {
            _received = false;
            _inputCurves = null;
            _newCurves = null;
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
    html, body { margin:0; padding:0; font-family:Arial,sans-serif; }
    body { display:flex; flex-direction:column; align-items:center; padding:20px; background:#f0f0f0; }
    #canvas { border:2px solid #333; background:#fff; border-radius:8px; touch-action:none; }
    .controls { margin-top:10px; display:flex; gap:10px; }
    button { padding:8px 16px; background:#007bff; color:#fff; border:none; border-radius:4px; cursor:pointer; }
    button:hover { background:#0056b3; }
  </style>
</head>
<body>
  <canvas id='canvas'></canvas>
  <div class='controls'>
    <button id='clearBtn'>Clear</button>
    <button id='submitBtn'>Submit</button>
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
      fetch('/strokes').then(r=>r.json()).then(d=>{ existingStrokes = d.strokes; drawAll(); });
    }
    window.addEventListener('load', ()=>{ adjustCanvas(); pollStrokes(); setInterval(pollStrokes,2000); });
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
        private class StrokeRecordNormalized { public List<PointDto> points; public string color; }
    }
}