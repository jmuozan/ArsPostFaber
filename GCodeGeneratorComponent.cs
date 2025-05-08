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
                // Bottom layer infill if requested
                if (fillBottom && Math.Abs(z - initialHeight) < tol)
                {
                    foreach (var crv in contours)
                    {
                        // Only polyline contours supported for infill
                        if (!crv.TryGetPolyline(out Polyline pl) || pl.Count < 3) continue;
                        var plCurve = pl.ToPolylineCurve();
                        var bbox = plCurve.GetBoundingBox(true);
                        double minY = bbox.Min.Y;
                        double maxY = bbox.Max.Y;
                        double spacing = layerHeight;
                        for (double y = minY + spacing / 2.0; y <= maxY; y += spacing)
                        {
                            var scan = new LineCurve(new Line(
                                new Point3d(bbox.Min.X, y, z),
                                new Point3d(bbox.Max.X, y, z)));
                            var inters = Intersection.CurveCurve(plCurve, scan, tol, tol);
                            if (inters == null || inters.Count < 2) continue;
                            var pts = new List<(double, Point3d)>();
                            foreach (var ev in inters)
                            {
                                if (ev.IsPoint) pts.Add((ev.ParameterB, ev.PointB));
                            }
                            if (pts.Count < 2) continue;
                            pts.Sort((a, b) => a.Item1.CompareTo(b.Item1));
                            for (int i = 0; i + 1 < pts.Count; i += 2)
                            {
                                var p0 = pts[i].Item2;
                                var p1 = pts[i + 1].Item2;
                                // Rapid move to start of infill segment
                                gcode.Add($"G0 X{p0.X:F3} Y{p0.Y:F3} F{feedRate}");
                                // Infill extrusion
                                gcode.Add($"G1 X{p1.X:F3} Y{p1.Y:F3} F{feedRate}");
                                allSections.Add(new LineCurve(p0, p1));
                            }
                        }
                    }
                }
                // Paths: preview curves and generate moves for polylines
                if (contours != null && contours.Length > 0)
                {
                    foreach (var crv in contours)
                    {
                        // Try to convert to polyline for G-code; otherwise use original for preview
                        crv.TryGetPolyline(out Polyline pl);
                        bool isPoly = pl != null && pl.Count >= 2;
                        Curve preview = isPoly ? pl.ToPolylineCurve() : crv.DuplicateCurve();
                        allSections.Add(preview);
                        if (isPoly)
                        {
                            // Rapid move to start
                            gcode.Add($"G0 X{pl[0].X:F3} Y{pl[0].Y:F3} F{feedRate}");
                            // Moves along path
                            for (int i = 1; i < pl.Count; i++)
                                gcode.Add($"G1 X{pl[i].X:F3} Y{pl[i].Y:F3} F{feedRate}");
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

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("d2accf38-8c3e-4f56-a5d9-123456789abc");
    }
}