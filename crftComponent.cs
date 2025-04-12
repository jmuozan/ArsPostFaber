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
        private readonly int _refreshInterval = 100; // in milliseconds

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
                // Create a shell script file that will capture webcam images
                string scriptPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), 
                    "capture_" + Guid.NewGuid().ToString() + ".sh");

                // For debugging
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Script path: {scriptPath}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Temporary image path: {_tempImagePath}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Device index: {_deviceIndex}");

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
                
                string scriptContent = @"#!/bin/bash
CAMERA_NAME=""" + selectedCamera.Replace("\"", "\\\"") + @"""
OUTPUT_PATH=""" + _tempImagePath + @"""

echo ""Capturing from camera: $CAMERA_NAME to $OUTPUT_PATH""

# Start the continuous capture
while true; do
    # Use imagesnap with camera name
    imagesnap -d ""$CAMERA_NAME"" -w 0.1 $OUTPUT_PATH
    sleep 0.1
done";

                System.IO.File.WriteAllText(scriptPath, scriptContent);
                
                // Make the script executable
                Process chmodProcess = new Process();
                chmodProcess.StartInfo.FileName = "chmod";
                chmodProcess.StartInfo.Arguments = $"+x {scriptPath}";
                chmodProcess.StartInfo.UseShellExecute = false;
                chmodProcess.StartInfo.CreateNoWindow = true;
                chmodProcess.Start();
                chmodProcess.WaitForExit();

                // First, check if imagesnap is installed
                Process checkImagesnap = new Process();
                checkImagesnap.StartInfo.FileName = "which";
                checkImagesnap.StartInfo.Arguments = "imagesnap";
                checkImagesnap.StartInfo.UseShellExecute = false;
                checkImagesnap.StartInfo.RedirectStandardOutput = true;
                checkImagesnap.StartInfo.CreateNoWindow = true;
                checkImagesnap.Start();
                string imagesnap = checkImagesnap.StandardOutput.ReadToEnd().Trim();
                checkImagesnap.WaitForExit();

                if (string.IsNullOrEmpty(imagesnap))
                {
                    // Install imagesnap
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Installing imagesnap...");
                    Process brewProcess = new Process();
                    brewProcess.StartInfo.FileName = "brew";
                    brewProcess.StartInfo.Arguments = "install imagesnap";
                    brewProcess.StartInfo.UseShellExecute = false;
                    brewProcess.StartInfo.CreateNoWindow = true;
                    brewProcess.Start();
                    brewProcess.WaitForExit();
                }

                // Start the capture script
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Running script directly with bash...");
                
                _avfProcess = new Process();
                _avfProcess.StartInfo.FileName = "bash";
                _avfProcess.StartInfo.Arguments = scriptPath;
                _avfProcess.StartInfo.UseShellExecute = false;
                _avfProcess.StartInfo.CreateNoWindow = true;
                _avfProcess.StartInfo.WorkingDirectory = System.IO.Path.GetTempPath(); // Set working directory to temp
                _avfProcess.StartInfo.RedirectStandardOutput = true;
                _avfProcess.StartInfo.RedirectStandardError = true;
                _avfProcess.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Process output: " + e.Data);
                };
                _avfProcess.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Process error: " + e.Data);
                };
                
                try
                {
                    _avfProcess.Start();
                    _avfProcess.BeginOutputReadLine();
                    _avfProcess.BeginErrorReadLine();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Process started successfully");
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to start process: " + ex.Message);
                    if (ex.InnerException != null)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Inner exception: " + ex.InnerException.Message);
                }
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Started capture process");

                // Take a first capture directly to see if it works
                try {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Taking a test capture...");
                    
                    Process testCapture = new Process();
                    testCapture.StartInfo.FileName = "imagesnap";
                    testCapture.StartInfo.Arguments = $"-d \"{selectedCamera}\" \"{_tempImagePath}\"";
                    testCapture.StartInfo.UseShellExecute = false;
                    testCapture.StartInfo.CreateNoWindow = true;
                    testCapture.StartInfo.WorkingDirectory = System.IO.Path.GetTempPath();
                    testCapture.Start();
                    testCapture.WaitForExit();
                    
                    if (testCapture.ExitCode == 0) {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Test capture successful!");
                    } else {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Test capture exited with code: " + testCapture.ExitCode);
                    }
                } catch (Exception ex) {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Test capture failed: " + ex.Message);
                }
                
                // Monitor for new frames
                while (!token.IsCancellationRequested)
                {
                    if (System.IO.File.Exists(_tempImagePath))
                    {
                        try
                        {
                            // Add debug information about file
                            var fileInfo = new System.IO.FileInfo(_tempImagePath);
                            if (fileInfo.Length > 0)
                            {
                                // Load the image
                                using (System.IO.FileStream stream = new System.IO.FileStream(_tempImagePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                                {
                                    if (stream.Length > 0) // Make sure we have data
                                    {
                                        // Create a new bitmap from the file
                                        Bitmap newFrame = new Bitmap(stream);
                                        
                                        // Update the current frame
                                        lock (this)
                                        {
                                            if (_currentFrame != null)
                                            {
                                                _currentFrame.Dispose();
                                            }
                                            _currentFrame = (Bitmap)newFrame.Clone();
                                            
                                            // Log first successful capture
                                            if (!_hasCapturedFrame)
                                            {
                                                _hasCapturedFrame = true;
                                                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"First frame captured successfully! Size: {_currentFrame.Width}x{_currentFrame.Height}");
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
                        }
                        catch (Exception ex)
                        {
                            // File might be in use, skip this frame
                            Debug.WriteLine("Frame capture error: " + ex.Message);
                        }
                    }

                    // Sleep before the next capture
                    Thread.Sleep(_refreshInterval);
                }

                // Clean up
                if (_avfProcess != null && !_avfProcess.HasExited)
                {
                    _avfProcess.Kill();
                }

                // Delete temporary files
                try
                {
                    if (System.IO.File.Exists(scriptPath))
                    {
                        System.IO.File.Delete(scriptPath);
                    }
                    if (System.IO.File.Exists(_tempImagePath))
                    {
                        System.IO.File.Delete(_tempImagePath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error cleaning up: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Capture error: " + ex.Message);
            }
        }

        private void StopCamera()
        {
            if (_isRunning)
            {
                // Cancel the capture task
                _cancellationSource?.Cancel();
                
                // Kill the process on macOS
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
                
                _isRunning = false;
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