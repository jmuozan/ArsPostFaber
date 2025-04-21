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
            
            // Output empty arrays for landmarks if hand tracking is disabled
            if (!_enableHandTracking)
            {
                DA.SetDataList(2, new List<PointF>());
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        public override Guid ComponentGuid => new Guid("e07bf1f4-d31c-4479-9e25-fa234b4c0bb5");
    }
    
    // Hand tracking viewer form that uses the shared BufferedPictureBox from WebcamComponent
    public class HandTrackingViewer : Form
    {
        internal BufferedPictureBox _pictureBox;
        private Label _statusLabel;
        private HandTrackingWebcamComponent _component;
        
        public HandTrackingViewer(HandTrackingWebcamComponent component)
        {
            _component = component;
            
            // Form setup
            this.Text = "Hand Tracking Viewer";
            this.ClientSize = new Size(640, 480);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            
            // Status label
            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Bottom;
            _statusLabel.Height = 24;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.BackColor = Color.FromArgb(64, 64, 64);
            _statusLabel.ForeColor = Color.White;
            
            // PictureBox setup
            _pictureBox = new BufferedPictureBox();
            _pictureBox.Dock = DockStyle.Fill;
            _pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            _pictureBox.BackColor = Color.Black;
            
            // Add controls
            this.Controls.Add(_pictureBox);
            this.Controls.Add(_statusLabel);
        }
    }
}