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
    // Hand landmarks data class to store tracking results
    public class HandLandmarks
    {
        public List<PointF> Points { get; set; } = new List<PointF>();
        public Bitmap DebugImage { get; set; }
        public bool HandDetected { get; set; } = false;
        
#pragma warning disable CA1416 // Validate platform compatibility
        public HandLandmarks Clone()
        {
            HandLandmarks clone = new HandLandmarks();
            clone.HandDetected = this.HandDetected;
            clone.Points = new List<PointF>(this.Points);
            
            if (this.DebugImage != null)
            {
                clone.DebugImage = (Bitmap)this.DebugImage.Clone();
            }
            
            return clone;
        }
        
        public void Dispose()
        {
            if (DebugImage != null)
            {
                DebugImage.Dispose();
                DebugImage = null;
            }
        }
#pragma warning restore CA1416 // Validate platform compatibility
    }
    
    public class HandTrackingWebcamComponent : WebcamComponent
    {
        // Hand tracking specific fields
        private string _pythonPath = "python"; // Default Python executable path
        private string _scriptPath = ""; // Path to the hand tracking script
        private string _handTrackTempDir; // Directory for temporary files
        private string _currentHandLandmarksPath; // Current landmarks file path
        private string _currentDebugImagePath; // Current debug visualization path
        internal bool _enableHandTracking = false; // Whether to enable hand tracking
        private int _trackingFrequency = 5; // Process every Nth frame
        private int _frameCounter = 0; // Frame counter for tracking frequency
        
        // Hand landmarks data
        internal HandLandmarks _currentHandLandmarks = new HandLandmarks();
        private Task _handTrackingTask = null;
        private bool _isProcessingHands = false;
        
        public HandTrackingWebcamComponent()
          : base()
        {
            // Override base component info
            this.Name = "Webcam Hand Tracking";
            this.NickName = "HandTrack";
            this.Description = "Captures webcam video feed and tracks hand landmarks using MediaPipe";
            this.Category = "Display";
            this.SubCategory = "Preview";
                
            // Create a temporary directory for hand tracking files
            _handTrackTempDir = Path.Combine(Path.GetTempPath(), 
                "gh_handtrack_" + Guid.NewGuid().ToString());
                
            if (!Directory.Exists(_handTrackTempDir))
            {
                Directory.CreateDirectory(_handTrackTempDir);
            }
                
            // Set paths for hand landmarks and debug images
            _currentHandLandmarksPath = Path.Combine(_handTrackTempDir, "landmarks.txt");
            _currentDebugImagePath = _currentHandLandmarksPath + ".debug.jpg";
            
            // Try multiple strategies to find the handtrack.py script
            _scriptPath = FindHandtrackScript();
            
            if (!string.IsNullOrEmpty(_scriptPath) && File.Exists(_scriptPath))
            {
                Debug.WriteLine($"Using existing handtrack.py script: {_scriptPath}");
            }
            else
            {
                Debug.WriteLine("Could not find handtrack.py script using any search method");
                _scriptPath = ""; // Reset to empty to ensure proper error handling later
            }
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // Call base implementation first to register standard webcam parameters
            base.RegisterInputParams(pManager);
            
            // Add hand tracking specific parameters
            pManager.AddBooleanParameter("Track Hands", "T", "Enable/disable hand tracking", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Track Frequency", "F", "Process every Nth frame (higher values = better performance)", GH_ParamAccess.item, 5);
            pManager.AddTextParameter("Python Path", "P", "Path to Python executable (default: 'python')", GH_ParamAccess.item, "python");
            pManager.AddTextParameter("Script Path", "S", "Path to handtrack.py script (leave empty to use default)", GH_ParamAccess.item, "");
            
            // Make the new parameters optional
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Call base implementation first to register standard webcam outputs
            base.RegisterOutputParams(pManager);
            
            // Add hand tracking specific outputs
            pManager.AddGenericParameter("Hand Landmarks", "H", "Detected hand landmarks as points (0-1 normalized)", GH_ParamAccess.item);
            pManager.AddGenericParameter("Debug Image", "D", "Image with hand landmarks visualized", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DiagnoseHandTrackingState();
            
            bool trackHands = false;
            int trackFrequency = 5;
            string pythonPath = "python";
            string scriptPath = "";

            // Let the base SolveInstance handle the standard webcam parameters and outputs
            base.SolveInstance(DA);
            
            // Process hand tracking specific parameters
            DA.GetData(3, ref trackHands);
            DA.GetData(4, ref trackFrequency);
            DA.GetData(5, ref pythonPath);
            DA.GetData(6, ref scriptPath);
            
            // Update hand tracking settings
            _enableHandTracking = trackHands;
            _trackingFrequency = Math.Max(1, trackFrequency); // Ensure minimum of 1
            _pythonPath = string.IsNullOrEmpty(pythonPath) ? "python" : pythonPath;
            
            // Update script path if provided and it exists
            if (!string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath))
            {
                _scriptPath = scriptPath;
                Debug.WriteLine($"Using custom handtrack.py script: {_scriptPath}");
            }
            
            // Check if we have a valid script path
            if (string.IsNullOrEmpty(_scriptPath) || !File.Exists(_scriptPath))
            {
                string message = "No valid handtrack.py script found. Please provide a valid script path via the 'Script Path' input.";
                
                // Add mediapipe directory existence check for better diagnostics
                string mediapipeDir = Path.Combine(Directory.GetCurrentDirectory(), "mediapipe");
                if (Directory.Exists(mediapipeDir))
                {
                    message += $"\n\nThe 'mediapipe' directory was found at: {mediapipeDir}";
                    
                    // Check if script exists in that directory
                    string potentialScript = Path.Combine(mediapipeDir, "handtrack.py");
                    if (File.Exists(potentialScript))
                    {
                        message += $"\nThe script exists at: {potentialScript}\nTry providing this path explicitly.";
                    }
                    else
                    {
                        message += "\nBut 'handtrack.py' was not found in that directory.";
                    }
                }
                else
                {
                    message += "\n\nThe 'mediapipe' directory was not found in the current working directory.";
                    
                    // Suggest location to place the script
                    string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string suggestedDir = Path.Combine(userDir, "Desktop", "crft", "mediapipe");
                    message += $"\n\nTry placing 'handtrack.py' in: {suggestedDir}";
                }
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
                _enableHandTracking = false;
            }
            
#pragma warning disable CA1416 // Validate platform compatibility
            // Process for hand tracking if enabled
            if (_enableHandTracking && _currentFrame != null)
            {
                try
                {
                    lock (_lockObject)
                    {
                        ProcessFrameForHandTracking((Bitmap)_currentFrame.Clone());
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing frame for hand tracking: {ex.Message}");
                }
            }
#pragma warning restore CA1416 // Validate platform compatibility
            
            // Report hand tracking status when active
            if (_enableHandTracking)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"Hand tracking enabled. Processing every {_trackingFrequency} frames. Using script: {_scriptPath}");
            }
            
            // Handle hand landmarks output if available
            if (_enableHandTracking && _currentHandLandmarks != null && _currentHandLandmarks.HandDetected)
            {
                lock (_lockObject)
                {
                    try
                    {
                        // Clone the landmarks before outputting
                        HandLandmarks landmarksCopy = _currentHandLandmarks.Clone();
                        
                        // Output the landmarks
                        DA.SetDataList(2, landmarksCopy.Points.Select(p => new Point2d(p.X, p.Y)));
                        
                        // Output the debug image
                        if (landmarksCopy.DebugImage != null)
                        {
                            DA.SetData(3, landmarksCopy.DebugImage);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                            $"Error outputting hand landmarks: {ex.Message}");
                    }
                }
            }
        }

        private void DiagnoseHandTrackingState()
        {
            StringBuilder diagnostic = new StringBuilder();
            diagnostic.AppendLine("=== HandTracking Webcam Component Diagnostic ===");
            diagnostic.AppendLine($"Current Frame Null: {_currentFrame == null}");
            
            if (_currentFrame != null)
            {
                diagnostic.AppendLine($"Frame Dimensions: {_currentFrame.Width}x{_currentFrame.Height}");
            }
            
            diagnostic.AppendLine($"Hand Tracking Enabled: {_enableHandTracking}");
            diagnostic.AppendLine($"Tracking Frequency: {_trackingFrequency}");
            diagnostic.AppendLine($"Python Path: {_pythonPath}");
            diagnostic.AppendLine($"Script Path: {_scriptPath}");
            diagnostic.AppendLine($"Script Exists: {File.Exists(_scriptPath)}");
            diagnostic.AppendLine($"Hand Tracking Temp Dir: {_handTrackTempDir}");
            diagnostic.AppendLine($"Is Processing Hands: {_isProcessingHands}");
            diagnostic.AppendLine($"Hand landmarks detected: {_currentHandLandmarks?.HandDetected}");
            
            if (_currentHandLandmarks != null && _currentHandLandmarks.HandDetected)
            {
                diagnostic.AppendLine($"Number of landmarks: {_currentHandLandmarks.Points.Count}");
            }
            
            Debug.WriteLine(diagnostic.ToString());
            
            // Add summary to runtime messages
            if (_enableHandTracking)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"The hand tracking feature is {(_enableHandTracking ? "enabled" : "disabled")}, but it's " +
                    $"{(_isProcessingHands ? "currently" : "not currently")} processing frames and " +
                    $"{(_currentHandLandmarks != null && _currentHandLandmarks.HandDetected ? "hands are detected" : "no hands are detected yet")}.");
            }
            
            // Check camera status based on frame existence
            bool hasFrame = _currentFrame != null;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                $"The webcam component is {(hasFrame ? "capturing" : "not capturing")} frames.");
            
            if (_detectedCameras.Count > 0 && _deviceIndex >= 0 && _deviceIndex < _detectedCameras.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"You have a camera selected (Camera {_detectedCameras[_deviceIndex]}).");
            }
            
            if (_enableHandTracking)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"Hand tracking is enabled and configured to process every {_trackingFrequency}th frame for better performance.");
            }
        }

        private bool CheckPython()
        {
            try
            {
                // Check for Python
                Process pythonCheck = new Process();
                pythonCheck.StartInfo.FileName = "which";
                pythonCheck.StartInfo.Arguments = _pythonPath;
                pythonCheck.StartInfo.UseShellExecute = false;
                pythonCheck.StartInfo.RedirectStandardOutput = true;
                pythonCheck.StartInfo.CreateNoWindow = true;
                
                pythonCheck.Start();
                string pythonActualPath = pythonCheck.StandardOutput.ReadToEnd().Trim();
                pythonCheck.WaitForExit();
                
                if (string.IsNullOrEmpty(pythonActualPath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Python not found at path: {_pythonPath}");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error checking Python: {ex.Message}");
                return false;
            }
        }

        // Process a frame for hand tracking
#pragma warning disable CA1416 // Validate platform compatibility
        private bool ProcessFrameForHandTracking(Bitmap frame)
        {
            if (!_enableHandTracking)
                return false;
                
            // Only process hand tracking at specified frequency
            _frameCounter++;
            if (_frameCounter < _trackingFrequency)
            {
                return false;
            }
            _frameCounter = 0;
            
            // Only process if we're not already processing
            if (_isProcessingHands)
            {
                return false;
            }
            
            // Verify script exists
            if (string.IsNullOrEmpty(_scriptPath) || !File.Exists(_scriptPath))
            {
                Debug.WriteLine("No valid handtrack.py script found");
                return false;
            }
            
            // Make sure Python is available
            if (!CheckPython())
            {
                return false;
            }
            
            // Set flag to prevent concurrent processing
            _isProcessingHands = true;
            
            try
            {
                // Save the frame to a temporary file for processing
                string framePath = Path.Combine(_handTrackTempDir, "frame.jpg");
                frame.Save(framePath, System.Drawing.Imaging.ImageFormat.Jpeg);
                
                // Start a new task to process the frame for hand tracking
                _handTrackingTask = Task.Run(() =>
                {
                    try
                    {
                        ProcessHandLandmarks(framePath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in hand tracking task: {ex.Message}");
                    }
                    finally
                    {
                        _isProcessingHands = false;
                    }
                });
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting hand tracking: {ex.Message}");
                _isProcessingHands = false;
                return false;
            }
        }
#pragma warning restore CA1416 // Validate platform compatibility
        
        private void ProcessHandLandmarks(string framePath)
        {
            try
            {
                // Run the Python script to process the frame
                Process handtrackProcess = new Process();
                handtrackProcess.StartInfo.FileName = _pythonPath;
                handtrackProcess.StartInfo.Arguments = $"\"{_scriptPath}\" \"{framePath}\" \"{_currentHandLandmarksPath}\"";
                handtrackProcess.StartInfo.UseShellExecute = false;
                handtrackProcess.StartInfo.RedirectStandardError = true;
                handtrackProcess.StartInfo.CreateNoWindow = true;
                
                Debug.WriteLine($"Running: {_pythonPath} {handtrackProcess.StartInfo.Arguments}");
                handtrackProcess.Start();
                
                // Capture stderr for debugging
                string errors = handtrackProcess.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(errors))
                {
                    Debug.WriteLine($"Hand tracking script output: {errors}");
                }
                
                // Wait for process to complete with timeout
                bool completed = handtrackProcess.WaitForExit(2000); // 2 second timeout
                if (!completed)
                {
                    handtrackProcess.Kill();
                    Debug.WriteLine("Hand tracking script timed out");
                    return;
                }
                
                // Check exit code to determine success
                if (handtrackProcess.ExitCode == 0)
                {
                    // Process successful, load landmarks
                    LoadHandLandmarks();
                }
                else
                {
                    // Process failed - clear landmarks
                    lock (_lockObject)
                    {
                        _currentHandLandmarks.HandDetected = false;
                    }
                    Debug.WriteLine($"Hand tracking script failed with exit code {handtrackProcess.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ProcessHandLandmarks: {ex.Message}");
            }
        }
        
        // Method to find the handtrack.py script using multiple search strategies
        private string FindHandtrackScript()
        {
            try
            {
                List<string> possiblePaths = new List<string>();
                
                // Strategy 1: Try using Assembly.Location (may return null on some platforms)
                try
                {
                    string assemblyLocation = typeof(HandTrackingWebcamComponent).Assembly.Location;
                    if (!string.IsNullOrEmpty(assemblyLocation))
                    {
                        string assemblyDir = Path.GetDirectoryName(assemblyLocation);
                        if (!string.IsNullOrEmpty(assemblyDir))
                        {
                            // Navigate up from bin/Debug/net7.0/osx-arm64/ to project root
                            string path1 = Path.Combine(assemblyDir, "..", "..", "..", "..", "mediapipe", "handtrack.py");
                            possiblePaths.Add(Path.GetFullPath(path1));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Strategy 1 (Assembly.Location) failed: {ex.Message}");
                }
                
                // Strategy 2: Try using the current working directory
                try
                {
                    string currentDir = Directory.GetCurrentDirectory();
                    string path2 = Path.Combine(currentDir, "mediapipe", "handtrack.py");
                    possiblePaths.Add(Path.GetFullPath(path2));
                    
                    // Also try one level up from current directory
                    string path3 = Path.Combine(currentDir, "..", "mediapipe", "handtrack.py");
                    possiblePaths.Add(Path.GetFullPath(path3));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Strategy 2 (Current Directory) failed: {ex.Message}");
                }
                
                // Strategy 3: Try using absolute path based on typical Grasshopper plugin location
                try
                {
                    // For Mac, try a common location
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        string userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        string macPath = Path.Combine(userDirectory, "Desktop", "crft", "mediapipe", "handtrack.py");
                        possiblePaths.Add(macPath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Strategy 3 (Absolute Path) failed: {ex.Message}");
                }
                
                // Strategy 4: Search for the file in common directories
                try
                {
                    // Start from root directory and look for mediapipe folder with handtrack.py
                    foreach (string drive in Directory.GetLogicalDrives())
                    {
                        // Skip network drives to avoid performance issues
                        if (drive.StartsWith("//") || drive.StartsWith("\\\\"))
                            continue;
                            
                        // Search for common project directory names
                        foreach (string projectDir in new[] { "crft", "rhino-plugins", "grasshopper-plugins" })
                        {
                            try
                            {
                                string searchPath = Path.Combine(drive, projectDir);
                                if (Directory.Exists(searchPath))
                                {
                                    string scriptPath = Path.Combine(searchPath, "mediapipe", "handtrack.py");
                                    possiblePaths.Add(scriptPath);
                                }
                            }
                            catch
                            {
                                // Ignore access errors while searching
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Strategy 4 (Search) failed: {ex.Message}");
                }
                
                // Find first path that exists
                foreach (string path in possiblePaths)
                {
                    try
                    {
                        Debug.WriteLine($"Checking potential script path: {path}");
                        if (File.Exists(path))
                        {
                            Debug.WriteLine($"Found handtrack.py at: {path}");
                            return path;
                        }
                    }
                    catch
                    {
                        // Ignore any access errors
                    }
                }
                
                // No path found, return empty string
                Debug.WriteLine("No valid handtrack.py script found in any location");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in FindHandtrackScript: {ex.Message}");
                return string.Empty;
            }
        }
        
        private void LoadHandLandmarks()
        {
            try
            {
                if (!File.Exists(_currentHandLandmarksPath))
                {
                    Debug.WriteLine("No landmarks file found");
                    lock (_lockObject)
                    {
                        _currentHandLandmarks.HandDetected = false;
                    }
                    return;
                }
                
                // Read landmarks file
                string[] lines = File.ReadAllLines(_currentHandLandmarksPath);
                if (lines.Length == 0)
                {
                    Debug.WriteLine("Landmarks file is empty");
                    lock (_lockObject)
                    {
                        _currentHandLandmarks.HandDetected = false;
                    }
                    return;
                }
                
                // Parse landmark points
                List<PointF> points = new List<PointF>();
                foreach (string line in lines)
                {
                    string[] parts = line.Split(',');
                    if (parts.Length == 2)
                    {
                        if (float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float y))
                        {
                            points.Add(new PointF(x, y));
                        }
                    }
                }
                
                if (points.Count > 0)
                {
                    Debug.WriteLine($"Loaded {points.Count} hand landmarks");
                    
#pragma warning disable CA1416 // Validate platform compatibility
                    // Load debug image if it exists
                    Bitmap debugImage = null;
                    if (File.Exists(_currentDebugImagePath))
                    {
                        debugImage = new Bitmap(_currentDebugImagePath);
                    }
                    
                    // Update landmarks
                    lock (_lockObject)
                    {
                        // Dispose previous debug image
                        if (_currentHandLandmarks.DebugImage != null)
                        {
                            _currentHandLandmarks.DebugImage.Dispose();
                        }
                        
                        _currentHandLandmarks.Points = points;
                        _currentHandLandmarks.DebugImage = debugImage;
                        _currentHandLandmarks.HandDetected = true;
                    }
#pragma warning restore CA1416 // Validate platform compatibility
                }
                else
                {
                    Debug.WriteLine("No valid landmarks found in file");
                    lock (_lockObject)
                    {
                        _currentHandLandmarks.HandDetected = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading hand landmarks: {ex.Message}");
                lock (_lockObject)
                {
                    _currentHandLandmarks.HandDetected = false;
                }
            }
        }
        
        // Public method to stop the camera (overrides base method)
        // The base method must be marked as virtual for this to work
        public void StopCameraWithHandTracking()
        {
            try
            {
                // Clean up hand tracking resources
                if (_handTrackingTask != null && !_handTrackingTask.IsCompleted)
                {
                    try
                    {
                        // Wait briefly for the task to complete
                        _handTrackingTask.Wait(500);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error waiting for hand tracking task: {ex.Message}");
                    }
                }
                
                // Clean up the current hand landmarks
                lock (_lockObject)
                {
                    _currentHandLandmarks.Dispose();
                    _currentHandLandmarks = new HandLandmarks();
                }
                
                _isProcessingHands = false;
                
                // Call base StopCamera method
                StopCamera();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in hand tracking StopCamera: {ex.Message}");
                _isProcessingHands = false;
                
                // Call base implementation even if there was an error
                StopCamera();
            }
        }
        
        public override void RemovedFromDocument(GH_Document document)
        {
            try
            {
                // Clean up hand tracking resources
                lock (_lockObject)
                {
                    _currentHandLandmarks.Dispose();
                }
                
                // Clean up temporary directory
                try
                {
                    if (Directory.Exists(_handTrackTempDir))
                    {
                        Directory.Delete(_handTrackTempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deleting temp directory: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in hand tracking RemovedFromDocument: {ex.Message}");
            }
            
            // Call base implementation
            base.RemovedFromDocument(document);
        }
        
        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            try
            {
                if (context == GH_DocumentContext.Close)
                {
                    // Clean up hand tracking resources
                    lock (_lockObject)
                    {
                        _currentHandLandmarks.Dispose();
                    }
                    
                    // Clean up temporary directory
                    try
                    {
                        if (Directory.Exists(_handTrackTempDir))
                        {
                            Directory.Delete(_handTrackTempDir, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error deleting temp directory: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in hand tracking DocumentContextChanged: {ex.Message}");
            }
            
            // Call base implementation
            base.DocumentContextChanged(document, context);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        public override Guid ComponentGuid => new Guid("e07bf1f4-d31c-4479-9e25-fa234b4c0bb5");
    }
    
    // Hand tracking webcam viewer - extends the standard webcam viewer with hand tracking visualization
    public class HandTrackingWebcamViewer : WebcamViewer
    {
        private Button _trackHandsButton;
        private HandTrackingWebcamComponent _handTrackingComponent;
        
        public HandTrackingWebcamViewer(HandTrackingWebcamComponent component)
            : base(component)
        {
            _handTrackingComponent = component;
            
            // Add hand tracking button
            _trackHandsButton = new Button();
            _trackHandsButton.Text = "Hand Tracking: OFF";
            _trackHandsButton.Width = 120;
            _trackHandsButton.Height = 24;
            _trackHandsButton.BackColor = Color.LightGray;
            _trackHandsButton.FlatStyle = FlatStyle.Flat;
            _trackHandsButton.Font = new Font("Arial", 8);
            _trackHandsButton.Click += OnTrackHandsButtonClick;
            
            // Add button to form
            this.Controls.Add(_trackHandsButton);
            _trackHandsButton.Location = new System.Drawing.Point(this.ClientSize.Width - _trackHandsButton.Width - 5, this.ClientSize.Height - _trackHandsButton.Height - 5);
            _trackHandsButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            
            // Update hand tracking button to reflect current state
            UpdateHandTrackingButton(_handTrackingComponent._enableHandTracking);
            
            // Add custom painting for hand landmarks
            this.Paint += HandTrackingViewer_Paint;
        }
        
        private void UpdateHandTrackingButton(bool enabled)
        {
            if (enabled)
            {
                _trackHandsButton.Text = "Hand Tracking: ON";
                _trackHandsButton.BackColor = Color.LightGreen;
            }
            else
            {
                _trackHandsButton.Text = "Hand Tracking: OFF";
                _trackHandsButton.BackColor = Color.LightGray;
            }
        }
        
        private void OnTrackHandsButtonClick(object sender, EventArgs e)
        {
            // Toggle hand tracking in the component
            _handTrackingComponent._enableHandTracking = !_handTrackingComponent._enableHandTracking;
            
            // Update button appearance
            UpdateHandTrackingButton(_handTrackingComponent._enableHandTracking);
            
            // Refresh the form to update hand tracking visualization
            this.Invalidate();
        }
        
        private void HandTrackingViewer_Paint(object sender, PaintEventArgs e)
        {
            if (_handTrackingComponent._enableHandTracking && 
                _handTrackingComponent._currentHandLandmarks != null && 
                _handTrackingComponent._currentHandLandmarks.HandDetected)
            {
                try
                {
                    // Find the PictureBox control
                    PictureBox pictureBox = null;
                    foreach (Control control in this.Controls)
                    {
                        if (control is BufferedPictureBox)
                        {
                            pictureBox = (BufferedPictureBox)control;
                            break;
                        }
                    }
                    
                    if (pictureBox != null && pictureBox.Image != null && 
                        _handTrackingComponent._currentHandLandmarks.Points.Count > 0)
                    {
                        // Get display rectangle of the image in the form
                        Rectangle imageRect = pictureBox.RectangleToScreen(pictureBox.ClientRectangle);
                        Rectangle formRect = this.RectangleToClient(imageRect);
                        
                        // Create a pen for drawing landmarks
                        using (Pen landmarkPen = new Pen(Color.Lime, 3f))
                        using (Pen connectionPen = new Pen(Color.Yellow, 1.5f))
                        {
                            // Draw each landmark point
                            foreach (PointF point in _handTrackingComponent._currentHandLandmarks.Points)
                            {
                                // Transform normalized coordinates (0-1) to picture box coordinates
                                float x = formRect.Left + point.X * formRect.Width;
                                float y = formRect.Top + point.Y * formRect.Height;
                                
                                // Draw a small circle at the landmark position
                                e.Graphics.DrawEllipse(landmarkPen, x - 2, y - 2, 4, 4);
                            }
                            
                            // Draw connections between landmarks (simplified)
                            // MediaPipe hand model has 21 points (0-20)
                            if (_handTrackingComponent._currentHandLandmarks.Points.Count >= 21)
                            {
                                // Draw palm connections
                                DrawConnection(e.Graphics, 0, 1, connectionPen, formRect);
                                DrawConnection(e.Graphics, 0, 5, connectionPen, formRect);
                                DrawConnection(e.Graphics, 0, 9, connectionPen, formRect);
                                DrawConnection(e.Graphics, 0, 13, connectionPen, formRect);
                                DrawConnection(e.Graphics, 0, 17, connectionPen, formRect);
                                
                                // Draw finger connections
                                for (int finger = 0; finger < 5; finger++)
                                {
                                    int base_idx = finger * 4 + 1;
                                    DrawConnection(e.Graphics, base_idx, base_idx + 1, connectionPen, formRect);
                                    DrawConnection(e.Graphics, base_idx + 1, base_idx + 2, connectionPen, formRect);
                                    DrawConnection(e.Graphics, base_idx + 2, base_idx + 3, connectionPen, formRect);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error drawing hand landmarks: {ex.Message}");
                }
            }
        }
        
        private void DrawConnection(Graphics g, int idx1, int idx2, Pen pen, Rectangle bounds)
        {
            try
            {
                if (idx1 < 0 || idx2 < 0 || 
                    idx1 >= _handTrackingComponent._currentHandLandmarks.Points.Count || 
                    idx2 >= _handTrackingComponent._currentHandLandmarks.Points.Count)
                {
                    return;
                }
                
                PointF p1 = _handTrackingComponent._currentHandLandmarks.Points[idx1];
                PointF p2 = _handTrackingComponent._currentHandLandmarks.Points[idx2];
                
                // Transform normalized coordinates (0-1) to picture box coordinates
                float x1 = bounds.Left + p1.X * bounds.Width;
                float y1 = bounds.Top + p1.Y * bounds.Height;
                float x2 = bounds.Left + p2.X * bounds.Width;
                float y2 = bounds.Top + p2.Y * bounds.Height;
                
                g.DrawLine(pen, x1, y1, x2, y2);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error drawing connection: {ex.Message}");
            }
        }
        
        // Add hand tracking status to any status display
        public void AddHandTrackingStatus(Label statusLabel)
        {
            if (statusLabel == null)
                return;
                
            // Add hand tracking status to the label if available
            if (_handTrackingComponent._enableHandTracking)
            {
                statusLabel.Text += " | Hand Tracking";
                
                // Add detected hands status if landmarks are available
                if (_handTrackingComponent._currentHandLandmarks != null && 
                    _handTrackingComponent._currentHandLandmarks.HandDetected)
                {
                    statusLabel.Text += " (Hand detected)";
                }
            }
        }
    }
}