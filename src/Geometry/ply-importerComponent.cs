using System;
using System.IO;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Drawing;
using System.Text;

namespace PlyImporter
{
    public class PlyImporterComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PlyImporter class.
        /// </summary>
        public PlyImporterComponent()
          : base("PLY Importer", "PLYImp",
              "Imports a .ply file from a specified path as point cloud data",
              "crft", "Geometry")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "P", "Path to the .ply file", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Import", "I", "Set to true to trigger import", GH_ParamAccess.item, true);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "Pt", "Point cloud data from PLY file", GH_ParamAccess.list);
            pManager.AddColourParameter("Colors", "C", "Vertex colors if available", GH_ParamAccess.list);
            pManager.AddTextParameter("Info", "I", "Information about the imported data", GH_ParamAccess.item);
        }

        // Cache values for efficiency
        private string _lastFilePath = string.Empty;
        private bool _lastImportState = false;
        private List<Point3d> _cachedPoints = new List<Point3d>();
        private List<Color> _cachedColors = new List<Color>();

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Reset cached values when component is reset
            if (GH_Document.IsEscapeKeyDown())
            {
                _lastFilePath = string.Empty;
                _cachedPoints.Clear();
                _cachedColors.Clear();
            }

            // Get input
            string filePath = string.Empty;
            if (!DA.GetData(0, ref filePath)) return;
            
            bool importNow = true; // Default to true
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

            // Check if we need to reprocess (new file or import state changed)
            bool needsProcessing = filePath != _lastFilePath || importNow != _lastImportState;
            if (needsProcessing && importNow)
            {
                try
                {
                    // Clear cache
                    _cachedPoints.Clear();
                    _cachedColors.Clear();
                    
                    // Import and extract points
                    ExtractPointsFromPlyBinary(filePath, _cachedPoints, _cachedColors);
                    
                    // Update state
                    _lastFilePath = filePath;
                    _lastImportState = importNow;
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error importing PLY file: " + ex.Message);
                }
            }

            // Generate info
            string info = GenerateInfo(filePath, _cachedPoints, _cachedColors);

            // Set outputs
            DA.SetDataList(0, _cachedPoints);
            DA.SetDataList(1, _cachedColors);
            DA.SetData(2, info);
        }

        private class PlyHeader
        {
            public int VertexCount { get; set; }
            public int FaceCount { get; set; }
            public bool IsBinary { get; set; }
            public bool IsBigEndian { get; set; }
            public bool HasColors { get; set; }
            public int HeaderEndPosition { get; set; }
            public List<PlyProperty> VertexProperties { get; set; } = new List<PlyProperty>();
        }

        private class PlyProperty
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public int ByteSize 
            { 
                get 
                {
                    switch (Type.ToLower())
                    {
                        case "char": return 1;
                        case "uchar": return 1;
                        case "short": return 2;
                        case "ushort": return 2;
                        case "int": return 4;
                        case "uint": return 4;
                        case "float": return 4;
                        case "double": return 8;
                        default: return 0;
                    }
                }
            }
        }

        private PlyHeader ParsePlyHeader(string filePath)
        {
            PlyHeader header = new PlyHeader();
            
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (StreamReader reader = new StreamReader(fs))
            {
                string line;
                int lineCount = 0;
                int headerStartPos = 0;
                
                // First line should be "ply"
                line = reader.ReadLine();
                lineCount++;
                
                if (line == null || !line.Equals("ply"))
                    throw new Exception("Not a valid PLY file: missing 'ply' header.");

                // Track the header start position
                headerStartPos = line.Length + 1; // +1 for newline
                
                // Process the rest of the header
                List<PlyProperty> currentProperties = null;
                
                while ((line = reader.ReadLine()) != null)
                {
                    lineCount++;
                    string trimmedLine = line.Trim();
                    headerStartPos += line.Length + 1; // +1 for newline
                    
                    if (trimmedLine.Equals("end_header"))
                    {
                        header.HeaderEndPosition = headerStartPos;
                        break;
                    }
                    
                    string[] tokens = trimmedLine.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (tokens.Length > 0)
                    {
                        if (tokens[0].Equals("format"))
                        {
                            header.IsBinary = !tokens[1].Equals("ascii");
                            header.IsBigEndian = tokens[1].Contains("big_endian");
                        }
                        else if (tokens[0].Equals("element"))
                        {
                            if (tokens[1].Equals("vertex"))
                            {
                                header.VertexCount = int.Parse(tokens[2]);
                                currentProperties = header.VertexProperties;
                            }
                            else if (tokens[1].Equals("face"))
                            {
                                header.FaceCount = int.Parse(tokens[2]);
                                currentProperties = null; // We're only interested in vertex properties
                            }
                            else
                            {
                                currentProperties = null; // Unknown element type
                            }
                        }
                        else if (tokens[0].Equals("property") && currentProperties != null)
                        {
                            // Skip "property list" declarations (used for faces)
                            if (tokens[1].Equals("list")) continue;
                            
                            // Regular property (e.g., "property float x")
                            PlyProperty prop = new PlyProperty
                            {
                                Type = tokens[1],
                                Name = tokens[2]
                            };
                            
                            currentProperties.Add(prop);
                            
                            // Check if this is a color property
                            if (prop.Name.Equals("red") || prop.Name.Equals("r") ||
                                prop.Name.Equals("green") || prop.Name.Equals("g") ||
                                prop.Name.Equals("blue") || prop.Name.Equals("b"))
                            {
                                header.HasColors = true;
                            }
                        }
                    }
                }
            }
            
            return header;
        }

        private void ExtractPointsFromPlyBinary(string filePath, List<Point3d> points, List<Color> colors)
        {
            // Parse the header first
            PlyHeader header = ParsePlyHeader(filePath);
            
            // Return early if no vertices to read
            if (header.VertexCount <= 0) return;
            
            // Find the indices of x, y, z and r, g, b properties
            int xIndex = -1, yIndex = -1, zIndex = -1;
            int rIndex = -1, gIndex = -1, bIndex = -1;
            
            for (int i = 0; i < header.VertexProperties.Count; i++)
            {
                string name = header.VertexProperties[i].Name.ToLower();
                if (name.Equals("x")) xIndex = i;
                else if (name.Equals("y")) yIndex = i;
                else if (name.Equals("z")) zIndex = i;
                else if (name.Equals("red") || name.Equals("r")) rIndex = i;
                else if (name.Equals("green") || name.Equals("g")) gIndex = i;
                else if (name.Equals("blue") || name.Equals("b")) bIndex = i;
            }
            
            if (xIndex == -1 || yIndex == -1 || zIndex == -1)
                throw new Exception("PLY file is missing x, y, or z coordinates.");
            
            bool hasColors = header.HasColors && rIndex >= 0 && gIndex >= 0 && bIndex >= 0;
            
            if (header.IsBinary)
            {
                // Process binary format
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    // Skip the header
                    fs.Seek(header.HeaderEndPosition, SeekOrigin.Begin);
                    
                    // Read each vertex
                    for (int i = 0; i < header.VertexCount; i++)
                    {
                        double x = 0, y = 0, z = 0;
                        int r = 255, g = 255, b = 255;
                        
                        // Read each property
                        for (int j = 0; j < header.VertexProperties.Count; j++)
                        {
                            PlyProperty prop = header.VertexProperties[j];
                            double value = ReadBinaryProperty(reader, prop.Type, header.IsBigEndian);
                            
                            if (j == xIndex) x = value;
                            else if (j == yIndex) y = value;
                            else if (j == zIndex) z = value;
                            else if (hasColors && j == rIndex) r = (int)value;
                            else if (hasColors && j == gIndex) g = (int)value;
                            else if (hasColors && j == bIndex) b = (int)value;
                        }
                        
                        points.Add(new Point3d(x, y, z));
                        
                        if (hasColors)
                        {
                            // Normalize if necessary (some PLY files use 0-255, others use 0-1)
                            if (r <= 1 && g <= 1 && b <= 1)
                            {
                                r = (int)(r * 255);
                                g = (int)(g * 255);
                                b = (int)(b * 255);
                            }
                            
                            colors.Add(Color.FromArgb(
                                Math.Min(r, 255),
                                Math.Min(g, 255),
                                Math.Min(b, 255)));
                        }
                    }
                }
            }
            else
            {
                // Process ASCII format
                using (StreamReader reader = new StreamReader(filePath))
                {
                    // Skip the header
                    string line;
                    while ((line = reader.ReadLine()) != null && !line.Equals("end_header")) { }
                    
                    // Read vertices
                    for (int i = 0; i < header.VertexCount; i++)
                    {
                        line = reader.ReadLine();
                        if (line == null) break;
                        
                        string[] values = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        if (values.Length < header.VertexProperties.Count) continue;
                        
                        double x = Convert.ToDouble(values[xIndex]);
                        double y = Convert.ToDouble(values[yIndex]);
                        double z = Convert.ToDouble(values[zIndex]);
                        
                        points.Add(new Point3d(x, y, z));
                        
                        if (hasColors)
                        {
                            int r = Convert.ToInt32(values[rIndex]);
                            int g = Convert.ToInt32(values[gIndex]);
                            int b = Convert.ToInt32(values[bIndex]);
                            
                            // Normalize if necessary
                            if (r <= 1 && g <= 1 && b <= 1)
                            {
                                r = (int)(r * 255);
                                g = (int)(g * 255);
                                b = (int)(b * 255);
                            }
                            
                            colors.Add(Color.FromArgb(
                                Math.Min(r, 255),
                                Math.Min(g, 255),
                                Math.Min(b, 255)));
                        }
                    }
                }
            }
        }

        private double ReadBinaryProperty(BinaryReader reader, string type, bool isBigEndian)
        {
            switch (type.ToLower())
            {
                case "char":
                    return (double)reader.ReadSByte();
                case "uchar":
                    return (double)reader.ReadByte();
                case "short":
                    {
                        short value = reader.ReadInt16();
                        if (isBigEndian)
                            value = ReverseBytes(value);
                        return (double)value;
                    }
                case "ushort":
                    {
                        ushort value = reader.ReadUInt16();
                        if (isBigEndian)
                            value = ReverseBytes(value);
                        return (double)value;
                    }
                case "int":
                    {
                        int value = reader.ReadInt32();
                        if (isBigEndian)
                            value = ReverseBytes(value);
                        return (double)value;
                    }
                case "uint":
                    {
                        uint value = reader.ReadUInt32();
                        if (isBigEndian)
                            value = ReverseBytes(value);
                        return (double)value;
                    }
                case "float":
                    {
                        float value = reader.ReadSingle();
                        if (isBigEndian)
                            value = ReverseBytes(value);
                        return (double)value;
                    }
                case "double":
                    {
                        double value = reader.ReadDouble();
                        if (isBigEndian)
                            value = ReverseBytes(value);
                        return value;
                    }
                default:
                    throw new Exception("Unsupported property type: " + type);
            }
        }

        // Helper methods for byte swapping (big endian support)
        private short ReverseBytes(short value)
        {
            return (short)((value & 0xFF) << 8 | ((value >> 8) & 0xFF));
        }

        private ushort ReverseBytes(ushort value)
        {
            return (ushort)((value & 0xFF) << 8 | ((value >> 8) & 0xFF));
        }

        private int ReverseBytes(int value)
        {
            return (int)((value & 0xFF) << 24 | ((value >> 8) & 0xFF) << 16 | ((value >> 16) & 0xFF) << 8 | ((value >> 24) & 0xFF));
        }

        private uint ReverseBytes(uint value)
        {
            return ((value & 0xFF) << 24 | ((value >> 8) & 0xFF) << 16 | ((value >> 16) & 0xFF) << 8 | ((value >> 24) & 0xFF));
        }

        private float ReverseBytes(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        private double ReverseBytes(double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return BitConverter.ToDouble(bytes, 0);
        }

        private string GenerateInfo(string filePath, List<Point3d> points, List<Color> colors)
        {
            StringBuilder info = new StringBuilder();
            
            try
            {
                // File information
                FileInfo fileInfo = new FileInfo(filePath);
                info.AppendLine($"File: {Path.GetFileName(filePath)}");
                info.AppendLine($"Size: {fileInfo.Length / 1024.0:F2} KB");
                
                // Point data information
                info.AppendLine($"Points Imported: {points.Count}");
                
                if (colors.Count > 0)
                {
                    info.AppendLine($"Color Data: Yes ({colors.Count} colors)");
                }
                else
                {
                    info.AppendLine("Color Data: No");
                }
                
                if (points.Count > 0)
                {
                    // Calculate bounding box
                    BoundingBox bbox = new BoundingBox(points);
                    
                    info.AppendLine();
                    info.AppendLine("Bounding Box:");
                    info.AppendLine($"  Min: ({bbox.Min.X:F2}, {bbox.Min.Y:F2}, {bbox.Min.Z:F2})");
                    info.AppendLine($"  Max: ({bbox.Max.X:F2}, {bbox.Max.Y:F2}, {bbox.Max.Z:F2})");
                    info.AppendLine($"  Size: ({bbox.Max.X - bbox.Min.X:F2}, {bbox.Max.Y - bbox.Min.Y:F2}, {bbox.Max.Z - bbox.Min.Z:F2})");
                }
            }
            catch (Exception ex)
            {
                info.AppendLine($"Error generating info: {ex.Message}");
            }
            
            return info.ToString();
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