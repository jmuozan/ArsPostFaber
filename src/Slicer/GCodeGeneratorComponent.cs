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
            // Home axes and configure firmware motion limits
            lines.Add("G28");
            // Raise max feedrates so F values in G1/G2 commands are not clamped (mm/sec)
            lines.Add("M203 X200 Y200 Z5 E25");
            // Apply firmware tuning: higher acceleration and junction deviation for short moves
            lines.Add("M201 X3000 Y3000 Z100 E3000");
            lines.Add("M204 P2000 R2000 T2000");
            lines.Add("M205 X20 Y20 Z0.5 E10");
            if (startLines.Count > 0)
                lines.AddRange(startLines);
            else
            {
                lines.Add("G21");
                lines.Add("G90");
            }
            double e = 0.0;
            // Generate toolpaths for shells and infill with smoothing & arc interpolation
            int pathCount = shellTree.PathCount;
            for (int i = 0; i < pathCount; i++)
            {
                var shellBranch = shellTree.Branches[i];
                if (shellBranch != null)
                {
                    foreach (var ghc in shellBranch)
                    {
                        var crv = ghc?.Value;
                        if (crv == null) continue;
                        var curveLines = ProcessCurveSmooth(crv, settings, ref e);
                        lines.AddRange(curveLines);
                    }
                }
                IList<GH_Curve> infillBranch = (i < infillTree.PathCount) ? infillTree.Branches[i] : null;
                if (infillBranch != null)
                {
                    foreach (var ghc in infillBranch)
                    {
                        var crv = ghc?.Value;
                        if (crv == null) continue;
                        var curveLines = ProcessCurveSmooth(crv, settings, ref e);
                        lines.AddRange(curveLines);
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
        /// <summary>
        /// Smoothing polyline
        /// </summary>
        private static Polyline SmoothPolyline(Polyline poly, int window)
        {
            int n = poly.Count;
            if (window <= 1 || n < 3)
                return poly;
            var pts = poly.ToArray();
            var result = new List<Point3d>(n);
            result.Add(pts[0]);
            int half = window / 2;
            for (int i = 1; i < n - 1; i++)
            {
                int start = Math.Max(1, i - half);
                int end = Math.Min(n - 2, i + half);
                double sx = 0, sy = 0, sz = 0;
                int count = end - start + 1;
                for (int j = start; j <= end; j++)
                {
                    sx += pts[j].X;
                    sy += pts[j].Y;
                    sz += pts[j].Z;
                }
                result.Add(new Point3d(sx / count, sy / count, sz / count));
            }
            result.Add(pts[n - 1]);
            return new Polyline(result);
        }

        // Preprocessing pipeline: remove degenerate and tiny segments
        private static Polyline RemoveDegenerateSegments(Polyline poly)
        {
            if (poly.Count < 2) return poly;
            var pts = new List<Point3d> { poly[0] };
            for (int i = 1; i < poly.Count; i++)
                if (poly[i].DistanceTo(poly[i - 1]) > 1e-6)
                    pts.Add(poly[i]);
            return new Polyline(pts);
        }
        private static Polyline MergeTinySegments(Polyline poly, double minSeg)
        {
            if (poly.Count < 2) return poly;
            var pts = new List<Point3d> { poly[0] };
            for (int i = 1; i < poly.Count; i++)
                if (poly[i].DistanceTo(pts[pts.Count - 1]) >= minSeg)
                    pts.Add(poly[i]);
            if (pts.Count > 1 && !pts[pts.Count - 1].Equals(poly[poly.Count - 1]))
                pts.Add(poly[poly.Count - 1]);
            return new Polyline(pts);
        }
        
        /// <summary>
        /// Split segments longer than threshold into smaller segments
        /// </summary>
        private static Polyline SplitLongSegments(Polyline poly, double maxSeg)
        {
            if (poly.Count < 2) return poly;
            var pts = new List<Point3d>();
            for (int i = 0; i < poly.Count - 1; i++)
            {
                var p0 = poly[i];
                var p1 = poly[i + 1];
                pts.Add(p0);
                double d = p0.DistanceTo(p1);
                if (d > maxSeg)
                {
                    int count = (int)Math.Ceiling(d / maxSeg);
                    for (int k = 1; k < count; k++)
                    {
                        double t = (double)k / count;
                        pts.Add(new Point3d(
                            p0.X + (p1.X - p0.X) * t,
                            p0.Y + (p1.Y - p0.Y) * t,
                            p0.Z + (p1.Z - p0.Z) * t));
                    }
                }
            }
            pts.Add(poly[poly.Count - 1]);
            return new Polyline(pts);
        }
        
        private static Polyline PreprocessPolyline(Polyline poly, SlicerSettings settings)
        {
            poly = RemoveDegenerateSegments(poly);
            poly = MergeTinySegments(poly, settings.MinSegmentLength);
            // Optional moving-average smoothing in preprocessing (small window)
            if (settings.SmoothingSamples > 1)
                poly = SmoothPolyline(poly, settings.SmoothingSamples);
            return poly;
        }

        // Robust circle fitting using least-squares method
        private static bool TryFitCircleRobust(List<Point3d> pts, double tol,
            out Point3d center, out double radius, out bool clockwise)
        {
            center = Point3d.Origin; radius = 0; clockwise = true;
            int n = pts.Count;
            if (n < 3) return false;
            double sumX = 0, sumY = 0, sumX2 = 0, sumY2 = 0, sumXY = 0;
            double sumX3 = 0, sumY3 = 0, sumX2Y = 0, sumXY2 = 0;
            foreach (var pt in pts)
            {
                double x = pt.X, y = pt.Y;
                sumX += x; sumY += y;
                sumX2 += x * x; sumY2 += y * y; sumXY += x * y;
                sumX3 += x * x * x; sumY3 += y * y * y;
                sumX2Y += x * x * y; sumXY2 += x * y * y;
            }
            double a = n * sumXY - sumX * sumY;
            double b = n * sumX2 - sumX * sumX;
            double c = n * sumY2 - sumY * sumY;
            double d = 0.5 * (n * sumXY2 - sumX * sumY2 + n * sumX3 - sumX * sumX2);
            double e = 0.5 * (n * sumX2Y - sumY * sumX2 + n * sumY3 - sumY * sumY2);
            double det = a * a - b * c;
            if (Math.Abs(det) < 1e-12) return false;
            double cx = (a * e - c * d) / det;
            double cy = (a * d - b * e) / det;
            center = new Point3d(cx, cy, 0);
            double sumR = 0;
            foreach (var pt in pts) sumR += pt.DistanceTo(center);
            radius = sumR / n;
            double maxDev = 0;
            foreach (var pt in pts)
                maxDev = Math.Max(maxDev, Math.Abs(pt.DistanceTo(center) - radius));
            if (maxDev > tol || radius < tol || radius > 1000) return false;
            var cross = Vector3d.CrossProduct(pts[1] - pts[0], pts[2] - pts[1]);
            clockwise = cross.Z < 0;
            return true;
        }
        private static bool IsValidArcGeometry(List<Point3d> pts, Point3d center, double radius, double tol)
        {
            if (radius < tol || radius > 500) return false;
            foreach (var pt in pts)
                if (Math.Abs(pt.DistanceTo(center) - radius) > tol)
                    return false;
            double chord = pts.First().DistanceTo(pts.Last());
            double arcLen = 0;
            for (int i = 1; i < pts.Count; i++) arcLen += pts[i].DistanceTo(pts[i - 1]);
            if (arcLen / chord < 1.02) return false;
            return true;
        }
        /// <summary>
        /// Find longest valid arc segment with extended lookahead
        /// </summary>
        private static ArcInfo FindLongestArcSegment(List<Point3d> points, int startIndex, double tol, int maxLookahead = 30)
        {
            var best = new ArcInfo { IsArc = false };
            int n = points.Count;
            int bestLen = 0;
            for (int end = startIndex + 2; end < Math.Min(startIndex + maxLookahead, n); end++)
            {
                var segment = points.GetRange(startIndex, end - startIndex + 1);
                if (TryFitCircleRobust(segment, tol, out Point3d center, out double radius, out bool cw) &&
                    IsValidArcGeometry(segment, center, radius, tol) && (end - startIndex) > bestLen)
                {
                    Vector3d v1 = points[startIndex] - center;
                    Vector3d v2 = points[end] - center;
                    double angle = Vector3d.VectorAngle(v1, v2);
                    double arcLen = radius * angle;
                    best = new ArcInfo
                    {
                        IsArc = true,
                        EndIndex = end,
                        Center = center,
                        IsClockwise = cw,
                        ArcLength = arcLen,
                        Radius = radius,
                        SweepAngle = angle
                    };
                    bestLen = end - startIndex;
                }
            }
            return best;
        }
        
        // Enhanced smoothing and arc interpolation methods
        /// <summary>
        /// Enhanced polyline simplification with distance and angle tolerances
        /// </summary>
        private static Polyline SimplifyPolylineAdvanced(Polyline poly, double angleTolRad, double distanceTol = 0.1)
        {
            if (poly.Count < 3) return poly;
            var pts = poly.ToArray();
            var simplified = new List<Point3d> { pts[0] };
            double cosTol = Math.Cos(angleTolRad);
            Vector3d prevDir = pts[1] - pts[0];
            prevDir.Unitize();
            double accumulatedDist = 0;
            for (int i = 2; i < pts.Length; i++)
            {
                Vector3d currDir = pts[i] - pts[i - 1];
                currDir.Unitize();
                double segmentDist = pts[i].DistanceTo(pts[i - 1]);
                accumulatedDist += segmentDist;
                bool angleChange = Vector3d.Multiply(prevDir, currDir) < cosTol;
                bool distanceThreshold = accumulatedDist > distanceTol;
                bool isLast = i == pts.Length - 1;
                if (angleChange || distanceThreshold || isLast)
                {
                    simplified.Add(pts[i - 1]);
                    prevDir = currDir;
                    accumulatedDist = 0;
                }
            }
            if (simplified[simplified.Count - 1] != pts[pts.Length - 1])
                simplified.Add(pts[pts.Length - 1]);
            return new Polyline(simplified);
        }
        
        /// <summary>
        /// Converts sequences of points to arc commands when possible
        /// </summary>
        private static List<string> ConvertToArcs(List<Point3d> points, double feedRate, ref double e, double arcTolerance = 0.1)
        {
            var gcode = new List<string>();
            if (points.Count < 3) return gcode;
            int i = 0;
            while (i < points.Count - 1)
            {
                var arcInfo = FindLongestArcSegment(points, i, arcTolerance);
                if (arcInfo.IsArc && arcInfo.EndIndex > i + 1)
                {
                    var start = points[i];
                    var end = points[arcInfo.EndIndex];
                    var center = arcInfo.Center;
                    double I = center.X - start.X;
                    double J = center.Y - start.Y;
                    double arcLen = arcInfo.ArcLength;
                    e += arcLen;
                    string cmd = arcInfo.IsClockwise ? "G2" : "G3";
                    gcode.Add($"{cmd} X{end.X:F3} Y{end.Y:F3} I{I:F3} J{J:F3} E{e:F4} F{feedRate:F0}");
                    i = arcInfo.EndIndex;
                }
                else
                {
                    var next = points[i + 1];
                    double dist = points[i].DistanceTo(next);
                    e += dist;
                    gcode.Add($"G1 X{next.X:F3} Y{next.Y:F3} E{e:F4} F{feedRate:F0}");
                    i++;
                }
            }
            return gcode;
        }
        
        private struct ArcInfo
        {
            public bool IsArc;
            public int EndIndex;
            public Point3d Center;
            public bool IsClockwise;
            public double ArcLength;
            public double Radius;
            public double SweepAngle;
        }
        
        private static ArcInfo FindArcSegment(List<Point3d> points, int startIndex, double tolerance)
        {
            var result = new ArcInfo { IsArc = false };
            if (startIndex + 2 >= points.Count) return result;
            for (int end = startIndex + 2; end < Math.Min(startIndex + 10, points.Count); end++)
            {
                var segment = points.GetRange(startIndex, end - startIndex + 1);
                if (TryFitCircle(segment, tolerance, out Point3d center, out double radius, out bool clockwise))
                {
                    bool ok = true;
                    for (int j = startIndex + 1; j < end; j++)
                    {
                        if (Math.Abs(points[j].DistanceTo(center) - radius) > tolerance)
                        {
                            ok = false; break;
                        }
                    }
                    if (ok)
                    {
                        Vector3d v1 = points[startIndex] - center;
                        Vector3d v2 = points[end] - center;
                        double angle = Vector3d.VectorAngle(v1, v2);
                        double arcLen = radius * angle;
                        return new ArcInfo { IsArc = true, EndIndex = end, Center = center, IsClockwise = clockwise, ArcLength = arcLen };
                    }
                }
            }
            return result;
        }
        
        private static bool TryFitCircle(List<Point3d> pts, double tol, out Point3d center, out double radius, out bool clockwise)
        {
            center = Point3d.Origin; radius = 0; clockwise = true;
            if (pts.Count < 3) return false;
            var p1 = pts[0]; var p2 = pts[1]; var p3 = pts[2];
            var mid12 = (p1 + p2) * 0.5;
            var mid23 = (p2 + p3) * 0.5;
            var d12 = p2 - p1; var d23 = p3 - p2;
            var perp12 = new Vector3d(-d12.Y, d12.X, 0); var perp23 = new Vector3d(-d23.Y, d23.X, 0);
            perp12.Unitize(); perp23.Unitize();
            var l1 = new Line(mid12, mid12 + perp12);
            var l2 = new Line(mid23, mid23 + perp23);
            if (!Rhino.Geometry.Intersect.Intersection.LineLine(l1, l2, out double t1, out double t2)) return false;
            center = l1.PointAt(t1);
            radius = center.DistanceTo(p1);
            if (radius < tol || radius > 1000) return false;
            var cross = Vector3d.CrossProduct(p2 - p1, p3 - p2);
            clockwise = cross.Z < 0;
            return true;
        }
        
        /// <summary>
        /// Process a curve with enhanced smoothing and motion planning
        /// </summary>
        private List<string> ProcessCurveSmooth(Curve crv, SlicerSettings settings, ref double e)
        {
            var lines = new List<string>();
            Polyline poly;
            if (!crv.TryGetPolyline(out poly))
            {
                double length = crv.GetLength();
                double maxSeg = settings.MaxSegmentLength > 0 ? settings.MaxSegmentLength : settings.NozzleDiameter * 2;
                int div = (int)Math.Ceiling(length / maxSeg);
                div = Math.Max(div, 2);
                var pts = new List<Point3d>();
                for (int j = 0; j <= div; j++)
                {
                    double t = crv.Domain.Min + crv.Domain.Length * j / div;
                    pts.Add(crv.PointAt(t));
                }
                poly = new Polyline(pts);
            }
            // Preprocess: remove degenerate/tiny segments
            poly = PreprocessPolyline(poly, settings);
            // Apply angular decimation: use UI angle or default 10°
            double angleDeg = settings.SmoothingAngle > 0 ? settings.SmoothingAngle : 10.0;
            double angleRad = angleDeg * Math.PI / 180.0;
            poly = SimplifyPolylineAdvanced(poly, angleRad, settings.MinSegmentLength);
            // Split long segments and smooth with moving average
            poly = SplitLongSegments(poly, settings.MinSegmentLength * 2);
            poly = SmoothPolyline(poly, settings.SmoothingSamples > 1 ? settings.SmoothingSamples : 3);
            if (poly.Count < 2) return lines;
            var ptsList = poly.ToArray().ToList();
            var start = ptsList[0];
            lines.Add($"G0 X{start.X:F3} Y{start.Y:F3} Z{start.Z:F3}");
            // Generate smooth toolpath: arcs where possible, else optimized linear moves
            if (settings.UseArcInterpolation)
                lines.AddRange(ConvertToArcs(ptsList, settings.PrintSpeed, ref e, settings.ArcTolerance));
            else
                lines.AddRange(GenerateOptimizedLinearMoves(ptsList, settings, ref e));
            return lines;
        }
        
        private List<string> GenerateOptimizedLinearMoves(List<Point3d> pts, SlicerSettings settings, ref double e)
        {
            var lines = new List<string>();
            for (int i = 1; i < pts.Count; i++)
            {
                var curr = pts[i]; var prev = pts[i - 1];
                double dist = curr.DistanceTo(prev);
                if (dist < settings.MinSegmentLength / 2) continue;
                e += dist;
                double feed = settings.PrintSpeed;
                if (settings.UseAdaptiveFeedRates && i < pts.Count - 1)
                {
                    var next = pts[i + 1];
                    var d1 = prev - curr; d1.Unitize();
                    var d2 = next - curr; d2.Unitize();
                    double ang = Vector3d.VectorAngle(d1, d2);
                    if (ang > Math.PI / 4) feed *= 0.7;
                    else if (ang > Math.PI / 6) feed *= 0.85;
                }
                lines.Add($"G1 X{curr.X:F3} Y{curr.Y:F3} E{e:F4} F{feed:F0}");
            }
            return lines;
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("F12348B0-2E5F-4C93-8A37-6A3BD8F4C478");
    }
}