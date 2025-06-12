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
    /// Generates perimeter shell offset curves and innermost region curves from sliced layers.
    /// </summary>
    public class ShellGeometryComponent : GH_Component
    {
        public ShellGeometryComponent()
          : base("Shell Geometry", "ShellGeo",
              "Generates perimeter shell offset curves from sliced geometry.",
              "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Settings", "S", "Slicer settings", GH_ParamAccess.item);
            pManager.AddCurveParameter("Layers", "L", "Sliced curves per layer", GH_ParamAccess.tree);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Settings", "S", "Slicer settings (pass-through)", GH_ParamAccess.item);
            pManager.AddCurveParameter("Shells", "C", "Shell offset curves per layer", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Region", "R", "Innermost region curves per layer", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper settingsWrapper = null;
            if (!DA.GetData(0, ref settingsWrapper) || !(settingsWrapper.Value is SlicerSettings settings))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slicer settings.");
                return;
            }
            GH_Structure<GH_Curve> layers = new GH_Structure<GH_Curve>();
            DA.GetDataTree(1, out layers);

            var shellTree = new GH_Structure<GH_Curve>();
            var regionTree = new GH_Structure<GH_Curve>();
            double tol = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;

            for (int i = 0; i < layers.PathCount; i++)
            {
                var path = layers.Paths[i];
                var branch = layers.Branches[i];
                if (branch == null) continue;

                // Determine layer Z from first curve
                double z = 0;
                var first = branch[0]?.Value;
                if (first != null)
                    z = first.GetBoundingBox(true).Min.Z;
                var plane = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);

                // Generate shells
                foreach (var ghc in branch)
                {
                    var baseCurve = ghc?.Value;
                    if (baseCurve == null) continue;
                    for (int s = 0; s < settings.NumShells; s++)
                    {
                        double offsetDist = s * settings.WallOffset;
                        if (s == 0)
                        {
                            shellTree.Append(new GH_Curve(baseCurve.DuplicateCurve()), path);
                        }
                        else
                        {
                            var offs = baseCurve.Offset(plane, -offsetDist, tol, CurveOffsetCornerStyle.Sharp);
                            if (offs != null)
                                foreach (var oc in offs)
                                    shellTree.Append(new GH_Curve(oc), path);
                        }
                    }
                }

                // Generate innermost region curves
                foreach (var ghc in branch)
                {
                    var baseCurve = ghc?.Value;
                    if (baseCurve == null) continue;
                    double dist = (settings.NumShells - 1) * settings.WallOffset;
                    if (dist <= 0)
                    {
                        regionTree.Append(new GH_Curve(baseCurve.DuplicateCurve()), path);
                    }
                    else
                    {
                        var regs = baseCurve.Offset(plane, -dist, tol, CurveOffsetCornerStyle.Sharp);
                        if (regs != null)
                            foreach (var rc in regs)
                                regionTree.Append(new GH_Curve(rc), path);
                    }
                }
            }

            DA.SetData(0, settings);
            DA.SetDataTree(1, shellTree);
            DA.SetDataTree(2, regionTree);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("B8A2AF79-3DB5-463B-AB2E-C5E40A1A4870");
    }
}