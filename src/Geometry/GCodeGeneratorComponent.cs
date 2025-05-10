using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino.Display;
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
        // Printing area bounds (X, Y, Z)
        private double _areaWidth = 20.0;
        private double _areaDepth = 20.0;
        private double _areaHeight = 25.0;
        // Preview path curves for drawing and output (connected toolpath)
        private List<Curve> _previewPathCurves = new List<Curve>();
        // Data for viewport draw
        private List<Curve> _gcodePaths = new List<Curve>();
        public GCodeGeneratorComponent()
          : base("GCode Generator", "GGen",
              "Generate simple G-code by slicing geometry", "crft", "Geometry")
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
            // Printing area dimensions
            pManager.AddNumberParameter("Print Area Width", "AW", "Printable area width in X (mm)", GH_ParamAccess.item, 20.0);
            pManager[5].Optional = true;
            pManager.AddNumberParameter("Print Area Depth", "AD", "Printable area depth in Y (mm)", GH_ParamAccess.item, 20.0);
            pManager[6].Optional = true;
            pManager.AddNumberParameter("Print Area Height", "AH", "Printable area max height in Z (mm)", GH_ParamAccess.item, 25.0);
            pManager[7].Optional = true;
            // Seam orientation for contour starts: 0=natural, R=random, N/S/E/W
            pManager.AddTextParameter("Seam Orientation", "SO", "Seam orientation: 0=natural, R=random, N,S,E,W", GH_ParamAccess.item, "0");
            pManager[8].Optional = true;
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
            // Get printing area dimensions
            double areaWidth = _areaWidth;
            double areaDepth = _areaDepth;
            double areaHeight = _areaHeight;
            if (Params.Input.Count > 5) DA.GetData(5, ref areaWidth);
            if (Params.Input.Count > 6) DA.GetData(6, ref areaDepth);
            if (Params.Input.Count > 7) DA.GetData(7, ref areaHeight);
            _areaWidth = areaWidth;
            _areaDepth = areaDepth;
            _areaHeight = areaHeight;
            // Travel rate for rapid moves
            double travelRate = feedRate * 2;

            // Seam orientation
            string seamStr = "0";
            if (Params.Input.Count > 8) DA.GetData(8, ref seamStr);
            char seamOrient = string.IsNullOrEmpty(seamStr) ? '0' : char.ToUpperInvariant(seamStr[0]);
            
            var gcode = new List<string>();
            // preview geometry outlines (unused now) and path curves
            var allSections = new List<Curve>();
            _previewPathCurves.Clear();
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
            // Detect Brep or Mesh and center in X/Y within print area
            Brep br = data as Brep;
            Mesh mesh = data as Mesh;
            if (br != null)
            {
                // center Brep in XY
                br = br.DuplicateBrep();
                var bb = br.GetBoundingBox(true);
                var center = (bb.Min + bb.Max) * 0.5;
                var shift = new Vector3d(areaWidth * 0.5 - center.X,
                                          areaDepth * 0.5 - center.Y, 0);
                br.Transform(Transform.Translation(shift));
                data = br;
            }
            else if (mesh != null)
            {
                // center Mesh in XY
                mesh = mesh.DuplicateMesh();
                var bb = mesh.GetBoundingBox(true);
                var center = (bb.Min + bb.Max) * 0.5;
                var shift = new Vector3d(areaWidth * 0.5 - center.X,
                                          areaDepth * 0.5 - center.Y, 0);
                mesh.Transform(Transform.Translation(shift));
                data = mesh;
            }
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
                    // Reorient shells by seam before path sequencing
                    for (int i = 0; i < shellPlines.Count; i++)
                        shellPlines[i] = ReorientSeam(shellPlines[i], seamOrient);
                    // Generate G-code for shells
                    var shellGcode = SequencePlines(shellPlines, z, feedRate, travelRate);
                    gcode.AddRange(shellGcode);
                    // Preview connected shell paths
                    var shellPathCurves = ConnectPlines(shellPlines);
                    _previewPathCurves.AddRange(shellPathCurves);
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
                    // Reorient each contour polyline to preferred seam
                    for (int i = 0; i < polylines.Count; i++)
                        polylines[i] = ReorientSeam(polylines[i], seamOrient);
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
                        // Preview connected contour paths
                        _previewPathCurves.AddRange(ConnectPlines(polylines));
                    }
                }
                else
                {
                    gcode.Add($"; No intersection at Z={z:F3}");
                }
            }
            // Output G-code
            DA.SetDataList(0, gcode);
            // Merge connected preview segments into a single PolylineCurve for the full 3D path
            if (_previewPathCurves.Count > 0)
            {
                var pts = new List<Point3d>();
                bool firstAdded = false;
                foreach (var crv in _previewPathCurves)
                {
                    // add start of first segment
                    if (!firstAdded)
                    {
                        pts.Add(crv.PointAtStart);
                        firstAdded = true;
                    }
                    // always add end point
                    pts.Add(crv.PointAtEnd);
                }
                var fullPath = new Polyline(pts).ToPolylineCurve();
                // Output as single path
                DA.SetData(1, fullPath);
                // Cache for viewport draw
                _gcodePaths.Clear();
                _gcodePaths.Add(fullPath);
            }
            else
            {
                DA.SetDataList(1, Array.Empty<Curve>());
            }
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
        /// <summary>
        /// Rotate a polyline so that its start vertex aligns with the specified seam orientation.
        /// 0 = natural, R = random, W/E = west/east (min/max X), N/S = north/south (min/max Y).
        /// </summary>
        private Polyline ReorientSeam(Polyline pl, char seam)
        {
            if (pl == null || pl.Count < 2) return pl;
            int n = pl.Count;
            int bestIdx = 0;
            if (seam == 'R')
            {
                bestIdx = new Random().Next(n);
            }
            else if (seam != '0')
            {
                bool minimize = (seam == 'W' || seam == 'N');
                Func<Point3d, double> val;
                switch (seam)
                {
                    case 'W': val = p => p.X; break;
                    case 'E': val = p => p.X; minimize = false; break;
                    case 'N': val = p => p.Y; break;
                    case 'S': val = p => p.Y; minimize = false; break;
                    default: return pl;
                }
                double opt = minimize ? double.MaxValue : double.MinValue;
                for (int i = 0; i < n; i++)
                {
                    double v = val(pl[i]);
                    if ((minimize && v < opt) || (!minimize && v > opt))
                    {
                        opt = v;
                        bestIdx = i;
                    }
                }
            }
            // rotate points
            var res = new Polyline();
            for (int i = 0; i < n; i++)
                res.Add(pl[(bestIdx + i) % n]);
            return res;
        }
        /// <summary>
        /// Builds a connected preview path across multiple polylines: adds each polyline and a travel line between them.
        /// </summary>
        private List<Curve> ConnectPlines(List<Polyline> plines)
        {
            var curves = new List<Curve>();
            if (plines == null || plines.Count == 0) return curves;
            int n = plines.Count;
            var used = new bool[n];
            var sequence = new List<int> { 0 };
            used[0] = true;
            // Nearest-neighbor ordering
            for (int i = 1; i < n; i++)
            {
                double bestDist = double.MaxValue;
                int best = -1;
                var prevPl = plines[sequence[i - 1]];
                var lastPt = prevPl[prevPl.Count - 1];
                for (int j = 0; j < n; j++)
                {
                    if (used[j]) continue;
                    var candPl = plines[j];
                    // measure dist to its first point
                    double d = lastPt.DistanceTo(candPl[0]);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = j;
                    }
                }
                if (best < 0) break;
                sequence.Add(best);
                used[best] = true;
            }
            // Build curves in order
            for (int k = 0; k < sequence.Count; k++)
            {
                var pl = plines[sequence[k]];
                // add extrusion path
                curves.Add(pl.ToPolylineCurve());
                // add travel to next
                if (k + 1 < sequence.Count)
                {
                    var nextPl = plines[sequence[k + 1]];
                    var end = pl[pl.Count - 1];
                    var start = nextPl[0];
                    curves.Add(new LineCurve(end, start));
                }
            }
            return curves;
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("d2accf38-8c3e-4f56-a5d9-123456789abc");
        // Preview: draw build volume and G-code paths
        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            // Draw bottom build platform as shaded rectangle
            if (_areaWidth > 0 && _areaDepth > 0)
            {
                var mesh = new Mesh();
                mesh.Vertices.Add(0, 0, 0);
                mesh.Vertices.Add(_areaWidth, 0, 0);
                mesh.Vertices.Add(_areaWidth, _areaDepth, 0);
                mesh.Vertices.Add(0, _areaDepth, 0);
                mesh.Faces.AddFace(0, 1, 2, 3);
                mesh.Normals.ComputeNormals();
                // Draw shaded bottom platform in green
                var mat = new DisplayMaterial();
                mat.Diffuse = Color.Green;
                mat.Transparency = 0.7f;
                args.Display.DrawMeshShaded(mesh, mat);
            }
            base.DrawViewportMeshes(args);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            // Draw build volume edges in blue
            var corners = new Point3d[] {
                new Point3d(0, 0, 0),
                new Point3d(_areaWidth, 0, 0),
                new Point3d(_areaWidth, _areaDepth, 0),
                new Point3d(0, _areaDepth, 0)
            };
            // Bottom rectangle
            for (int i = 0; i < 4; i++)
                args.Display.DrawLine(new Line(corners[i], corners[(i + 1) % 4]), Color.Blue, 1);
            // Top rectangle
            for (int i = 0; i < 4; i++)
            {
                var p0 = new Point3d(corners[i].X, corners[i].Y, _areaHeight);
                var p1 = new Point3d(corners[(i + 1) % 4].X, corners[(i + 1) % 4].Y, _areaHeight);
                args.Display.DrawLine(new Line(p0, p1), Color.Blue, 1);
            }
            // Vertical edges
            for (int i = 0; i < 4; i++)
            {
                var p0 = corners[i];
                var p1 = new Point3d(corners[i].X, corners[i].Y, _areaHeight);
                args.Display.DrawLine(new Line(p0, p1), Color.Blue, 1);
            }
            // Draw G-code paths: green inside, red if outside
            foreach (var crv in _gcodePaths)
            {
                var bb = crv.GetBoundingBox(false);
                bool outside = bb.Min.X < 0 || bb.Min.Y < 0 || bb.Max.X > _areaWidth || bb.Max.Y > _areaDepth;
                var col = outside ? Color.Red : Color.Green;
                args.Display.DrawCurve(crv, col, 2);
            }
            base.DrawViewportWires(args);
        }
    }
}