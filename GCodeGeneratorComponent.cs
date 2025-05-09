using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Rhino;
using Rhino.Geometry.Intersect;

namespace crft
{
    /// <summary>
    /// Generates simple G-code by slicing input geometry at a specified Z height.
    /// </summary>
    public class GCodeGeneratorComponent : GH_Component
    {
        public GCodeGeneratorComponent()
          : base("GCode Generator", "GGen",
              "Generate simple G-code by slicing geometry", "crft", "Control")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Geometry to slice: supports Brep or Mesh
            pManager.AddGenericParameter("Geometry", "Geo", "Brep or Mesh to slice", GH_ParamAccess.item);
            // Allow missing geometry until streaming
            pManager[0].Optional = true;
            // Slice settings
            pManager.AddNumberParameter("Initial Height", "H", "Initial Z height of first layer (mm)", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Layer Height", "L", "Layer height increment (mm)", GH_ParamAccess.item, 0.5);
            pManager.AddBooleanParameter("Fill Bottom", "FB", "Whether to generate bottom layer", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Feed Rate", "F", "Feed rate (mm/min)", GH_ParamAccess.item, 1500);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("GCode", "G", "Generated G-code lines", GH_ParamAccess.list);
            pManager.AddCurveParameter("Path", "P", "Sliced path curves per layer", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Retrieve input geometry (Brep or Mesh)
            GH_ObjectWrapper geoWrapper = null;
            DA.GetData(0, ref geoWrapper);
            // Slice settings
            double initialHeight = 0.5;
            double layerHeight = 0.5;
            bool fillBottom = true;
            double feedRate = 1500;
            // Tolerance for geometric operations
            double tol = RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            DA.GetData(1, ref initialHeight);
            DA.GetData(2, ref layerHeight);
            DA.GetData(3, ref fillBottom);
            DA.GetData(4, ref feedRate);
            // Travel rate default: twice feed rate for rapid moves
            double travelRate = feedRate * 2;

            var gcode = new List<string>();
            var allSections = new List<Curve>();
            // Validate geometry
            if (geoWrapper == null || geoWrapper.Value == null)
            {
                gcode.Add("; No geometry provided");
                DA.SetDataList(0, gcode);
                DA.SetDataList(1, allSections);
                return;
            }
            // Unwrap any GH_GeometricGoo to raw Rhino object
            object data = geoWrapper.Value;
            var goo = data as IGH_GeometricGoo;
            if (goo != null)
                data = goo.ScriptVariable();
            // Detect Brep or Mesh
            Brep br = data as Brep;
            Mesh mesh = data as Mesh;
            if (br == null && mesh == null)
            {
                gcode.Add("; Unsupported geometry type");
                DA.SetDataList(0, gcode);
                DA.SetDataList(1, allSections);
                return;
            }
            // Determine max Z
            double maxZ = br != null
                ? br.GetBoundingBox(true).Max.Z
                : mesh.GetBoundingBox(true).Max.Z;
            // Slice layers
            bool firstLayer = true;
            for (double z = initialHeight; z <= maxZ + RhinoDoc.ActiveDoc.ModelAbsoluteTolerance; z += layerHeight)
            {
                if (firstLayer && !fillBottom)
                {
                    firstLayer = false;
                    continue;
                }
                firstLayer = false;
                Plane plane = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);
                Curve[] contours = null;
                // Intersection
                if (br != null)
                {
                    Point3d[] pts;
                    Intersection.BrepPlane(br, plane, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance, out contours, out pts);
                }
                else
                {
                    contours = Mesh.CreateContourCurves(mesh, plane, RhinoDoc.ActiveDoc.ModelAbsoluteTolerance);
                }
                // Header
                gcode.Add($"; Layer at Z={z:F3}");
                gcode.Add("G90");
                gcode.Add("G21");
                gcode.Add($"G1 Z{z:F3} F{feedRate}");
                // Bottom layer fill by offsetting shells
                if (fillBottom && Math.Abs(z - initialHeight) < tol)
                {
                    gcode.Add($"; Bottom layer shells at Z={z:F3}");
                    // Collect shell curves via successive inward offsets
                    var shellCurves = new List<Curve>();
                    foreach (var crv in contours)
                    {
                        // Use duplicated curve for offsets
                        Curve current = crv.DuplicateCurve();
                        while (current != null)
                        {
                            shellCurves.Add(current);
                            // Offset inward by one layerHeight
                            var offsets = current.Offset(new Plane(new Point3d(0, 0, z), Vector3d.ZAxis),
                                                         -layerHeight, tol, CurveOffsetCornerStyle.Sharp);
                            if (offsets == null || offsets.Length == 0)
                                break;
                            // Pick the longest offset curve
                            Curve best = null;
                            double bestLen = 0;
                            foreach (var oc in offsets)
                            {
                                if (oc == null) continue;
                                double len = oc.GetLength();
                                if (len > bestLen)
                                {
                                    bestLen = len;
                                    best = oc;
                                }
                            }
                            current = best;
                        }
                    }
                    // Convert shells to polylines and add previews
                    var shellPlines = new List<Polyline>();
                    foreach (var sc in shellCurves)
                    {
                        if (sc.TryGetPolyline(out Polyline pl) && pl.Count >= 2)
                        {
                            shellPlines.Add(pl);
                            allSections.Add(pl.ToPolylineCurve());
                        }
                        else
                        {
                            var approx = sc.ToPolyline(tol, tol, 0, 0).ToPolyline();
                            if (approx != null && approx.Count >= 2)
                            {
                                shellPlines.Add(approx);
                                allSections.Add(approx.ToPolylineCurve());
                            }
                        }
                    }
                    // Generate G-code for shells
                    var shellGcode = SequencePlines(shellPlines, z, feedRate, travelRate);
                    gcode.AddRange(shellGcode);
                    // Skip default contour loops for bottom layer
                    continue;
                }
                // Paths: preview and generate connected contours
                if (contours != null && contours.Length > 0)
                {
                    // Extract polylines and add previews
                    var polylines = new List<Polyline>();
                    foreach (var crv in contours)
                    {
                        if (crv.TryGetPolyline(out Polyline pl) && pl.Count >= 2)
                        {
                            polylines.Add(pl);
                            allSections.Add(pl.ToPolylineCurve());
                        }
                        else
                        {
                            allSections.Add(crv.DuplicateCurve());
                        }
                    }
                    if (polylines.Count > 0)
                    {
                        var used = new bool[polylines.Count];
                        var sequence = new List<int>();
                        int currentIdx = 0;
                        sequence.Add(currentIdx);
                        used[currentIdx] = true;
                        // Build sequence by nearest neighbor
                        for (int k = 1; k < polylines.Count; k++)
                        {
                            double bestDist = double.MaxValue;
                            int bestIdx = -1;
                            var lastPt = polylines[currentIdx][polylines[currentIdx].Count - 1];
                            for (int j = 0; j < polylines.Count; j++)
                            {
                                if (used[j]) continue;
                                for (int m = 0; m < polylines[j].Count; m++)
                                {
                                    double dist = lastPt.DistanceTo(polylines[j][m]);
                                    if (dist < bestDist)
                                    {
                                        bestDist = dist;
                                        bestIdx = j;
                                    }
                                }
                            }
                            if (bestIdx < 0) break;
                            sequence.Add(bestIdx);
                            used[bestIdx] = true;
                            currentIdx = bestIdx;
                        }
                        Point3d globalCurrentPt = new Point3d();
                        bool firstContour = true;
                        foreach (int idx in sequence)
                        {
                            var pl = polylines[idx];
                            int startIdx = 0;
                            if (!firstContour)
                            {
                                double minDist = double.MaxValue;
                                for (int m = 0; m < pl.Count; m++)
                                {
                                    double dist = globalCurrentPt.DistanceTo(pl[m]);
                                    if (dist < minDist)
                                    {
                                        minDist = dist;
                                        startIdx = m;
                                    }
                                }
                            }
                            var orderedPl = new List<Point3d>();
                            for (int m = 0; m < pl.Count; m++)
                                orderedPl.Add(pl[(startIdx + m) % pl.Count]);
                            // Rapid move to start of contour
                            var sp = orderedPl[0];
                            gcode.Add($"G0 X{sp.X:F3} Y{sp.Y:F3} F{feedRate}");
                            // Extrude along contour
                            for (int m = 1; m < orderedPl.Count; m++)
                            {
                                var pt = orderedPl[m];
                                gcode.Add($"G1 X{pt.X:F3} Y{pt.Y:F3} F{feedRate}");
                            }
                            globalCurrentPt = orderedPl[orderedPl.Count - 1];
                            firstContour = false;
                        }
                    }
                }
                else
                {
                    gcode.Add($"; No intersection at Z={z:F3}");
                }
            }
            // Output G-code
            DA.SetDataList(0, gcode);
            // Unite curve segments for smoother preview/path
            Curve[] outputPaths;
            if (allSections.Count > 0)
                outputPaths = Curve.JoinCurves(allSections.ToArray(), tol);
            else
                outputPaths = Array.Empty<Curve>();
            DA.SetDataList(1, outputPaths);
        }
        
        /// <summary>
        /// Sequence multiple polylines into a continuous toolpath (rapid moves + extrusion).
        /// </summary>
        private List<string> SequencePlines(List<Polyline> plines, double z, double feedRate, double travelRate)
        {
            var pathGcode = new List<string>();
            if (plines == null || plines.Count == 0) return pathGcode;
            var used = new bool[plines.Count];
            var sequence = new List<int> { 0 };
            used[0] = true;
            // Build sequence by nearest neighbor
            for (int k = 1; k < plines.Count; k++)
            {
                double bestDist = double.MaxValue;
                int bestIdx = -1;
                var prevPl = plines[sequence[k - 1]];
                var lastPt = prevPl[prevPl.Count - 1];
                for (int j = 0; j < plines.Count; j++)
                {
                    if (used[j]) continue;
                    for (int m = 0; m < plines[j].Count; m++)
                    {
                        double d = lastPt.DistanceTo(plines[j][m]);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestIdx = j;
                        }
                    }
                }
                if (bestIdx < 0) break;
                sequence.Add(bestIdx);
                used[bestIdx] = true;
            }
            // Generate G-code
            Point3d currentPt = new Point3d();
            bool first = true;
            foreach (var idx in sequence)
            {
                var pl = plines[idx];
                int startIdx = 0;
                if (!first)
                {
                    double minD = double.MaxValue;
                    for (int i = 0; i < pl.Count; i++)
                    {
                        double d = currentPt.DistanceTo(pl[i]);
                        if (d < minD)
                        {
                            minD = d;
                            startIdx = i;
                        }
                    }
                }
                // Reorder points
                var pts = new List<Point3d>();
                for (int i = 0; i < pl.Count; i++)
                    pts.Add(pl[(startIdx + i) % pl.Count]);
                // Rapid move to start
                var sp = pts[0];
                pathGcode.Add($"G0 X{sp.X:F3} Y{sp.Y:F3} F{travelRate}");
                // Extrude along path
                for (int i = 1; i < pts.Count; i++)
                {
                    var p = pts[i];
                    pathGcode.Add($"G1 X{p.X:F3} Y{p.Y:F3} F{feedRate}");
                }
                currentPt = pts[pts.Count - 1];
                first = false;
            }
            return pathGcode;
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("d2accf38-8c3e-4f56-a5d9-123456789abc");
    }
}