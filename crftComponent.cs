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
        private readonly int _refreshInterval = 100; // in milliseconds - lower value may overload imagesnap

        /// <summary>
        /// Initializes a new instance of the WebcamComponent class.
        /// </summary>
        public WebcamComponent()
          : base("Webcam", "", // Empty name and nickname to avoid text display
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
            pManager.AddIntegerParameter("Device", "D", "Webcam device index (FaceTime=1, OBS=0)", GH_ParamAccess.item, 1);
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
                
                // First show all found cameras in runtime messages
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Available cameras:");
                int camIndex = 0;
                
                foreach (string line in lines)
                {
                    if (line.StartsWith("=>"))
                    {
                        string cameraName = line.Substring(3).Trim();
                        cameraNames.Add(cameraName);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Camera {camIndex}: {cameraName}");
                        camIndex++;
                    }
                }
                
                // Check if we have cameras and if deviceIndex is valid
                if (cameraNames.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No webcams found with imagesnap -l");
                    return;
                }
                
                // Find built-in webcam by name
                int builtInIndex = -1;
                for (int i = 0; i < cameraNames.Count; i++)
                {
                    string name = cameraNames[i].ToLower();
                    if (name.Contains("facetime") || name.Contains("built-in") || name.Contains("macbook") || name.Contains("integrated"))
                    {
                        builtInIndex = i;
                        break;
                    }
                }
                
                // Use deviceIndex from input if valid, otherwise try to use built-in camera
                if (_deviceIndex < 0 || _deviceIndex >= cameraNames.Count)
                {
                    if (builtInIndex >= 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Device index {_deviceIndex} out of range. Using built-in camera at index {builtInIndex}.");
                        _deviceIndex = builtInIndex;
                    }
                    else
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Device index {_deviceIndex} out of range and no built-in camera found. Using camera 0.");
                        _deviceIndex = 0;
                    }
                }
                
                // Map the logical device indexes to actual camera names
                // This ensures the user input (0, 1, 2) maps correctly to the device they want
                // List all available cameras for user reference
                string cameraList = "Available cameras:\n";
                for (int i = 0; i < cameraNames.Count; i++)
                {
                    cameraList += $"{i}: {cameraNames[i]}\n";
                }
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, cameraList);
                
                // Make sure device index is valid
                if (_deviceIndex < 0 || _deviceIndex >= cameraNames.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Device index {_deviceIndex} out of range. Using camera 0 instead.");
                    _deviceIndex = 0;
                }
                
                string selectedCamera = cameraNames[_deviceIndex];
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Using camera {_deviceIndex}: {selectedCamera}");
                
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
                        
                        // Force FaceTime camera by actual name when deviceIndex = 1
                        string deviceArg;
                        if (_deviceIndex == 1)
                        {
                            deviceArg = "-d \"Càmera FaceTime HD\"";
                        }
                        else if (_deviceIndex == 0)
                        {
                            deviceArg = "-d \"OBS Virtual Camera\"";
                        }
                        else if (_deviceIndex == 2)
                        {
                            deviceArg = "-d \"Càmera del dispositiu \\\"Jorgei Munious\\\"\"";
                        }
                        else
                        {
                            // Fallback to name-based selection
                            deviceArg = $"-d \"{selectedCamera}\"";
                        }
                        
                        captureProcess.StartInfo.Arguments = $"{deviceArg} \"{_tempImagePath}\"";
                        captureProcess.StartInfo.UseShellExecute = false;
                        captureProcess.StartInfo.CreateNoWindow = true;
                        captureProcess.StartInfo.WorkingDirectory = System.IO.Path.GetTempPath();
                        
                        // Debug output to see the full command
                        string fullCommand = $"imagesnap {captureProcess.StartInfo.Arguments}";
                        Debug.WriteLine($"Running: {fullCommand}");
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Command: {fullCommand}");
                        
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
                
                        // We'll keep the temp file to avoid disk I/O overhead
                // This helps with performance
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
        
        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            // This ensures the component has the standard context menu
        }
        
        public override GH_Exposure Exposure => GH_Exposure.primary;

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
        private new readonly WebcamComponent Owner;
        private RectangleF PreviewBounds;
        private RectangleF ComponentBounds;
        private const int PreviewWidth = 320;  // Preview width
        private const int PreviewHeight = 240; // Preview height

        public WebcamComponentAttributes(WebcamComponent owner) : base(owner)
        {
            Owner = owner;
        }

        protected override void Layout()
        {
            // First calculate the standard component layout
            base.Layout();
            
            // Get the standard component bounds
            Rectangle baseRectangle = GH_Convert.ToRectangle(Bounds);
            
            // Remember the original component area for parameter positioning
            ComponentBounds = new RectangleF(
                baseRectangle.X, 
                baseRectangle.Y, 
                baseRectangle.Width, 
                baseRectangle.Height
            );
            
            // Determine width to fit the preview centered
            int totalWidth = Math.Max(baseRectangle.Width, PreviewWidth + 20);
            
            // Make the component wider if needed to fit the preview
            if (totalWidth > baseRectangle.Width)
            {
                baseRectangle.Width = totalWidth;
            }
            
            // Add space for the preview below the standard component UI
            baseRectangle.Height += PreviewHeight + 10;
            
            // Calculate preview rectangle - centered horizontally
            PreviewBounds = new RectangleF(
                baseRectangle.X + (baseRectangle.Width - PreviewWidth) / 2, // Center horizontally
                ComponentBounds.Bottom + 5, // Position below component area
                PreviewWidth,
                PreviewHeight
            );
            
            Bounds = baseRectangle;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                // For objects channel (main component rendering)
                base.Render(canvas, graphics, channel);
                
                // Draw border for preview area
                graphics.DrawRectangle(Pens.DarkGray, PreviewBounds.X, PreviewBounds.Y, PreviewBounds.Width, PreviewBounds.Height);
                
                // Draw the video frame
                Bitmap frame = null;
                lock (Owner)
                {
                    if (Owner._currentFrame != null)
                    {
                        frame = Owner._currentFrame;
                        
                        // Calculate proportional dimensions to maintain aspect ratio
                        float frameRatio = (float)frame.Width / frame.Height;
                        float previewRatio = PreviewBounds.Width / PreviewBounds.Height;
                        
                        RectangleF targetRect;
                        
                        if (frameRatio > previewRatio)
                        {
                            // Image is wider than preview area - fit to width
                            float targetHeight = PreviewBounds.Width / frameRatio;
                            float yOffset = (PreviewBounds.Height - targetHeight) / 2;
                            targetRect = new RectangleF(
                                PreviewBounds.X,
                                PreviewBounds.Y + yOffset,
                                PreviewBounds.Width,
                                targetHeight
                            );
                        }
                        else
                        {
                            // Image is taller than preview area - fit to height
                            float targetWidth = PreviewBounds.Height * frameRatio;
                            float xOffset = (PreviewBounds.Width - targetWidth) / 2;
                            targetRect = new RectangleF(
                                PreviewBounds.X + xOffset,
                                PreviewBounds.Y,
                                targetWidth,
                                PreviewBounds.Height
                            );
                        }
                        
                        // Draw the video frame with proper aspect ratio
                        graphics.DrawImage(frame, targetRect);
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
                
                // No component name text - removed
            }
            else
            {
                // For all other channels, use default rendering
                base.Render(canvas, graphics, channel);
            }
        }
    }
}