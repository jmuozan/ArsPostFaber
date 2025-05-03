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
            
            if (enable && !_isProcessing)
            {
                // Save input mesh to temporary file
                SaveMeshToStl(inputMesh, _tempInputPath);
                
                // Start the mesh editor
                StartMeshEditor();
            }
            else if (!enable && _isProcessing)
            {
                // Stop the mesh editor
                StopMeshEditor();
            }
            
            // Check output
            if (_lastEditedMesh != null)
            {
                DA.SetData(0, _lastEditedMesh);
            }
            else
            {
                // If we don't have an edited mesh yet, pass through the input mesh
                DA.SetData(0, inputMesh);
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
                
                // Create a 3DM file
                Rhino.FileIO.File3dm file = new Rhino.FileIO.File3dm();
                file.Objects.AddMesh(mesh);
                
                bool success = file.Write(filePath, 7); // Version 7 for newest format
                
                if (!success)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to save mesh to {filePath}");
                }
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
                
                // Use a simpler approach - create a mesh and import from file
                Mesh mesh = new Mesh();
                bool success = false;
                
                try
                {
                    // Use Rhino.FileIO.File3dm.Read with correct parameters
                    var file = Rhino.FileIO.File3dm.Read(filePath);
                    if (file != null)
                    {
                        success = true;
                        foreach (Rhino.FileIO.File3dmObject obj in file.Objects)
                        {
                            if (obj.Geometry is Mesh objMesh)
                            {
                                mesh = objMesh;
                                return mesh;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If File3dm doesn't work, try to import directly
                    Debug.WriteLine($"Error reading 3dm file: {ex.Message}");
                    success = false;
                }
                
                if (!success)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to read mesh from {filePath}");
                    return null;
                }
                
                return mesh;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error loading mesh: {ex.Message}");
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
                
                // Create a simplified Python script that just runs the original script
                string pythonLauncherScript = @"
import sys
import os
import argparse
import subprocess

def parse_args():
    parser = argparse.ArgumentParser(description='Mesh Editor Launcher')
    parser.add_argument('--input', type=str, required=True, help='Path to input mesh file')
    parser.add_argument('--output', type=str, required=True, help='Path to output mesh file')
    parser.add_argument('--device', type=int, default=0, help='Camera device index')
    return parser.parse_args()

def main():
    args = parse_args()
    # Find the original meshedit.py script
    script_dir = os.path.dirname(os.path.abspath(__file__))
    home_dir = os.path.expanduser('~')
    possible_paths = [
        os.path.join(script_dir, 'meshedit.py'),
        os.path.join(home_dir, 'Desktop', 'crft', 'meshedit.py'),
        os.path.join(os.path.dirname(script_dir), 'meshedit.py')
    ]
    
    script_path = None
    for path in possible_paths:
        if os.path.exists(path):
            script_path = path
            break
    
    if script_path is None:
        print('Error: Could not find meshedit.py script')
        sys.exit(1)
    
    # Launch the original script with the arguments
    cmd = [sys.executable, script_path, 
           '--input', args.input, 
           '--output', args.output, 
           '--device', str(args.device)]
    
    print('Launching: ' + ' '.join(cmd))
    subprocess.run(cmd)

if __name__ == '__main__':
    main()
";

                // Create temporary directory and script file
                string tempScriptDir = Path.Combine(Path.GetTempPath(), $"meshedit_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempScriptDir);
                string tempScriptPath = Path.Combine(tempScriptDir, "meshedit_launcher.py");
                
                // Write the script to the temporary file
                File.WriteAllText(tempScriptPath, pythonLauncherScript);
                
                // Create an AppleScript to open Terminal and run the mesh editor
                string appleScript = $@"
tell application ""Terminal""
    do script ""cd '{tempScriptDir}' && python3 meshedit_launcher.py --input '{_tempInputPath}' --output '{_tempOutputPath}' --device {_deviceIndex}""
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
                    "Mesh editor has been launched. Edit the mesh in the window and press 'S' to save changes.");
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
                    killProcess.StartInfo.Arguments = "-f meshedit.py";
                    killProcess.StartInfo.UseShellExecute = false;
                    killProcess.StartInfo.CreateNoWindow = true;
                    
                    killProcess.Start();
                    killProcess.WaitForExit(1000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error running pkill command: {ex.Message}");
                }
                
                // Try to load the final mesh from the output file
                try
                {
                    if (File.Exists(_tempOutputPath))
                    {
                        Mesh finalMesh = LoadMeshFromStl(_tempOutputPath);
                        if (finalMesh != null)
                        {
                            _lastEditedMesh = finalMesh;
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Loaded final edited mesh");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading final mesh: {ex.Message}");
                }
                
                _isProcessing = false;
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
                        if (fileInfo.LastWriteTime > _lastModifiedTime)
                        {
                            _lastModifiedTime = fileInfo.LastWriteTime;
                            
                            // Load the mesh from the output file
                            Mesh updatedMesh = LoadMeshFromStl(_tempOutputPath);
                            if (updatedMesh != null)
                            {
                                _lastEditedMesh = updatedMesh;
                                
                                // Update the solution
                                this.OnDisplayExpired(true);
                            }
                        }
                    }
                    
                    // Wait before checking again
                    Thread.Sleep(1000); // Check every second
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
            try
            {
                if (File.Exists(_tempInputPath))
                {
                    File.Delete(_tempInputPath);
                }
                
                if (File.Exists(_tempOutputPath))
                {
                    File.Delete(_tempOutputPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning up temporary files: {ex.Message}");
            }
            
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

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("f3eb7dcd-8c52-4b46-8252-964a9552eb5a");
    }
}