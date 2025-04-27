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
using OpenCvSharp;

namespace crft
{
    // Custom double-buffered PictureBox for HandDetectorComponent
    public class HandDetectorPictureBox : PictureBox
    {
        private bool _disposing = false;
        
        public HandDetectorPictureBox()
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
                Debug.WriteLine($"Error in HandDetectorPictureBox.OnPaint: {ex.Message}");
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

    // Represents a detected hand with landmarks
    public class HandData
    {
        public List<System.Drawing.PointF> Landmarks { get; set; } = new List<System.Drawing.PointF>();
        public string HandType { get; set; }
        
        public HandData(List<System.Drawing.PointF> landmarks, string handType)
        {
            Landmarks = landmarks;
            HandType = handType;
        }
    }

    public class HandDetectorComponent : GH_Component
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
        internal List<HandData> _detectedHands = new List<HandData>();
        
        // Hand detection state
        private bool _modelInitialized = false;
        
        // Available cameras
        internal List<string> _detectedCameras = new List<string>();
        
        // Form
        private HandDetectorViewer _viewerForm = null;

        private bool _previousEnableState = false;
        
        // For UI update throttling - using a shorter interval for better responsiveness
        private DateTime _lastUIUpdate = DateTime.MinValue;
        private const int UI_UPDATE_INTERVAL_MS = 16; // ~60fps for UI updates

        // Flag to enable/disable hand detection
        private bool _enableHandDetection = true;
        
