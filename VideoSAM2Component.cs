using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace crft
{
    public class VideoSAM2Component : GH_Component
    {
        private string _selectedVideoPath = "";
        private int _frameRate = 10;
        private Process _extractProcess;
        private Process _segmentProcess;
        private Process _maskProcess;
        private CancellationTokenSource _cancellationSource;
        private string _framesDir = "";
        private string _masksDir = "";
        private string _outputDir = "";
        private bool _isProcessing = false;
        
        public VideoSAM2Component()
          : base("SAM2 Video", "SAM2Video",
              "Segment videos using Segment Anything Model 2",
              "crft", "Segment")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Video Path", "V", "Path to video file to segment", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Frame Rate", "FPS", "Frame rate to extract from video (lower = faster processing)", GH_ParamAccess.item, 10);
            pManager.AddBooleanParameter("Run", "R", "Run the SAM2 segmentation UI", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Processing status", GH_ParamAccess.item);
            pManager.AddTextParameter("Output Path", "O", "Path to output directory with masked objects", GH_ParamAccess.item);
            pManager.AddTextParameter("Web Interface", "UI", "URL to the web interface (when running)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string videoPath = "";
            int frameRate = 10;
            bool run = false;

            if (!DA.GetData(0, ref videoPath)) return;
            if (!DA.GetData(1, ref frameRate)) return;
            if (!DA.GetData(2, ref run)) return;
            
            // Validate path
            if (string.IsNullOrEmpty(videoPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No video path provided");
                DA.SetData(0, "Error: No video path provided");
                return;
            }
            
            if (!File.Exists(videoPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Video file not found: {videoPath}");
                DA.SetData(0, $"Error: Video file not found");
                return;
            }
            
            // Debug info
            Debug.WriteLine($"Video path: {videoPath}");
            Debug.WriteLine($"Frame rate: {frameRate}");
            Debug.WriteLine($"Run: {run}");
            
            // Check python installation
            string pythonPath = "/Users/jorgemuyo/.pyenv/versions/3.11.0/bin/python";
            if (!File.Exists(pythonPath))
            {
                pythonPath = "python";  // Fallback to PATH python
                Debug.WriteLine("Python path not found, falling back to PATH");
            }
            
            // Set up output paths
            string videoFileName = Path.GetFileNameWithoutExtension(videoPath);
            
            // Get a valid base directory for temp files
            string baseDir = Path.GetTempPath();
            if (String.IsNullOrEmpty(baseDir))
            {
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (String.IsNullOrEmpty(baseDir))
                {
                    baseDir = "."; // Last resort - current directory
                }
            }
            
            // Create output directories
            _framesDir = Path.Combine(baseDir, "frames_" + videoFileName);
            _masksDir = Path.Combine(_framesDir, "segmentation_output", "masks");
            _outputDir = Path.Combine(baseDir, "output_" + videoFileName);
            
            Debug.WriteLine($"Frames directory: {_framesDir}");
            Debug.WriteLine($"Masks directory: {_masksDir}");
            Debug.WriteLine($"Output directory: {_outputDir}");
            
            // Ensure directories exist
            try {
                if (!Directory.Exists(_framesDir))
                    Directory.CreateDirectory(_framesDir);
                    
                if (!Directory.Exists(_outputDir))
                    Directory.CreateDirectory(_outputDir);
                    
                // Create segmentation output and masks directories
                string segOutDir = Path.Combine(_framesDir, "segmentation_output");
                if (!Directory.Exists(segOutDir))
                    Directory.CreateDirectory(segOutDir);
                if (!Directory.Exists(_masksDir))
                    Directory.CreateDirectory(_masksDir);
            }
            catch (Exception ex) {
                Debug.WriteLine($"Error creating directories: {ex.Message}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error creating output directories: {ex.Message}");
                DA.SetData(0, $"Error: {ex.Message}");
                return;
            }
            
            // Store inputs
            bool videoPathChanged = _selectedVideoPath != videoPath;
            bool frameRateChanged = _frameRate != frameRate;
            _selectedVideoPath = videoPath;
            _frameRate = frameRate;
            
            // Output the video path
            DA.SetData(0, $"Selected video: {Path.GetFileName(videoPath)}, FPS: {frameRate}");
            DA.SetData(1, _outputDir);
            
            if (run && !_isProcessing)
            {
                // Start processing
                _isProcessing = true;
                
                // Run the extraction and segmentation (start in a background thread)
                _cancellationSource = new CancellationTokenSource();
                Task.Factory.StartNew(() => 
                {
                    try {
                        ProcessVideoAsync(videoPath, frameRate, _cancellationSource.Token).Wait();
                    }
                    catch (Exception ex) {
                        Debug.WriteLine($"Processing task error: {ex.Message}");
                        _isProcessing = false;
                        
                        // Force update of component
                        try {
                            Rhino.RhinoApp.InvokeOnUiThread(new Action(() => {
                                OnSolutionExpired(true);
                            }));
                        } 
                        catch { }
                    }
                }, TaskCreationOptions.LongRunning);
                
                DA.SetData(0, "Processing started. Check console for progress.");
                DA.SetData(2, "Processing will open SAM2 UI when frames are extracted");
            }
            else if (run && _isProcessing)
            {
                // Already running
                DA.SetData(0, "Already processing video. Please wait for completion.");
                DA.SetData(2, "http://localhost:8000 (if SAM2 UI is running)");
            }
            else if (!run && _isProcessing)
            {
                // Stop processing
                if (_cancellationSource != null)
                {
                    _cancellationSource.Cancel();
                    
                    // Kill processes if running
                    KillProcess(_extractProcess);
                    KillProcess(_segmentProcess);
                    KillProcess(_maskProcess);
                    
                    _isProcessing = false;
                    DA.SetData(0, "Processing stopped by user");
                }
            }
        }
        
        private void KillProcess(Process process)
        {
            if (process != null && !process.HasExited)
            {
                try
                {
                    process.Kill();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error killing process: {ex.Message}");
                }
            }
        }
        
        private async Task ProcessVideoAsync(string videoPath, int frameRate, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            
            try
            {
                // Step 1: Extract frames
                bool extractSuccess = await ExtractFramesAsync(videoPath, _framesDir, frameRate, token);
                if (!extractSuccess || token.IsCancellationRequested) return;
                
                // Step 2: Run segmentation UI
                bool segmentSuccess = await RunSegmentationAsync(_framesDir, token);
                if (!segmentSuccess || token.IsCancellationRequested) return;
                
                // Step 3: Apply masks to frames
                bool maskSuccess = await ApplyMasksAsync(_framesDir, _masksDir, _outputDir, token);
                
                // Notify completion
                if (!token.IsCancellationRequested)
                {
                    _isProcessing = false;
                    OnSolutionExpired(true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ProcessVideoAsync: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                _isProcessing = false;
                OnSolutionExpired(true);
            }
        }
        
        private async Task<bool> ExtractFramesAsync(string videoPath, string framesDir, int frameRate, CancellationToken token)
        {
            // Get path to extract script
            string scriptPath = Path.Combine(
                Path.GetDirectoryName(typeof(VideoSAM2Component).Assembly.Location),
                "..", "..", "..", "segmentanything", "1_extract_frames.py"
            );
            
            // Resolve to absolute path
            scriptPath = Path.GetFullPath(scriptPath);
            
            if (!File.Exists(scriptPath))
            {
                Debug.WriteLine($"Extract script not found: {scriptPath}");
                return false;
            }
            
            Debug.WriteLine($"Running extraction script: {scriptPath}");
            Debug.WriteLine($"Video: {videoPath}");
            Debug.WriteLine($"Output: {framesDir}");
            Debug.WriteLine($"Frame rate: {frameRate}");
            
            try
            {
                // Set up extraction process
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "/Users/jorgemuyo/.pyenv/versions/3.11.0/bin/python";  // Use full path to Python
                psi.Arguments = $"\"{scriptPath}\" \"{videoPath}\" \"{framesDir}\" -r {frameRate}";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                
                // Print to console for debugging
                Debug.WriteLine($"COMMAND: {psi.FileName} {psi.Arguments}");
                
                _extractProcess = new Process();
                _extractProcess.StartInfo = psi;
                
                // Set up output handlers
                var outputBuilder = new System.Text.StringBuilder();
                _extractProcess.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        Debug.WriteLine($"Extract: {e.Data}");
                        
                        // Force component update to show progress
                        try {
                            Rhino.RhinoApp.InvokeOnUiThread(new Action(() => {
                                OnSolutionExpired(true);
                            }));
                        } catch (Exception ex) {
                            Debug.WriteLine($"Update UI error: {ex.Message}");
                        }
                    }
                };
                
                _extractProcess.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Debug.WriteLine($"Extract Error: {e.Data}");
                        
                        // Force component update to show error messages
                        try {
                            Rhino.RhinoApp.InvokeOnUiThread(new Action(() => {
                                OnSolutionExpired(true);
                            }));
                        } catch (Exception ex) {
                            Debug.WriteLine($"Update UI error: {ex.Message}");
                        }
                    }
                };
                
                // Start process
                _extractProcess.Start();
                _extractProcess.BeginOutputReadLine();
                _extractProcess.BeginErrorReadLine();
                
                // Wait for completion
                await Task.Run(() => {
                    while (!_extractProcess.HasExited)
                    {
                        if (token.IsCancellationRequested)
                        {
                            _extractProcess.Kill();
                            return;
                        }
                        Thread.Sleep(100);
                    }
                });
                
                if (_extractProcess.ExitCode != 0)
                {
                    Debug.WriteLine("Frame extraction failed");
                    return false;
                }
                
                Debug.WriteLine("Frame extraction completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ExtractFramesAsync: {ex.Message}");
                return false;
            }
        }
        
        private async Task<bool> RunSegmentationAsync(string framesDir, CancellationToken token)
        {
            // Get path to segmentation script
            string scriptPath = Path.Combine(
                Path.GetDirectoryName(typeof(VideoSAM2Component).Assembly.Location),
                "..", "..", "..", "segmentanything", "2_segmenter.py"
            );
            
            // Resolve to absolute path
            scriptPath = Path.GetFullPath(scriptPath);
            
            if (!File.Exists(scriptPath))
            {
                Debug.WriteLine($"Segmentation script not found: {scriptPath}");
                return false;
            }
            
            Debug.WriteLine($"Running segmentation script: {scriptPath}");
            Debug.WriteLine($"Frames directory: {framesDir}");
            
            try
            {
                // Set up segmentation process
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "/Users/jorgemuyo/.pyenv/versions/3.11.0/bin/python";  // Use full path to Python
                psi.Arguments = $"\"{scriptPath}\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;
                
                // Print to console for debugging
                Debug.WriteLine($"COMMAND: {psi.FileName} {psi.Arguments}");
                
                // Set working directory to find models
                psi.WorkingDirectory = Path.GetDirectoryName(scriptPath);
                
                _segmentProcess = new Process();
                _segmentProcess.StartInfo = psi;
                
                // Handle output
                var outputBuilder = new System.Text.StringBuilder();
                
                _segmentProcess.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        Debug.WriteLine($"Segment: {e.Data}");
                    }
                };
                
                _segmentProcess.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Debug.WriteLine($"Segment Error: {e.Data}");
                    }
                };
                
                // Start process
                _segmentProcess.Start();
                _segmentProcess.BeginOutputReadLine();
                _segmentProcess.BeginErrorReadLine();
                
                // Wait for prompt and provide frames directory
                await Task.Delay(2000); // Give the script time to start
                _segmentProcess.StandardInput.WriteLine(framesDir);
                
                // Let user interact with UI and wait for completion
                await Task.Run(() => {
                    while (!_segmentProcess.HasExited)
                    {
                        if (token.IsCancellationRequested)
                        {
                            _segmentProcess.Kill();
                            return;
                        }
                        Thread.Sleep(500);
                    }
                });
                
                if (_segmentProcess.ExitCode != 0)
                {
                    Debug.WriteLine("Segmentation failed");
                    return false;
                }
                
                Debug.WriteLine("Segmentation completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in RunSegmentationAsync: {ex.Message}");
                return false;
            }
        }
        
        private async Task<bool> ApplyMasksAsync(string framesDir, string masksDir, string outputDir, CancellationToken token)
        {
            // Get path to masking script
            string scriptPath = Path.Combine(
                Path.GetDirectoryName(typeof(VideoSAM2Component).Assembly.Location),
                "..", "..", "..", "segmentanything", "3_masking_out.py"
            );
            
            // Resolve to absolute path
            scriptPath = Path.GetFullPath(scriptPath);
            
            if (!File.Exists(scriptPath))
            {
                Debug.WriteLine($"Masking script not found: {scriptPath}");
                return false;
            }
            
            // Create temp copy of script with the correct paths
            string tempScriptPath = Path.Combine(Path.GetTempPath(), "temp_masking_out.py");
            string scriptContent = File.ReadAllText(scriptPath);
            
            // Replace hardcoded paths in the script with our paths
            scriptContent = scriptContent.Replace("output_dir = \"db\"", $"output_dir = \"{outputDir}\"");
            scriptContent = scriptContent.Replace("frames_dir = \"pottery\"", $"frames_dir = \"{framesDir}\"");
            scriptContent = scriptContent.Replace("masks_dir = \"pottery/segmentation_output/masks\"", $"masks_dir = \"{masksDir}\"");
            
            // Write the modified script
            File.WriteAllText(tempScriptPath, scriptContent);
            
            Debug.WriteLine($"Running masking script with modified paths");
            Debug.WriteLine($"Frames directory: {framesDir}");
            Debug.WriteLine($"Masks directory: {masksDir}");
            Debug.WriteLine($"Output directory: {outputDir}");
            
            try
            {
                // Create output directory if it doesn't exist
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // Set up masking process
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "/Users/jorgemuyo/.pyenv/versions/3.11.0/bin/python";  // Use full path to Python
                psi.Arguments = $"\"{tempScriptPath}\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                
                // Print to console for debugging
                Debug.WriteLine($"COMMAND: {psi.FileName} {psi.Arguments}");
                
                _maskProcess = new Process();
                _maskProcess.StartInfo = psi;
                
                // Handle output
                var outputBuilder = new System.Text.StringBuilder();
                
                _maskProcess.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        Debug.WriteLine($"Mask: {e.Data}");
                    }
                };
                
                _maskProcess.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Debug.WriteLine($"Mask Error: {e.Data}");
                    }
                };
                
                // Start process
                _maskProcess.Start();
                _maskProcess.BeginOutputReadLine();
                _maskProcess.BeginErrorReadLine();
                
                // Wait for completion
                await Task.Run(() => {
                    while (!_maskProcess.HasExited)
                    {
                        if (token.IsCancellationRequested)
                        {
                            _maskProcess.Kill();
                            return;
                        }
                        Thread.Sleep(100);
                    }
                });
                
                // Clean up temp script
                if (File.Exists(tempScriptPath))
                {
                    try { File.Delete(tempScriptPath); } catch { }
                }
                
                if (_maskProcess.ExitCode != 0)
                {
                    Debug.WriteLine("Mask application failed");
                    return false;
                }
                
                Debug.WriteLine("Mask application completed successfully");
                Debug.WriteLine($"Results saved to: {outputDir}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ApplyMasksAsync: {ex.Message}");
                return false;
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            // Clean up processes
            KillProcess(_extractProcess);
            KillProcess(_segmentProcess);
            KillProcess(_maskProcess);
            
            _isProcessing = false;
            _cancellationSource?.Cancel();
            
            base.RemovedFromDocument(document);
        }
        
        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                // Clean up processes
                KillProcess(_extractProcess);
                KillProcess(_segmentProcess);
                KillProcess(_maskProcess);
                
                _isProcessing = false;
                _cancellationSource?.Cancel();
            }
            
            base.DocumentContextChanged(document, context);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("b7c852e4-7a54-4b57-9d2f-2f45b8e6d1c8"); }
        }
    }
}