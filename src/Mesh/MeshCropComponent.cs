using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace crft
{
    /// <summary>
    /// Crops a mesh by an axis-aligned bounding box defined by min/max values on X, Y, Z.
    /// </summary>
    public class MeshCropComponent : GH_Component
    {
        public MeshCropComponent()
          : base("Mesh Crop", "CropMesh",
              "Crops the input mesh to the specified axis-aligned bounding box", 
              "crft", "Mesh")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Input mesh to crop", GH_ParamAccess.item);
            pManager.AddNumberParameter("Scale X", "SX", "Scale factor of bounding box along X axis (1 = original)", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Scale Y", "SY", "Scale factor of bounding box along Y axis (1 = original)", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Scale Z", "SZ", "Scale factor of bounding box along Z axis (1 = original)", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Offset X", "OX", "Offset of bounding box center along X axis", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Offset Y", "OY", "Offset of bounding box center along Y axis", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Offset Z", "OZ", "Offset of bounding box center along Z axis", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Cropped Mesh", "M", "Mesh cropped to bounding box", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // read inputs
            Mesh mesh = null;
            double sx = 1.0, sy = 1.0, sz = 1.0;
            double ox = 0.0, oy = 0.0, oz = 0.0;
            if (!DA.GetData(0, ref mesh) || mesh is null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No input mesh.");
                return;
            }
            DA.GetData(1, ref sx);
            DA.GetData(2, ref sy);
            DA.GetData(3, ref sz);
            DA.GetData(4, ref ox);
            DA.GetData(5, ref oy);
            DA.GetData(6, ref oz);
            // original bbox
            var bbox = mesh.GetBoundingBox(false);
            var center = bbox.Center;
            var hx = bbox.Max.X - center.X;
            var hy = bbox.Max.Y - center.Y;
            var hz = bbox.Max.Z - center.Z;
            // scale extents
            hx *= sx; hy *= sy; hz *= sz;
            // apply offset
            center.X += ox; center.Y += oy; center.Z += oz;
            // compute new min/max
            var min = new Point3d(center.X - hx, center.Y - hy, center.Z - hz);
            var max = new Point3d(center.X + hx, center.Y + hy, center.Z + hz);
            // crop mesh
            var outMesh = new Mesh();
            var indexMap = new Dictionary<int,int>();
            foreach (var fi in mesh.Faces)
            {
                int[] vids = fi.IsQuad ? new[] { fi.A, fi.B, fi.C, fi.D } : new[] { fi.A, fi.B, fi.C };
                bool inside = true;
                foreach (var vi in vids)
                {
                    var v = mesh.Vertices[vi];
                    if (v.X < min.X || v.X > max.X || v.Y < min.Y || v.Y > max.Y || v.Z < min.Z || v.Z > max.Z)
                    {
                        inside = false; break;
                    }
                }
                if (!inside) continue;
                var newIdx = new List<int>();
                foreach (var vi in vids)
                {
                    if (!indexMap.TryGetValue(vi, out int ni))
                    {
                        ni = outMesh.Vertices.Add(mesh.Vertices[vi]);
                        indexMap[vi] = ni;
                    }
                    newIdx.Add(ni);
                }
                if (newIdx.Count == 3) outMesh.Faces.AddFace(newIdx[0], newIdx[1], newIdx[2]);
                else if (newIdx.Count == 4) outMesh.Faces.AddFace(newIdx[0], newIdx[1], newIdx[2], newIdx[3]);
            }
            outMesh.Normals.ComputeNormals();
            outMesh.Compact();
            if (outMesh.Faces.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No faces in the scaled/offset box.");
            DA.SetData(0, outMesh);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("A1E4B2F5-D7C8-4E3F-9B21-C8D7E6F5A123");
    }
}