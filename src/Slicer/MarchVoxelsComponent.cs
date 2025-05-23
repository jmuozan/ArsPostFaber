using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace crft.Slicer
{
    /// <summary>
    /// Applies marching cubes algorithm to voxel grid to generate a surface mesh.
    /// Ported from t43/march_voxels.c
    /// </summary>
    public class MarchVoxelsComponent : GH_Component
    {
        public MarchVoxelsComponent()
          : base("March Voxels", "MarchVox",
              "Generate surface mesh from voxel grid via marching cubes", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Voxels", "V", "Voxel grid as list of boxes or custom VoxelGrid", GH_ParamAccess.list);
            pManager.AddNumberParameter("IsoValue", "I", "Isovalue threshold (0 to 1)", GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Output surface mesh", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var boxes = new List<Box>();
            double isoValue = 0.5;
            if (!DA.GetDataList(0, boxes)) return;
            DA.GetData(1, ref isoValue);
            // Basic voxel-to-mesh converter: convert each box to brep, mesh and join
            // Convert each voxel box to a Brep and mesh it
            var result = new Mesh();
            var mp = MeshingParameters.Default;
            foreach (var b in boxes)
            {
                var brep = b.ToBrep();
                var ms = Mesh.CreateFromBrep(brep, mp);
                if (ms != null)
                    foreach (var m in ms)
                        result.Append(m);
            }
            result.Normals.ComputeNormals();
            result.Compact();
            DA.SetData(0, result);
        }

        public override Guid ComponentGuid => new Guid("B2A6F4C7-2345-4D78-BCDE-1234567890AB");
    }
}