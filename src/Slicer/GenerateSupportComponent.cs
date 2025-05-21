using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.Kernel.Types;

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
            // Placeholder: no support generation implemented yet
            double overhangAngle = 45.0;
            DA.GetData(1, ref overhangAngle);
            DA.SetDataList(0, new List<GeometryBase>());
        }

        public override Guid ComponentGuid => new Guid("5D9BD3B3-4697-42E5-9ED2-0553A61E8069");
    }
}