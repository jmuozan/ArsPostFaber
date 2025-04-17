using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;

namespace crft
{
    public class SAMComponent : GH_Component
    {
        private bool _isProcessing = false;
        private string _lastPlyPath = null;
        private List<Point3d> _points = new List<Point3d>();
        
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
            pManager.AddBooleanParameter("Preserve", "P", "Preserve temporary files (default: false)", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Processing status", GH_ParamAccess.item);
            pManager.AddTextParameter("Output Path", "O", "Path to output directory with masked frames", GH_ParamAccess.item);
            pManager.AddPointParameter("Points", "P", "Point cloud from 3D reconstruction", GH_ParamAccess.list);
        }
        
        private List<Point3d> ImportPlyFile(string plyPath)
        {
            List<Point3d> points = new List<Point3d>();
            
            try
            {
                if (!File.Exists(plyPath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"PLY file not found: {plyPath}");
                    return points;
                }
                
                using (StreamReader reader = new StreamReader(plyPath))
                {
                    string line;
                    bool readingVertices = false;
                    int numVertices = 0;
                    int currentVertex = 0;
                    
                    // Read header
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("element vertex "))
                        {
                            numVertices = int.Parse(line.Split(' ')[2]);
                        }
                        else if (line == "end_header")
                        {
                            readingVertices = true;
                            break;
                        }
                    }
                    
                    // Read vertices
                    while (readingVertices && (line = reader.ReadLine()) != null && currentVertex < numVertices)
                    {
                        string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            double x, y, z;
                            if (double.TryParse(parts[0], System.Globalization.NumberStyles.Float, 
                                               System.Globalization.CultureInfo.InvariantCulture, out x) &&
                                double.TryParse(parts[1], System.Globalization.NumberStyles.Float, 
                                               System.Globalization.CultureInfo.InvariantCulture, out y) &&
                                double.TryParse(parts[2], System.Globalization.NumberStyles.Float, 
                                               System.Globalization.CultureInfo.InvariantCulture, out z))
                            {
                                points.Add(new Point3d(x, y, z));
                                currentVertex++;
                            }
                        }
                    }
                }
                
                // Scale points appropriately if needed
                if (points.Count > 0)
                {
                    double maxCoord = 0;
                    foreach (Point3d pt in points)
                    {
                        maxCoord = Math.Max(maxCoord, Math.Abs(pt.X));
                        maxCoord = Math.Max(maxCoord, Math.Abs(pt.Y));
                        maxCoord = Math.Max(maxCoord, Math.Abs(pt.Z));
                    }
                    
                    if (maxCoord < 0.001 || maxCoord > 1000)
                    {
                        double scale = (maxCoord < 0.001) ? 10.0 / maxCoord : 100.0 / maxCoord;
                        for (int i = 0; i < points.Count; i++)
                        {
                            points[i] = new Point3d(
                                points[i].X * scale,
                                points[i].Y * scale,
                                points[i].Z * scale
                            );
                        }
                    }
                }
                
                return points;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error importing PLY file: {ex.Message}");
                return new List<Point3d>();
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string videoPath = "";
            int frameRate = 10;
            bool run = false;
            bool preserveFiles = false;

            if (!DA.GetData(0, ref videoPath)) return;
            if (!DA.GetData(1, ref frameRate)) frameRate = 10;
            if (!DA.GetData(2, ref run)) return;
            if (!DA.GetData(3, ref preserveFiles)) preserveFiles = false;
            
            // Compute standard PLY file path - independent of the video file
            string standardPlyPath = "/tmp/frames_porcelain/reconstruction/model.ply";
            
            // Compute video-specific PLY path
            string videoFileName = Path.GetFileNameWithoutExtension(videoPath);
            string framesDir = Path.Combine("/tmp", $"frames_{videoFileName}");
            string reconstructionDir = Path.Combine(framesDir, "reconstruction");
            string videoPlyPath = Path.Combine(reconstructionDir, "model.ply");
            
            // Try both possible PLY paths
            string plyFilePath = File.Exists(videoPlyPath) ? videoPlyPath : standardPlyPath;
            
            // Always set the output to the PLY path regardless of existence
            DA.SetData(1, plyFilePath);
            
            // Check if the PLY file exists
            if (File.Exists(plyFilePath))
            {
                // Only reload if it's a new file or we have no points yet
                if (plyFilePath != _lastPlyPath || _points.Count == 0)
                {
                    _points = ImportPlyFile(plyFilePath);
                    _lastPlyPath = plyFilePath;
                    
                    if (_points.Count > 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Loaded {_points.Count} points from {plyFilePath}");
                    }
                }
                
                // Output the points
                DA.SetDataList(2, _points);
                
                if (_points.Count > 0)
                {
                    DA.SetData(0, $"Loaded {_points.Count} points from PLY file");
                }
                else
                {
                    DA.SetData(0, "PLY file exists but no points were loaded");
                }
                
                // If we're not running, we're done
                if (!run) return;
            }
            else
            {
                // Empty points list if no PLY file
                DA.SetDataList(2, new List<Point3d>());
                DA.SetData(0, "PLY file will be created at: " + plyFilePath);
            }
            
            // Rest of validation continues below
            
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
            
            // Launch SAM UI if requested
            if (run && !_isProcessing)
            {
                try
                {
                    _isProcessing = true;
                    DA.SetData(0, "Launching SAM2 UI...");
                    
                    // Reset points when launching a new process
                    _points = new List<Point3d>();
                    _lastPlyPath = null;
                    
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
                    
                    // Clean up previous temporary files if not preserving
                    if (!preserveFiles && Directory.Exists(framesDir))
                    {
                        try
                        {
                            Directory.Delete(framesDir, true);
                        }
                        catch (Exception ex)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Error cleaning up temporary files: {ex.Message}");
                        }
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
                    DA.SetData(0, $"SAM2 UI has been launched in a new Terminal window. After processing, the PLY file will be imported automatically.");
                    _isProcessing = false;
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error: {ex.Message}");
                    DA.SetData(0, $"Error: {ex.Message}");
                    _isProcessing = false;
                }
            }
            else if (!run && _points.Count == 0)
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