using System;
using System.IO;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Drawing;

namespace PlyImporter
{
    public class PlyImporterComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PlyImporter class.
        /// </summary>
        public PlyImporterComponent()
          : base("PLY Importer", "PLYImp",
              "Imports a .ply file from a specified path using Rhino's import command",
              "Mesh", "Import")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "P", "Path to the .ply file", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Import", "I", "Set to true to trigger import", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Info", "I", "Information about the PLY file", GH_ParamAccess.item);
        }

        // Remember last imported file and state
        private string _lastFilePath = string.Empty;

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get input
            string filePath = string.Empty;
            if (!DA.GetData(0, ref filePath)) return;
            
            bool importNow = false;
            DA.GetData(1, ref importNow);

            // Check if file exists
            if (!File.Exists(filePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File does not exist at the specified path.");
                return;
            }

            // Check if it's a PLY file
            if (!filePath.ToLower().EndsWith(".ply"))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "File must be a .ply file.");
                return;
            }

            // Generate file info
            string info = GenerateFileInfo(filePath);

            // Import if requested
            if (importNow && filePath != _lastFilePath)
            {
                ImportPlyFile(filePath);
                _lastFilePath = filePath;
            }

            // Set output
            DA.SetData(0, info);
        }

        private string GenerateFileInfo(string filePath)
        {
            System.Text.StringBuilder info = new System.Text.StringBuilder();
            
            try
            {
                // File information
                FileInfo fileInfo = new FileInfo(filePath);
                info.AppendLine($"File: {Path.GetFileName(filePath)}");
                info.AppendLine($"Full Path: {filePath}");
                info.AppendLine($"Size: {fileInfo.Length / 1024.0:F2} KB");
                info.AppendLine($"Last Modified: {fileInfo.LastWriteTime}");
                info.AppendLine("");
                info.AppendLine("To import the PLY file into Rhino, set the Import toggle to True.");
                info.AppendLine("");
                info.AppendLine("Note: This component will import the PLY file directly into the Rhino document.");
                info.AppendLine("The imported geometry will not be connected to Grasshopper's data flow.");
            }
            catch (Exception ex)
            {
                info.AppendLine($"Error reading file info: {ex.Message}");
            }
            
            return info.ToString();
        }

        private void ImportPlyFile(string filePath)
        {
            try
            {
                // Format the file path properly for scripting
                string escapedPath = filePath.Replace("\\", "\\\\");
                
                // Run the import command using Rhino's scripting interface
                string script = string.Format("_-Import \"{0}\" _Enter", escapedPath);
                Rhino.RhinoApp.RunScript(script, false);
                
                // Notify the user
                Rhino.RhinoApp.WriteLine("PLY file imported: " + filePath);
            }
            catch (Exception ex)
            {
                Rhino.RhinoApp.WriteLine("Error importing PLY file: " + ex.Message);
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add your own icon here
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("5A8D4E31-B5C6-40B4-9A19-7B60AF2E9C40"); }
        }
    }
}