using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;
using System.Threading;
using System.Threading.Tasks;

namespace crft
{
    public class MeshEditorComponent : GH_Component
    {
        private bool _isProcessing = false;
        private string _tempMeshPath = null;
        private string _pythonScriptPath = null;
        private string _modifiedMeshPath = null;
        private Process _editorProcess = null;
        private CancellationTokenSource _cancellationSource;
        private bool _previousEnabledState = false;
        private int _previousDeviceIndex = 0;
        private bool _shouldRefreshOutput = false;
        private Mesh _outputMesh = null;
        
        public MeshEditorComponent()
          : base("Mesh Editor", "MeshEdit",
              "Interactive 3D mesh editor with camera-based editing",
              "crft", "Edit")
        {
            // Create a unique file name for the temp mesh
            string guid = Guid.NewGuid().ToString();
            _tempMeshPath = Path.Combine(Path.GetTempPath(), $"gh_mesh_edit_{guid}.stl");
            _modifiedMeshPath = Path.Combine(Path.GetTempPath(), $"gh_mesh_edit_modified_{guid}.stl");
            
            // Path to the Python script
            _pythonScriptPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Desktop", "crft", "edit.py");
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Input mesh to edit", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Enable", "E", "Enable/disable mesh editor", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Device", "D", "Camera device index (default: 0)", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Edited mesh", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "S", "Editor status", GH_ParamAccess.item);
        }
        
