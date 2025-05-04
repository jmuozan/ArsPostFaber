using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Collections;

namespace crft
{
    public class MeshEditorComponent : GH_Component
    {
        private bool _isProcessing = false;
        private Process _editorProcess = null;
        private string _tempInputPath = null;
        private string _tempOutputPath = null;
        private CancellationTokenSource _cancellationSource = null;
        private Task _monitorTask = null;
        private DateTime _lastModifiedTime = DateTime.MinValue;
        private Mesh _lastEditedMesh = null;
        private int _deviceIndex = 0;
        private bool _previousEnableState = false;
        private bool _keepTempFiles = false; // Set to false to delete temp files after use
        private bool _useBinaryStl = true; // Use binary STL format for better compatibility
        
        public MeshEditorComponent()
          : base("Mesh Editor", "MeshEdit",
              "Interactive mesh editing with webcam hand tracking",
              "crft", "Mesh")
        {
            // Create unique temporary file paths for this instance
            string guid = Guid.NewGuid().ToString();
            _tempInputPath = Path.Combine(Path.GetTempPath(), $"gh_meshedit_input_{guid}.stl");
            _tempOutputPath = Path.Combine(Path.GetTempPath(), $"gh_meshedit_output_{guid}.stl");
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Mesh to edit", Grasshopper.Kernel.GH_ParamAccess.item);
            pManager.AddBooleanParameter("Enable", "E", "Enable/disable mesh editor", Grasshopper.Kernel.GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Device", "D", "Camera device index", Grasshopper.Kernel.GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Edited mesh", Grasshopper.Kernel.GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh inputMesh = null;
            bool enable = false;
            int deviceIndex = 0;

            if (!DA.GetData(0, ref inputMesh)) return;
            if (!DA.GetData(1, ref enable)) return;
            if (!DA.GetData(2, ref deviceIndex)) deviceIndex = 0;
            
            // Store the device index for use in the editor
            _deviceIndex = deviceIndex;
            
            // Only update state if the enable value actually changed
            bool stateChanged = (enable != _previousEnableState);
            _previousEnableState = enable;
            
            if (enable && !_isProcessing && stateChanged)
            {
                // Save input mesh to temporary file
                SaveMeshToStl(inputMesh, _tempInputPath);
                
                // Clear previous messages
                
                // Start the mesh editor
                StartMeshEditor();
                
                // Set message to indicate active editing
                this.Message = "Editing";
            }
            else if (!enable && _isProcessing && stateChanged)
            {
                // Stop the mesh editor
                StopMeshEditor();
                
                // Reset component phase
                this.Message = "Idle";
            }
            
            // Check output
            if (_lastEditedMesh != null)
            {
                // Set edited mesh as output
                DA.SetData(0, _lastEditedMesh);
                
                // Add info message when not editing
                if (!_isProcessing)
                {
                    this.Message = "Edited";
                }
            }
            else
            {
                // If we don't have an edited mesh yet, pass through the input mesh
                DA.SetData(0, inputMesh);
                
                // Set appropriate message when not in edit mode
                if (!_isProcessing)
                {
                    this.Message = "Original";
                }
            }
            
            // Set appropriate icons/colors based on state
            if (_isProcessing)
            {
                this.Message = "Editing";
            }
        }
        
        private void SaveMeshToStl(Mesh mesh, string filePath)
        {
            try
            {
                // Ensure parent directory exists
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Save as an ASCII STL file that Open3D can read
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    // Write STL header
                    writer.WriteLine("solid RHINOMESH");
                    
                    // Get mesh components
                    MeshFaceList faces = mesh.Faces;
                    MeshVertexList vertices = mesh.Vertices;
                    
                    // Write each face
                    for (int i = 0; i < faces.Count; i++)
                    {
                        // Get face vertices
                        Point3f v1 = vertices[faces[i].A];
                        Point3f v2 = vertices[faces[i].B];
                        Point3f v3 = vertices[faces[i].C];
                        
                        // Compute normal
                        Vector3d vec1 = new Vector3d(v2.X - v1.X, v2.Y - v1.Y, v2.Z - v1.Z);
                        Vector3d vec2 = new Vector3d(v3.X - v1.X, v3.Y - v1.Y, v3.Z - v1.Z);
                        Vector3d normal = Vector3d.CrossProduct(vec1, vec2);
                        normal.Unitize();
                        
                        // Write facet
                        writer.WriteLine($"  facet normal {normal.X} {normal.Y} {normal.Z}");
                        writer.WriteLine("    outer loop");
                        writer.WriteLine($"      vertex {v1.X} {v1.Y} {v1.Z}");
                        writer.WriteLine($"      vertex {v2.X} {v2.Y} {v2.Z}");
                        writer.WriteLine($"      vertex {v3.X} {v3.Y} {v3.Z}");
                        writer.WriteLine("    endloop");
                        writer.WriteLine("  endfacet");
                        
                        // Handle quad faces by splitting into triangles
                        if (faces[i].IsQuad)
                        {
                            Point3f v4 = vertices[faces[i].D];
                            
                            // Compute normal for second triangle
                            vec1 = new Vector3d(v3.X - v1.X, v3.Y - v1.Y, v3.Z - v1.Z);
                            vec2 = new Vector3d(v4.X - v1.X, v4.Y - v1.Y, v4.Z - v1.Z);
                            normal = Vector3d.CrossProduct(vec1, vec2);
                            normal.Unitize();
                            
                            // Write second triangle
                            writer.WriteLine($"  facet normal {normal.X} {normal.Y} {normal.Z}");
                            writer.WriteLine("    outer loop");
                            writer.WriteLine($"      vertex {v1.X} {v1.Y} {v1.Z}");
                            writer.WriteLine($"      vertex {v3.X} {v3.Y} {v3.Z}");
                            writer.WriteLine($"      vertex {v4.X} {v4.Y} {v4.Z}");
                            writer.WriteLine("    endloop");
                            writer.WriteLine("  endfacet");
                        }
                    }
                    
                    // Write STL footer
                    writer.WriteLine("endsolid RHINOMESH");
                }
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Saved mesh with {mesh.Faces.Count} faces to STL file");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error saving mesh: {ex.Message}");
            }
        }
        
