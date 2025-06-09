using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Eto.Forms;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
// using Rhino.RhinoDoc; // not a namespace
using System.Linq;
using QRCoder;
using Eto.Drawing;

namespace crft
{
    /// <summary>
    /// Component that runs photogrammetry on a folder of images.
    /// </summary>
    public class PhotogrammetryComponent : GH_Component
    {
        // State fields for asynchronous capture and processing
        private bool _startLast = false;
        private bool _serverStarted = false;
        private bool _videoReceived = false;
        private bool _processing = false;
        private bool _finished = false;
        private int _port;
        private string _videoPath;
        private string _modelPath;
        private System.Net.HttpListener _listener;
        private System.Threading.Tasks.Task _serverTask;
        private string _detail;
        private string _sampleOrdering;
        private string _featureSensitivity;

        public PhotogrammetryComponent()
          : base("Photogrammetry", "Photogrammetry",
              "Reconstructs a 3D USDZ model from a folder of images using HelloPhotogrammetry.",
              "crft", "Photogrammetry")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Start", "S", "Activate camera capture and photogrammetry", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Detail", "D", "Detail level {preview, reduced, medium, full, raw}", GH_ParamAccess.item, string.Empty);
            pManager.AddTextParameter("Sample Ordering", "SO", "Sample ordering {unordered, sequential}", GH_ParamAccess.item, string.Empty);
            pManager.AddTextParameter("Feature Sensitivity", "FS", "Feature sensitivity value", GH_ParamAccess.item, string.Empty);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Server URL", "URL", "URL for phone to connect and upload video", GH_ParamAccess.item);
            pManager.AddMeshParameter("Mesh", "M", "Reconstructed mesh from photogrammetry", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Read inputs
            bool start = false;
            DA.GetData(0, ref start);
            DA.GetData(1, ref _detail);
            DA.GetData(2, ref _sampleOrdering);
            DA.GetData(3, ref _featureSensitivity);

            // Handle start toggle
            if (start && !_startLast)
            {
                StartServer();
                _startLast = true;
                DA.SetData(0, GetServerUrl());
                return;
            }
            if (!start && _startLast)
            {
                StopServer();
                ResetFlags();
                _startLast = false;
                return;
            }
            _startLast = start;

            // Output server URL if running
            DA.SetData(0, _serverStarted ? GetServerUrl() : string.Empty);

            // If video uploaded and not yet processing, start photogrammetry
            if (_serverStarted && _videoReceived && !_processing)
            {
                _processing = true;
                RunPhotogrammetry();
            }

            // Output model file when finished
            if (_finished)
            {
                // Import the generated model into the Rhino document and capture new mesh
                var doc = Rhino.RhinoDoc.ActiveDoc;
                // record existing object IDs
                var beforeIds = new HashSet<Guid>(doc.Objects.Select(o => o.Id));
                // import the USDZ file, adding new geometry
                Rhino.RhinoApp.RunScript($"_-Import \"{_modelPath}\" _Enter", false);
                // find newly added objects
                var newObjs = doc.Objects
                    .Where(o => !beforeIds.Contains(o.Id))
                    .ToList();
                // combine meshes from new geometry
                var combinedMesh = new Mesh();
                bool found = false;
                foreach (var obj in newObjs)
                {
                    var geo = obj.Geometry;
                    if (geo is Mesh m)
                    {
                        combinedMesh.Append(m.DuplicateMesh()); found = true;
                    }
                    else if (geo is Brep b)
                    {
                        var meshes = Mesh.CreateFromBrep(b, MeshingParameters.Default);
                        foreach (var piece in meshes)
                        {
                            combinedMesh.Append(piece); found = true;
                        }
                    }
                }
                if (found)
                {
                    DA.SetData(1, combinedMesh);
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No mesh found in imported model.");
                }
                // cleanup imported objects
                foreach (var obj in newObjs)
                    doc.Objects.Delete(obj.Id, true);
                // reset flags
                _finished = false; _processing = false; _videoReceived = false;
                // Reset flags to allow reprocessing
                _finished = false;
                _processing = false;
                _videoReceived = false;
            }
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("3F1D4C82-5B7D-4C3E-8E12-A3B4567890FA");
        /// <summary>
        /// Builds the server URL for client connection.
        /// </summary>
        private string GetServerUrl()
        {
            var ip = GetLocalIPAddress();
            return $"http://{ip}:{_port}/";
        }

        private void StartServer()
        {
            _port = GetFreePort();
            _listener = new System.Net.HttpListener();
            // Listen on all network interfaces and on specific LAN IP
            _listener.Prefixes.Add($"http://*:{_port}/");
            var ip = GetLocalIPAddress();
            _listener.Prefixes.Add($"http://{ip}:{_port}/");
            try
            {
                _listener.Start();
                _serverStarted = true;
                // Show QR code for quick connection and notify user
                var url = GetServerUrl();
                Application.Instance.Invoke(() =>
                {
                    ShowQrCode(url);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Photogrammetry server listening at {url}");
                });
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to start server: {ex.Message}");
                return;
            }
            _serverTask = System.Threading.Tasks.Task.Run(() =>
            {
                while (_listener.IsListening)
                {
                    try
                    {
                        var ctx = _listener.GetContext();
                        var req = ctx.Request;
                        var resp = ctx.Response;
                        if (req.Url.AbsolutePath == "/upload")
                        {
                            // Clear previous temp data
                            var dir = Path.Combine(Path.GetTempPath(), "gh_photogrammetry");
                            if (Directory.Exists(dir))
                            {
                                try { Directory.Delete(dir, true); } catch { }
                            }
                            Directory.CreateDirectory(dir);
                            _videoPath = Path.Combine(dir, "video.mp4");
                            using var fs = File.Create(_videoPath);
                            req.InputStream.CopyTo(fs);
                            resp.StatusCode = 200;
                            resp.Close();
                            // Notify video upload and trigger processing
                            Application.Instance.Invoke(() => AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                                $"Video uploaded: {_videoPath}"));
                            _videoReceived = true;
                            // Keep server running for subsequent uploads
                            Eto.Forms.Application.Instance.Invoke(() => ExpireSolution(true));
                        }
                        else
                        {
                            // Serve a mobile-friendly upload page with camera capture
                            var html = @"<!DOCTYPE html>
<html>
<head>
  <meta name='viewport' content='width=device-width, initial-scale=1.0'>
  <title>Upload Video</title>
  <style>
    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; margin:20px; text-align:center; }
    h1 { font-weight:600; }
    input[type=file] { margin:10px 0; }
    button { background-color:#007AFF; color:#fff; border:none; border-radius:8px; padding:10px 20px; font-size:1em; margin:5px; }
    button:hover { background-color:#005BB5; }
    label { display:block; margin:15px 0; }
  </style>
</head>
<body>
<h1>Upload Video</h1>
<form id='uploadForm'>
  <label>
    Record Video:<br/>
    <input type='file' id='captureInput' accept='video/*' capture='environment'>
  </label>
  <label>
    Or Choose Existing Video:<br/>
    <input type='file' id='fileInput' accept='video/*'>
  </label>
  <button type='submit'>Upload</button>
</form>
<script>
document.getElementById('uploadForm').addEventListener('submit', function(e) {
  e.preventDefault();
  var fileInput = document.getElementById('fileInput');
  var captureInput = document.getElementById('captureInput');
  var file = (fileInput.files.length? fileInput.files[0] : (captureInput.files.length? captureInput.files[0] : null));
  if (!file) { alert('Please record or choose a video.'); return; }
  fetch('/upload', { method:'POST', body:file })
    .then(function(resp) { document.body.innerHTML = '<h2>Uploaded. Thank you!</h2>'; })
    .catch(function(err) { alert('Upload failed: '+err); });
});
</script>
</body>
</html>";
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

        private void ResetFlags()
        {
            _videoReceived = false;
            _processing = false;
            _finished = false;
            _videoPath = null;
            _modelPath = null;
        }

        private void RunPhotogrammetry()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                var dir = Path.Combine(Path.GetTempPath(), "gh_photogrammetry");
                var framesDir = Path.Combine(dir, "frames");
                Directory.CreateDirectory(framesDir);
                // Notify frame extraction start
                Application.Instance.Invoke(() => AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Extracting frames..."));
                // Extract frames (requires ffmpeg in PATH)
                try
                {
                    var psiF = new ProcessStartInfo("ffmpeg", $"-i \"{_videoPath}\" \"{framesDir}/frame_%04d.jpg\"")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                    using var pF = Process.Start(psiF);
                    pF.WaitForExit();
                    if (pF.ExitCode != 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Frame extraction failed.");
                    }
                    else
                    {
                        Application.Instance.Invoke(() => AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Frames extracted to {framesDir}"));
                    }
                }
                catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Error extracting frames: " + e.Message); }
                // Run photogrammetry
                Application.Instance.Invoke(() => AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Running photogrammetry..."));
                _modelPath = Path.Combine(dir, "model.usdz");
                var exePath = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "photogrammetry", "HelloPhotogrammetry");
                var parts = new List<string> { framesDir, _modelPath };
                if (!string.IsNullOrWhiteSpace(_detail)) parts.Add("--detail " + _detail);
                if (!string.IsNullOrWhiteSpace(_sampleOrdering)) parts.Add("--sample-ordering " + _sampleOrdering);
                if (!string.IsNullOrWhiteSpace(_featureSensitivity)) parts.Add("--feature-sensitivity " + _featureSensitivity);
                try
                {
                    var psiP = new ProcessStartInfo(exePath, string.Join(" ", parts))
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                    using var pP = Process.Start(psiP);
                    pP.WaitForExit();
                    if (pP.ExitCode != 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Photogrammetry failed: " + pP.StandardError.ReadToEnd());
                    }
                    else
                    {
                        Application.Instance.Invoke(() => AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Photogrammetry completed: {_modelPath}"));
                    }
                }
                catch (Exception e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error running photogrammetry: " + e.Message); }
                // Cleanup temporary files, but preserve the generated model
                try
                {
                    // Delete captured video
                    if (!string.IsNullOrEmpty(_videoPath) && File.Exists(_videoPath))
                        File.Delete(_videoPath);
                    // Delete extracted frames
                    if (Directory.Exists(framesDir))
                        Directory.Delete(framesDir, true);
                }
                catch { }
                _finished = true;
                Eto.Forms.Application.Instance.Invoke(() => ExpireSolution(true));
            });
        }

        private int GetFreePort()
        {
            var listener = System.Net.Sockets.TcpListener.Create(0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private string GetLocalIPAddress()
        {
            // Try UDP socket trick to find outward-facing IPv4
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endPoint)
                    return endPoint.Address.ToString();
            }
            catch
            {
                // ignore and fallback
            }
            // Fallback: pick first non-loopback IPv4 address
            try
            {
                var addrs = Dns.GetHostAddresses(Dns.GetHostName());
                foreach (var addr in addrs)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                        return addr.ToString();
                }
            }
            catch
            {
                // ignore
            }
            return "localhost";
        }

        /// <summary>
        /// Displays a window containing the QR code and URL for mobile connection.
        /// </summary>
        private void ShowQrCode(string url)
        {
            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            // Generate PNG QR code as byte array
            var pngQr = new PngByteQRCode(qrData);
            var qrBytes = pngQr.GetGraphic(20);
            // Load PNG bytes directly into Eto.Drawing.Bitmap
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
            form.Show();
        }
    }
}