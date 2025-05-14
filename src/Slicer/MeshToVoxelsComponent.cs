using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

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
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "MeshToVoxels component not yet implemented (port mesh_to_voxels.c)");
        }

        public override Guid ComponentGuid => new Guid("E1E5C8D1-1234-4F56-ABCD-0123456789AB");
    }
}