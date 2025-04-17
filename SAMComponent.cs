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
        private List<Point3d> _plyPoints = new List<Point3d>();
        private Mesh _plyMesh = null;
        
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
        
        /// <summary>
        /// Helper method to parse a PLY file into a Rhino Mesh
        /// </summary>
        private Mesh ParsePlyFile(string plyPath)
        {
            if (string.IsNullOrEmpty(plyPath) || !File.Exists(plyPath))
                return null;
                
            try
            {
                // Create a new mesh
                Mesh mesh = new Mesh();
                
                // Check file size - don't try to load huge files all at once
                FileInfo fileInfo = new FileInfo(plyPath);
                if (fileInfo.Length > 100 * 1024 * 1024) // 100MB limit
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"PLY file too large ({fileInfo.Length / (1024*1024)}MB). Creating simple placeholder mesh.");
                    
                    // Create a simple cube mesh as a placeholder
                    mesh.Vertices.Add(new Point3d(0, 0, 0));
                    mesh.Vertices.Add(new Point3d(1, 0, 0));
                    mesh.Vertices.Add(new Point3d(1, 1, 0));
                    mesh.Vertices.Add(new Point3d(0, 1, 0));
                    mesh.Vertices.Add(new Point3d(0, 0, 1));
                    mesh.Vertices.Add(new Point3d(1, 0, 1));
                    mesh.Vertices.Add(new Point3d(1, 1, 1));
                    mesh.Vertices.Add(new Point3d(0, 1, 1));
                    
                    mesh.Faces.AddFace(0, 1, 2, 3); // bottom
                    mesh.Faces.AddFace(4, 5, 6, 7); // top
                    mesh.Faces.AddFace(0, 1, 5, 4); // front
                    mesh.Faces.AddFace(1, 2, 6, 5); // right
                    mesh.Faces.AddFace(2, 3, 7, 6); // back
                    mesh.Faces.AddFace(3, 0, 4, 7); // left
                    
                    return mesh;
                }
                
                // Read the PLY file
                using (StreamReader reader = new StreamReader(plyPath))
                {
                    string line;
                    bool inHeader = true;
                    int vertexCount = 0;
                    int faceCount = 0;
                    
                    // Skip header until we get to vertex count
                    while (inHeader && (line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("element vertex "))
                        {
                            vertexCount = int.Parse(line.Split(' ')[2]);
                        }
                        else if (line.StartsWith("element face "))
                        {
                            faceCount = int.Parse(line.Split(' ')[2]);
                        }
                        else if (line == "end_header")
                        {
                            inHeader = false;
                        }
                    }
                    
                    if (vertexCount == 0)
                        return null;
                    
                    // Prepare for vertices
                    List<Point3f> vertices = new List<Point3f>(vertexCount);
                    List<System.Drawing.Color> vertexColors = new List<System.Drawing.Color>();
                    bool hasColors = false;
                    
                    // Read vertices
                    for (int i = 0; i < vertexCount; i++)
                    {
                        line = reader.ReadLine();
                        if (line == null)
                            break;
                            
                        string[] values = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (values.Length >= 3)
                        {
                            // Try to parse coordinates with invariant culture to avoid locale issues
                            float x, y, z;
                            bool success = float.TryParse(values[0], System.Globalization.NumberStyles.Float, 
                                                          System.Globalization.CultureInfo.InvariantCulture, out x);
                            success &= float.TryParse(values[1], System.Globalization.NumberStyles.Float, 
                                                      System.Globalization.CultureInfo.InvariantCulture, out y);
                            success &= float.TryParse(values[2], System.Globalization.NumberStyles.Float, 
                                                      System.Globalization.CultureInfo.InvariantCulture, out z);
                            
                            if (success)
                            {
                                vertices.Add(new Point3f(x, y, z));
                                
                                // If there are color values
                                if (values.Length >= 6)
                                {
                                    hasColors = true;
                                    byte r, g, b;
                                    bool colorSuccess = byte.TryParse(values[3], out r);
                                    colorSuccess &= byte.TryParse(values[4], out g);
                                    colorSuccess &= byte.TryParse(values[5], out b);
                                    
                                    if (colorSuccess)
                                    {
                                        vertexColors.Add(System.Drawing.Color.FromArgb(r, g, b));
                                    }
                                    else
                                    {
                                        vertexColors.Add(System.Drawing.Color.Gray);
                                    }
                                }
                                else if (hasColors)
                                {
                                    // Add a default color if some vertices have colors but this one doesn't
                                    vertexColors.Add(System.Drawing.Color.Gray);
                                }
                            }
                        }
                    }
                    
                    // Skip empty vertices
                    if (vertices.Count == 0)
                    {
                        Debug.WriteLine("No vertices found in PLY file");
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No vertices found in PLY file");
                        return CreateDefaultMesh();
                    }
                    
                    Debug.WriteLine($"Read {vertices.Count} vertices from PLY file");
                    
                    // Scale the mesh to a reasonable size if it's very small or very large
                    float maxCoord = 0;
                    foreach (Point3f pt in vertices)
                    {
                        maxCoord = Math.Max(maxCoord, Math.Abs(pt.X));
                        maxCoord = Math.Max(maxCoord, Math.Abs(pt.Y));
                        maxCoord = Math.Max(maxCoord, Math.Abs(pt.Z));
                    }
                    
                    if (maxCoord < 0.001f || maxCoord > 1000f)
                    {
                        float scale = (maxCoord < 0.001f) ? 10.0f / maxCoord : 100.0f / maxCoord;
                        for (int i = 0; i < vertices.Count; i++)
                        {
                            vertices[i] = new Point3f(
                                vertices[i].X * scale,
                                vertices[i].Y * scale,
                                vertices[i].Z * scale
                            );
                        }
                    }
                    
                    try
                    {
                        // Add vertices to mesh
                        foreach (Point3f vertex in vertices)
                        {
                            mesh.Vertices.Add(vertex);
                        }
                        
                        Debug.WriteLine($"Added {mesh.Vertices.Count} vertices to mesh");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error adding vertices to mesh: {ex.Message}");
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error adding vertices to mesh: {ex.Message}");
                        return CreateDefaultMesh();
                    }
                    
                    // Add vertex colors if available
                    if (hasColors && vertexColors.Count == vertices.Count)
                    {
                        mesh.VertexColors.AppendColors(vertexColors.ToArray());
                    }
                    
                    // Read faces if available
                    for (int i = 0; i < faceCount; i++)
                    {
                        line = reader.ReadLine();
                        if (line == null)
                            break;
                            
                        string[] values = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (values.Length >= 4 && int.TryParse(values[0], out int numVertices))
                        {
                            if (numVertices == 3 && values.Length >= 4)
                            {
                                // Triangle face
                                int a = int.Parse(values[1]);
                                int b = int.Parse(values[2]);
                                int c = int.Parse(values[3]);
                                
                                // Check indices are within bounds
                                if (a < vertices.Count && b < vertices.Count && c < vertices.Count)
                                {
                                    mesh.Faces.AddFace(a, b, c);
                                }
                            }
                            else if (numVertices == 4 && values.Length >= 5)
                            {
                                // Quad face
                                int a = int.Parse(values[1]);
                                int b = int.Parse(values[2]);
                                int c = int.Parse(values[3]);
                                int d = int.Parse(values[4]);
                                
                                // Check indices are within bounds
                                if (a < vertices.Count && b < vertices.Count && 
                                    c < vertices.Count && d < vertices.Count)
                                {
                                    mesh.Faces.AddFace(a, b, c, d);
                                }
                            }
                        }
                    }
                }
                
                // If no faces were defined but we have vertices, create a point cloud visualization
                if (mesh.Faces.Count == 0 && mesh.Vertices.Count > 0)
                {
                    Debug.WriteLine("No faces in mesh, creating point cloud visualization");
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"No faces found in PLY file. Creating visualization for {mesh.Vertices.Count} points.");
                    
                    try
                    {
                        // Create a simpler visualization by adding basic triangles between points
                        int triangleCount = 0;
                        
                        // If we have a large number of vertices, create a simplified mesh to avoid performance issues
                        if (mesh.Vertices.Count > 1000)
                        {
                            // Create a new mesh with a subset of vertices
                            Mesh simpleMesh = new Mesh();
                            int step = Math.Max(1, mesh.Vertices.Count / 1000);
                            
                            for (int i = 0; i < mesh.Vertices.Count; i += step)
                            {
                                simpleMesh.Vertices.Add(mesh.Vertices[i]);
                                if (mesh.VertexColors.Count > i)
                                {
                                    simpleMesh.VertexColors.Add(mesh.VertexColors[i]);
                                }
                            }
                            
                            // Add faces to make points visible
                            for (int i = 0; i < simpleMesh.Vertices.Count - 2; i++)
                            {
                                if (i % 3 == 0)
                                {
                                    simpleMesh.Faces.AddFace(i, i+1, i+2);
                                    triangleCount++;
                                }
                            }
                            
                            mesh = simpleMesh;
                            Debug.WriteLine($"Created simplified point cloud mesh with {mesh.Vertices.Count} vertices and {triangleCount} triangles");
                        }
                        else
                        {
                            // For smaller meshes, add triangles between existing vertices
                            for (int i = 0; i < mesh.Vertices.Count - 2; i++)
                            {
                                if (i % 3 == 0)
                                {
                                    mesh.Faces.AddFace(i, i+1, i+2);
                                    triangleCount++;
                                }
                            }
                            Debug.WriteLine($"Added {triangleCount} triangles to point cloud mesh");
                        }
                        
                        if (triangleCount == 0 && mesh.Vertices.Count > 0)
                        {
                            // Fallback: Create a single large triangle to make points visible
                            mesh.Faces.AddFace(0, 
                                              Math.Min(1, mesh.Vertices.Count-1), 
                                              Math.Min(2, mesh.Vertices.Count-1));
                            Debug.WriteLine("Added fallback triangle to point cloud mesh");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error creating point cloud visualization: {ex.Message}");
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                            $"Error creating point visualization: {ex.Message}");
                    }
                }
                
                // Clean up and optimize the mesh
                mesh.Compact();
                mesh.Weld(Math.PI / 4); // Weld vertices that are close together
                
                return mesh;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Error parsing PLY file: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Creates a simple sphere mesh (octahedron) for point visualization
        /// </summary>
        private Mesh CreateSimpleSphere(Point3d center, double radius)
        {
            Mesh m = new Mesh();
            
            // Add vertices (6 points of octahedron)
            m.Vertices.Add(new Point3f((float)(center.X), (float)(center.Y), (float)(center.Z + radius))); // top
            m.Vertices.Add(new Point3f((float)(center.X), (float)(center.Y), (float)(center.Z - radius))); // bottom
            m.Vertices.Add(new Point3f((float)(center.X + radius), (float)(center.Y), (float)(center.Z))); // right
            m.Vertices.Add(new Point3f((float)(center.X - radius), (float)(center.Y), (float)(center.Z))); // left
            m.Vertices.Add(new Point3f((float)(center.X), (float)(center.Y + radius), (float)(center.Z))); // front
            m.Vertices.Add(new Point3f((float)(center.X), (float)(center.Y - radius), (float)(center.Z))); // back
            
            // Add triangular faces (8 faces of octahedron)
            m.Faces.AddFace(0, 2, 4); // top-right-front
            m.Faces.AddFace(0, 4, 3); // top-front-left
            m.Faces.AddFace(0, 3, 5); // top-left-back
            m.Faces.AddFace(0, 5, 2); // top-back-right
            m.Faces.AddFace(1, 4, 2); // bottom-front-right
            m.Faces.AddFace(1, 3, 4); // bottom-left-front
            m.Faces.AddFace(1, 5, 3); // bottom-back-left
            m.Faces.AddFace(1, 2, 5); // bottom-right-back
            
            return m;
        }
        
        /// <summary>
        /// Creates a default mesh to use when no other mesh is available
        /// </summary>
        private Mesh CreateDefaultMesh()
        {
            // Create a simple cube mesh as a placeholder
            Mesh mesh = new Mesh();
            
            // Add vertices
            mesh.Vertices.Add(new Point3f(0, 0, 0));
            mesh.Vertices.Add(new Point3f(1, 0, 0));
            mesh.Vertices.Add(new Point3f(1, 1, 0));
            mesh.Vertices.Add(new Point3f(0, 1, 0));
            mesh.Vertices.Add(new Point3f(0, 0, 1));
            mesh.Vertices.Add(new Point3f(1, 0, 1));
            mesh.Vertices.Add(new Point3f(1, 1, 1));
            mesh.Vertices.Add(new Point3f(0, 1, 1));
            
            // Add faces
            mesh.Faces.AddFace(0, 1, 2, 3); // bottom
            mesh.Faces.AddFace(4, 5, 6, 7); // top
            mesh.Faces.AddFace(0, 1, 5, 4); // front
            mesh.Faces.AddFace(1, 2, 6, 5); // right
            mesh.Faces.AddFace(2, 3, 7, 6); // back
            mesh.Faces.AddFace(3, 0, 4, 7); // left
            
            // Add a message as vertex colors
            mesh.VertexColors.CreateMonotoneMesh(System.Drawing.Color.Red);
            
            return mesh;
        }
        
        /// <summary>
        /// Parse a PLY file and extract points only, ignoring mesh data
        /// </summary>
        private List<Point3d> ParsePointsFromPlyFile(string plyPath)
        {
            if (string.IsNullOrEmpty(plyPath) || !File.Exists(plyPath))
                return new List<Point3d>();
                
            List<Point3d> points = new List<Point3d>();
            
            try
            {
                // Read the PLY file
                using (StreamReader reader = new StreamReader(plyPath))
                {
                    string line;
                    bool inHeader = true;
                    int vertexCount = 0;
                    
                    // Skip header until we get to vertex count
                    while (inHeader && (line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("element vertex "))
                        {
                            vertexCount = int.Parse(line.Split(' ')[2]);
                        }
                        else if (line == "end_header")
                        {
                            inHeader = false;
                        }
                    }
                    
                    if (vertexCount == 0)
                        return points;
                    
                    // Allocate space for the points
                    points.Capacity = vertexCount;
                    
                    // Read vertices
                    for (int i = 0; i < vertexCount; i++)
                    {
                        line = reader.ReadLine();
                        if (line == null)
                            break;
                            
                        string[] values = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (values.Length >= 3)
                        {
                            // Try to parse coordinates with invariant culture to avoid locale issues
                            double x, y, z;
                            bool success = double.TryParse(values[0], System.Globalization.NumberStyles.Float, 
                                                          System.Globalization.CultureInfo.InvariantCulture, out x);
                            success &= double.TryParse(values[1], System.Globalization.NumberStyles.Float, 
                                                      System.Globalization.CultureInfo.InvariantCulture, out y);
                            success &= double.TryParse(values[2], System.Globalization.NumberStyles.Float, 
                                                      System.Globalization.CultureInfo.InvariantCulture, out z);
                            
                            if (success)
                            {
                                points.Add(new Point3d(x, y, z));
                            }
                        }
                    }
                }
                
                // Scale the points to a reasonable size if needed
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
                Debug.WriteLine($"Error parsing points from PLY: {ex.Message}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Error parsing points from PLY: {ex.Message}");
                return points;
            }
        }
        
        /// <summary>
        /// Helper method to clean up temporary files
        /// </summary>
        private void CleanupTempFiles(string framesDir)
        {
            try
            {
                if (Directory.Exists(framesDir))
                {
                    Directory.Delete(framesDir, true);
                    Debug.WriteLine($"Cleaned up temporary directory: {framesDir}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning up temporary files: {ex.Message}");
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
            string maskedOutputDir = Path.Combine(framesDir, "masking_output");
            string reconstructionDir = Path.Combine(framesDir, "reconstruction");
            string plyFilePath = Path.Combine(reconstructionDir, "model.ply");
            
            DA.SetData(1, maskedOutputDir);
            
            // Check if the PLY file exists and load it if it's a new file
            if (File.Exists(plyFilePath) && (plyFilePath != _lastPlyPath || _plyPoints.Count == 0))
            {
                try
                {
                    // Parse points directly from the PLY file
                    _plyPoints = ParsePointsFromPlyFile(plyFilePath);
                    _lastPlyPath = plyFilePath;
                    
                    // Always output the points, even if empty
                    if (_plyPoints.Count > 0)
                    {
                        DA.SetDataList(2, _plyPoints);
                        DA.SetData(0, $"Loaded {_plyPoints.Count} points from 3D reconstruction");
                    }
                    else
                    {
                        // Create a default point if no points were found
                        List<Point3d> defaultPoints = new List<Point3d>();
                        defaultPoints.Add(new Point3d(0, 0, 0));
                        defaultPoints.Add(new Point3d(1, 0, 0));
                        defaultPoints.Add(new Point3d(0, 1, 0));
                        
                        DA.SetDataList(2, defaultPoints);
                        DA.SetData(0, "No points found in reconstruction. Using default points.");
                    }
                }
                catch (Exception ex)
                {
                    // Log the error
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error loading points: {ex.Message}");
                    
                    // Create a default point
                    List<Point3d> defaultPoints = new List<Point3d>();
                    defaultPoints.Add(new Point3d(0, 0, 0));
                    DA.SetDataList(2, defaultPoints);
                    
                    DA.SetData(0, $"Error loading points: {ex.Message}");
                }
            }
            else if (_plyPoints.Count > 0)
            {
                // Use the previously loaded points
                DA.SetDataList(2, _plyPoints);
                DA.SetData(0, $"Using {_plyPoints.Count} points from 3D reconstruction");
            }
            else
            {
                // Make sure Points output is never null
                List<Point3d> emptyPoints = new List<Point3d>();
                DA.SetDataList(2, emptyPoints);
                
                // No data loaded yet
                DA.SetData(0, "No 3D data available yet. Set Run to True to process the video.");
            }
            
            if (run && !_isProcessing)
            {
                try
                {
                    _isProcessing = true;
                    DA.SetData(0, "Launching SAM2 UI...");
                    
                    // Clear previous mesh if we're running a new process
                    _plyMesh = null;
                    _lastPlyPath = null;
                    
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
                    
                    // Clean up previous temporary files if not preserving
                    if (!preserveFiles && Directory.Exists(framesDir))
                    {
                        CleanupTempFiles(framesDir);
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
            else if (!run && !preserveFiles && Directory.Exists(framesDir))
            {
                // If Run is false and Preserve is false, clean up temporary files
                CleanupTempFiles(framesDir);
                DA.SetData(0, "Temporary files cleaned up. Ready to launch SAM2 UI.");
            }
            else if (!run)
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