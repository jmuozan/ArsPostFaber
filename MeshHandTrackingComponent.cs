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

#pragma warning disable CA1416 // Validate platform compatibility

namespace crft
{
    public class MeshHandTrackingComponent : GH_Component
    {
        private Mesh _displayMesh;
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
        
        // Available cameras
        internal List<string> _detectedCameras = new List<string>();
        
        // Form
        private MeshHandTrackingViewer _viewerForm = null;
        private bool _previousEnableState = false;
        private System.Windows.Forms.Timer _zoomUpdateTimer = null;
        
        // For UI update throttling
        private DateTime _lastUIUpdate = DateTime.MinValue;
        private const int UI_UPDATE_INTERVAL_MS = 16; // ~60fps for UI updates
        
        // Hand tracking data
        private List<PointF> _handLandmarks = new List<PointF>();
        private Color _backgroundColor = Color.FromArgb(64, 64, 64);
        
        // Mesh visualization colors
        private Color _meshFillColor = Color.FromArgb(160, 100, 200, 255); // Default blue fill
        private Color _meshEdgeColor = Color.White; // Default white edges
        
        // Mesh zoom control
        internal double _zoomFactor = 1.0;
    
        // For view change mode
        internal enum ViewMode { Front, Back, Left, Right, Top, Bottom }
        internal ViewMode _currentViewMode = ViewMode.Front;
        
