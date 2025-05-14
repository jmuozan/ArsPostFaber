using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace crft.Slicer
{
    /// <summary>
    /// Applies accentuation to voxel grid (sharpen filter).
    /// Ported from t43/accentuate_voxels.c
    /// </summary>
    public class AccentuateVoxelsComponent : GH_Component
    {
        public AccentuateVoxelsComponent()
          : base("Accentuate Voxels", "VoxAccent",
              "Apply accentuation filter to voxel grid", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Voxels", "V", "Voxel grid as boxes", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Strength", "S", "Accentuation strength", GH_ParamAccess.item, 1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBoxParameter("Accentuated Voxels", "AV", "Accentuated voxel grid", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "AccentuateVoxels component not yet implemented (port accentuate_voxels.c)");
        }

        public override Guid ComponentGuid => new Guid("F6E0H8I5-6789-4H1C-FGH2-567890ABCDEF");
    }
}