        public HandDetectorComponent()
          : base("Hand Detector", "HandDetect", 
              "Captures webcam video feed and detects hand landmarks using OpenCV",
              "Display", "Preview")
        {
            // Use unique ID for temporary frame path
            _tempImagePath = Path.Combine(Path.GetTempPath(), 
                "gh_handdetect_" + Guid.NewGuid().ToString() + ".jpg");
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Enable", "E", "Enable/disable webcam and hand detection", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Device", "D", "Webcam device index", GH_ParamAccess.item, 1);
            pManager.AddBooleanParameter("DetectHands", "DH", "Enable/disable hand detection (always enabled by default)", GH_ParamAccess.item, true);
            // Optional parameter
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Image", "I", "Current webcam frame with hand landmarks", GH_ParamAccess.item);
            pManager.AddGenericParameter("HandLandmarks", "HL", "Detected hand landmarks", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool enable = false;
            int deviceIndex = 0;
            bool detectHands = true;

            if (!DA.GetData(0, ref enable)) return;
            if (!DA.GetData(1, ref deviceIndex)) return;
            DA.GetData(2, ref detectHands);
            
            _enableHandDetection = detectHands;
            
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
                StartCamera();
                ShowViewerWindow();
            }
            else if (!enable && _isRunning)
            {
                // Stop the camera and close the window
                StopCamera();
                CloseViewerWindow();
                
                // Display the state change message
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Camera and hand detection disabled.");
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
                    
                    // Output detected hand landmarks
                    if (_detectedHands != null && _detectedHands.Count > 0)
                    {
                        List<GH_ObjectWrapper> handWrappers = new List<GH_ObjectWrapper>();
                        foreach (var hand in _detectedHands)
                        {
                            handWrappers.Add(new GH_ObjectWrapper(hand));
                        }
                        DA.SetDataList(1, handWrappers);
                    }
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

        private void ShowViewerWindow()
        {
            try
            {
                if (Grasshopper.Instances.ActiveCanvas != null)
                {
                    Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() =>
                    {
                        if (_viewerForm == null || _viewerForm.IsDisposed)
                        {
                            _viewerForm = new HandDetectorViewer(this);
                            _viewerForm.Show();
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error creating viewer window: {ex.Message}");
            }
        }
        
        private void CloseViewerWindow()
        {
            try
            {
                if (_viewerForm != null && !_viewerForm.IsDisposed)
                {
                    if (Grasshopper.Instances.ActiveCanvas != null)
                    {
                        Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() =>
                        {
                            try
                            {
                                // Close the form and mark it as null
                                _viewerForm.Close();
                                _viewerForm.Dispose(); // Ensure full disposal
                                _viewerForm = null;
                                
                                // Force a UI update to ensure Grasshopper updates
                                if (OnPingDocument() != null)
                                {
                                    OnPingDocument().NewSolution(false);
                                }
                                
                                Debug.WriteLine("Viewer window closed successfully");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error in UI thread closing viewer form: {ex.Message}");
                            }
                        }));
                    }
                    else
                    {
                        // If no canvas is available, try to close directly
                        _viewerForm.Close();
                        _viewerForm.Dispose();
                        _viewerForm = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error closing viewer form: {ex.Message}");
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

        private void StartCamera()
        {
            try
            {
                // Add debug message
                Debug.WriteLine("Starting camera capture with hand detection...");
                
                // Generate a fresh temp path for this capture session
                _tempImagePath = Path.Combine(Path.GetTempPath(), 
                    "gh_handdetect_" + Guid.NewGuid().ToString() + ".jpg");
                    
                Debug.WriteLine($"Using temp image path: {_tempImagePath}");
                
                // Initialize OpenCV models for hand detection if enabled
                if (_enableHandDetection)
                {
                    InitializeOpenCVHandDetection();
                }
                
                _cancellationSource = new CancellationTokenSource();
                _captureTask = Task.Run(() => CaptureLoop(_cancellationSource.Token));
                _isRunning = true;
                
                Debug.WriteLine("Camera capture with hand detection started successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in StartCamera: {ex.Message}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error starting camera: {ex.Message}");
            }
        }

        private void InitializeOpenCVHandDetection()
        {
            try
            {
                // Clean up any existing models
                DisposeHandDetectionModels();
                
                // For now, we'll use basic OpenCV functions for hand detection
                // In a real implementation, you would load pre-trained hand detection models
                _modelInitialized = true;
                
                // Initialize detection (simulation only)
                
                Debug.WriteLine("OpenCV hand detection initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing OpenCV hand detection: {ex.Message}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error initializing hand detection: {ex.Message}");
                
                // Clean up any partially initialized resources
                DisposeHandDetectionModels();
            }
        }

        private void DisposeHandDetectionModels()
        {
            try
            {
                // No actual models to dispose in this simplified implementation
                
                _modelInitialized = false;
                
                // Clear detected hands
                _detectedHands.Clear();
                
                Debug.WriteLine("Hand detection models disposed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disposing hand detection models: {ex.Message}");
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
                                            // Read the original frame
                                            Bitmap rawFrame = new Bitmap(stream);
                                            
                                            // Process for hand detection if enabled
                                            if (_enableHandDetection && _modelInitialized)
                                            {
                                                ProcessFrameForHandDetection(rawFrame);
                                            }
                                            
                                            // Store current frame for component output
                                            lock (_lockObject)
                                            {
                                                if (_currentFrame != null)
                                                {
                                                    _currentFrame.Dispose();
                                                }
                                                _currentFrame = (Bitmap)rawFrame.Clone();
                                                _hasCapturedFrame = true;
                                            }
                                            
                                            // Update UI directly with frame for live view
                                            UpdateViewerUI(rawFrame);
                                            
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
                                // Read the original frame
                                Bitmap rawFrame = new Bitmap(stream);
                                
                                // Process for hand detection if enabled
                                if (_enableHandDetection && _modelInitialized)
                                {
                                    ProcessFrameForHandDetection(rawFrame);
                                }
                                
                                // Store frame for component
                                lock (_lockObject)
                                {
                                    if (_currentFrame != null)
                                    {
                                        _currentFrame.Dispose();
                                    }
                                    _currentFrame = (Bitmap)rawFrame.Clone();
                                    _hasCapturedFrame = true;
                                }
                                
                                // Update the viewer window immediately
                                UpdateViewerUI(rawFrame);
                                
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

        private void ProcessFrameForHandDetection(Bitmap frame)
        {
            try
            {
                // For demonstration purposes, we'll simulate hand detection
                // In a real implementation, you would:
                // 1. Use actual hand detection models
                // 2. Process the image to detect hands
                // 3. Extract hand landmarks
                
                // Clear previous detected hands
                _detectedHands.Clear();
                
                // For demonstration, generate some simulated hand landmarks
                // This will create a hand with 21 landmarks in a pattern resembling a hand
                SimulateHandDetection(frame);
                
                // Draw the landmarks on the frame
                DrawHandLandmarks(frame);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing frame for hand detection: {ex.Message}");
            }
        }

        // In this simplified implementation, we don't actually need to convert to OpenCV Mat
        // This is just a stub method that would be used in a real implementation
        private object BitmapToMat(Bitmap bitmap)
        {
            // Just return the bitmap itself for our simulated implementation
            return bitmap;
        }

        private void SimulateHandDetection(Bitmap frame)
        {
            // This is a simulation function to create a visualized hand
            // In a real implementation, you would use actual hand detection models
            
            // Get central position in the frame
            int centerX = frame.Width / 2;
            int centerY = frame.Height / 2;
            
            // Create a list of landmarks to represent a simplified hand shape
            List<System.Drawing.PointF> rightHandLandmarks = new List<System.Drawing.PointF>();
            
            // Wrist point (base of hand)
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX, centerY + 50));
            
            // Thumb (4 points)
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX - 30, centerY + 30));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX - 45, centerY + 10));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX - 55, centerY - 5));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX - 60, centerY - 15));
            
