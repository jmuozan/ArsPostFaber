using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace crft.Slicer
{
    /// <summary>
    /// Applies blur filter to voxel grid.
    /// Ported from t43/blur_voxels.c
    /// </summary>
    public class BlurVoxelsComponent : GH_Component
    {
        public BlurVoxelsComponent()
          : base("Blur Voxels", "VoxBlur",
              "Apply blur filter to voxel grid", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Voxels", "V", "Voxel grid as boxes", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Radius", "R", "Blur radius", GH_ParamAccess.item, 1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBoxParameter("Blurred Voxels", "BV", "Blurred voxel grid", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "BlurVoxels component not yet implemented (port blur_voxels.c)");
        }

        public override Guid ComponentGuid => new Guid("E5D9G7H4-5678-4G0B-EFG1-4567890ABCDE");
    }
}