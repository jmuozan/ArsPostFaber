using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.GUI.Canvas;
using Rhino.Geometry;

namespace MyNamespace
{
    public class WebcamComponent : GH_Component
    {
        internal Bitmap _currentFrame;
        private bool _isRunning = false;
        private bool _hasCapturedFrame = false;
        private int _deviceIndex = 0;
        private CancellationTokenSource _cancellationSource;
        private Task _captureTask;
        private Process _avfProcess;
        private string _tempImagePath;
        private readonly int _refreshInterval = 33; // in milliseconds (30 fps)

        /// <summary>
        /// Initializes a new instance of the WebcamComponent class.
        /// </summary>
        public WebcamComponent()
          : base("Webcam", "Cam",
              "Captures and displays webcam video feed",
              "Display", "Preview")
        {
            _tempImagePath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), 
                "grasshopper_webcam_" + Guid.NewGuid().ToString() + ".jpg");
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Enable", "E", "Enable/disable webcam", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Device", "D", "Webcam device index", GH_ParamAccess.item, 0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Image", "I", "Current webcam frame as bitmap", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool enable = false;
            int deviceIndex = 0;

            if (!DA.GetData(0, ref enable)) return;
            if (!DA.GetData(1, ref deviceIndex)) return;

            if (enable && !_isRunning)
            {
                _deviceIndex = deviceIndex;
                StartCamera();
            }
            else if (!enable && _isRunning)
            {
                StopCamera();
            }