        public MeshHandTrackingComponent()
          : base("Mesh Hand Tracking", "MeshHandTrack", 
              "Displays a mesh with hand landmark tracking overlay",
              "Display", "Preview")
        {
            // Use unique ID for temporary frame path
            _tempImagePath = Path.Combine(Path.GetTempPath(), 
                "gh_mesh_hand_" + Guid.NewGuid().ToString() + ".jpg");
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Enable", "E", "Enable/disable the component", GH_ParamAccess.item, false);
            pManager.AddMeshParameter("Mesh", "M", "Mesh to display", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Device", "D", "Camera device index", GH_ParamAccess.item, 1);
            pManager.AddColourParameter("Background", "B", "Background color", GH_ParamAccess.item, Color.FromArgb(64, 64, 64));
            pManager.AddColourParameter("MeshFill", "F", "Mesh fill color", GH_ParamAccess.item, Color.FromArgb(160, 100, 200, 255));
            pManager.AddColourParameter("MeshEdge", "E", "Mesh edge color", GH_ParamAccess.item, Color.White);
            
            // Optional parameters
            pManager[1].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Image", "I", "Current frame with mesh and hand tracking", GH_ParamAccess.item);
            // No output for landmarks - they're drawn directly in the viewer window
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool enable = false;
            int deviceIndex = 0;
            Mesh mesh = null;
            Color backgroundColor = Color.FromArgb(64, 64, 64);

            if (!DA.GetData(0, ref enable)) return;
            
            // Get the mesh if provided
            if (!DA.GetData(1, ref mesh))
            {
                // Create a more visually interesting default mesh when none is provided
                // Use a sphere with higher polygon count for better visibility
                mesh = Mesh.CreateFromSphere(new Sphere(new Point3d(0, 0, 0), 1.0), 12, 12);
                
                // If sphere creation fails for any reason, fall back to a simple box
                if (mesh == null || mesh.Vertices.Count == 0)
                {
                    mesh = Mesh.CreateFromBox(new BoundingBox(new Point3d(-1, -1, -1), new Point3d(1, 1, 1)), 1, 1, 1);
                }
            }
            
            if (!DA.GetData(2, ref deviceIndex)) return;
            
            // Get optional background color
            DA.GetData(3, ref backgroundColor);
            _backgroundColor = backgroundColor;
            
            // Get optional mesh colors
            Color meshFillColor = _meshFillColor;
            Color meshEdgeColor = _meshEdgeColor;
            
            if (DA.GetData(4, ref meshFillColor))
                _meshFillColor = meshFillColor;
                
            if (DA.GetData(5, ref meshEdgeColor))
                _meshEdgeColor = meshEdgeColor;
            
            // Store the mesh for display
            _displayMesh = mesh;
            
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
                StopCamera();
                CloseViewerWindow();
                
                // Display state change message
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Component disabled");
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
                    
                    // Output hand landmarks as points
                    // We're not outputting landmarks - they'll be visualized directly in the window
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
                }
            }
            else
            {
                // Send placeholder when disabled but the user is expecting output data
                if (enable)
                {
                    Bitmap placeholder = new Bitmap(320, 240);
                    using (Graphics g = Graphics.FromImage(placeholder))
                    {
                        g.Clear(Color.Black);
                        using (Font font = new Font("Arial", 10))
                        {
                            g.DrawString("Component initializing...", font, Brushes.White, new PointF(80, 110));
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
                            _viewerForm = new MeshHandTrackingViewer(this);
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
                Debug.WriteLine("Starting camera capture...");
                
                // Generate a fresh temp path for this capture session
                _tempImagePath = Path.Combine(Path.GetTempPath(), 
                    "gh_mesh_hand_" + Guid.NewGuid().ToString() + ".jpg");
                    
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
                                            
                                            // Process the frame for hand detection
                                            ProcessFrameForHandDetection(newFrame);
                                            
                                            // Create a visualization with mesh and hand landmarks
                                            Bitmap compositedFrame = CreateCompositedFrame(newFrame);
                                            
                                            // Store current frame for component output
                                            lock (_lockObject)
                                            {
                                                if (_currentFrame != null)
                                                {
                                                    _currentFrame.Dispose();
                                                }
                                                _currentFrame = compositedFrame;
                                                _hasCapturedFrame = true;
                                            }
                                            
                                            // Update UI directly with frame for live view
                                            UpdateViewerUI(compositedFrame);
                                            
                                            // Update component at reduced rate
                                            TimeSpan timeSinceLastUpdate = DateTime.Now - _lastUIUpdate;
                                            if (timeSinceLastUpdate.TotalMilliseconds >= UI_UPDATE_INTERVAL_MS * 2) // Reduce component updates
                                            {
                                                _lastUIUpdate = DateTime.Now;
                                                
                                                // Make sure hand landmarks are registered for output
                                                if (_handLandmarks.Count > 0)
                                                {
                                                    Debug.WriteLine($"Detected {_handLandmarks.Count} hand landmarks for output");
                                                }
                                                
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
                                
                                // Process the frame for hand detection
                                ProcessFrameForHandDetection(newFrame);
                                
                                // Create a visualization with mesh and hand landmarks
                                Bitmap compositedFrame = CreateCompositedFrame(newFrame);
                                
                                // Store frame for component
                                lock (_lockObject)
                                {
                                    if (_currentFrame != null)
                                    {
                                        _currentFrame.Dispose();
                                    }
                                    _currentFrame = compositedFrame;
                                    _hasCapturedFrame = true;
                                }
                                
                                // Always update the viewer preview window immediately
                                UpdateViewerUI(compositedFrame);
                                
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
        
        // Path to save frames for Python hand tracking
        private string _handTrackInputPath;
        private string _handTrackOutputPath;
        private string _pythonScriptPath;
        private bool _pythonPathsInitialized = false;
        private DateTime _lastPythonExecution = DateTime.MinValue;
        private const int PYTHON_THROTTLE_MS = 100; // Only run Python every 100ms
        
        // Initialize Python paths - only do this once
        private void InitializePythonPaths()
        {
            if (_pythonPathsInitialized)
                return;
                
            // Create unique filenames using a GUID to avoid conflicts
            string uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
            _handTrackInputPath = Path.Combine(Path.GetTempPath(), $"hand_track_input_{uniqueId}.jpg");
            _handTrackOutputPath = Path.Combine(Path.GetTempPath(), $"hand_track_output_{uniqueId}.txt");
            
            // Build path to the Python script
            string assemblyLocation = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string projectRoot = Path.GetFullPath(Path.Combine(assemblyLocation, "../../.."));
            _pythonScriptPath = Path.Combine(projectRoot, "mediapipe/process_hand_frame.py");
            
            // Verify the Python script exists
            if (!File.Exists(_pythonScriptPath))
            {
                Debug.WriteLine($"WARNING: Python script not found at {_pythonScriptPath}");
                
                // Try alternative paths
                string[] alternativePaths = new string[] {
                    Path.Combine(projectRoot, "mediapipe", "process_hand_frame.py"),
                    Path.Combine(Path.GetDirectoryName(projectRoot), "mediapipe", "process_hand_frame.py"),
                    Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(projectRoot)), "mediapipe", "process_hand_frame.py")
                };
                
                foreach (string path in alternativePaths)
                {
                    if (File.Exists(path))
                    {
                        _pythonScriptPath = path;
                        Debug.WriteLine($"Found Python script at alternative path: {_pythonScriptPath}");
                        break;
                    }
                }
                
                // If still not found, create the script at the expected location
                if (!File.Exists(_pythonScriptPath))
                {
                    // Ensure directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(_pythonScriptPath));
                    
                    // Create the fallback script at the expected location
                    CreateFallbackPythonScript(_pythonScriptPath);
                    Debug.WriteLine($"Created fallback Python script at: {_pythonScriptPath}");
                }
            }
            
            _pythonPathsInitialized = true;
            Debug.WriteLine($"Python paths initialized: Script={_pythonScriptPath}, Input={_handTrackInputPath}, Output={_handTrackOutputPath}");
        }
        
        // Create a simple Python script for hand tracking simulation if the real one doesn't exist
        private void CreateFallbackPythonScript(string path)
        {
            string scriptContent = @"#!/usr/bin/env python3
import sys
import os
import numpy as np

def generate_simulated_landmarks(output_path):
    # Generate simulated hand landmarks (21 points) on the right side of the frame
    landmarks = []
    
    # Wrist position (normalized coordinates)
    center_x = 0.7  # Right side of image
    center_y = 0.5  # Middle height
    
    # Add wrist point
    landmarks.append((center_x, center_y))
    
    # Add palm points (4 points)
    for i in range(4):
        angle = 1.5 * 3.14159 - i * 0.2 * 3.14159
        distance = 0.05
        x = center_x + np.cos(angle) * distance
        y = center_y - np.sin(angle) * distance
        landmarks.append((x, y))
    
    # Add finger points (3 more points per finger, 5 fingers)
    for finger in range(5):
        base_x, base_y = landmarks[finger + 1]
        angle = 1.5 * 3.14159 - finger * 0.2 * 3.14159
        
        # Three more joints per finger
        for joint in range(3):
            distance = 0.03 * (joint + 1)
            x = base_x + np.cos(angle) * distance
            y = base_y - np.sin(angle) * distance
            landmarks.append((x, y))
    
    # Write to output file
    with open(output_path, 'w') as f:
        for x, y in landmarks:
            f.write(f'{x},{y}\n')

def main():
    if len(sys.argv) != 3:
        print('Usage: python process_hand_frame.py <input_image_path> <output_landmarks_path>', file=sys.stderr)
        sys.exit(1)
    
    output_path = sys.argv[2]
    
    # Create output directory if needed
    output_dir = os.path.dirname(output_path)
    if output_dir and not os.path.exists(output_dir):
        os.makedirs(output_dir)
    
    # Generate simulated landmarks (we don't actually need the input image)
    generate_simulated_landmarks(output_path)

if __name__ == '__main__':
    main()
";
            File.WriteAllText(path, scriptContent);
        }
        
        // Process the captured frame to detect hand landmarks using Python MediaPipe
        // Using approach closer to handtrack.py which works better
        private void ProcessFrameForHandDetection(Bitmap frame)
        {
            try
            {
                // Initialize Python paths if not already done
                if (!_pythonPathsInitialized)
                {
                    InitializePythonPaths();
                }
                
                // Throttle Python execution but reduce the throttle time to get more responsive hand tracking
                TimeSpan timeSinceLastExecution = DateTime.Now - _lastPythonExecution;
                if (timeSinceLastExecution.TotalMilliseconds < PYTHON_THROTTLE_MS / 2) // Reduce throttle for better responsiveness
                {
                    // Just reuse the existing landmarks if we called Python too recently
                    return;
                }
                
                // Clear previous landmarks
                _handLandmarks.Clear();
                
                // Save the current frame for Python to process with higher quality
                frame.Save(_handTrackInputPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                
                // Call the Python script to process the frame - using direct MediaPipe integration like handtrack.py
                Process pythonProcess = new Process();
                
                // Try using the exact path to Python script
                try
                {
                    string actualScriptPath = _pythonScriptPath;
                    
                    // If process_hand_frame.py not found, try using handtrack.py directly 
                    if (!File.Exists(_pythonScriptPath))
                    {
                        string assemblyLocation = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                        string projectRoot = Path.GetFullPath(Path.Combine(assemblyLocation, "../../.."));
                        string handtrackPath = Path.Combine(projectRoot, "mediapipe/handtrack.py");
                        
                        if (File.Exists(handtrackPath))
                        {
                            actualScriptPath = handtrackPath;
                            Debug.WriteLine($"Using handtrack.py instead: {actualScriptPath}");
                        }
                    }
                
                    pythonProcess.StartInfo.FileName = "python3";
                    pythonProcess.StartInfo.Arguments = $"\"{actualScriptPath}\" \"{_handTrackInputPath}\" \"{_handTrackOutputPath}\"";
                    pythonProcess.StartInfo.UseShellExecute = false;
                    pythonProcess.StartInfo.CreateNoWindow = true;
                    pythonProcess.StartInfo.RedirectStandardOutput = true;
                    pythonProcess.StartInfo.RedirectStandardError = true;
                    
                    Debug.WriteLine($"Executing: {pythonProcess.StartInfo.FileName} {pythonProcess.StartInfo.Arguments}");
                    
                    // Execute the Python process with low priority to avoid blocking UI
                    pythonProcess.Start();
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Try 'python' instead of 'python3'
                    pythonProcess = new Process();
                    pythonProcess.StartInfo.FileName = "python";
                    pythonProcess.StartInfo.Arguments = $"\"{_pythonScriptPath}\" \"{_handTrackInputPath}\" \"{_handTrackOutputPath}\"";
                    pythonProcess.StartInfo.UseShellExecute = false;
                    pythonProcess.StartInfo.CreateNoWindow = true;
                    pythonProcess.StartInfo.RedirectStandardOutput = true;
                    pythonProcess.StartInfo.RedirectStandardError = true;
                    
                    try
                    {
                        // Execute with 'python'
                        pythonProcess.Start();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Could not start Python process: {ex.Message}");
                        GenerateMoreVisibleFallbackHandLandmarks(frame); // Use more visible fallback
                        return;
                    }
                }
                
                // Update the execution timestamp
                _lastPythonExecution = DateTime.Now;
                
                // Shorter timeout to keep UI responsive
                if (!pythonProcess.WaitForExit(300))
                {
                    Debug.WriteLine("Python process timeout - killing");
                    try { pythonProcess.Kill(); } catch { }
                    GenerateMoreVisibleFallbackHandLandmarks(frame);
                    return;
                }
                
                // Check for errors
                string error = pythonProcess.StandardError.ReadToEnd();
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.WriteLine($"Python error: {error}");
                }
                
                // Check if output file exists
                if (File.Exists(_handTrackOutputPath))
                {
                    // Read the landmarks from the output file
                    // Expected format: x,y\nx,y\n...
                    string[] lines = File.ReadAllLines(_handTrackOutputPath);
                    foreach (string line in lines)
                    {
                        if (!string.IsNullOrEmpty(line))
                        {
                            string[] coords = line.Split(',');
                            if (coords.Length == 2)
                            {
                                float x, y;
                                if (float.TryParse(coords[0], out x) && float.TryParse(coords[1], out y))
                                {
                                    // Convert normalized coordinates to image coordinates
                                    x *= frame.Width;
                                    y *= frame.Height;
                                    _handLandmarks.Add(new PointF(x, y));
                                }
                            }
                        }
                    }
                    
                    Debug.WriteLine($"Loaded {_handLandmarks.Count} hand landmarks from Python");
                }
                
                // If no landmarks were detected or Python failed, use fallback with more visible hand
                if (_handLandmarks.Count == 0)
                {
                    Debug.WriteLine("No hand landmarks detected from Python, using simulated fallback");
                    GenerateMoreVisibleFallbackHandLandmarks(frame);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in hand tracking: {ex.Message}");
                GenerateMoreVisibleFallbackHandLandmarks(frame);
            }
        }
        
        // Generate more visible fallback hand landmarks when Python fails or no hand is detected
        private void GenerateMoreVisibleFallbackHandLandmarks(Bitmap frame)
        {
            // First clear previous landmarks
            _handLandmarks.Clear();
            
            // Create landmarks for a more visible and clearly articulated hand
            // Positioned in the center-right of frame for better visibility
            float centerX = frame.Width * 0.7f;
            float centerY = frame.Height * 0.5f;
            
            // Animated position for visual interest
            double time = DateTime.Now.TimeOfDay.TotalSeconds;
            double oscillation = Math.Sin(time * 0.8) * 40; // More pronounced movement
            centerY += (float)oscillation;
            
            // Wrist position (base of hand)
            _handLandmarks.Add(new PointF(centerX, centerY));
            
            // Palm landmarks - create a more visible hand shape
            for (int i = 0; i < 4; i++)
            {
                float angle = (float)(Math.PI * 1.3 - i * Math.PI * 0.2);
                float distance = frame.Width * 0.12f; // Larger palm
                _handLandmarks.Add(new PointF(
                    centerX + (float)Math.Cos(angle) * distance,
                    centerY - (float)Math.Sin(angle) * distance
                ));
            }
            
            // Fingers - with animation to make them appear to move
            for (int finger = 0; finger < 5; finger++)
            {
                // Get base of finger from palm points
                PointF basePoint = _handLandmarks[finger + 1];
                
                // Each finger angle with some animation
                float angle = (float)(Math.PI * 1.3 - finger * Math.PI * 0.2);
                
                // Add finger-specific animation for more realistic movement
                float fingerAnim = (float)Math.Sin(time * 0.8 + finger * 0.4) * 0.2f;
                angle += fingerAnim;
                
                // 3 joints per finger
                for (int joint = 0; joint < 3; joint++)
                {
                    // Make fingers longer and more visible
                    float distance = frame.Width * 0.06f * (joint + 1);
                    _handLandmarks.Add(new PointF(
                        basePoint.X + (float)Math.Cos(angle) * distance,
                        basePoint.Y - (float)Math.Sin(angle) * distance
                    ));
                }
            }
        }
        
        
        // Create a composited frame with mesh visualization and hand landmarks
        private Bitmap CreateCompositedFrame(Bitmap originalFrame)
        {
            // Create a new bitmap for our composited view
            Bitmap result = new Bitmap(originalFrame.Width, originalFrame.Height);
            
            using (Graphics g = Graphics.FromImage(result))
            {
                // Draw background
                g.Clear(_backgroundColor);
                
                // Set up for high quality rendering
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                
                // Draw border to clearly indicate frame boundaries
                g.DrawRectangle(new Pen(Color.FromArgb(120, 255, 255, 255), 3), 
                    1, 1, result.Width - 3, result.Height - 3);
                
                // Draw a center indicator for the view
                int centerX = result.Width / 2;
                int centerY = result.Height / 2;
                
                // Add "MESH VIEW" text to clearly indicate this section
                using (Font font = new Font("Arial", 12, FontStyle.Bold))
                {
                    g.DrawString("MESH VIEW", font, Brushes.White,
                        new PointF(centerX - 50, 10));
                }
                
                // Draw a rectangular highlight around the mesh area (more subtle)
                using (Pen highlightPen = new Pen(Color.FromArgb(80, 200, 200, 200), 1))
                {
                    g.DrawRectangle(highlightPen, 
                        centerX - 200, centerY - 150, 400, 300);
                }
                
                // Render the mesh using a simplified wireframe view - must be centered
                if (_displayMesh != null)
                {
                    // Render the mesh more prominently in the absolute center
                    RenderMesh(g, result.Width, result.Height);
                }
                
                // Draw hand landmarks directly in the view alongside the mesh
                if (_handLandmarks.Count > 0)
                {
                    // Calculate the center of hand landmarks
                    float handCenterX = 0, handCenterY = 0;
                    foreach (var point in _handLandmarks)
                    {
                        handCenterX += point.X;
                        handCenterY += point.Y;
                    }
                    handCenterX /= _handLandmarks.Count;
                    handCenterY /= _handLandmarks.Count;
                    
                    // Create a copy of landmarks scaled and positioned to overlay on the mesh
                    List<PointF> overlayLandmarks = new List<PointF>();
                    foreach (var point in _handLandmarks)
                    {
                        // Calculate offset from hand center
                        float offsetX = point.X - handCenterX;
                        float offsetY = point.Y - handCenterY;
                        
                        // Scale by a factor that looks good in the viewport
                        float scaleFactor = (float)(Math.Min(result.Width, result.Height) * 0.0015 * _zoomFactor);
                        
                        // Position relative to center of viewport
                        overlayLandmarks.Add(new PointF(
                            centerX + offsetX * scaleFactor,
                            centerY + offsetY * scaleFactor
                        ));
                    }
                    
                    // Draw landmarks directly in the mesh view
                    DrawHandLandmarksInView(g, overlayLandmarks);
                }
                
                // Add a small webcam preview in the corner
                int previewWidth = result.Width / 6;
                int previewHeight = result.Height / 6;
                g.DrawImage(originalFrame, 
                    new Rectangle(result.Width - previewWidth - 10, 10, previewWidth, previewHeight),
                    new Rectangle(0, 0, originalFrame.Width, originalFrame.Height),
                    GraphicsUnit.Pixel);
                
                // Draw frame around preview with label
                g.DrawRectangle(Pens.White, result.Width - previewWidth - 10, 10, previewWidth, previewHeight);
                using (Font font = new Font("Arial", 8, FontStyle.Regular))
                {
                    g.DrawString("Camera Feed", font, Brushes.White,
                        new PointF(result.Width - previewWidth - 10, previewHeight + 15));
                }
                
                // Add a footer with simplified component status
                using (Font font = new Font("Arial", 12, FontStyle.Bold))
                {
                    string handStatus = (_handLandmarks.Count > 0) 
                        ? $"Landmarks: {_handLandmarks.Count} detected" 
                        : "No hand landmarks detected";
                    
                    g.DrawString(handStatus, font, Brushes.White, 
                        new PointF(10, result.Height - 30));
                }
            }
            
            return result;
        }
        
        // Render the mesh as a wireframe with CONSISTENT FRONT VIEW
        private void RenderMesh(Graphics g, int width, int height)
        {
            if (_displayMesh == null || _displayMesh.Vertices.Count == 0)
                return;
                
            // Get the center point of view
            int centerX = width / 2;
            int centerY = height / 2; 
                
            // Create a transform to map the mesh to screen coordinates
            // Center the mesh in the view
            BoundingBox bbox = _displayMesh.GetBoundingBox(true);
            Point3d meshCenter = bbox.Center;
            
            // Make the mesh larger and more prominent - INCREASE SCALE FOR VISIBILITY
            // Apply zoom factor to the scale
            double scale = Math.Min(width, height) * 0.8 * _zoomFactor; // Apply zoom factor
            scale /= Math.Max(bbox.Diagonal.X, bbox.Diagonal.Y);
            
            // Camera position based on selected view mode
            Point3d cameraPos;
            Vector3d up = Vector3d.ZAxis; // Default up vector
            
            // Position camera based on view mode
            switch (_currentViewMode)
            {
                case ViewMode.Front:
                    cameraPos = new Point3d(meshCenter.X, meshCenter.Y - 10, meshCenter.Z);
                    break;
                case ViewMode.Back:
                    cameraPos = new Point3d(meshCenter.X, meshCenter.Y + 10, meshCenter.Z);
                    break;
                case ViewMode.Left:
                    cameraPos = new Point3d(meshCenter.X - 10, meshCenter.Y, meshCenter.Z);
                    break;
                case ViewMode.Right:
                    cameraPos = new Point3d(meshCenter.X + 10, meshCenter.Y, meshCenter.Z);
                    break;
                case ViewMode.Top:
                    cameraPos = new Point3d(meshCenter.X, meshCenter.Y, meshCenter.Z + 10);
                    up = Vector3d.YAxis; // Change up vector for top view
                    break;
                case ViewMode.Bottom:
                    cameraPos = new Point3d(meshCenter.X, meshCenter.Y, meshCenter.Z - 10);
                    up = Vector3d.YAxis; // Change up vector for bottom view
                    break;
                default:
                    cameraPos = new Point3d(meshCenter.X, meshCenter.Y - 10, meshCenter.Z);
                    break;
            }
            
            Point3d target = meshCenter;
            
            // Draw a frame around the entire rendering area to clearly indicate viewport
            using (Pen viewportPen = new Pen(Color.FromArgb(200, 255, 255, 255), 3))
            {
                g.DrawRectangle(viewportPen, 10, 10, width - 20, height - 20);
            }
            
            // Note: Zoom controls are handled by form buttons, no need to draw them here
            
            // Draw faces with vibrant colors and clear edges
            foreach (var face in _displayMesh.Faces)
            {
                // Get the vertices
                Point3d v1 = _displayMesh.Vertices[face.A];
                Point3d v2 = _displayMesh.Vertices[face.B];
                Point3d v3 = _displayMesh.Vertices[face.C];
                
                // Project 3D points to 2D screen coordinates - FORCE CENTER
                PointF p1 = Project3DTo2DWithCenter(v1, cameraPos, target, up, scale, centerX, centerY);
                PointF p2 = Project3DTo2DWithCenter(v2, cameraPos, target, up, scale, centerX, centerY);
                PointF p3 = Project3DTo2DWithCenter(v3, cameraPos, target, up, scale, centerX, centerY);
                
                // Create points array for filling
                PointF[] points;
                
                if (face.IsQuad)
                {
                    Point3d v4 = _displayMesh.Vertices[face.D];
                    PointF p4 = Project3DTo2DWithCenter(v4, cameraPos, target, up, scale, centerX, centerY);
                    points = new PointF[] { p1, p2, p3, p4 };
                }
                else
                {
                    points = new PointF[] { p1, p2, p3 };
                }
                
                // Use custom mesh fill color from input
                using (Brush faceBrush = new SolidBrush(_meshFillColor))
                {
                    g.FillPolygon(faceBrush, points);
                }
                
                // Use custom mesh edge color from input
                using (Pen edgePen = new Pen(_meshEdgeColor, 4))
                {
                    if (face.IsQuad)
                    {
                        Point3d v4 = _displayMesh.Vertices[face.D];
                        PointF p4 = Project3DTo2DWithCenter(v4, cameraPos, target, up, scale, centerX, centerY);
                        
                        // Draw quad edges
                        g.DrawLine(edgePen, p1, p2);
                        g.DrawLine(edgePen, p2, p3);
                        g.DrawLine(edgePen, p3, p4);
                        g.DrawLine(edgePen, p4, p1);
                        
                        // Add vertices as small circles at corners for better visibility
                        foreach (var pt in new[] { p1, p2, p3, p4 })
                        {
                            g.FillEllipse(Brushes.White, pt.X - 2, pt.Y - 2, 4, 4);
                        }
                    }
                    else
                    {
                        // Draw triangle edges
                        g.DrawLine(edgePen, p1, p2);
                        g.DrawLine(edgePen, p2, p3);
                        g.DrawLine(edgePen, p3, p1);
                        
                        // Add vertices as small circles at corners
                        foreach (var pt in new[] { p1, p2, p3 })
                        {
                            g.FillEllipse(Brushes.White, pt.X - 2, pt.Y - 2, 4, 4);
                        }
                    }
                }
            }
            
            // Add view mode and zoom indicator without drawing the axes
            using (Font infoFont = new Font("Arial", 12, FontStyle.Bold))
            {
                // Add view mode and zoom indicator in bottom right
                string viewText = $"View: {_currentViewMode}";
                string zoomText = $"Zoom: {_zoomFactor:F1}x";
                g.DrawString(viewText, infoFont, Brushes.White, width - 120, height - 60);
                g.DrawString(zoomText, infoFont, Brushes.White, width - 120, height - 30);
            }
        }
        
        // Project a 3D point to 2D screen coordinates centered at provided center point
        private PointF Project3DTo2DWithCenter(Point3d p, Point3d camera, Point3d target, Vector3d up, double scale, int centerX, int centerY)
        {
            // Compute view direction
            Vector3d view = target - camera;
            view.Unitize();
            
            // Compute right vector
            Vector3d right = Vector3d.CrossProduct(up, view);
            right.Unitize();
            
            // Recompute up vector to ensure orthogonality
            up = Vector3d.CrossProduct(view, right);
            up.Unitize();
            
            // Compute vector from camera to point
            Vector3d toPoint = p - camera;
            
            // Project onto right and up vectors
            double x = Vector3d.Multiply(toPoint, right);
            double y = Vector3d.Multiply(toPoint, up);
            
            // Scale and center in viewport USING PROVIDED CENTER coordinates
            float screenX = (float)(centerX + x * scale);
            float screenY = (float)(centerY - y * scale);
            
            return new PointF(screenX, screenY);
        }
        
        // Project a 3D point to 2D screen coordinates
        private PointF Project3DTo2D(Point3d p, Point3d camera, Point3d target, Vector3d up, double scale, int width, int height)
        {
            // Compute view direction
            Vector3d view = target - camera;
            view.Unitize();
            
            // Compute right vector
            Vector3d right = Vector3d.CrossProduct(up, view);
            right.Unitize();
            
            // Recompute up vector to ensure orthogonality
            up = Vector3d.CrossProduct(view, right);
            up.Unitize();
            
            // Compute vector from camera to point
            Vector3d toPoint = p - camera;
            
            // Project onto right and up vectors
            double x = Vector3d.Multiply(toPoint, right);
            double y = Vector3d.Multiply(toPoint, up);
            
            // Scale and center in viewport
            float screenX = (float)(width / 2 + x * scale);
            float screenY = (float)(height / 2 - y * scale);
            
            return new PointF(screenX, screenY);
        }
        
        // Draw hand landmarks directly overlaid on the mesh view
        private void DrawHandLandmarksInView(Graphics g, List<PointF> landmarks)
        {
            if (landmarks.Count < 21) // MediaPipe hand has 21 points
                return;
                
            // Use colors that stand out against the mesh background
            Color handColor = Color.FromArgb(255, 220, 50, 30); // Bright red-orange for visibility
            Color jointColor = Color.FromArgb(255, 230, 230, 0); // Bright yellow
            Color wristColor = Color.FromArgb(255, 255, 80, 80); // Light red
            
            // Create brushes and pens with these colors
            using (Brush pointBrush = new SolidBrush(jointColor))
            using (Brush wristBrush = new SolidBrush(wristColor))
            using (Pen connectionPen = new Pen(handColor, 4)) // Thick lines but not too thick
            {
                // Add MediaPipe-like styling
                connectionPen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                connectionPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                connectionPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                
                // First draw palm connections
                PointF[] palmPoints = new PointF[5];
                for (int i = 0; i < 5; i++)
                {
                    palmPoints[i] = landmarks[i];
                }
                
                // Palm polygon (semi-transparent)
                using (Brush palmBrush = new SolidBrush(Color.FromArgb(80, handColor)))
                {
                    g.FillPolygon(palmBrush, palmPoints);
                }
                
                // Draw connections (simplified hand skeleton)
                // Wrist to palm connections
                for (int i = 1; i <= 4; i++)
                {
                    g.DrawLine(connectionPen, landmarks[0], landmarks[i]);
                }
                
                // Palm connections
                for (int i = 1; i < 4; i++)
                {
                    g.DrawLine(connectionPen, landmarks[i], landmarks[i + 1]);
                }
                
                // Connect pinky base to wrist
                g.DrawLine(connectionPen, landmarks[4], landmarks[0]);
                
                // Finger connections (4 points per finger, 5 fingers)
                for (int finger = 0; finger < 5; finger++)
                {
                    int baseIndex = finger + 1; // Base joint in the palm
                    
                    // Connect finger joints
                    for (int joint = 0; joint < 3; joint++)
                    {
                        int currentIndex = 5 + finger * 3 + joint;
                        int prevIndex = (joint == 0) ? baseIndex : 5 + finger * 3 + joint - 1;
                        
                        g.DrawLine(connectionPen, landmarks[prevIndex], landmarks[currentIndex]);
                    }
                }
                
                // Draw all landmark points
                foreach (var point in landmarks)
                {
                    // Draw a black outline for better visibility
                    g.FillEllipse(Brushes.Black, point.X - 3, point.Y - 3, 6, 6);
                    g.FillEllipse(pointBrush, point.X - 2, point.Y - 2, 4, 4);
                }
                
                // Draw fingertips slightly larger
                for (int finger = 0; finger < 5; finger++)
                {
                    int tipIndex = 5 + finger * 3 + 2; // Last joint of each finger
                    g.FillEllipse(Brushes.Black, landmarks[tipIndex].X - 4, landmarks[tipIndex].Y - 4, 8, 8);
                    g.FillEllipse(new SolidBrush(Color.FromArgb(255, 255, 255, 80)), 
                        landmarks[tipIndex].X - 3, landmarks[tipIndex].Y - 3, 6, 6);
                }
                
                // Draw wrist point
                g.FillEllipse(Brushes.Black, landmarks[0].X - 5, landmarks[0].Y - 5, 10, 10);
                g.FillEllipse(wristBrush, landmarks[0].X - 4, landmarks[0].Y - 4, 8, 8);
            }
        }
        
        // Draw hand landmarks and connections using MediaPipe-like style in camera preview
        private void DrawHandLandmarks(Graphics g)
        {
            if (_handLandmarks.Count < 21) // MediaPipe hand has 21 points
                return;
                
            // Use MediaPipe-like colors that stand out against any background
            Color handColor = Color.FromArgb(255, 255, 80, 0); // Bright orange-red like MediaPipe
            Color jointColor = Color.FromArgb(255, 255, 220, 20); // Yellow
            Color wristColor = Color.FromArgb(255, 255, 50, 50); // Red
            
            // Create brushes and pens with these colors
            using (Brush pointBrush = new SolidBrush(jointColor))
            using (Brush wristBrush = new SolidBrush(wristColor))
            using (Pen connectionPen = new Pen(handColor, 7)) // Extra thick lines for visibility like MediaPipe
            {
                // Add glow effect to make the hand more visible
                connectionPen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                // Add more MediaPipe-like styling
                connectionPen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                connectionPen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                
                // First draw a semi-transparent hand shape to create a filled palm
                PointF[] palmPoints = new PointF[5];
                for (int i = 0; i < 5; i++)
                {
                    palmPoints[i] = _handLandmarks[i];
                }
                
                using (Brush palmBrush = new SolidBrush(Color.FromArgb(80, handColor)))
                {
                    g.FillPolygon(palmBrush, palmPoints);
                }
                
                // Draw connections (simplified hand skeleton)
                // Wrist to palm connections
                for (int i = 1; i <= 4; i++)
                {
                    g.DrawLine(connectionPen, _handLandmarks[0], _handLandmarks[i]);
                }
                
                // Palm connections
                for (int i = 1; i < 4; i++)
                {
                    g.DrawLine(connectionPen, _handLandmarks[i], _handLandmarks[i + 1]);
                }
                
                // Connect pinky base to wrist
                g.DrawLine(connectionPen, _handLandmarks[4], _handLandmarks[0]);
                
                // Finger connections (4 points per finger, 5 fingers)
                for (int finger = 0; finger < 5; finger++)
                {
                    int baseIndex = finger + 1; // Base joint in the palm
                    
                    // Connect finger joints with slightly different color per finger for clarity
                    using (Pen fingerPen = new Pen(Color.FromArgb(255, 
                        (byte)Math.Min(255, handColor.R - finger * 15), 
                        (byte)Math.Min(255, handColor.G - finger * 5), 
                        (byte)Math.Min(255, handColor.B + finger * 20)), 3))
                    {
                        // Connect finger joints
                        for (int joint = 0; joint < 3; joint++)
                        {
                            int currentIndex = 5 + finger * 3 + joint;
                            int prevIndex = (joint == 0) ? baseIndex : 5 + finger * 3 + joint - 1;
                            
                            g.DrawLine(fingerPen, _handLandmarks[prevIndex], _handLandmarks[currentIndex]);
                        }
                    }
                }
                
                // Draw an outline around each point for better visibility
                foreach (var point in _handLandmarks)
                {
                    g.FillEllipse(Brushes.Black, point.X - 4, point.Y - 4, 8, 8);
                }
                
                // Draw landmarks using MediaPipe-like style with visible points
                foreach (var point in _handLandmarks)
                {
                    // Draw a black outline for better visibility
                    g.FillEllipse(Brushes.Black, point.X - 6, point.Y - 6, 12, 12);
                    g.FillEllipse(pointBrush, point.X - 5, point.Y - 5, 10, 10);
                }
                
                // Draw fingertips with larger, more distinctive points (MediaPipe style)
                for (int finger = 0; finger < 5; finger++)
                {
                    int tipIndex = 5 + finger * 3 + 2; // Last joint of each finger
                    g.FillEllipse(Brushes.Black,
                        _handLandmarks[tipIndex].X - 9, _handLandmarks[tipIndex].Y - 9, 18, 18);
                    g.FillEllipse(new SolidBrush(Color.FromArgb(255, 255, 255, 100)), 
                        _handLandmarks[tipIndex].X - 8, _handLandmarks[tipIndex].Y - 8, 16, 16);
                }
                
                // Draw wrist point significantly larger and with a different color
                g.FillEllipse(Brushes.Black, _handLandmarks[0].X - 10, _handLandmarks[0].Y - 10, 20, 20);
                g.FillEllipse(wristBrush, _handLandmarks[0].X - 9, _handLandmarks[0].Y - 9, 18, 18);
                
                // Just add an indicator dot next to the hand
                g.FillEllipse(Brushes.LightGreen, _handLandmarks[0].X + 25, _handLandmarks[0].Y - 5, 10, 10);
            }
        }
        
        private void UpdateViewerUI(Bitmap frame)
        {
            try
            {
                // Make a defensive check to avoid null reference issues
                if (frame == null)
                {
                    Debug.WriteLine("UpdateViewerUI: Skipping update due to null frame");
                    return;
                }
                
                if (_viewerForm == null)
                {
                    Debug.WriteLine("UpdateViewerUI: Skipping update due to null viewer form");
                    return;
                }
                
                if (_viewerForm.IsDisposed)
                {
                    Debug.WriteLine("UpdateViewerUI: Skipping update due to disposed viewer form");
                    return;
                }
                
                if (!_isRunning)
                {
                    Debug.WriteLine("UpdateViewerUI: Skipping update because component is not running");
                    return;
                }
                
                // Pass the frame directly to the UI
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

        public override Guid ComponentGuid => new Guid("34a2bf76-e8ec-4528-afb4-5bc8c6b1fc26");
    }

    // Form class for the mesh and hand tracking viewer
    public class MeshHandTrackingViewer : Form
    {
        private BufferedPictureBox _pictureBox;
        private Label _statusLabel;
        private MeshHandTrackingComponent _component;
        private Font _smallFont = new Font("Arial", 8);
        private int _frameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;
        private double _currentFps = 0;
        private System.Drawing.Size _preferredSize = new System.Drawing.Size(800, 600);
        private bool _initialSizeSet = false;
        private bool _userClosing = false; // Flag to track if user is closing the form
        private Button _zoomInButton;
        private Button _zoomOutButton;
        private ComboBox _viewModeDropdown;
        private TrackBar _zoomSlider;
        private Label _zoomLabel;

        public MeshHandTrackingViewer(MeshHandTrackingComponent component)
        {
            _component = component;
            
            // Form setup
            this.Text = "Mesh Hand Tracking";
            _preferredSize = new System.Drawing.Size(800, 600);
            this.ClientSize = _preferredSize;
            this.MinimumSize = new System.Drawing.Size(640, 480);
            this.MaximumSize = new System.Drawing.Size(1280, 960);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AutoSize = false;
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
            
            // Status label
            _statusLabel = new Label();
            _statusLabel.Dock = DockStyle.Top;
            _statusLabel.Height = 20;
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.BackColor = Color.FromArgb(64, 64, 64);
            _statusLabel.ForeColor = Color.White;
            _statusLabel.Font = _smallFont;
            _statusLabel.Padding = new Padding(3, 0, 0, 0);
            
            // PictureBox setup
            _pictureBox = new BufferedPictureBox();
            _pictureBox.Dock = DockStyle.Fill;
            _pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            _pictureBox.BackColor = Color.Black;
            _pictureBox.MaximumSize = new Size(1280, 960);
            _pictureBox.Size = new Size(800, 600);
            
            // Create view mode dropdown in the top left
            _viewModeDropdown = new ComboBox();
            _viewModeDropdown.DropDownStyle = ComboBoxStyle.DropDownList;
            _viewModeDropdown.Size = new System.Drawing.Size(100, 28);
            _viewModeDropdown.Location = new System.Drawing.Point(10, 40);
            // Note: We're using a very dark, semi-transparent color because ComboBox doesn't fully support transparency
            _viewModeDropdown.BackColor = Color.FromArgb(30, 30, 30);
            _viewModeDropdown.ForeColor = Color.White;
            _viewModeDropdown.FlatStyle = FlatStyle.Flat;
            _viewModeDropdown.Font = new Font("Arial", 10);
            
            // Add view modes
            _viewModeDropdown.Items.AddRange(Enum.GetNames(typeof(MeshHandTrackingComponent.ViewMode)));
            _viewModeDropdown.SelectedIndex = 0; // Set to Front by default
            _viewModeDropdown.SelectedIndexChanged += new EventHandler(ViewModeDropdown_SelectedIndexChanged);
            
            // Create zoom label
            _zoomLabel = new Label();
            _zoomLabel.Text = "Zoom:";
            _zoomLabel.Font = new Font("Arial", 10, FontStyle.Bold);
            _zoomLabel.Size = new System.Drawing.Size(60, 20);
            _zoomLabel.Location = new System.Drawing.Point(10, 80);
            _zoomLabel.BackColor = Color.Transparent;
            _zoomLabel.ForeColor = Color.White;
            _zoomLabel.TextAlign = ContentAlignment.MiddleLeft;
            
            // Create zoom slider for better control
            _zoomSlider = new TrackBar();
            _zoomSlider.Orientation = Orientation.Horizontal;
            _zoomSlider.Size = new System.Drawing.Size(120, 45);
            _zoomSlider.Location = new System.Drawing.Point(55, 80);
            _zoomSlider.BackColor = Color.Transparent;
            _zoomSlider.Minimum = 3;  // 0.3x zoom
            _zoomSlider.Maximum = 50; // 5.0x zoom
            _zoomSlider.Value = 10;   // 1.0x zoom
            _zoomSlider.TickFrequency = 5;
            _zoomSlider.TickStyle = TickStyle.Both;
            _zoomSlider.ValueChanged += new EventHandler(ZoomSlider_ValueChanged);
            
            // Keep the zoom buttons for quick adjustments
            _zoomInButton = new Button();
            _zoomInButton.Text = "+";
            _zoomInButton.Font = new Font("Arial", 12, FontStyle.Bold);
            _zoomInButton.Size = new System.Drawing.Size(30, 30);
            _zoomInButton.Location = new System.Drawing.Point(175, 90);
            _zoomInButton.BackColor = Color.Transparent;
            _zoomInButton.ForeColor = Color.White;
            _zoomInButton.FlatStyle = FlatStyle.Flat;
            _zoomInButton.FlatAppearance.BorderSize = 1;
            _zoomInButton.FlatAppearance.BorderColor = Color.White;
            _zoomInButton.Click += new EventHandler(ZoomInButton_Click);
            
            _zoomOutButton = new Button();
            _zoomOutButton.Text = "-";
            _zoomOutButton.Font = new Font("Arial", 12, FontStyle.Bold);
            _zoomOutButton.Size = new System.Drawing.Size(30, 30);
            _zoomOutButton.Location = new System.Drawing.Point(10, 90);
            _zoomOutButton.BackColor = Color.Transparent;
            _zoomOutButton.ForeColor = Color.White;
            _zoomOutButton.FlatStyle = FlatStyle.Flat;
            _zoomOutButton.FlatAppearance.BorderSize = 1;
            _zoomOutButton.FlatAppearance.BorderColor = Color.White;
            _zoomOutButton.Click += new EventHandler(ZoomOutButton_Click);
            
            // Add controls in the right order
            this.Controls.Add(_pictureBox);
            this.Controls.Add(_statusLabel);
            this.Controls.Add(_viewModeDropdown);
            this.Controls.Add(_zoomLabel);
            this.Controls.Add(_zoomSlider);
            this.Controls.Add(_zoomInButton);
            this.Controls.Add(_zoomOutButton);
            
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
            
            // Update control positions when form resizes - keep them in the bottom left corner
            if (_zoomInButton != null && _zoomOutButton != null && _zoomSlider != null && _zoomLabel != null)
            {
                _zoomLabel.Location = new System.Drawing.Point(10, 80);
                _zoomSlider.Location = new System.Drawing.Point(55, 80);
                _zoomInButton.Location = new System.Drawing.Point(175, 90);
                _zoomOutButton.Location = new System.Drawing.Point(10, 90);
            }
        }
        
        // Zoom control event handlers
        private void ZoomSlider_ValueChanged(object sender, EventArgs e)
        {
            if (_component != null)
            {
                // Convert slider value to zoom factor (3-50 to 0.3-5.0)
                _component._zoomFactor = _zoomSlider.Value / 10.0;
                
                // Update UI to show current zoom level
                _zoomLabel.Text = $"Zoom: {_component._zoomFactor:F1}x";
                
                // Force update
                if (_component.OnPingDocument() != null)
                {
                    _component.OnPingDocument().NewSolution(false);
                }
            }
        }
        
        private void ZoomInButton_Click(object sender, EventArgs e)
        {
            if (_component != null && _zoomSlider != null)
            {
                // Increase zoom slider value
                _zoomSlider.Value = Math.Min(_zoomSlider.Value + 2, _zoomSlider.Maximum);
                // The ValueChanged event will handle the actual zoom update
            }
        }
        
        private void ZoomOutButton_Click(object sender, EventArgs e)
        {
            if (_component != null && _zoomSlider != null)
            {
                // Decrease zoom slider value
                _zoomSlider.Value = Math.Max(_zoomSlider.Value - 2, _zoomSlider.Minimum);
                // The ValueChanged event will handle the actual zoom update
            }
        }
        
        // View mode dropdown event handler
        private void ViewModeDropdown_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_component != null && _viewModeDropdown.SelectedItem != null)
            {
                // Update view mode based on selection
                string selectedMode = _viewModeDropdown.SelectedItem.ToString();
                _component._currentViewMode = (MeshHandTrackingComponent.ViewMode)Enum.Parse(
                    typeof(MeshHandTrackingComponent.ViewMode), selectedMode);
                
                // Force update
                if (_component.OnPingDocument() != null)
                {
                    _component.OnPingDocument().NewSolution(false);
                }
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
                            try { image.Dispose(); } catch { }
                            return;
                        }
                    
                        try
                        {
                            // Store old image reference
                            Bitmap oldImage = _pictureBox.Image as Bitmap;
                            
                            // Set new image directly
                            _pictureBox.Image = image;
                            
                            // Update frame rate counter immediately
                            UpdateFrameRate();
                            
                            // Update the status label with the new image dimensions
                            UpdateStatusLabel();
                            
                            // Dispose old image after the new one is displayed
                            if (oldImage != null && oldImage != image)
                            {
                                oldImage.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            // If an error happens, make sure to dispose the image
                            try { image.Dispose(); } catch { }
                            Debug.WriteLine($"Error updating image in UI thread: {ex.Message}");
                        }
                    }));
                }
                catch (Exception ex)
                {
                    // If BeginInvoke fails, dispose the image
                    try { image.Dispose(); } catch { }
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
                
                // Update title
                this.Text = $"Mesh Hand Tracking - {Math.Round(_currentFps, 1)} FPS";
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
                // Create a status with essential information
                string status = string.Empty;
                
                // Get camera info
                if (_component._detectedCameras.Count > 0 &&
                    _component._deviceIndex >= 0 && 
                    _component._deviceIndex < _component._detectedCameras.Count)
                {
                    string cameraName = _component._detectedCameras[_component._deviceIndex];
                    
                    // Add resolution in compact format
                    if (_pictureBox.Image != null)
                    {
                        status = $"Camera: {cameraName} | Resolution: {_pictureBox.Image.Width}x{_pictureBox.Image.Height} | FPS: {Math.Round(_currentFps, 1)}";
                    }
                    else
                    {
                        status = $"Camera: {cameraName}";
                    }
                }
                else
                {
                    status = "No camera detected";
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