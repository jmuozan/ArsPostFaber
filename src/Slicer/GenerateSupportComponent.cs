using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace crft.Slicer
{
    /// <summary>
    /// Generates support structures for a given geometry.
    /// Ported from t43/generate_support.c
    /// </summary>
    public class GenerateSupportComponent : GH_Component
    {
        public GenerateSupportComponent()
          : base("Generate Support", "SupGen",
              "Generate support structures for mesh or brep", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Geometry", "G", "Mesh or Brep to generate support for", GH_ParamAccess.item);
            pManager.AddNumberParameter("Overhang Angle", "OA", "Maximum overhang angle", GH_ParamAccess.item, 45.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("Support Geometry", "S", "Generated support geometry", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "GenerateSupport component not yet implemented (port generate_support.c)");
        }

        public override Guid ComponentGuid => new Guid("07F1I9J6-7890-4I2D-GHI3-67890ABCDEF1");
    }
}