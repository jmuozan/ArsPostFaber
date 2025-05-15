using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace crft.Slicer
{
    /// <summary>
    /// Converts voxel grid to G-code for additive manufacturing.
    /// Ported from t43/voxels_to_gcode.c
    /// </summary>
    public class VoxelsToGCodeComponent : GH_Component
    {
        public VoxelsToGCodeComponent()
          : base("Voxels To GCode", "Vox2GCode",
              "Generate G-code from voxel grid", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Voxels", "V", "Voxel grid as boxes", GH_ParamAccess.list);
            pManager.AddNumberParameter("Feed Rate", "F", "Feed rate (mm/min)", GH_ParamAccess.item, 1500.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("GCode", "G", "Generated G-code lines", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "VoxelsToGCode component not yet implemented (port voxels_to_gcode.c)");
        }

        public override Guid ComponentGuid => new Guid("EC178F86-190A-48F6-B41A-C0C25851EE86");
    }
}