            if (_currentFrame != null)
            {
                DA.SetData(0, _currentFrame);
            }
        }

        private void StartCamera()
        {
            try
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Starting webcam capture...");
                
                _cancellationSource = new CancellationTokenSource();
                _captureTask = Task.Run(() => CaptureLoop(_cancellationSource.Token));
                
                _isRunning = true;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error starting webcam: " + ex.Message);
                if (ex.InnerException != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Inner exception: " + ex.InnerException.Message);
                }
            }
        }

        private void CaptureLoop(CancellationToken token)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS approach
                MacOSCaptureLoop(token);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows would use AForge here - not implemented in this version
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Windows implementation uses AForge.Video.DirectShow");
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unsupported platform");
            }
        }

        private void MacOSCaptureLoop(CancellationToken token)
        {
            try
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Starting macOS webcam capture...");

                // Get available webcams and log them
                Process listDevices = new Process();
                listDevices.StartInfo.FileName = "imagesnap";
                listDevices.StartInfo.Arguments = "-l";
                listDevices.StartInfo.UseShellExecute = false;
                listDevices.StartInfo.RedirectStandardOutput = true;
                listDevices.StartInfo.CreateNoWindow = true;
                listDevices.Start();
                string deviceList = listDevices.StandardOutput.ReadToEnd();
                listDevices.WaitForExit();
                
                // Extract camera names from output
                string[] lines = deviceList.Split('\n');
                List<string> cameraNames = new List<string>();
                
                foreach (string line in lines)
                {
                    if (line.StartsWith("=>"))
                    {
                        string cameraName = line.Substring(3).Trim();
                        cameraNames.Add(cameraName);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Found camera: {cameraName}");
                    }
                }
                
                // Check if we have cameras and if deviceIndex is valid
                if (cameraNames.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No webcams found with imagesnap -l");
                    return;
                }
                
                // Adjust device index if out of range
                if (_deviceIndex < 0 || _deviceIndex >= cameraNames.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Device index {_deviceIndex} out of range. Using camera 0.");
                    _deviceIndex = 0;
                }
                
                string selectedCamera = cameraNames[_deviceIndex];
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Using camera: {selectedCamera}");
                
                // Start continuous capture process (single long-running process)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Starting continuous capture...");
                
                // Start continuous capture with direct method
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Starting continuous capture...");
                
                int captureCount = 0;
                
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Capture a new frame directly
                        Process captureProcess = new Process();
                        captureProcess.StartInfo.FileName = "imagesnap";
                        captureProcess.StartInfo.Arguments = $"-d \"{selectedCamera}\" \"{_tempImagePath}\"";
                        captureProcess.StartInfo.UseShellExecute = false;
                        captureProcess.StartInfo.CreateNoWindow = true;
                        captureProcess.StartInfo.WorkingDirectory = System.IO.Path.GetTempPath();
                        captureProcess.Start();
                        
                        // Wait for imagesnap to finish
                        captureProcess.WaitForExit();
                        
                        // Check if the file was updated
                        if (System.IO.File.Exists(_tempImagePath))
                        {
                            var fileInfo = new System.IO.FileInfo(_tempImagePath);
                            if (fileInfo.Length > 0)
                            {
                                try
                                {
                                    using (System.IO.FileStream stream = new System.IO.FileStream(_tempImagePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                                    {
                                        if (stream.Length > 0)
                                        {
                                            Bitmap newFrame = new Bitmap(stream);
                                            
                                            lock (this)
                                            {
                                                if (_currentFrame != null)
                                                {
                                                    _currentFrame.Dispose();
                                                }
                                                _currentFrame = (Bitmap)newFrame.Clone();
                                                
                                                captureCount++;
                                                
                                                if (!_hasCapturedFrame)
                                                {
                                                    _hasCapturedFrame = true;
                                                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"First frame captured! Size: {_currentFrame.Width}x{_currentFrame.Height}");
                                                }
                                                else if (captureCount % 10 == 0)
                                                {
                                                    // Log progress occasionally
                                                    Debug.WriteLine($"Captured frame #{captureCount}");
                                                }
                                            }
                                            
                                            // Force UI update
                                            if (Grasshopper.Instances.ActiveCanvas != null)
                                            {
                                                Grasshopper.Instances.ActiveCanvas.Invoke(new Action(() => 
                                                {
                                                    Grasshopper.Instances.ActiveCanvas.Invalidate();
                                                }));
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // File might be in use, skip this frame
                                    Debug.WriteLine("Frame capture error: " + ex.Message);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Capture process error: " + ex.Message);
                    }
                    
                    // Sleep between frames
                    Thread.Sleep(_refreshInterval);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Capture error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Inner exception: {ex.InnerException.Message}");
                }
            }
            finally
            {
                // Clean up the process if needed
                if (_avfProcess != null && !_avfProcess.HasExited)
                {
                    try
                    {
                        _avfProcess.Kill();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error stopping capture process: {ex.Message}");
                    }
                }
            }
        }

        private void StopCamera()
        {
            if (_isRunning)
            {
                // Cancel the capture task
                _cancellationSource?.Cancel();
                
                // Kill the process if running
                if (_avfProcess != null && !_avfProcess.HasExited)
                {
                    try
                    {
                        _avfProcess.Kill();
                        _avfProcess = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Error stopping process: " + ex.Message);
                    }
                }
                
                // Reset flags
                _isRunning = false;
                _hasCapturedFrame = false;
                
                // Clean up temporary files
                try
                {
                    if (System.IO.File.Exists(_tempImagePath))
                    {
                        System.IO.File.Delete(_tempImagePath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error cleaning up temporary files: " + ex.Message);
                }
            }
        }

        public override void CreateAttributes()
        {
            m_attributes = new WebcamComponentAttributes(this);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            // Stop camera before serialization
            if (_isRunning)
            {
                StopCamera();
            }
            return base.Write(writer);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopCamera();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                StopCamera();
            }
            base.DocumentContextChanged(document, context);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("dfeb6f18-ec49-41b2-ace1-02ae334f5f1e"); }
        }
    }

    public class WebcamComponentAttributes : Grasshopper.Kernel.Attributes.GH_ComponentAttributes
    {
        private WebcamComponent Owner;
        private RectangleF PreviewBounds;
        private const int PreviewWidth = 320;  // Default width of preview
        private const int PreviewHeight = 240; // Default height of preview

        public WebcamComponentAttributes(WebcamComponent owner) : base(owner)
        {
            Owner = owner;
        }

        protected override void Layout()
        {
            base.Layout();

            // Define a rectangle for the video preview below the standard component UI
            Rectangle baseRectangle = GH_Convert.ToRectangle(Bounds);
            
            // Add space for the preview area
            baseRectangle.Height += PreviewHeight + 10;

            // Calculate the preview rectangle
            PreviewBounds = new RectangleF(
                baseRectangle.X,
                baseRectangle.Y + baseRectangle.Height - PreviewHeight - 5,
                PreviewWidth,
                PreviewHeight
            );

            Bounds = baseRectangle;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel == GH_CanvasChannel.Objects)
            {
                // Draw border for preview area
                graphics.DrawRectangle(Pens.DarkGray, PreviewBounds.X, PreviewBounds.Y, PreviewBounds.Width, PreviewBounds.Height);

                Bitmap frame = null;
                lock (Owner)
                {
                    if (Owner._currentFrame != null)
                    {
                        frame = Owner._currentFrame;
                        // Draw the video frame
                        graphics.DrawImage(frame, PreviewBounds);
                    }
                    else
                    {
                        // Draw a message when no video is available
                        StringFormat format = new StringFormat();
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        graphics.DrawString("No webcam feed available", GH_FontServer.Standard, Brushes.DarkGray, PreviewBounds, format);
                    }
                }
            }
        }
    }
}