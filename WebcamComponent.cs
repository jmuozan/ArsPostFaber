using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.GUI.Canvas;
using Rhino.Geometry;
using System.IO;
using System.Linq;
using System.Text;

namespace crft
{
    // Custom double-buffered PictureBox with improved rendering and error handling
    public class BufferedPictureBox : PictureBox
    {
        private bool _disposing = false;
        
        public BufferedPictureBox()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                          ControlStyles.AllPaintingInWmPaint | 
                          ControlStyles.UserPaint, true);
            
            // Force refresh whenever the Image property changes
            this.ImageChanged += OnImageChanged;
        }
        
        private void OnImageChanged(object sender, EventArgs e)
        {
            // Don't invalidate if we're in the process of disposing
            if (!_disposing && !this.IsDisposed && this.IsHandleCreated)
            {
                // Ensure control is refreshed when the image changes
                try
                {
                    this.Invalidate();
                }
                catch (Exception ex)
                {
                    // Just log and continue if invalidate fails
                    Debug.WriteLine($"Error invalidating PictureBox: {ex.Message}");
                }
            }
        }
        
        // We need to add this event to detect image changes
        private EventHandler _imageChanged;
        public event EventHandler ImageChanged
        {
            add 
            { 
                // Safe event attachment
                if (_imageChanged == null)
                    _imageChanged = value;
                else
                    _imageChanged += value; 
            }
            remove 
            { 
                // Safe event detachment
                _imageChanged -= value; 
            }
        }
        
        // Override the Image property to raise the ImageChanged event
        public new Image Image
        {
            get 
            { 
                // Safe getter
                try
                {
                    if (this.IsDisposed || _disposing)
                        return null;
                        
                    return base.Image;
                }
                catch
                {
                    return null; // Return null if any exception occurs
                }
            }
            set
            {
                // Skip if disposing or disposed
                if (_disposing || this.IsDisposed)
                    return;
                    
                try
                {
                    // Store old image for proper disposal
                    Image oldImage = base.Image;
                    
                    // Only update if different
                    if (oldImage != value)
                    {
                        // Set the new image first
                        base.Image = value;
                        
                        // Notify event subscribers (if any)
                        if (!_disposing && !this.IsDisposed)
                            _imageChanged?.Invoke(this, EventArgs.Empty);
                            
                        // Dispose old image if applicable
                        if (oldImage != null && oldImage != value)
                        {
                            try 
                            { 
                                oldImage.Dispose(); 
                            }
                            catch 
                            { 
                                // Ignore errors in disposal
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting PictureBox.Image: {ex.Message}");
                }
            }
        }
        
        // Override OnPaint to ensure the image is always repainted
        protected override void OnPaint(PaintEventArgs pe)
        {
            if (this.IsDisposed || _disposing || pe == null || pe.Graphics == null)
                return;
                
            try
            {
                base.OnPaint(pe);
                
                // Only attempt to draw if we have a valid image
                if (this.Image != null)
                {
                    // Force refresh of the entire control area
                    pe.Graphics.Clear(this.BackColor);
                    
                    // Draw the image with high quality
                    pe.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    pe.Graphics.DrawImage(this.Image, this.ClientRectangle);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in BufferedPictureBox.OnPaint: {ex.Message}");
            }
        }
        
        // Override Dispose to ensure proper cleanup
        protected override void Dispose(bool disposing)
        {
            // Set flag to avoid event raising during disposal
            _disposing = true;
            
            // Dispose the image first to avoid null reference issues
            if (disposing)
            {
                try
                {
                    if (base.Image != null)
                    {
                        Image oldImage = base.Image;
                        base.Image = null; // Clear the reference first
                        oldImage.Dispose(); // Then dispose the image
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing PictureBox image: {ex.Message}");
                }
            }
            
            // Call base implementation
            base.Dispose(disposing);
        }
    }
    
    public class WebcamComponent : GH_Component
    {
        internal Bitmap _currentFrame;
        private bool _isRunning = false;
        private bool _hasCapturedFrame = false;
        internal int _deviceIndex = 0;
        private CancellationTokenSource _cancellationSource;
        private Task _captureTask;
        private Process _avfProcess;
        private string _tempImagePath;
        private int _refreshInterval = 33; // ~30 fps
        internal readonly object _lockObject = new object();
        
        // Recording
        private bool _isRecording = false;
        private string _recordingPath = null;
        private string _videoOutputPath = null;
        private Process _ffmpegRecordProcess = null;
        
        // Available cameras
        internal List<string> _detectedCameras = new List<string>();
        
        // Form
        private WebcamViewer _webcamForm = null;

        private bool _previousEnableState = false;
        
        // For UI update throttling - using a shorter interval for better responsiveness
        private DateTime _lastUIUpdate = DateTime.MinValue;
        private const int UI_UPDATE_INTERVAL_MS = 16; // ~60fps for UI updates

        private string _videoName = "webcam";
        
        public WebcamComponent()
          : base("Webcam", "Webcam", 
              "Captures and displays webcam video feed with recording capability",
              "Display", "Preview")
        {
            // Use unique ID for temporary frame path
            _tempImagePath = Path.Combine(Path.GetTempPath(), 
                "gh_webcam_" + Guid.NewGuid().ToString() + ".jpg");
                
            // Use the same directory structure as SAM for output
            // Format: /tmp/frames_{name}
            _videoName = "webcam_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            
            // Use the same directory structure as SAM
            _recordingPath = Path.Combine("/tmp", $"frames_{_videoName}");
            
            // Generate a video file output path with .mp4 extension in the same location
            _videoOutputPath = Path.Combine(_recordingPath, $"{_videoName}.mp4");
            
            if (!Directory.Exists(_recordingPath))
            {
                Directory.CreateDirectory(_recordingPath);
            }
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Enable", "E", "Enable/disable webcam", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Device", "D", "Webcam device index", GH_ParamAccess.item, 1);
            pManager.AddTextParameter("Name", "N", "Video name prefix (used for SAM compatibility)", GH_ParamAccess.item, "webcam");
            // Optional parameter
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Image", "I", "Current webcam frame", GH_ParamAccess.item);
            pManager.AddTextParameter("VideoPath", "V", "Path to recorded video file (.mp4)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DiagnoseWebcamState();
            bool enable = false;
            int deviceIndex = 0;
            string videoName = "";

            if (!DA.GetData(0, ref enable)) return;
            if (!DA.GetData(1, ref deviceIndex)) return;
            
            // Get optional video name parameter for SAM compatibility
            if (DA.GetData(2, ref videoName) && !string.IsNullOrEmpty(videoName))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string previousName = _videoName;
                
                // Clean up the name to be file-system friendly
                string safeName = string.Join("_", videoName.Split(Path.GetInvalidFileNameChars()));
                _videoName = safeName;
                
                // Only update paths if name has changed
                if (_videoName != previousName.Split('_')[0])
                {
                    // Update recording and video paths using the new name
                    _recordingPath = Path.Combine("/tmp", $"frames_{_videoName}");
                    _videoOutputPath = Path.Combine(_recordingPath, $"{_videoName}_{timestamp}.mp4");
                    
                    // Create directory if it doesn't exist
                    if (!Directory.Exists(_recordingPath))
                    {
                        try 
                        { 
                            Directory.CreateDirectory(_recordingPath); 
                        }
                        catch (Exception ex)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                                $"Could not create recording directory: {ex.Message}");
                        }
                    }
                    
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"Set video name to: {_videoName}");
                }
            }
            
            // Only update state if the enable value actually changed
            bool stateChanged = (enable != _previousEnableState);
            _previousEnableState = enable;
            
            // Scan for cameras when needed
            if (_detectedCameras.Count == 0)
            {
                ScanForCameras();
            }
            
            // Store the device index immediately to ensure it's used correctly
            _deviceIndex = deviceIndex;
            
            // Report camera info even when not enabled
            ReportCameraInfo(deviceIndex);
            
            // Always respond to enable state
            if (enable && !_isRunning)
            {
                // Delete previous recording if it exists
                if (_videoOutputPath != null && File.Exists(_videoOutputPath))
                {
                    try
                    {
                        File.Delete(_videoOutputPath);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Deleted previous recording");
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                            $"Could not delete previous recording: {ex.Message}");
                    }
                }
                
                StartCamera();
                ShowWebcamWindow();
            }
            else if (!enable && _isRunning)
            {
                // If we're recording when disabled, stop the recording first
                if (_isRecording)
                {
                    try
                    {
                        // Call ToggleRecording to stop recording cleanly
                        ToggleRecording();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error stopping recording on disable: {ex.Message}");
                    }
                }
                
                // Stop the camera and close the window
                StopCamera();
                CloseWebcamWindow();
                
                // Display the state change message
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    "Camera disabled. Video path is preserved for output.");
            }

            // Handle outputs based on component state
            if (enable && _currentFrame != null)
            {
                try
                {
                    Bitmap frameCopy;
                    lock (_lockObject)
                    {
                        frameCopy = (Bitmap)_currentFrame.Clone();
                    }
                    
                    DA.SetData(0, frameCopy);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
                }
            }
            else
            {
                // Send placeholder when disabled but only if the user is expecting output data
                if (enable)
                {
                    Bitmap placeholder = new Bitmap(320, 240);
                    using (Graphics g = Graphics.FromImage(placeholder))
                    {
                        g.Clear(Color.Black);
                        using (Font font = new Font("Arial", 10))
                        {
                            g.DrawString("Camera initializing...", font, Brushes.White, new PointF(80, 110));
                        }
                    }
                    DA.SetData(0, placeholder);
                }
            }
            
            // Always output the video path if we have a valid recorded file, regardless of current enable state
            if (_videoOutputPath != null && File.Exists(_videoOutputPath))
            {
                DA.SetData(1, _videoOutputPath);
            }
        }

        private void ReportCameraInfo(int deviceIndex)
        {
            if (_detectedCameras.Count > 0)
            {
                if (deviceIndex >= 0 && deviceIndex < _detectedCameras.Count)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"Selected camera (index {deviceIndex}): {_detectedCameras[deviceIndex]}");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                        $"Device index {deviceIndex} is out of range. {_detectedCameras.Count} cameras detected.");
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No cameras detected.");
            }
        }

        private void ScanForCameras()
        {
            _detectedCameras.Clear();
            
            try
            {
                Process listDevices = new Process();
                listDevices.StartInfo.FileName = "imagesnap";
                listDevices.StartInfo.Arguments = "-l";
                listDevices.StartInfo.UseShellExecute = false;
                listDevices.StartInfo.RedirectStandardOutput = true;
                listDevices.StartInfo.CreateNoWindow = true;
                
                listDevices.Start();
                string deviceList = listDevices.StandardOutput.ReadToEnd();
                listDevices.WaitForExit();
                
                // Log all available cameras for debugging
                Debug.WriteLine("Available cameras from imagesnap -l:");
                Debug.WriteLine(deviceList);
                
                // Extract camera names from output
                string[] lines = deviceList.Split('\n');
                int cameraIndex = 0;
                
                foreach (string line in lines)
                {
                    if (line.StartsWith("=>"))
                    {
                        string cameraName = line.Substring(3).Trim();
                        _detectedCameras.Add(cameraName);
                        
                        // Log each camera with its index
                        Debug.WriteLine($"Camera {cameraIndex}: {cameraName}");
                        cameraIndex++;
                    }
                }
                
                // Log the number of cameras detected
                Debug.WriteLine($"Total cameras detected: {_detectedCameras.Count}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error scanning for cameras: {ex.Message}");
            }
        }

        private void ShowWebcamWindow()
        {
            try
            {
                if (Grasshopper.Instances.ActiveCanvas != null)
                {
                    Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() =>
                    {
                        if (_webcamForm == null || _webcamForm.IsDisposed)
                        {
                            _webcamForm = new WebcamViewer(this);
                            _webcamForm.Show();
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error creating webcam window: {ex.Message}");
            }
        }
        
        private void CloseWebcamWindow()
        {
            try
            {
                if (_webcamForm != null && !_webcamForm.IsDisposed)
                {
                    if (Grasshopper.Instances.ActiveCanvas != null)
                    {
                        Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() =>
                        {
                            try
                            {
                                // Close the form and mark it as null
                                _webcamForm.Close();
                                _webcamForm.Dispose(); // Ensure full disposal
                                _webcamForm = null;
                                
                                // Force a UI update to ensure Grasshopper updates
                                if (OnPingDocument() != null)
                                {
                                    OnPingDocument().NewSolution(false);
                                }
                                
                                Debug.WriteLine("Webcam window closed successfully");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error in UI thread closing webcam form: {ex.Message}");
                            }
                        }));
                    }
                    else
                    {
                        // If no canvas is available, try to close directly
                        _webcamForm.Close();
                        _webcamForm.Dispose();
                        _webcamForm = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing webcam form: {ex.Message}");
            }
        }

        // Creates the directory structure expected by the SAM component
        private void CreateSAMCompatibleDirectories()
        {
            try
            {
                // Create main frames directory
                if (!Directory.Exists(_recordingPath))
                {
                    Directory.CreateDirectory(_recordingPath);
                }
                
                // Create segmentation_output/masks directory
                string segmentationDir = Path.Combine(_recordingPath, "segmentation_output");
                string masksDir = Path.Combine(segmentationDir, "masks");
                if (!Directory.Exists(masksDir))
                {
                    Directory.CreateDirectory(masksDir);
                }
                
                // Create masking_output directory
                string maskingDir = Path.Combine(_recordingPath, "masking_output");
                if (!Directory.Exists(maskingDir))
                {
                    Directory.CreateDirectory(maskingDir);
                }
                
                // Create reconstruction directory
                string reconstructionDir = Path.Combine(_recordingPath, "reconstruction");
                if (!Directory.Exists(reconstructionDir))
                {
                    Directory.CreateDirectory(reconstructionDir);
                }
                
                Debug.WriteLine($"Created SAM-compatible directory structure in {_recordingPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating SAM-compatible directories: {ex.Message}");
            }
        }

        private void DiagnoseWebcamState()
        {
            StringBuilder diagnostic = new StringBuilder();
            diagnostic.AppendLine("=== Webcam Component Diagnostic ===");
            diagnostic.AppendLine($"Is Running: {_isRunning}");
            diagnostic.AppendLine($"Has Captured Frame: {_hasCapturedFrame}");
            diagnostic.AppendLine($"Device Index: {_deviceIndex}");
            diagnostic.AppendLine($"Current Frame Null: {_currentFrame == null}");
            diagnostic.AppendLine($"Video Name: {_videoName}");
            diagnostic.AppendLine($"Recording Path: {_recordingPath}");
            diagnostic.AppendLine($"Video Output Path: {_videoOutputPath}");
            
            if (_currentFrame != null)
            {
                diagnostic.AppendLine($"Frame Dimensions: {_currentFrame.Width}x{_currentFrame.Height}");
            }
            
            diagnostic.AppendLine($"Temp Image Path: {_tempImagePath}");
            diagnostic.AppendLine($"Temp Image Exists: {File.Exists(_tempImagePath)}");
            
            if (File.Exists(_tempImagePath))
            {
                FileInfo info = new FileInfo(_tempImagePath);
                diagnostic.AppendLine($"Temp Image Size: {info.Length} bytes");
                diagnostic.AppendLine($"Temp Image Last Write: {info.LastWriteTime}");
            }
            
            diagnostic.AppendLine($"Detected Cameras: {_detectedCameras.Count}");
            for (int i = 0; i < _detectedCameras.Count; i++)
            {
                diagnostic.AppendLine($"  {i}: {_detectedCameras[i]}");
            }
            
            Debug.WriteLine(diagnostic.ToString());
            
            // Add summary to runtime messages
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                $"Diagnostics: Running={_isRunning}, HasFrame={_hasCapturedFrame}, " +
                $"FrameExists={_currentFrame != null}, VideoPath={_videoOutputPath}");
        }
        private void StartCamera()
        {
            try
            {
                // Add debug message
                Debug.WriteLine("Starting camera capture...");
                
                // Generate a fresh temp path for this capture session
                _tempImagePath = Path.Combine(Path.GetTempPath(), 
                    "gh_webcam_" + Guid.NewGuid().ToString() + ".jpg");
                    
                Debug.WriteLine($"Using temp image path: {_tempImagePath}");
                
                _cancellationSource = new CancellationTokenSource();
                _captureTask = Task.Run(() => CaptureLoop(_cancellationSource.Token));
                _isRunning = true;
                
                Debug.WriteLine("Camera capture started successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in StartCamera: {ex.Message}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error starting camera: {ex.Message}");
            }
        }

        private void CaptureLoop(CancellationToken token)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                MacOSCaptureLoop(token);
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                    "This component currently only supports macOS");
            }
        }
        

        private bool CheckRequiredTools()
        {
            try
            {
                // Check for ffmpeg
                Process ffmpegCheck = new Process();
                ffmpegCheck.StartInfo.FileName = "which";
                ffmpegCheck.StartInfo.Arguments = "ffmpeg";
                ffmpegCheck.StartInfo.UseShellExecute = false;
                ffmpegCheck.StartInfo.RedirectStandardOutput = true;
                ffmpegCheck.StartInfo.CreateNoWindow = true;
                
                ffmpegCheck.Start();
                string ffmpegPath = ffmpegCheck.StandardOutput.ReadToEnd().Trim();
                ffmpegCheck.WaitForExit();
                
                // Check for imagesnap
                Process imagesnapCheck = new Process();
                imagesnapCheck.StartInfo.FileName = "which";
                imagesnapCheck.StartInfo.Arguments = "imagesnap";
                imagesnapCheck.StartInfo.UseShellExecute = false;
                imagesnapCheck.StartInfo.RedirectStandardOutput = true;
                imagesnapCheck.StartInfo.CreateNoWindow = true;
                
                imagesnapCheck.Start();
                string imagesnapPath = imagesnapCheck.StandardOutput.ReadToEnd().Trim();
                imagesnapCheck.WaitForExit();
                
                if (string.IsNullOrEmpty(ffmpegPath) || string.IsNullOrEmpty(imagesnapPath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Required tools not found. ffmpeg: {!string.IsNullOrEmpty(ffmpegPath)}, imagesnap: {!string.IsNullOrEmpty(imagesnapPath)}");
                    return false;
                }
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Required tools found.");
                return true;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error checking tools: {ex.Message}");
                return false;
            }
        }

        private void MacOSCaptureLoop(CancellationToken token)
        {
            try
            {
                // Check required tools first
                if (!CheckRequiredTools())
                {
                    return;
                }
                
                if (_detectedCameras.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No webcams found");
                    return;
                }
                
                // Valid device index check
                if (_deviceIndex < 0 || _deviceIndex >= _detectedCameras.Count)
                {
                    // Try to find FaceTime camera
                    int builtInIndex = _detectedCameras
                        .FindIndex(n => n.ToLower().Contains("facetime"));
                        
                    if (builtInIndex >= 0)
                    {
                        _deviceIndex = builtInIndex;
                    }
                    else
                    {
                        _deviceIndex = 0; // Default to first camera
                    }
                }
                
                string selectedCamera = _detectedCameras[_deviceIndex];
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"Using camera {_deviceIndex}: {selectedCamera}");
                
                // Set up faster refresh rate for better video smoothness
                _refreshInterval = 16; // 60fps target
                
                // Use ffmpeg in continuous streaming mode for better performance
                Process streamProcess = null;
                
                try
                {
                    // Start a continuous stream with ffmpeg
                    streamProcess = new Process();
                    streamProcess.StartInfo.FileName = "ffmpeg";
                    
                    // Target a specific resolution to avoid size issues
                    streamProcess.StartInfo.Arguments = $"-f avfoundation -framerate 30 " +
                                                      $"-video_size 640x480 " + // Set specific size constraint
                                                      $"-i \"{_deviceIndex}\" " +
                                                      $"-f image2 -update 1 -y \"{_tempImagePath}\"";
                    
                    streamProcess.StartInfo.UseShellExecute = false;
                    streamProcess.StartInfo.CreateNoWindow = true;
                    streamProcess.StartInfo.RedirectStandardError = true;
                    
                    Debug.WriteLine($"Starting FFMPEG with: {streamProcess.StartInfo.Arguments}");
                    
                    // Start the streaming process
                    streamProcess.Start();
                    _avfProcess = streamProcess; // Store for cleanup later
                    
                    // Short wait for process to start
                    Thread.Sleep(500);
                    
                    // Verify initial success - this is important for live view
                    if (!File.Exists(_tempImagePath))
                    {
                        // If ffmpeg fails, try imagesnap as fallback
                        Process initialCapture = new Process();
                        initialCapture.StartInfo.FileName = "imagesnap";
                        initialCapture.StartInfo.Arguments = $"-d \"{selectedCamera}\" -w 640 -h 480 \"{_tempImagePath}\"";
                        initialCapture.StartInfo.UseShellExecute = false;
                        initialCapture.StartInfo.CreateNoWindow = true;
                        
                        initialCapture.Start();
                        initialCapture.WaitForExit();
                    }
                    
                    // Main capture loop - poll the continuously updated file
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            // Check if ffmpeg is still running
                            if (streamProcess.HasExited)
                            {
                                Debug.WriteLine("FFMPEG process exited unexpectedly, restarting...");
                                break;
                            }
                            
                            // Process the current image if it exists
                            if (File.Exists(_tempImagePath))
                            {
                                var fileInfo = new FileInfo(_tempImagePath);
                                if (fileInfo.Length > 0)
                                {
                                    // Use a short file access timeout to avoid blocking
                                    try
                                    {
                                        using (FileStream stream = new FileStream(
                                            _tempImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                        {
                                            Bitmap newFrame = new Bitmap(stream);
                                            
                                            // Store current frame for component output
                                            lock (_lockObject)
                                            {
                                                if (_currentFrame != null)
                                                {
                                                    _currentFrame.Dispose();
                                                }
                                                _currentFrame = (Bitmap)newFrame.Clone();
                                                _hasCapturedFrame = true;
                                            }
                                            
                                            // CRITICAL: Update UI directly with frame for live view
                                            UpdateWebcamUI(newFrame);
                                            
                                            // Update component at reduced rate
                                            TimeSpan timeSinceLastUpdate = DateTime.Now - _lastUIUpdate;
                                            if (timeSinceLastUpdate.TotalMilliseconds >= UI_UPDATE_INTERVAL_MS * 2) // Reduce component updates
                                            {
                                                _lastUIUpdate = DateTime.Now;
                                                ExpireSolution(true);
                                            }
                                        }
                                    }
                                    catch (IOException)
                                    {
                                        // File might be being written to, just skip and try next time
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Frame processing error: {ex.Message}");
                        }
                        
                        // Short sleep for high-frequency polling
                        Thread.Sleep(_refreshInterval);
                    }
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                        $"Streaming error: {ex.Message}. Falling back to individual capture mode.");
                    // Fall back to individual captures
                    SnapshotCaptureLoop(token, selectedCamera);
                }
                finally
                {
                    // Clean up the streaming process
                    if (streamProcess != null && !streamProcess.HasExited)
                    {
                        try { streamProcess.Kill(); } catch { /* ignore */ }
                    }
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Capture loop error: {ex.Message}");
            }
        }
        
        // Fallback method using imagesnap for individual frames
        private void SnapshotCaptureLoop(CancellationToken token, string selectedCamera)
        {
            // Set more aggressive timing for snapshots to improve live view experience
            int snapshotInterval = 50; // 20fps target for fallback mode
            
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Determine device argument
                    string deviceArg = $"-d \"{selectedCamera}\"";
                    
                    // Add size constraint to prevent window sizing issues
                    string sizeArg = "-w 480 -h 360"; // Force smaller size for better performance
                    
                    // Choose save path
                    string currentFramePath = _tempImagePath;
                    
                    // Capture frame with size constraints
                    Process captureProcess = new Process();
                    captureProcess.StartInfo.FileName = "imagesnap";
                    captureProcess.StartInfo.Arguments = $"{deviceArg} {sizeArg} \"{currentFramePath}\"";
                    captureProcess.StartInfo.UseShellExecute = false;
                    captureProcess.StartInfo.CreateNoWindow = true;
                    
                    // Start capture with timeout
                    captureProcess.Start();
                    bool completed = captureProcess.WaitForExit(500); // 500ms timeout
                    
                    if (!completed)
                    {
                        // If it takes too long, kill the process and retry
                        try { captureProcess.Kill(); } catch { /* ignore */ }
                        Thread.Sleep(snapshotInterval);
                        continue;
                    }
                    
                    // Process capture
                    if (File.Exists(currentFramePath))
                    {
                        var fileInfo = new FileInfo(currentFramePath);
                        if (fileInfo.Length > 0)
                        {
                            using (FileStream stream = new FileStream(
                                currentFramePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                Bitmap newFrame = new Bitmap(stream);
                                
                                // Store frame for component
                                lock (_lockObject)
                                {
                                    if (_currentFrame != null)
                                    {
                                        _currentFrame.Dispose();
                                    }
                                    _currentFrame = (Bitmap)newFrame.Clone();
                                    _hasCapturedFrame = true;
                                }
                                
                                // CRITICAL: Always update the webcam preview window immediately
                                UpdateWebcamUI(newFrame);
                                
                                // Only update Grasshopper component at controlled rate
                                TimeSpan timeSinceLastUpdate = DateTime.Now - _lastUIUpdate;
                                if (timeSinceLastUpdate.TotalMilliseconds >= UI_UPDATE_INTERVAL_MS * 2)
                                {
                                    _lastUIUpdate = DateTime.Now;
                                    ExpireSolution(true);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Frame capture error: {ex.Message}");
                }
                
                // Use shorter sleep time for more responsive preview
                Thread.Sleep(snapshotInterval);
            }
        }
        
        private void UpdateWebcamUI(Bitmap frame)
        {
            try
            {
                // Make a defensive check to avoid null reference issues
                if (frame == null)
                {
                    Debug.WriteLine("UpdateWebcamUI: Skipping update due to null frame");
                    return;
                }
                
                if (_webcamForm == null)
                {
                    Debug.WriteLine("UpdateWebcamUI: Skipping update due to null webcam form");
                    return;
                }
                
                if (_webcamForm.IsDisposed)
                {
                    Debug.WriteLine("UpdateWebcamUI: Skipping update due to disposed webcam form");
                    return;
                }
                
                if (!_isRunning)
                {
                    Debug.WriteLine("UpdateWebcamUI: Skipping update because camera is not running");
                    return;
                }
                
                // CRITICAL: Pass the frame directly to the UI without an intermediate clone
                // The UpdateImage method will handle cloning and resizing
                _webcamForm.UpdateImage(frame);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating UI: {ex.Message}");
            }
        }

        public void StopCamera()
        {
            Debug.WriteLine("StopCamera called");
            
            // Always attempt cleanup regardless of _isRunning state for safety
            try
            {
                // Cancel the token source if it exists
                if (_cancellationSource != null)
                {
                    try 
                    { 
                        _cancellationSource.Cancel(); 
                        _cancellationSource.Dispose();
                        _cancellationSource = null;
                    }
                    catch (Exception ex) 
                    { 
                        Debug.WriteLine($"Error cancelling token: {ex.Message}"); 
                    }
                }
                
                // Stop the camera process if running
                if (_avfProcess != null)
                {
                    try 
                    { 
                        if (!_avfProcess.HasExited)
                        {
                            _avfProcess.Kill(); 
                            _avfProcess.WaitForExit(1000);
                        }
                        _avfProcess.Dispose(); 
                    }
                    catch (Exception ex) 
                    { 
                        Debug.WriteLine($"Error killing camera process: {ex.Message}"); 
                    }
                    finally
                    {
                        _avfProcess = null;
                    }
                }
                
                // Handle recording cleanup if we're recording
                if (_isRecording)
                {
                    Debug.WriteLine("Stopping recording");
                    
                    // Stop the recording process if it exists
                    if (_ffmpegRecordProcess != null)
                    {
                        try 
                        { 
                            if (!_ffmpegRecordProcess.HasExited)
                            {
                                // Send 'q' to ffmpeg to ensure a clean exit
                                try
                                {
                                    _ffmpegRecordProcess.StandardInput.Write("q");
                                    _ffmpegRecordProcess.StandardInput.Flush();
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error sending 'q' to ffmpeg: {ex.Message}");
                                }
                                
                                // Wait for the process to gracefully exit
                                bool exited = _ffmpegRecordProcess.WaitForExit(3000);
                                
                                if (!exited)
                                {
                                    Debug.WriteLine("ffmpeg didn't exit gracefully, killing process");
                                    // If it doesn't exit gracefully, kill it
                                    _ffmpegRecordProcess.Kill();
                                    _ffmpegRecordProcess.WaitForExit(1000);
                                }
                            }
                            
                            _ffmpegRecordProcess.Dispose();
                        }
                        catch (Exception ex) 
                        { 
                            Debug.WriteLine($"Error stopping recording process: {ex.Message}");
                        }
                        finally
                        {
                            _ffmpegRecordProcess = null;
                        }
                    }
                    
                    _isRecording = false;
                    
                    // Log the recording result if a file was created
                    if (_videoOutputPath != null && File.Exists(_videoOutputPath))
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            $"Recording stopped. Video saved to: {_videoOutputPath}");
                    }
                }
                
                // Clean up the current frame bitmap if it exists
                if (_currentFrame != null)
                {
                    try
                    {
                        Bitmap oldFrame;
                        
                        // Use lock when accessing the shared bitmap
                        lock (_lockObject)
                        {
                            oldFrame = _currentFrame;
                            _currentFrame = null;
                        }
                        
                        // Dispose outside the lock
                        if (oldFrame != null)
                        {
                            oldFrame.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing current frame: {ex.Message}");
                    }
                }
                
                _isRunning = false;
                _hasCapturedFrame = false;
                
                Debug.WriteLine("Camera stopped successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in StopCamera: {ex.Message}");
                
                // Force state reset in case of error
                _isRunning = false;
                _isRecording = false;
                _hasCapturedFrame = false;
            }
        }

        public void ToggleRecording()
        {
            if (_isRecording)
            {
                _isRecording = false;
                
                // If we have a recording process running, stop it
                if (_ffmpegRecordProcess != null && !_ffmpegRecordProcess.HasExited)
                {
                    try 
                    { 
                        // Send 'q' to ffmpeg to ensure a clean exit
                        _ffmpegRecordProcess.StandardInput.Write("q");
                        _ffmpegRecordProcess.StandardInput.Flush();
                        
                        // Wait for the process to gracefully exit
                        bool exited = _ffmpegRecordProcess.WaitForExit(3000);
                        
                        if (!exited)
                        {
                            // If it doesn't exit gracefully, kill it
                            _ffmpegRecordProcess.Kill();
                        }
                    }
                    catch (Exception ex) 
                    { 
                        Debug.WriteLine($"Error stopping recording: {ex.Message}");
                    }
                    
                    _ffmpegRecordProcess = null;
                }
                
                // Check if the video file was created
                if (File.Exists(_videoOutputPath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"Recording stopped. Video saved to: {_videoOutputPath}");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                        "Recording stopped, but no video file was created.");
                }
            }
            else
            {
                // Create a new video output path for each recording
                // Use timestamp for uniqueness
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                // Create SAM-compatible directory structure
                CreateSAMCompatibleDirectories();
                
                // Set the file name
                _videoOutputPath = Path.Combine(_recordingPath, $"{_videoName}_{timestamp}.mp4");
                
                // Start recording with ffmpeg
                try
                {
                    // Use ffmpeg to record video directly to MP4
                    _ffmpegRecordProcess = new Process();
                    _ffmpegRecordProcess.StartInfo.FileName = "ffmpeg";
                    
                    // Set up ffmpeg to record from camera directly to MP4
                    // Use a high quality preset for better visual quality
                    _ffmpegRecordProcess.StartInfo.Arguments = $"-f avfoundation -framerate 30 " +
                                                        $"-video_size 640x480 " +
                                                        $"-i \"{_deviceIndex}\" " +
                                                        $"-c:v libx264 -preset medium -crf 23 " + 
                                                        $"-pix_fmt yuv420p -y \"{_videoOutputPath}\"";
                    
                    _ffmpegRecordProcess.StartInfo.UseShellExecute = false;
                    _ffmpegRecordProcess.StartInfo.CreateNoWindow = true;
                    _ffmpegRecordProcess.StartInfo.RedirectStandardInput = true; // For sending 'q' to stop
                    _ffmpegRecordProcess.StartInfo.RedirectStandardError = true;
                    
                    // Start the recording process
                    _ffmpegRecordProcess.Start();
                    
                    _isRecording = true;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"Recording started to: {_videoOutputPath}");
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Failed to start recording: {ex.Message}");
                    _isRecording = false;
                    return;
                }
            }
            
            ExpireSolution(true);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            if (_isRunning) { StopCamera(); }
            return base.Write(writer);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            // Ensure recording is stopped first
            if (_isRecording)
            {
                try
                {
                    ToggleRecording();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping recording on component removal: {ex.Message}");
                }
            }
            
            StopCamera();
            CloseWebcamWindow();
            
            // Clear resources but preserve video path for potential access
            if (_currentFrame != null)
            {
                try
                {
                    _currentFrame.Dispose();
                    _currentFrame = null;
                }
                catch { /* ignore */ }
            }
            
            _isRunning = false;
            _hasCapturedFrame = false;
            
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                // Ensure recording is stopped first
                if (_isRecording)
                {
                    try
                    {
                        ToggleRecording();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error stopping recording on document close: {ex.Message}");
                    }
                }
                
                StopCamera();
                CloseWebcamWindow();
                
                // Clear resources but preserve video path for potential access
                if (_currentFrame != null)
                {
                    try
                    {
                        _currentFrame.Dispose();
                        _currentFrame = null;
                    }
                    catch { /* ignore */ }
                }
            }
            base.DocumentContextChanged(document, context);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        public override Guid ComponentGuid => new Guid("dfeb6f18-ec49-41b2-ace1-02ae334f5f1e");
    }

    // Form class for the webcam viewer
    public class WebcamViewer : Form
    {
        private BufferedPictureBox _pictureBox;
        private Button _recordButton;
        private Label _statusLabel;
        private WebcamComponent _component;
        private bool _isRecording = false;
        private Font _smallFont = new Font("Arial", 8);
        private int _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private double _currentFps = 0;
        private Size _preferredSize = new Size(320, 240); // Smaller default size
        private bool _initialSizeSet = false;
        private bool _userClosing = false; // Flag to track if user is closing the form

        public WebcamViewer(WebcamComponent component)
        {
            _component = component;
            
            // Form setup - very strict size control to prevent window growing too large
            this.Text = "Webcam Preview";
            _preferredSize = new Size(480, 360); // Smaller fixed size
            this.ClientSize = _preferredSize;
            this.MinimumSize = new Size(320, 240);
            this.MaximumSize = new Size(640, 480); // Enforce a strict maximum size
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AutoSize = false; // Critical - disable auto-sizing
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.DoubleBuffered = true;
            
            // These styles help prevent window resizing issues
            SetStyle(ControlStyles.OptimizedDoubleBuffer | 
                    ControlStyles.AllPaintingInWmPaint | 
                    ControlStyles.UserPaint, true);
                    
            // Prevent Windows from auto-adjusting the size
            this.SizeGripStyle = SizeGripStyle.Hide;
            
            // Add form closing event handler to detect user closing
            this.FormClosing += OnFormClosing;
            
            // Status label - small and compact
            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Top;
            _statusLabel.Height = 20;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.BackColor = Color.FromArgb(64, 64, 64);
            _statusLabel.ForeColor = Color.White;
            _statusLabel.Font = _smallFont;
            _statusLabel.Padding = new Padding(3, 0, 0, 0);
            
            // PictureBox setup with stricter constraints
            _pictureBox = new BufferedPictureBox();
            _pictureBox.Dock = DockStyle.Fill;
            _pictureBox.SizeMode = PictureBoxSizeMode.Zoom; // Important: Maintain aspect ratio
            _pictureBox.BackColor = Color.Black;
            _pictureBox.MaximumSize = new Size(640, 480); // Match form's maximum size
            // Prevent image growth issues
            _pictureBox.Size = new Size(400, 300); // Start with a fixed size
            
            // Record button - small and compact
            _recordButton = new Button();
            _recordButton.Text = "REC";
            _recordButton.Dock = DockStyle.Bottom;
            _recordButton.Height = 24; // Slightly smaller
            _recordButton.BackColor = Color.LightGray;
            _recordButton.FlatStyle = FlatStyle.Flat;
            _recordButton.Font = _smallFont;
            _recordButton.Click += OnRecordButtonClick;
            
            // Add controls in the right order
            this.Controls.Add(_pictureBox);
            this.Controls.Add(_recordButton);
            this.Controls.Add(_statusLabel);
            
            // Update initial label
            UpdateStatusLabel();
            
            // Add resize event handler to manage form size when user resizes
            this.ResizeBegin += OnResizeBegin;
            this.ResizeEnd += OnResizeEnd;
            this.Resize += OnFormResize;
            
            // Force initial size again after all controls are added
            this.ClientSize = _preferredSize;
        }

        private void OnResizeBegin(object sender, EventArgs e)
        {
            // Called when user starts resizing the form
            _initialSizeSet = true;
        }
        
        private void OnResizeEnd(object sender, EventArgs e)
        {
            // Called when user finishes resizing the form
            if (WindowState == FormWindowState.Normal)
            {
                _preferredSize = this.ClientSize;
                Debug.WriteLine($"User resized to: {_preferredSize.Width}x{_preferredSize.Height}");
            }
        }
        
        private void OnFormResize(object sender, EventArgs e)
        {
            // Ensure we don't exceed max size during resize
            if (this.ClientSize.Width > MaximumSize.Width || this.ClientSize.Height > MaximumSize.Height)
            {
                this.ClientSize = new Size(
                    Math.Min(this.ClientSize.Width, MaximumSize.Width),
                    Math.Min(this.ClientSize.Height, MaximumSize.Height)
                );
            }
        }

        private void OnRecordButtonClick(object sender, EventArgs e)
        {
            _isRecording = !_isRecording;
            
            if (_isRecording)
            {
                _recordButton.Text = "STOP";
                _recordButton.BackColor = Color.Red;
            }
            else
            {
                _recordButton.Text = "REC";
                _recordButton.BackColor = Color.LightGray;
            }
            
            _component.ToggleRecording();
            UpdateStatusLabel();
        }

        public void UpdateImage(Bitmap image)
        {
            // Multiple null/disposed checks to prevent crashes
            if (this.IsDisposed) 
            {
                Debug.WriteLine("UpdateImage: Form is disposed, skipping update");
                return;
            }
            
            if (_pictureBox == null) 
            {
                Debug.WriteLine("UpdateImage: PictureBox is null, skipping update");
                return;
            }
            
            if (_pictureBox.IsDisposed) 
            {
                Debug.WriteLine("UpdateImage: PictureBox is disposed, skipping update");
                return;
            }
            
            if (image == null)
            {
                Debug.WriteLine("UpdateImage: Input image is null, skipping update");
                return;
            }
                
            try
            {
                // CRITICAL: Resize the incoming image to a manageable size to prevent window growth issues
                Bitmap resizedImage = null;
                
                try
                {
                    if (image.Width > 640 || image.Height > 480)
                    {
                        // Calculate aspect ratio and resize to fit maximum dimensions
                        double aspectRatio = (double)image.Width / image.Height;
                        int newWidth, newHeight;
                        
                        if (aspectRatio > 1) // Wider than tall
                        {
                            newWidth = Math.Min(image.Width, 480); // Strict maximum width
                            newHeight = (int)(newWidth / aspectRatio);
                        }
                        else // Taller than wide
                        {
                            newHeight = Math.Min(image.Height, 360); // Strict maximum height
                            newWidth = (int)(newHeight * aspectRatio);
                        }
                        
                        // Create the resized image
                        resizedImage = new Bitmap(image, new Size(newWidth, newHeight));
                    }
                    else
                    {
                        // Clone without resizing if image is already small enough
                        resizedImage = (Bitmap)image.Clone();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error resizing image: {ex.Message}");
                    return; // Exit if we couldn't resize
                }
                
                // Final check before invoking UI update
                if (resizedImage == null)
                {
                    Debug.WriteLine("Resized image is null, skipping update");
                    return;
                }
                
                // Use BeginInvoke to avoid blocking the capture thread
                try
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        // Need to re-check if form or pictureBox were disposed
                        // while waiting for BeginInvoke to execute
                        if (this.IsDisposed || _pictureBox == null || _pictureBox.IsDisposed)
                        {
                            // Clean up and exit
                            try { resizedImage?.Dispose(); } catch { }
                            return;
                        }
                    
                        try
                        {
                            // Store old image reference
                            Bitmap oldImage = _pictureBox.Image as Bitmap;
                            
                            // Set new image directly
                            _pictureBox.Image = resizedImage;
                            
                            // Update frame rate counter immediately
                            UpdateFrameRate();
                            
                            // Update the status label with the new image dimensions
                            UpdateStatusLabel();
                            
                            // Dispose old image after the new one is displayed
                            if (oldImage != null && oldImage != resizedImage)
                            {
                                oldImage.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            // If an error happens, make sure to dispose the resized image
                            try { resizedImage?.Dispose(); } catch { }
                            Debug.WriteLine($"Error updating image in UI thread: {ex.Message}");
                        }
                    }));
                }
                catch (Exception ex)
                {
                    // If BeginInvoke fails, dispose the resized image
                    try { resizedImage?.Dispose(); } catch { }
                    Debug.WriteLine($"Error invoking UI update: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error preparing image update: {ex.Message}");
            }
        }


        private void UpdateFrameRate()
        {
            // Update frame counter for FPS calculation
            _frameCount++;
            TimeSpan elapsed = DateTime.Now - _lastFpsUpdate;
            if (elapsed.TotalSeconds >= 1)
            {
                // Calculate FPS
                _currentFps = _frameCount / elapsed.TotalSeconds;
                _frameCount = 0;
                _lastFpsUpdate = DateTime.Now;
                
                // Only update title when FPS changes significantly
                this.Text = $"Camera {_component._deviceIndex} - {Math.Round(_currentFps, 1)} FPS";
            }
        }
        
        private string GetCameraName()
        {
            if (_component._detectedCameras.Count > 0 &&
                _component._deviceIndex >= 0 && 
                _component._deviceIndex < _component._detectedCameras.Count)
            {
                return _component._detectedCameras[_component._deviceIndex];
            }
            return "Unknown";
        }

        private void UpdateStatusLabel()
        {
            if (this.IsDisposed || _statusLabel.IsDisposed)
                return;
                
            try
            {
                // Create a compact status with just the essential information
                string status = string.Empty;
                
                // Get camera info - just the name
                if (_component._detectedCameras.Count > 0 &&
                    _component._deviceIndex >= 0 && 
                    _component._deviceIndex < _component._detectedCameras.Count)
                {
                    string cameraName = _component._detectedCameras[_component._deviceIndex];
                    // Shorten camera name if too long
                    if (cameraName.Length > 20)
                    {
                        cameraName = cameraName.Substring(0, 17) + "...";
                    }
                    
                    // Add resolution in compact format
                    if (_pictureBox.Image != null)
                    {
                        status = $"{cameraName} | {_pictureBox.Image.Width}x{_pictureBox.Image.Height}";
                    }
                    else
                    {
                        status = cameraName;
                    }
                    
                    // Add small recording indicator
                    if (_isRecording)
                    {
                        status += " | REC";
                    }
                }
                else
                {
                    status = "No camera";
                }
                
                _statusLabel.Text = status;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating status: {ex.Message}");
            }
        }

        // Handle form closing event - detect when user closes the window
        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // Set flag that user is closing the form
            _userClosing = true;
            
            // Ensure PictureBox reference is safely cleared before form closes
            try
            {
                if (_pictureBox != null && !_pictureBox.IsDisposed && _pictureBox.Image != null)
                {
                    // Store the image reference
                    Image oldImage = _pictureBox.Image;
                    
                    // Clear the PictureBox image reference first
                    _pictureBox.Image = null;
                    
                    // Then dispose the old image
                    oldImage.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing PictureBox image during form closing: {ex.Message}");
            }
            
            // If the form is being closed by the user (not programmatically)
            // and it's not due to application shutdown
            if (e.CloseReason == CloseReason.UserClosing)
            {
                try
                {
                    Debug.WriteLine("Form closing by user action");
                    
                    // Stop the component's camera operations immediately
                    if (_component != null)
                    {
                        // First stop the camera directly on this thread to ensure immediate stopping
                        try
                        {
                            _component.StopCamera();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error stopping camera directly: {ex.Message}");
                        }
                        
                        // Then update the component state on the UI thread
                        if (Grasshopper.Instances.ActiveCanvas != null)
                        {
                            // Set the component state to disabled via the document
                            Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() =>
                            {
                                try
                                {
                                    // Update document to reflect camera stopped state
                                    var doc = _component.OnPingDocument();
                                    if (doc != null)
                                    {
                                        // Force the entire document to update
                                        doc.NewSolution(false);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error updating component state on form close: {ex.Message}");
                                }
                            }));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error handling form closing: {ex.Message}");
                }
            }
        }
        
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Clean up resources - with additional null checks
            try
            {
                if (_pictureBox != null && !_pictureBox.IsDisposed && _pictureBox.Image != null)
                {
                    Image oldImage = _pictureBox.Image;
                    _pictureBox.Image = null; // Set to null first to avoid potential access
                    oldImage.Dispose(); // Then dispose the old reference
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning up PictureBox image: {ex.Message}");
            }
            
            // Tell the component to disable the camera when form is closed
            try
            {
                // Only need to do this if the form wasn't closed by user (already handled in FormClosing)
                if (!_userClosing && _component != null && Grasshopper.Instances.ActiveCanvas != null)
                {
                    Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            // Stop the component's camera operations
                            _component.StopCamera();
                            
                            // Update the component's state
                            if (_component.OnPingDocument() != null)
                            {
                                _component.OnPingDocument().NewSolution(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error stopping camera on form close: {ex.Message}");
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling form close: {ex.Message}");
            }
            
            base.OnFormClosed(e);
        }
    }
}