        private Mesh LoadMeshFromStl(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Output mesh file not found: {filePath}");
                    return null;
                }
                
                // Check file size to ensure it's not empty
                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Output mesh file is empty");
                    return null;
                }
                
                // First attempt: Use standard Rhino mesh import
                Mesh mesh = null;
                
                try
                {
                    // Try to directly create a mesh from the STL file
                    string fileExtension = Path.GetExtension(filePath).ToLower();
                    if (fileExtension == ".stl")
                    {
                        // Try the native Rhino method first
                        try
                        {
                            mesh = new Mesh();
                            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                            {
                                byte[] buffer = new byte[1024];
                                fs.Read(buffer, 0, buffer.Length);
                                
                                // Check for binary vs ASCII
                                bool isBinary = IsBinaryStl(buffer);
                                fs.Seek(0, SeekOrigin.Begin);
                                
                                if (isBinary)
                                {
                                    // Parse binary STL
                                    mesh = ParseBinaryStlFile(fs);
                                }
                                else
                                {
                                    // Parse ASCII STL
                                    fs.Close();
                                    mesh = ParseStlFile(filePath);
                                }
                                
                                if (mesh != null && mesh.Faces.Count > 0)
                                {
                                    // Compute normals
                                    mesh.Normals.ComputeNormals();
                                    
                                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Successfully loaded mesh with {mesh.Faces.Count} faces");
                                    return mesh;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error with primary STL parser: {ex.Message}");
                            // Continue to fallback method
                        }
                        
                        // Fallback to ASCII parser
                        mesh = ParseStlFile(filePath);
                        
                        if (mesh != null && mesh.Faces.Count > 0)
                        {
                            // Compute normals
                            mesh.Normals.ComputeNormals();
                            
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Successfully loaded mesh with {mesh.Faces.Count} faces");
                            return mesh;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading mesh directly: {ex.Message}");
                    // Continue to fallback methods
                }
                
                // Fallback method 1: Try using File3DM (though STL isn't a 3DM format, this sometimes works)
                try
                {
                    var file = Rhino.FileIO.File3dm.Read(filePath);
                    if (file != null)
                    {
                        foreach (Rhino.FileIO.File3dmObject obj in file.Objects)
                        {
                            if (obj.Geometry is Mesh objMesh)
                            {
                                objMesh.Normals.ComputeNormals();
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Loaded mesh with fallback method");
                                return objMesh;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in fallback method: {ex.Message}");
                }
                
                // Fallback method 2: Manual STL parsing
                try
                {
                    mesh = ParseStlFile(filePath);
                    if (mesh != null && mesh.Faces.Count > 0)
                    {
                        mesh.Normals.ComputeNormals();
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Loaded mesh with manual parsing method");
                        return mesh;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in manual parsing: {ex.Message}");
                }
                
                // If we get here, all attempts failed
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to read mesh from {filePath}");
                return null;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error loading mesh: {ex.Message}");
                return null;
            }
        }
        
        private bool IsBinaryStl(byte[] buffer)
        {
            // Check for binary STL format
            // Binary STL files don't begin with "solid"
            
            if (buffer.Length < 80)
                return false;
                
            // Convert first 5 bytes to ASCII to check for "solid"
            string header = System.Text.Encoding.ASCII.GetString(buffer, 0, 5).ToLower();
            
            if (header.StartsWith("solid"))
            {
                // Further verify by checking for ASCII data structures
                // ASCII files should have keywords like "facet", "normal", etc.
                string sample = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(buffer.Length, 512)).ToLower();
                return !sample.Contains("facet") || !sample.Contains("normal");
            }
            
            return true; // Not starting with "solid" indicates binary format
        }
        
        private Mesh ParseBinaryStlFile(FileStream fs)
        {
            var mesh = new Mesh();
            
            try
            {
                // Skip header (80 bytes)
                fs.Seek(80, SeekOrigin.Begin);
                
                // Read number of triangles (4 bytes)
                byte[] triangleCountBytes = new byte[4];
                fs.Read(triangleCountBytes, 0, 4);
                int triangleCount = BitConverter.ToInt32(triangleCountBytes, 0);
                
                // Reasonable sanity check
                if (triangleCount < 0 || triangleCount > 1000000)
                {
                    Debug.WriteLine($"Invalid STL triangle count: {triangleCount}");
                    return null;
                }
                
                // Buffer for a single triangle (50 bytes)
                byte[] triangleData = new byte[50];
                
                for (int i = 0; i < triangleCount; i++)
                {
                    // Read triangle data
                    int bytesRead = fs.Read(triangleData, 0, 50);
                    if (bytesRead < 50)
                        break;
                    
                    // Skip normal (12 bytes), start with vertices (3 vertices, 12 bytes each)
                    // Each vertex is x,y,z as floats
                    
                    // First vertex
                    float x1 = BitConverter.ToSingle(triangleData, 12);
                    float y1 = BitConverter.ToSingle(triangleData, 16);
                    float z1 = BitConverter.ToSingle(triangleData, 20);
                    
                    // Second vertex
                    float x2 = BitConverter.ToSingle(triangleData, 24);
                    float y2 = BitConverter.ToSingle(triangleData, 28);
                    float z2 = BitConverter.ToSingle(triangleData, 32);
                    
                    // Third vertex
                    float x3 = BitConverter.ToSingle(triangleData, 36);
                    float y3 = BitConverter.ToSingle(triangleData, 40);
                    float z3 = BitConverter.ToSingle(triangleData, 44);
                    
                    // Add vertices to mesh
                    Point3f v1 = new Point3f(x1, y1, z1);
                    Point3f v2 = new Point3f(x2, y2, z2);
                    Point3f v3 = new Point3f(x3, y3, z3);
                    
                    int idx1 = mesh.Vertices.Add(v1);
                    int idx2 = mesh.Vertices.Add(v2);
                    int idx3 = mesh.Vertices.Add(v3);
                    
                    mesh.Faces.AddFace(idx1, idx2, idx3);
                }
                
                return mesh;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing binary STL: {ex.Message}");
                return null;
            }
        }
        
        private Mesh ParseStlFile(string filePath)
        {
            // ASCII STL parser as a fallback
            var mesh = new Mesh();
            List<Point3d> vertices = new List<Point3d>();
            int vertexCount = 0;
            
            try
            {
                using (StreamReader reader = new StreamReader(filePath))
                {
                    string firstLine = reader.ReadLine()?.Trim().ToLower();
                    if (firstLine == null)
                    {
                        // Empty file
                        return null;
                    }
                    
                    // If it doesn't start with "solid", try a different approach
                    if (!firstLine.StartsWith("solid"))
                    {
                        // Try Open3D/custom format (may not have proper 'solid' header)
                        reader.BaseStream.Seek(0, SeekOrigin.Begin);
                        reader.DiscardBufferedData();
                    }
                    
                    // Continue parsing
                    string line;
                    Point3d[] faceVertices = new Point3d[3];
                    
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        
                        // Skip empty lines and endsolid statements
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("endsolid"))
                            continue;
                            
                        // Handle vertex lines
                        if (line.StartsWith("vertex"))
                        {
                            // Extract vertex coordinates
                            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4)
                            {
                                if (double.TryParse(parts[1], out double x) &&
                                    double.TryParse(parts[2], out double y) &&
                                    double.TryParse(parts[3], out double z))
                                {
                                    faceVertices[vertexCount % 3] = new Point3d(x, y, z);
                                    vertexCount++;
                                    
                                    // When we have 3 vertices, add a face
                                    if (vertexCount % 3 == 0)
                                    {
                                        // Add vertices to the mesh
                                        int v1 = mesh.Vertices.Add(faceVertices[0]);
                                        int v2 = mesh.Vertices.Add(faceVertices[1]);
                                        int v3 = mesh.Vertices.Add(faceVertices[2]);
                                        
                                        // Add the face
                                        mesh.Faces.AddFace(v1, v2, v3);
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Simple validation - a mesh should have faces
                if (mesh.Faces.Count == 0)
                {
                    Debug.WriteLine("Failed to parse any faces from STL file");
                    return null;
                }
                
                return mesh;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ASCII STL parsing: {ex.Message}");
                return null;
            }
        }
        

        private void StartMeshEditor()
        {
            try
            {
                if (_isProcessing)
                {
                    return;
                }
                
                // Mark as processing
                _isProcessing = true;
                
                // Create a unique temporary directory for our Python scripts
                string scriptTempDir = Path.Combine(Path.GetTempPath(), $"meshedit_{Guid.NewGuid()}");
                Directory.CreateDirectory(scriptTempDir);
                
                // Create a very simple Python script that runs meshedit.py directly
                string launcherPath = Path.Combine(scriptTempDir, "mesh_launcher.py");
                
                using (StreamWriter writer = new StreamWriter(launcherPath))
                {
                    writer.WriteLine("#!/usr/bin/env python3");
                    writer.WriteLine("import sys");
                    writer.WriteLine("import os");
                    writer.WriteLine("import subprocess");
                    writer.WriteLine("");
                    writer.WriteLine("# Look for the Grasshopper-specific editor first");
                    writer.WriteLine("gh_paths = [");
                    writer.WriteLine("    os.path.join(os.path.expanduser('~'), 'Desktop', 'crft', 'gh_edit.py'),");
                    writer.WriteLine("    os.path.join(os.path.dirname(os.path.dirname(__file__)), 'gh_edit.py')");
                    writer.WriteLine("]");
                    writer.WriteLine("");
                    writer.WriteLine("# Fallback to standard editor if GH version not found");
                    writer.WriteLine("standard_paths = [");
                    writer.WriteLine("    os.path.join(os.path.expanduser('~'), 'Desktop', 'crft', 'meshedit.py'),");
                    writer.WriteLine("    os.path.join(os.path.dirname(os.path.dirname(__file__)), 'meshedit.py')");
                    writer.WriteLine("]");
                    writer.WriteLine("");
                    writer.WriteLine("# First try GH-specific version");
                    writer.WriteLine("script_path = None");
                    writer.WriteLine("for path in gh_paths:");
                    writer.WriteLine("    if os.path.exists(path):");
                    writer.WriteLine("        script_path = path");
                    writer.WriteLine("        print(f'Using Grasshopper-optimized editor: {path}')");
                    writer.WriteLine("        break");
                    writer.WriteLine("");
                    writer.WriteLine("# Fall back to standard version if needed");
                    writer.WriteLine("if not script_path:");
                    writer.WriteLine("    for path in standard_paths:");
                    writer.WriteLine("        if os.path.exists(path):");
                    writer.WriteLine("            script_path = path");
                    writer.WriteLine("            print(f'Using standard mesh editor: {path}')");
                    writer.WriteLine("            break");
                    writer.WriteLine("");
                    writer.WriteLine("if not script_path:");
                    writer.WriteLine("    print('Error: Could not find meshedit.py or gh_edit.py')");
                    writer.WriteLine("    sys.exit(1)");
                    writer.WriteLine("");
                    writer.WriteLine("# Run the script");
                    writer.WriteLine("cmd = [sys.executable, script_path,");
                    writer.WriteLine($"       '--input', '{_tempInputPath}',");
                    writer.WriteLine($"       '--output', '{_tempOutputPath}',");
                    writer.WriteLine($"       '--device', '{_deviceIndex}']");
                    writer.WriteLine("");
                    writer.WriteLine("print('Running mesh editor with command:', ' '.join(cmd))");
                    writer.WriteLine("subprocess.run(cmd)");
                }
                
                // Create a simple script to close the editor using command line (no tkinter)
                string closeButtonPath = Path.Combine(scriptTempDir, "close_button.py");
                
                using (StreamWriter writer = new StreamWriter(closeButtonPath))
                {
                    writer.WriteLine("#!/usr/bin/env python3");
                    writer.WriteLine("import subprocess");
                    writer.WriteLine("import time");
                    writer.WriteLine("import os");
                    writer.WriteLine("");
                    writer.WriteLine("# Print instructions for user");
                    writer.WriteLine("print('\\n\\033[1;32m===== MESH EDITOR CONTROL =====\\033[0m')");
                    writer.WriteLine("print('\\033[1;36mPress Ctrl+C to save and close the mesh editor\\033[0m')");
                    writer.WriteLine("print('\\033[1;33mOr press S key in the mesh editor window to save directly\\033[0m')");
                    writer.WriteLine("print('\\033[1;31mEdited mesh will be embedded in the Grasshopper component\\033[0m\\n')");
                    writer.WriteLine("");
                    writer.WriteLine("# Function to save and close");
                    writer.WriteLine("def save_and_close():");
                    writer.WriteLine("    print('\\n\\033[1;32mSaving and closing mesh editor...\\033[0m')");
                    writer.WriteLine("    subprocess.run(['pkill', '-f', 'meshedit.py'])");
                    writer.WriteLine("    print('\\033[1;32mMesh editor closed! You can close this window.\\033[0m')");
                    writer.WriteLine("    print('\\033[1;32mEdited mesh has been embedded in the Grasshopper component.\\033[0m\\n')");
                    writer.WriteLine("");
                    writer.WriteLine("# Wait for Ctrl+C");
                    writer.WriteLine("try:");
                    writer.WriteLine("    # Main loop - wait for Ctrl+C");
                    writer.WriteLine("    while True:");
                    writer.WriteLine("        time.sleep(0.5)");
                    writer.WriteLine("except KeyboardInterrupt:");
                    writer.WriteLine("    # User pressed Ctrl+C");
                    writer.WriteLine("    save_and_close()");
                }
                
                // Create an AppleScript to open two Terminal windows:
                // 1. One for the mesh editor
                // 2. One for the close controller
                string appleScript = $@"
tell application ""Terminal""
    do script ""cd '{scriptTempDir}' && python3 mesh_launcher.py""
    do script ""cd '{scriptTempDir}' && python3 close_button.py""
    activate
end tell
";
                
                // Save the AppleScript to a temporary file
                string appleScriptPath = Path.Combine(Path.GetTempPath(), $"run_meshedit_{Guid.NewGuid()}.scpt");
                File.WriteAllText(appleScriptPath, appleScript);
                
                // Run the AppleScript
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "osascript";
                psi.Arguments = appleScriptPath;
                psi.UseShellExecute = false;
                
                _editorProcess = Process.Start(psi);
                
                // Start monitoring for changes to the output file
                _cancellationSource = new CancellationTokenSource();
                _monitorTask = Task.Run(() => MonitorOutputFile(_cancellationSource.Token));
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    "Mesh editor has been launched. Edit the mesh in the window and click 'Save & Close Editor' to finish.");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error starting mesh editor: {ex.Message}");
                _isProcessing = false;
            }
        }
        
        private void StopMeshEditor()
        {
            try
            {
                // Stop the monitor task
                if (_cancellationSource != null)
                {
                    _cancellationSource.Cancel();
                    _cancellationSource.Dispose();
                    _cancellationSource = null;
                }
                
                // Terminate the editor process if it's running
                if (_editorProcess != null)
                {
                    try
                    {
                        if (!_editorProcess.HasExited)
                        {
                            _editorProcess.Kill();
                            _editorProcess.WaitForExit(1000);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error killing process: {ex.Message}");
                    }
                    finally
                    {
                        _editorProcess.Dispose();
                        _editorProcess = null;
                    }
                }
                
                // Use the pkill command to ensure all meshedit.py processes are terminated
                try
                {
                    Process killProcess = new Process();
                    killProcess.StartInfo.FileName = "pkill";
                    killProcess.StartInfo.Arguments = "-f meshedit";
                    killProcess.StartInfo.UseShellExecute = false;
                    killProcess.StartInfo.CreateNoWindow = true;
                    
                    killProcess.Start();
                    killProcess.WaitForExit(1000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error running pkill command: {ex.Message}");
                }
                
                // Wait a short moment to ensure file system has synchronized
                System.Threading.Thread.Sleep(500);
                
                // Try to load the final mesh from the output file
                try
                {
                    if (File.Exists(_tempOutputPath))
                    {
                        // Check if the file size is non-zero before trying to load
                        FileInfo fileInfo = new FileInfo(_tempOutputPath);
                        if (fileInfo.Length > 0)
                        {
                            Mesh finalMesh = LoadMeshFromStl(_tempOutputPath);
                            if (finalMesh != null)
                            {
                                _lastEditedMesh = finalMesh;
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Loaded final edited mesh");
                                
                                // Force component to recompute and display the updated mesh
                                this.ExpireSolution(true);
                            }
                            else
                            {
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Could not load edited mesh, it may be corrupted");
                            }
                        }
                        else
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Output mesh file exists but is empty");
                        }
                    }
                    else
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No output mesh file found");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading final mesh: {ex.Message}");
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error loading mesh: {ex.Message}");
                }
                
                _isProcessing = false;
                
                // Clean up temp files if we have the edited mesh
                if (_lastEditedMesh != null && !_keepTempFiles)
                {
                    CleanupTempFiles();
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error stopping mesh editor: {ex.Message}");
            }
        }
        
        private void MonitorOutputFile(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Check if the output file exists and has been modified
                    if (File.Exists(_tempOutputPath))
                    {
                        FileInfo fileInfo = new FileInfo(_tempOutputPath);
                        if (fileInfo.LastWriteTime > _lastModifiedTime && fileInfo.Length > 0)
                        {
                            _lastModifiedTime = fileInfo.LastWriteTime;
                            
                            // Wait a short moment to ensure file is fully written
                            Thread.Sleep(100);
                            
                            // Load the mesh from the output file
                            Mesh updatedMesh = LoadMeshFromStl(_tempOutputPath);
                            if (updatedMesh != null)
                            {
                                _lastEditedMesh = updatedMesh;
                                
                                // Trigger full component recompute to update output mesh preview
                                this.ExpireSolution(true);
                                
                                // Log that mesh was updated
                                Debug.WriteLine($"Mesh updated from file. Last write time: {fileInfo.LastWriteTime}");
                            }
                            else
                            {
                                Debug.WriteLine("Failed to load updated mesh");
                            }
                        }
                    }
                    
                    // Wait before checking again
                    Thread.Sleep(500); // Check every half-second for more responsive updates
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in monitor task: {ex.Message}");
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopMeshEditor();
            
            // Clean up temporary files
            CleanupTempFiles();
            
            base.RemovedFromDocument(document);
        }
        
        private void CleanupTempFiles()
        {
            try
            {
                if (!_keepTempFiles)
                {
                    // Clean up temp input file
                    if (File.Exists(_tempInputPath))
                    {
                        File.Delete(_tempInputPath);
                        Debug.WriteLine($"Deleted temporary input file: {_tempInputPath}");
                    }
                    
                    // Clean up temp output file
                    if (File.Exists(_tempOutputPath))
                    {
                        File.Delete(_tempOutputPath);
                        Debug.WriteLine($"Deleted temporary output file: {_tempOutputPath}");
                    }
                    
                    // Cleanup any temporary directories we created
                    string tempDir = Path.GetDirectoryName(_tempInputPath);
                    if (Directory.Exists(tempDir) && tempDir.Contains("meshedit_"))
                    {
                        try
                        {
                            Directory.Delete(tempDir, true);
                            Debug.WriteLine($"Deleted temporary directory: {tempDir}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Could not delete temp directory: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("Keeping temporary files as requested");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning up temporary files: {ex.Message}");
            }
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                StopMeshEditor();
                CleanupTempFiles();
            }
            base.DocumentContextChanged(document, context);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("f3eb7dcd-8c52-4b46-8252-964a9552eb5a");
    }
}