using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.Kernel.Types;

namespace crft.Slicer
{
    /// <summary>
    /// Converts input geometry (Mesh or Brep) to a voxel grid representation.
    /// Ported from t43/mesh_to_voxels.c
    /// </summary>
    public class MeshToVoxelsComponent : GH_Component
    {
        public MeshToVoxelsComponent()
          : base("Mesh To Voxels", "M2Vox",
              "Convert mesh or brep to voxel grid", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Geometry", "G", "Mesh or Brep to voxelize", GH_ParamAccess.item);
            pManager.AddNumberParameter("Voxel Size", "VS", "Size of each voxel edge", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBoxParameter("Voxels", "V", "Voxels as box geometry", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Unwrap geometry input (Mesh or Brep)
            GH_ObjectWrapper geoWrapper = null;
            if (!DA.GetData(0, ref geoWrapper)) return;
            double voxelSize = 1.0;
            DA.GetData(1, ref voxelSize);
            object data = geoWrapper.Value;
            var goo = data as IGH_GeometricGoo;
            if (goo != null) data = goo.ScriptVariable();
            Mesh mesh = data as Mesh;
            Brep brep = data as Brep;
            if (mesh == null && brep == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input must be a Mesh or Brep");
                return;
            }
            Brep solid = brep;
            if (mesh != null)
            {
                // Convert mesh to Brep for solid containment testing
                Brep newSolid = Brep.CreateFromMesh(mesh, false);
                if (newSolid == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh conversion to Brep failed");
                    return;
                }
                solid = newSolid;
            }
            double tol = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            var bbox = solid.GetBoundingBox(true);
            double xmin = bbox.Min.X, xmax = bbox.Max.X;
            double ymin = bbox.Min.Y, ymax = bbox.Max.Y;
            double zmin = bbox.Min.Z, zmax = bbox.Max.Z;
            int nx = (int)Math.Ceiling((xmax - xmin) / voxelSize);
            int ny = (int)Math.Ceiling((ymax - ymin) / voxelSize);
            int nz = (int)Math.Ceiling((zmax - zmin) / voxelSize);
            var voxels = new List<Box>();
            for (int i = 0; i < nx; i++)
            {
                double x0 = xmin + i * voxelSize;
                double x1 = x0 + voxelSize;
                for (int j = 0; j < ny; j++)
                {
                    double y0 = ymin + j * voxelSize;
                    double y1 = y0 + voxelSize;
                    for (int k = 0; k < nz; k++)
                    {
                        double z0 = zmin + k * voxelSize;
                        double z1 = z0 + voxelSize;
                        var pt = new Point3d(x0 + voxelSize * 0.5, y0 + voxelSize * 0.5, z0 + voxelSize * 0.5);
                        if (solid.IsPointInside(pt, tol, false))
                        {
                            var box = new Box(Plane.WorldXY, new Interval(x0, x1), new Interval(y0, y1), new Interval(z0, z1));
                            voxels.Add(box);
                        }
                    }
                }
            }
            DA.SetDataList(0, voxels);
        }

        public override Guid ComponentGuid => new Guid("E1E5C8D1-1234-4F56-ABCD-0123456789AB");
    }
}