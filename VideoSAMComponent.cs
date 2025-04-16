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
    public class VideoSAMComponent : GH_Component
    {
        private string _selectedVideoPath = "";
        private Process _samProcess;
        private CancellationTokenSource _cancellationSource;
        
        public VideoSAMComponent()
          : base("SAM Video", "SAMVideo",
              "Segment videos using Segment Anything Model (SAM)",
              "crft", "Segment")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Video Path", "V", "Path to video file to segment", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Run", "R", "Run the SAM segmentation UI", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Output", "O", "Output results", GH_ParamAccess.item);
            pManager.AddTextParameter("Web Interface", "UI", "URL to the web interface", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string videoPath = "";
            bool run = false;

            if (!DA.GetData(0, ref videoPath)) return;
            if (!DA.GetData(1, ref run)) return;
            
            // Validate path
            if (string.IsNullOrEmpty(videoPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No video path provided");
                return;
            }
            
            if (!File.Exists(videoPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Video file not found: {videoPath}");
                return;
            }
            
            // Check if video path changed
            bool videoPathChanged = _selectedVideoPath != videoPath;
            _selectedVideoPath = videoPath;
            
            // Output the video path
            DA.SetData(0, $"Selected video: {Path.GetFileName(videoPath)}");
            
            if (run)
            {
                // If already running, check if we need to restart with new video
                if (_samProcess != null && !_samProcess.HasExited)
                {
                    if (videoPathChanged)
                    {
                        // Stop existing process
                        try
                        {
                            _cancellationSource?.Cancel();
                            
                            if (!_samProcess.WaitForExit(3000))
                            {
                                _samProcess.Kill();
                            }
                            
                            _samProcess = null;
                        }
                        catch (Exception ex)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Error stopping SAM process: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Already running with the same video
                        DA.SetData(0, $"SAM UI running with: {Path.GetFileName(videoPath)}");
                        DA.SetData(1, "http://localhost:8000");
                        return;
                    }
                }
                
                if (_samProcess == null || _samProcess.HasExited)
                {
                    // Start SAM process
                    try
                    {
                        // Get the path to the segment_video.sh script
                        string scriptPath = Path.Combine(
                            Path.GetDirectoryName(typeof(VideoSAMComponent).Assembly.Location),
                            "..", "..", "..", "segment_video.sh"
                        );
                        
                        // Resolve to absolute path
                        scriptPath = Path.GetFullPath(scriptPath);
                        
                        if (!File.Exists(scriptPath))
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"SAM script not found: {scriptPath}");
                            return;
                        }
                        
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Starting SAM with script: {scriptPath}");
                        
                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.FileName = "/bin/bash";
                        psi.Arguments = $"\"{scriptPath}\" \"{videoPath}\"";
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.RedirectStandardOutput = true;
                        psi.RedirectStandardError = true;
                        
                        _samProcess = new Process();
                        _samProcess.StartInfo = psi;
                        
                        // Handle output
                        _cancellationSource = new CancellationTokenSource();
                        CancellationToken token = _cancellationSource.Token;
                        
                        _samProcess.OutputDataReceived += (sender, e) => {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                if (token.IsCancellationRequested) return;
                                
                                if (e.Data.Contains("http://localhost"))
                                {
                                    Task.Run(() => {
                                        try
                                        {
                                            ExpireSolution(true);
                                        }
                                        catch { }
                                    });
                                }
                                
                                Debug.WriteLine($"SAM: {e.Data}");
                            }
                        };
                        
                        _samProcess.ErrorDataReceived += (sender, e) => {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                if (token.IsCancellationRequested) return;
                                Debug.WriteLine($"SAM Error: {e.Data}");
                            }
                        };
                        
                        _samProcess.Start();
                        _samProcess.BeginOutputReadLine();
                        _samProcess.BeginErrorReadLine();
                        
                        DA.SetData(0, $"Started SAM UI with: {Path.GetFileName(videoPath)}");
                        DA.SetData(1, "http://localhost:8000");
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error starting SAM: {ex.Message}");
                    }
                }
            }
            else
            {
                // Stop SAM process if running
                if (_samProcess != null && !_samProcess.HasExited)
                {
                    try
                    {
                        _cancellationSource?.Cancel();
                        
                        if (!_samProcess.WaitForExit(3000))
                        {
                            _samProcess.Kill();
                        }
                        
                        _samProcess = null;
                        DA.SetData(0, "SAM UI stopped");
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Error stopping SAM process: {ex.Message}");
                    }
                }
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            // Stop SAM process if running
            if (_samProcess != null && !_samProcess.HasExited)
            {
                try
                {
                    _cancellationSource?.Cancel();
                    
                    if (!_samProcess.WaitForExit(3000))
                    {
                        _samProcess.Kill();
                    }
                    
                    _samProcess = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping SAM process: {ex.Message}");
                }
            }
            
            base.RemovedFromDocument(document);
        }
        
        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                // Stop SAM process if running
                if (_samProcess != null && !_samProcess.HasExited)
                {
                    try
                    {
                        _cancellationSource?.Cancel();
                        
                        if (!_samProcess.WaitForExit(3000))
                        {
                            _samProcess.Kill();
                        }
                        
                        _samProcess = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error stopping SAM process: {ex.Message}");
                    }
                }
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
            get { return new Guid("a3c852e4-7a64-4a57-9c2f-2f45b8e6d1a8"); }
        }
    }
}