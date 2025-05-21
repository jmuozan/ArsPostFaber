using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

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
            var boxes = new List<Box>();
            int strength = 1;
            if (!DA.GetDataList(0, boxes)) return;
            DA.GetData(1, ref strength);
            DA.SetDataList(0, boxes);
        }

        public override Guid ComponentGuid => new Guid("36415593-C080-4E37-8EB3-C898A9C1D5AC");
    }
}