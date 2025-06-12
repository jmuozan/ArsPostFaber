using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System.Linq;

namespace crft
{
    /// <summary>
    /// Generates G-Code from sliced layer curves and settings.
    /// </summary>
    public class GCodeGeneratorComponent : GH_Component
    {
        public GCodeGeneratorComponent()
          : base("G-Code Generator", "GCodeGen",
              "Generates G-Code from sliced curves.",
              "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Settings", "S", "Slicer settings", GH_ParamAccess.item);
            pManager.AddCurveParameter("Shells", "C", "Shell offset curves per layer", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Infill", "I", "Optional infill curves per layer", GH_ParamAccess.tree);
            pManager[2].Optional = true;
            pManager.AddTextParameter("Start", "Start", "Optional start G-Code lines", GH_ParamAccess.list);
            pManager.AddTextParameter("End", "End", "Optional end G-Code lines", GH_ParamAccess.list);
            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("G-Code", "G", "Generated G-Code lines", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper settingsWrapper = null;
            if (!DA.GetData(0, ref settingsWrapper) || !(settingsWrapper.Value is SlicerSettings settings))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slicer settings.");
                return;
            }
            var shellTree = new GH_Structure<GH_Curve>();
            DA.GetDataTree(1, out shellTree);
            var infillTree = new GH_Structure<GH_Curve>();
            DA.GetDataTree(2, out infillTree);
            var startLines = new List<string>();
            var endLines = new List<string>();
            DA.GetDataList(3, startLines);
            DA.GetDataList(4, endLines);
            var lines = new List<string>();
            // Always home axes at start
            lines.Add("G28");
            if (startLines.Count > 0)
                lines.AddRange(startLines);
            else
            {
                lines.Add("G21");
                lines.Add("G90");
            }
            double e = 0.0;
            // Precompute smoothing angle in radians
            double smoothingAngleRad = settings.SmoothingAngle * Math.PI / 180.0;
            // Iterate through each layer branch
            // Iterate through each layer: shells then infill
            int pathCount = shellTree.PathCount;
            for (int i = 0; i < pathCount; i++)
            {
                var path = shellTree.Paths[i];
                // Shells
                var shellBranch = shellTree.Branches[i];
                if (shellBranch != null)
                {
                    foreach (var ghc in shellBranch)
                    {
                        var crv = ghc?.Value;
                        if (crv == null) continue;
                        Polyline polyline;
                        if (!crv.TryGetPolyline(out polyline))
                        {
                            double length = crv.GetLength();
                            double maxSeg = settings.MaxSegmentLength > 0 ? settings.MaxSegmentLength : settings.NozzleDiameter;
                            int divisions = (int)Math.Ceiling(length / maxSeg);
                            divisions = Math.Max(divisions, 1);
                            var pts = new List<Point3d>();
                            for (int j = 0; j <= divisions; j++)
                            {
                                double t = crv.Domain.Min + crv.Domain.Length * j / divisions;
                                pts.Add(crv.PointAt(t));
                            }
                            polyline = new Polyline(pts);
                        }
                        // Apply smoothing decimation
                        if (smoothingAngleRad > 0.0)
                            polyline = SimplifyPolyline(polyline, smoothingAngleRad);
                        if (polyline.Count < 2) continue;
                        var prev = polyline[0];
                        lines.Add($"G0 X{prev.X:F3} Y{prev.Y:F3} Z{prev.Z:F3}");
                        for (int k = 1; k < polyline.Count; k++)
                        {
                            var pt = polyline[k];
                            double d = pt.DistanceTo(prev);
                            e += d;
                            lines.Add($"G1 X{pt.X:F3} Y{pt.Y:F3} E{e:F4} F{settings.PrintSpeed:F0}");
                            prev = pt;
                        }
                    }
                }
                // Infill
                IList<GH_Curve> infillBranch = (i < infillTree.PathCount) ? infillTree.Branches[i] : null;
                if (infillBranch != null)
                {
                    foreach (var ghc in infillBranch)
                    {
                        var crv = ghc?.Value;
                        if (crv == null) continue;
                        if (!crv.TryGetPolyline(out Polyline polyline)) continue;
                        // Apply smoothing decimation
                        if (smoothingAngleRad > 0.0)
                            polyline = SimplifyPolyline(polyline, smoothingAngleRad);
                        var prev = polyline[0];
                        lines.Add($"G0 X{prev.X:F3} Y{prev.Y:F3} Z{prev.Z:F3}");
                        for (int k = 1; k < polyline.Count; k++)
                        {
                            var pt = polyline[k];
                            double d = pt.DistanceTo(prev);
                            e += d;
                            lines.Add($"G1 X{pt.X:F3} Y{pt.Y:F3} E{e:F4} F{settings.PrintSpeed:F0}");
                            prev = pt;
                        }
                    }
                }
            }
            if (endLines.Count > 0)
                lines.AddRange(endLines);
            DA.SetDataList(0, lines);
        }
        /// <summary>
        /// Simplifies a polyline by merging nearly collinear segments within an angular tolerance.
        /// </summary>
        private static Polyline SimplifyPolyline(Polyline poly, double angleTolRad)
        {
            if (angleTolRad <= 0 || poly.Count < 3)
                return poly;
            double cosTol = Math.Cos(angleTolRad);
            var pts = poly.ToArray();
            var outPts = new List<Point3d> { pts[0] };
            Vector3d prevDir = pts[1] - pts[0];
            prevDir.Unitize();
            for (int i = 2; i < pts.Length; i++)
            {
                var currDir = pts[i] - pts[i - 1];
                currDir.Unitize();
                if (Vector3d.Multiply(prevDir, currDir) < cosTol)
                {
                    outPts.Add(pts[i - 1]);
                    prevDir = currDir;
                }
            }
            outPts.Add(pts[pts.Length - 1]);
            return new Polyline(outPts);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("F12348B0-2E5F-4C93-8A37-6A3BD8F4C478");
    }
}