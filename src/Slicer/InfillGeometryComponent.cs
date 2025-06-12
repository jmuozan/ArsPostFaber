using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace crft
{
    /// <summary>
    /// Generates infill curves within the innermost region per layer.
    /// </summary>
    public class InfillGeometryComponent : GH_Component
    {
        public InfillGeometryComponent()
          : base("Infill Geometry", "InfillGeo",
              "Generates infill toolpath curves within the region defined by shells.",
              "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Settings", "S", "Slicer settings", GH_ParamAccess.item);
            pManager.AddCurveParameter("Region", "R", "Innermost region curves per layer", GH_ParamAccess.tree);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Settings", "S", "Slicer settings (pass-through)", GH_ParamAccess.item);
            pManager.AddCurveParameter("Infill", "I", "Infill curves per layer", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper settingsWrapper = null;
            if (!DA.GetData(0, ref settingsWrapper) || !(settingsWrapper.Value is SlicerSettings settings))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slicer settings.");
                return;
            }
            GH_Structure<GH_Curve> regionTree = new GH_Structure<GH_Curve>();
            DA.GetDataTree(1, out regionTree);

            var infillTree = new GH_Structure<GH_Curve>();
            double tol = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
            double spacing = settings.InfillSpacing;

            for (int i = 0; i < regionTree.PathCount; i++)
            {
                var path = regionTree.Paths[i];
                var branch = regionTree.Branches[i];
                if (branch == null || branch.Count == 0) continue;

                // Build planar breps from region curves
                var regionCurves = new List<Curve>();
                foreach (var ghc in branch)
                    if (ghc?.Value != null)
                        regionCurves.Add(ghc.Value.DuplicateCurve());
                var breps = Brep.CreatePlanarBreps(regionCurves);
                if (breps == null) continue;

                foreach (var brep in breps)
                {
                    var bbox = brep.GetBoundingBox(true);
                    double minX = bbox.Min.X, maxX = bbox.Max.X;
                    double minY = bbox.Min.Y, maxY = bbox.Max.Y;
                    double z = bbox.Min.Z;
                    for (double y = minY + spacing/2.0; y <= maxY; y += spacing)
                    {
                        var line = new Line(new Point3d(minX - spacing, y, z), new Point3d(maxX + spacing, y, z));
                        var lineCurve = new LineCurve(line);
                        // Intersect line with brep to get infill segments
                        Curve[] segs;
                        Point3d[] pts;
                        Intersection.CurveBrep(lineCurve, brep, tol, out segs, out pts);
                        if (segs != null)
                            foreach (var seg in segs)
                                infillTree.Append(new GH_Curve(seg), path);
                    }
                }
            }
            DA.SetData(0, settings);
            DA.SetDataTree(1, infillTree);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("5EE6180A-747E-4FD1-B83B-5CA6B1B83D9D");
    }
}