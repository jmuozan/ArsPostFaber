using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace crft
{
    public class SAMComponent : GH_Component
    {
        private bool _isProcessing = false;
        
        public SAMComponent()
          : base("SAM UI", "SAM",
              "Launch the SAM2 segmentation interface",
              "crft", "Segment")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Video Path", "V", "Path to video file to segment", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Frame Rate", "F", "Frames per second to extract (default: 10)", GH_ParamAccess.item, 10);
            pManager.AddBooleanParameter("Run", "R", "Launch the SAM2 UI", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Processing status", GH_ParamAccess.item);
            pManager.AddTextParameter("Masks Path", "M", "Path to segmentation masks", GH_ParamAccess.item);
            pManager.AddTextParameter("Masked Frames", "O", "Path to masked output frames", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string videoPath = "";
            int frameRate = 10;
            bool run = false;

            if (!DA.GetData(0, ref videoPath)) return;
            if (!DA.GetData(1, ref frameRate)) frameRate = 10;
            if (!DA.GetData(2, ref run)) return;
            
            // Validate video path
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
            
            // Compute output directory paths
            string videoFileName = Path.GetFileNameWithoutExtension(videoPath);
            string framesDir = Path.Combine("/tmp", $"frames_{videoFileName}");
            string masksDir = Path.Combine(framesDir, "segmentation_output", "masks");
            string maskedOutputDir = Path.Combine(framesDir, "masking_output");
            
            DA.SetData(1, masksDir);
            DA.SetData(2, maskedOutputDir);
            
            if (run && !_isProcessing)
            {
                try
                {
                    _isProcessing = true;
                    DA.SetData(0, "Launching SAM2 UI...");
                    
                    // Find the SAM script
                    string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string scriptDir = Path.Combine(baseDir, "Desktop", "crft");
                    string samScript = Path.Combine(scriptDir, "run_sam.sh");
                    
                    if (!File.Exists(samScript))
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"SAM script not found: {samScript}");
                        DA.SetData(0, $"Error: Script not found at {samScript}");
                        _isProcessing = false;
                        return;
                    }
                    
                    // Ensure the script is executable
                    ProcessStartInfo chmodPsi = new ProcessStartInfo();
                    chmodPsi.FileName = "chmod";
                    chmodPsi.Arguments = $"+x \"{samScript}\"";
                    chmodPsi.UseShellExecute = false;
                    Process.Start(chmodPsi).WaitForExit();
                    
                    // Create an AppleScript to open Terminal and run the SAM script with the video path and frame rate
                    string appleScript = $@"
tell application ""Terminal""
    do script ""cd '{scriptDir}' && ./run_sam.sh '{videoPath}' {frameRate}""
    activate
end tell
";
                    
                    // Save the AppleScript to a temporary file
                    string appleScriptPath = Path.Combine(Path.GetTempPath(), "run_sam.scpt");
                    File.WriteAllText(appleScriptPath, appleScript);
                    
                    // Run the AppleScript with osascript
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "osascript";
                    psi.Arguments = appleScriptPath;
                    psi.UseShellExecute = false;
                    
                    Process.Start(psi);
                    
                    // Set the output text
                    DA.SetData(0, $"SAM2 UI has been launched in a new Terminal window. After processing, masks will be applied automatically.");
                    _isProcessing = false;
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
                    DA.SetData(0, $"Error: {ex.Message}");
                    _isProcessing = false;
                }
            }
            else
            {
                DA.SetData(0, "Ready to launch SAM2 UI. Set Run to True to begin.");
            }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return null; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("a5c852e4-7a34-4b57-9d2f-2f45b8e6d1c0"); }
        }
    }
}