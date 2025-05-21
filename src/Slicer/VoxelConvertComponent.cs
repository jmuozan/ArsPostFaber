using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace crft.Slicer
{
    /// <summary>
    /// Converts between voxel file formats or data structures.
    /// Ported from t43/voxel_convert.c
    /// </summary>
    public class VoxelConvertComponent : GH_Component
    {
        public VoxelConvertComponent()
          : base("Voxel Convert", "VoxConv",
              "Convert voxel data between formats", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxel Input", "VIn", "Input voxel data or file path", GH_ParamAccess.item);
            pManager.AddTextParameter("Output Format", "Fmt", "Desired output format", GH_ParamAccess.item, "raw");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxel Output", "VOut", "Converted voxel data or file", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object input = null;
            string format = "raw";
            if (!DA.GetData(0, ref input)) return;
            DA.GetData(1, ref format);
            // Placeholder: no format conversion implemented yet
            DA.SetData(0, input);
        }

        public override Guid ComponentGuid => new Guid("D4C8F6E3-4567-4F9A-DEF0-34567890ABCD");
    }
}