        private bool ExportMeshToStl(Mesh mesh, string filePath)
        {
            try
            {
                // Convert Rhino mesh to format suitable for STL export
                Mesh exportMesh = mesh.DuplicateMesh();
                
                // Ensure mesh is valid for export
                if (exportMesh.Vertices.Count == 0 || exportMesh.Faces.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cannot export empty mesh");
                    return false;
                }
                
                // Use File.IO to write the mesh to STL
                using (StreamWriter writer = new StreamWriter(filePath))
                {
                    writer.WriteLine("solid rhino_mesh");
                    
                    // Process each face
                    for (int i = 0; i < exportMesh.Faces.Count; i++)
                    {
                        // Get face vertices
                        MeshFace face = exportMesh.Faces[i];
                        Point3f v1 = exportMesh.Vertices[face.A];
                        Point3f v2 = exportMesh.Vertices[face.B];
                        Point3f v3 = exportMesh.Vertices[face.C];
                        
                        // Calculate face normal
                        Vector3d vec1 = new Vector3d(v2.X - v1.X, v2.Y - v1.Y, v2.Z - v1.Z);
                        Vector3d vec2 = new Vector3d(v3.X - v1.X, v3.Y - v1.Y, v3.Z - v1.Z);
                        Vector3d normal = Vector3d.CrossProduct(vec1, vec2);
                        normal.Unitize();
                        
                        // Write facet info
                        writer.WriteLine($"  facet normal {normal.X} {normal.Y} {normal.Z}");
                        writer.WriteLine("    outer loop");
                        writer.WriteLine($"      vertex {v1.X} {v1.Y} {v1.Z}");
                        writer.WriteLine($"      vertex {v2.X} {v2.Y} {v2.Z}");
                        writer.WriteLine($"      vertex {v3.X} {v3.Y} {v3.Z}");
                        writer.WriteLine("    endloop");
                        writer.WriteLine("  endfacet");
                        
                        // Handle quad faces by splitting into two triangles
                        if (face.IsQuad)
                        {
                            Point3f v4 = exportMesh.Vertices[face.D];
                            
                            // Calculate second triangle normal
                            vec1 = new Vector3d(v3.X - v1.X, v3.Y - v1.Y, v3.Z - v1.Z);
                            vec2 = new Vector3d(v4.X - v1.X, v4.Y - v1.Y, v4.Z - v1.Z);
                            normal = Vector3d.CrossProduct(vec1, vec2);
                            normal.Unitize();
                            
                            // Write second facet
                            writer.WriteLine($"  facet normal {normal.X} {normal.Y} {normal.Z}");
                            writer.WriteLine("    outer loop");
                            writer.WriteLine($"      vertex {v1.X} {v1.Y} {v1.Z}");
                            writer.WriteLine($"      vertex {v3.X} {v3.Y} {v3.Z}");
                            writer.WriteLine($"      vertex {v4.X} {v4.Y} {v4.Z}");
                            writer.WriteLine("    endloop");
                            writer.WriteLine("  endfacet");
                        }
                    }
                    
                    writer.WriteLine("endsolid rhino_mesh");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error exporting mesh: {ex.Message}");
                return false;
            }
        }
        
        private Mesh ImportMeshFromStl(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Modified mesh file not found: {filePath}");
                    return null;
                }
                
                // Create a new Rhino Mesh
                Mesh importedMesh = new Mesh();
                
                // Parse the STL file
                using (StreamReader reader = new StreamReader(filePath))
                {
                    string line;
                    bool readingLoop = false;
                    List<Point3d> vertices = new List<Point3d>();
                    
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        
                        if (line.StartsWith("facet normal"))
                        {
                            vertices.Clear();
                        }
                        else if (line.StartsWith("outer loop"))
                        {
                            readingLoop = true;
                        }
                        else if (line.StartsWith("vertex") && readingLoop)
                        {
                            string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4)
                            {
                                double x = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                                double y = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                                double z = double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                                vertices.Add(new Point3d(x, y, z));
                            }
                        }
                        else if (line.StartsWith("endloop"))
                        {
                            readingLoop = false;
                        }
                        else if (line.StartsWith("endfacet"))
                        {
                            // Add the face to the mesh if we have exactly 3 vertices
                            if (vertices.Count == 3)
                            {
                                int[] indices = new int[3];
                                for (int i = 0; i < 3; i++)
                                {
                                    indices[i] = importedMesh.Vertices.Add(new Point3f(
                                        (float)vertices[i].X,
                                        (float)vertices[i].Y,
                                        (float)vertices[i].Z));
                                }
                                
                                importedMesh.Faces.AddFace(indices[0], indices[1], indices[2]);
                            }
                        }
                    }
                }
                
                if (importedMesh.Vertices.Count == 0 || importedMesh.Faces.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Imported mesh is empty or invalid");
                    return null;
                }
                
                // Clean up and weld the mesh
                importedMesh.Compact();
                importedMesh.Weld(Math.PI / 2.0); // 90-degree welding angle
                importedMesh.RebuildNormals();
                
                return importedMesh;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error importing modified mesh: {ex.Message}");
                return null;
            }
        }
        
        private bool CheckForRequiredDependencies()
        {
            try
            {
                // Check if the Python script exists
                if (!File.Exists(_pythonScriptPath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Python script not found: {_pythonScriptPath}");
                    return false;
                }
                
                // Create a process to check Python dependencies
                Process checkProcess = new Process();
                checkProcess.StartInfo.FileName = "python3";
                checkProcess.StartInfo.Arguments = "-c \"import cv2, numpy, open3d, mediapipe; print('Dependencies OK')\"";
                checkProcess.StartInfo.UseShellExecute = false;
                checkProcess.StartInfo.RedirectStandardOutput = true;
                checkProcess.StartInfo.RedirectStandardError = true;
                checkProcess.StartInfo.CreateNoWindow = true;
                
                StringBuilder output = new StringBuilder();
                StringBuilder error = new StringBuilder();
                
                checkProcess.OutputDataReceived += (sender, e) => {
                    if (e.Data != null) output.AppendLine(e.Data);
                };
                checkProcess.ErrorDataReceived += (sender, e) => {
                    if (e.Data != null) error.AppendLine(e.Data);
                };
                
                checkProcess.Start();
                checkProcess.BeginOutputReadLine();
                checkProcess.BeginErrorReadLine();
                
                bool completed = checkProcess.WaitForExit(5000); // Wait up to 5 seconds
                
                if (!completed)
                {
                    checkProcess.Kill();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Dependency check timed out");
                    return false;
                }
                
                if (checkProcess.ExitCode != 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Missing Python dependencies. Please install: cv2, numpy, open3d, mediapipe");
                    return false;
                }
                
                if (output.ToString().Contains("Dependencies OK"))
                {
                    return true;
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Dependency check failed. Error: {error.ToString()}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error checking dependencies: {ex.Message}");
                return false;
            }
        }
        
        private void LaunchMeshEditor(int deviceIndex)
        {
            try
            {
                if (_isProcessing)
                {
                    // Already running
                    return;
                }
                
                // Set up cancellation token source
                _cancellationSource = new CancellationTokenSource();
                
                // Start monitoring for the modified mesh file
                Task.Run(() => MonitorModifiedMesh(_cancellationSource.Token));
                
                // Create process to launch Python script
                _editorProcess = new Process();
                _editorProcess.StartInfo.FileName = "python3";
                _editorProcess.StartInfo.Arguments = $"\"{_pythonScriptPath}\" --input \"{_tempMeshPath}\" --output \"{_modifiedMeshPath}\" --device {deviceIndex}";
                _editorProcess.StartInfo.UseShellExecute = false;
                _editorProcess.StartInfo.CreateNoWindow = false; // Show the window
                
                // Set up environment variables (if needed)
                _editorProcess.StartInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                
                // Setup error handling
                _editorProcess.EnableRaisingEvents = true;
                _editorProcess.Exited += (sender, args) => {
                    _isProcessing = false;
                    
                    // Try to import the modified mesh when the process exits
                    _shouldRefreshOutput = true;
                    ExpireSolution(true);
                };
                
                // Launch the process
                _editorProcess.Start();
                _isProcessing = true;
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"Launched mesh editor with device {deviceIndex}");
            }
            catch (Exception ex)
            {
                _isProcessing = false;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error launching editor: {ex.Message}");
                
                // Clean up resources
                if (_cancellationSource != null)
                {
                    _cancellationSource.Cancel();
                    _cancellationSource.Dispose();
                    _cancellationSource = null;
                }
            }
        }
        
        private void MonitorModifiedMesh(CancellationToken token)
        {
            // Check for the modified file periodically
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(_modifiedMeshPath))
                    {
                        // Get the file info to check timestamp and size
                        FileInfo fileInfo = new FileInfo(_modifiedMeshPath);
                        
                        // Ensure file is not empty and not being written to
                        if (fileInfo.Length > 0 && !IsFileLocked(_modifiedMeshPath))
                        {
                            // File exists and is valid - trigger update
                            _shouldRefreshOutput = true;
                            ExpireSolution(true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error monitoring file: {ex.Message}");
                }
                
                // Wait a short period before checking again
                try
                {
                    Task.Delay(500, token).Wait(); // Check every 500ms
                }
                catch (TaskCanceledException)
                {
                    // Cancellation is expected behavior
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in delay: {ex.Message}");
                }
            }
        }
        
        private bool IsFileLocked(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Close();
                }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }
        
        private void StopMeshEditor()
        {
            if (!_isProcessing)
            {
                return;
            }
            
            try
            {
                // Cancel the monitoring task
                if (_cancellationSource != null)
                {
                    _cancellationSource.Cancel();
                    _cancellationSource.Dispose();
                    _cancellationSource = null;
                }
                
                // Kill the editor process if it's still running
                if (_editorProcess != null && !_editorProcess.HasExited)
                {
                    try
                    {
                        _editorProcess.Kill();
                        _editorProcess.WaitForExit(1000); // Wait up to 1 second
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error killing process: {ex.Message}");
                    }
                    
                    _editorProcess.Dispose();
                    _editorProcess = null;
                }
                
                _isProcessing = false;
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Mesh editor stopped");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error stopping editor: {ex.Message}");
            }
        }
        
        private void UpdateMeshEditorScript()
        {
            try
            {
                // Check if edit.py exists in the repository folder
                string editPyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Desktop", "crft", "edit.py");
                
                if (!File.Exists(editPyPath))
                {
                    // If the file doesn't exist, we need to modify the original script to handle command-line arguments
                    string scriptContent = File.ReadAllText(editPyPath);
                    
                    // Check if the script already has the modifications
                    if (!scriptContent.Contains("--input") && !scriptContent.Contains("--output") && !scriptContent.Contains("--device"))
                    {
                        // Modify the script to handle command-line arguments
                        string modifiedScript = ModifyPythonScript(scriptContent);
                        File.WriteAllText(editPyPath, modifiedScript);
                        
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            "Updated Python script with command-line arguments support");
                    }
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                    $"Could not update Python script: {ex.Message}");
            }
        }
        
        private string ModifyPythonScript(string originalScript)
        {
            // Add import for argparse at the top
            string importSection = "import cv2\nimport numpy as np\nimport open3d as o3d\nimport mediapipe as mp\nimport threading\nimport time\nimport copy\nimport math\nimport os\nimport argparse\nfrom collections import deque";
            originalScript = originalScript.Replace("import cv2\nimport numpy as np\nimport open3d as o3d\nimport mediapipe as mp\nimport threading\nimport time\nimport copy\nimport math\nimport os\nfrom collections import deque", importSection);
            
            // Add command-line argument parsing before the main function
            string argsParser = @"
def parse_args():
    # Parse command-line arguments for the mesh editor
    parser = argparse.ArgumentParser(description='Professional 3D Mesh Editor')
    parser.add_argument('--input', type=str, default='cylinder.stl',
                        help='Path to input mesh file')
    parser.add_argument('--output', type=str, default='modified_mesh.stl',
                        help='Path to output modified mesh file')
    parser.add_argument('--device', type=int, default=0,
                        help='Camera device index (default: 0)')
    return parser.parse_args()
";
            
            // Modify the main function to use arguments
            string oldMainFunc = @"def main():
    # Entry point for the application
    print(""nInitializing Professional Mesh Editor"")
    try:
        editor = MeshEditor(""cylinder.stl"")
        editor.run()
    except Exception as e:
        print(f""Fatal error: {e}"")";
            
            string newMainFunc = @"def main():
    # Entry point for the application
    print(""nInitializing Professional Mesh Editor"")
    try:
        # Parse command-line arguments
        args = parse_args()
        
        # Create editor with input mesh path and device index
        editor = MeshEditor(args.input)
        
        # Configure output path
        editor._output_path = args.output
        
        # Set camera device index if provided
        if hasattr(editor, '_deviceIndex'):
            editor._deviceIndex = args.device
            
        editor.run()
    except Exception as e:
        print(f""Fatal error: {e}"")";
            
            // Add a save method that uses the specified output path
            string saveMethod = @"    def save_mesh(self):
        # Save modified mesh to file
        if self.mesh_modified:
            try:
                output_path = getattr(self, '_output_path', 'modified_mesh.stl')
                o3d.io.write_triangle_mesh(output_path, self.mesh)
                print(f""Mesh saved as {output_path}"")
            except Exception as e:
                print(f""Error saving mesh: {e}"")";
            
            // Replace the existing save_mesh method
            originalScript = originalScript.Replace(@"    def save_mesh(self):
        # Save modified mesh to file
        if self.mesh_modified:
            try:
                o3d.io.write_triangle_mesh(""modified_mesh.stl"", self.mesh)
                print(""Mesh saved as modified_mesh.stl"")
            except Exception as e:
                print(f""Error saving mesh: {e}"")", saveMethod);
            
            // Replace the main function
            originalScript = originalScript.Replace(oldMainFunc, newMainFunc);
            
            // Add the argument parser function before if __name__ == "__main__"
            int mainPos = originalScript.LastIndexOf("if __name__ == \"__main__\":");
            if (mainPos >= 0)
            {
                originalScript = originalScript.Insert(mainPos, argsParser);
            }
            
            return originalScript;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Initialize output status
            string status = "Ready";
            DA.SetData(1, status);
            
            // Get input parameters
            Mesh inputMesh = null;
            bool enable = false;
            int deviceIndex = 0;
            
            if (!DA.GetData(0, ref inputMesh)) return;
            if (!DA.GetData(1, ref enable)) return;
            if (!DA.GetData(2, ref deviceIndex)) deviceIndex = 0;
            
            // Check if enable state changed
            bool stateChanged = (enable != _previousEnabledState || 
                                (enable && deviceIndex != _previousDeviceIndex));
            
            _previousEnabledState = enable;
            _previousDeviceIndex = deviceIndex;
            
            // If enabling, check dependencies
            if (enable && !_isProcessing)
            {
                // First, check if our Python dependencies are installed
                if (!CheckForRequiredDependencies())
                {
                    status = "Missing Python dependencies";
                    DA.SetData(1, status);
                    return;
                }
                
                // Update the Python script to accept command-line arguments if needed
                UpdateMeshEditorScript();
                
                // Export the input mesh to STL
                if (ExportMeshToStl(inputMesh, _tempMeshPath))
                {
                    // Launch the editor
                    LaunchMeshEditor(deviceIndex);
                    status = "Mesh editor running";
                }
                else
                {
                    status = "Failed to export mesh";
                }
            }
            else if (!enable && _isProcessing)
            {
                // Stop the editor if it's running
                StopMeshEditor();
                status = "Mesh editor stopped";
            }
            else if (enable && _isProcessing)
            {
                // Editor already running
                status = "Mesh editor running";
                
                // If device changed, restart the editor
                if (deviceIndex != _previousDeviceIndex)
                {
                    StopMeshEditor();
                    LaunchMeshEditor(deviceIndex);
                    status = "Restarted mesh editor with new device";
                }
            }
            
            // Check if we should update the output mesh
            if (_shouldRefreshOutput || (_outputMesh == null && File.Exists(_modifiedMeshPath)))
            {
                try
                {
                    Mesh freshMesh = ImportMeshFromStl(_modifiedMeshPath);
                    if (freshMesh != null)
                    {
                        _outputMesh = freshMesh;
                        _shouldRefreshOutput = false;
                        status = "Mesh updated from editor";
                    }
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                        $"Error refreshing mesh: {ex.Message}");
                }
            }
            
            // Output the latest mesh
            if (_outputMesh != null)
            {
                DA.SetData(0, _outputMesh);
            }
            else
            {
                // If no modified mesh yet, output the input mesh
                DA.SetData(0, inputMesh);
            }
            
            // Set the status output
            DA.SetData(1, status);
        }
        
        public override void RemovedFromDocument(GH_Document document)
        {
            StopMeshEditor();
            base.RemovedFromDocument(document);
        }
        
        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                StopMeshEditor();
            }
            base.DocumentContextChanged(document, context);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // Create a simple icon
                Bitmap icon = new Bitmap(24, 24);
                using (Graphics g = Graphics.FromImage(icon))
                {
                    g.Clear(Color.White);
                    using (Pen pen = new Pen(Color.Black, 1))
                    {
                        // Draw a mesh-like structure
                        g.DrawLine(pen, 4, 4, 20, 4);
                        g.DrawLine(pen, 4, 4, 4, 20);
                        g.DrawLine(pen, 4, 20, 20, 20);
                        g.DrawLine(pen, 20, 4, 20, 20);
                        g.DrawLine(pen, 4, 4, 20, 20);
                        g.DrawLine(pen, 4, 20, 20, 4);
                    }
                }
                return icon;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("c5d65f2e-82b9-4e7d-a801-9d78b3c72d9f"); }
        }
    }
}