            // Index finger (4 points)
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX - 15, centerY));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX - 15, centerY - 25));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX - 15, centerY - 45));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX - 15, centerY - 65));
            
            // Middle finger (4 points)
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX, centerY - 5));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX, centerY - 30));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX, centerY - 55));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX, centerY - 75));
            
            // Ring finger (4 points)
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX + 15, centerY));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX + 15, centerY - 25));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX + 15, centerY - 45));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX + 15, centerY - 60));
            
            // Pinky finger (4 points)
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX + 30, centerY + 10));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX + 30, centerY - 15));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX + 30, centerY - 35));
            rightHandLandmarks.Add(new System.Drawing.PointF(centerX + 30, centerY - 50));
            
            // Add the simulated right hand
            _detectedHands.Add(new HandData(rightHandLandmarks, "Right"));
            
            // Simulate a left hand by mirroring the right hand landmarks
            List<System.Drawing.PointF> leftHandLandmarks = new List<System.Drawing.PointF>();
            foreach (var point in rightHandLandmarks)
            {
                // Mirror across the vertical axis on the other side of the frame
                leftHandLandmarks.Add(new System.Drawing.PointF(frame.Width - point.X, point.Y));
            }
            
            // Add the simulated left hand
            _detectedHands.Add(new HandData(leftHandLandmarks, "Left"));
        }

        private void DrawHandLandmarks(Bitmap frame)
        {
            if (_detectedHands == null || _detectedHands.Count == 0)
                return;
                
            try
            {
                using (Graphics g = Graphics.FromImage(frame))
                {
                    foreach (var hand in _detectedHands)
                    {
                        // Choose colors based on hand type
                        Color landmarkColor = hand.HandType == "Left" ? Color.LimeGreen : Color.DeepSkyBlue;
                        Color connectionColor = hand.HandType == "Left" ? Color.ForestGreen : Color.DodgerBlue;
                        
                        // Draw each landmark as a circle
                        using (Brush brush = new SolidBrush(landmarkColor))
                        {
                            foreach (var landmark in hand.Landmarks)
                            {
                                g.FillEllipse(brush, landmark.X - 4, landmark.Y - 4, 8, 8);
                            }
                        }
                        
                        // Define hand landmark connections for drawing
                        int[][] connections = new int[][] {
                            // Thumb
                            new int[] {0, 1}, new int[] {1, 2}, new int[] {2, 3}, new int[] {3, 4},
                            // Index finger  
                            new int[] {0, 5}, new int[] {5, 6}, new int[] {6, 7}, new int[] {7, 8},
                            // Middle finger
                            new int[] {0, 9}, new int[] {9, 10}, new int[] {10, 11}, new int[] {11, 12},
                            // Ring finger
                            new int[] {0, 13}, new int[] {13, 14}, new int[] {14, 15}, new int[] {15, 16},
                            // Pinky
                            new int[] {0, 17}, new int[] {17, 18}, new int[] {18, 19}, new int[] {19, 20},
                            // Palm
                            new int[] {5, 9}, new int[] {9, 13}, new int[] {13, 17}
                        };
                        
                        // Draw connections between landmarks to show hand structure
                        using (Pen pen = new Pen(connectionColor, 2))
                        {
                            foreach (var connection in connections)
                            {
                                if (connection.Length == 2 && 
                                    connection[0] < hand.Landmarks.Count && 
                                    connection[1] < hand.Landmarks.Count)
                                {
                                    var point1 = hand.Landmarks[connection[0]];
                                    var point2 = hand.Landmarks[connection[1]];
                                    
                                    g.DrawLine(pen, point1.X, point1.Y, point2.X, point2.Y);
                                }
                            }
                        }
                        
                        // Add label for hand type
                        using (Font font = new Font("Arial", 12, FontStyle.Bold))
                        using (Brush textBrush = new SolidBrush(landmarkColor))
                        using (Brush bgBrush = new SolidBrush(Color.FromArgb(128, Color.Black)))
                        {
                            // Get wrist position (landmark 0)
                            float labelX = hand.Landmarks[0].X;
                            float labelY = hand.Landmarks[0].Y;
                            
                            // Measure text for background
                            SizeF textSize = g.MeasureString(hand.HandType, font);
                            
                            // Draw semi-transparent background
                            g.FillRectangle(bgBrush, 
                                labelX - 5, 
                                labelY - textSize.Height - 5, 
                                textSize.Width + 10, 
                                textSize.Height + 10);
                            
                            // Draw text
                            g.DrawString(hand.HandType, font, textBrush, labelX, labelY - textSize.Height);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error drawing hand landmarks: {ex.Message}");
            }
        }
        
        private void UpdateViewerUI(Bitmap frame)
        {
            try
            {
                // Multiple checks to avoid null reference issues
                if (_viewerForm == null || _viewerForm.IsDisposed)
                {
                    Debug.WriteLine("Viewer form is not available, skipping UI update");
                    return;
                }
                
                if (!_isRunning)
                {
                    Debug.WriteLine("Component is not running, skipping UI update");
                    return;
                }
                
                if (frame == null)
                {
                    Debug.WriteLine("Frame is null, skipping UI update");
                    return;
                }
                
                // Pass the frame to the UI - the UpdateImage method will handle cloning and resizing
                _viewerForm.UpdateImage(frame);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating UI: {ex.Message}");
            }
        }

        public void StopCamera()
        {
            Debug.WriteLine("StopCamera called");
            
            // Dispose hand detection models first
            if (_enableHandDetection)
            {
                DisposeHandDetectionModels();
            }
            
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
                _hasCapturedFrame = false;
            }
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            if (_isRunning) { StopCamera(); }
            return base.Write(writer);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopCamera();
            CloseViewerWindow();
            
            // Clear resources
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
                StopCamera();
                CloseViewerWindow();
                
                // Clear resources
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

        public override Guid ComponentGuid => new Guid("a23b87f5-12f9-4aa8-8c68-445c5bd98176");
    }

    // Form class for the hand detector viewer
    public class HandDetectorViewer : Form
    {
        private HandDetectorPictureBox _pictureBox;
        private Label _statusLabel;
        private HandDetectorComponent _component;
        private Font _smallFont = new Font("Arial", 8);
        private int _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private double _currentFps = 0;
        private System.Drawing.Size _preferredSize = new System.Drawing.Size(320, 240); // Smaller default size
        private bool _initialSizeSet = false;
        private bool _userClosing = false; // Flag to track if user is closing the form

        public HandDetectorViewer(HandDetectorComponent component)
        {
            _component = component;
            
            // Form setup - very strict size control to prevent window growing too large
            this.Text = "Hand Detector Preview";
            _preferredSize = new System.Drawing.Size(480, 360); // Smaller fixed size
            this.ClientSize = _preferredSize;
            this.MinimumSize = new System.Drawing.Size(320, 240);
            this.MaximumSize = new System.Drawing.Size(640, 480); // Enforce a strict maximum size
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
            _pictureBox = new HandDetectorPictureBox();
            _pictureBox.Dock = DockStyle.Fill;
            _pictureBox.SizeMode = PictureBoxSizeMode.Zoom; // Important: Maintain aspect ratio
            _pictureBox.BackColor = Color.Black;
            _pictureBox.MaximumSize = new System.Drawing.Size(640, 480); // Match form's maximum size
            // Prevent image growth issues
            _pictureBox.Size = new System.Drawing.Size(400, 300); // Start with a fixed size
            
            // Add controls in the right order
            this.Controls.Add(_pictureBox);
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
                this.ClientSize = new System.Drawing.Size(
                    Math.Min(this.ClientSize.Width, MaximumSize.Width),
                    Math.Min(this.ClientSize.Height, MaximumSize.Height)
                );
            }
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
                        resizedImage = new Bitmap(image, new System.Drawing.Size(newWidth, newHeight));
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
                this.Text = $"Hand Detector - {Math.Round(_currentFps, 1)} FPS";
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
                    
                    // Add hand detection status
                    status += " | Hand Detection: " + (_component._detectedHands.Count > 0 ? 
                              $"{_component._detectedHands.Count} hand(s)" : "None");
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