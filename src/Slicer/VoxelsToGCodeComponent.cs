using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Drawing;
using Rhino.Display;

namespace crft.Slicer
{
    /// <summary>
    /// Converts voxel grid to G-code for additive manufacturing.
    /// Ported from t43/voxels_to_gcode.c
    /// </summary>
    public class VoxelsToGCodeComponent : GH_Component
    {
        private List<Curve> _paths = new List<Curve>();
        public VoxelsToGCodeComponent()
          : base("Voxels To GCode", "Vox2GCode",
              "Generate G-code from voxel grid", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Voxels", "V", "Voxel grid as boxes", GH_ParamAccess.list);
            pManager.AddNumberParameter("Feed Rate", "F", "Feed rate (mm/min)", GH_ParamAccess.item, 1500.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("GCode", "G", "Generated G-code lines", GH_ParamAccess.list);
            pManager.AddCurveParameter("Path", "P", "G-code path preview curves", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var boxes = new List<Box>();
            double feedRate = 1500.0;
            if (!DA.GetDataList(0, boxes)) return;
            DA.GetData(1, ref feedRate);
            _paths.Clear();
            var gcode = new List<string>();
            // Must supply a voxel grid as boxes; not a mesh bounding box
            if (boxes.Count <= 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Voxels To GCode requires a voxel grid (multiple boxes). It looks like you passed a mesh or a single box. Connect the voxel grid output directly (skip MarchVoxels)."
                );
                DA.SetDataList(0, gcode);
                DA.SetDataList(1, _paths);
                ExpirePreview(true);
                return;
            }
            // Compute voxel layer height
            double dz = boxes[0].Z.Max - boxes[0].Z.Min;
            double minZ = double.MaxValue;
            foreach (var b in boxes) minZ = Math.Min(minZ, b.Z.Min);
            // Group boxes by layer index
            var layers = new SortedDictionary<int, List<Box>>();
            foreach (var b in boxes)
            {
                int k = (int)Math.Round((b.Z.Min - minZ) / dz);
                if (!layers.ContainsKey(k)) layers[k] = new List<Box>();
                layers[k].Add(b);
            }
            // G-code header
            gcode.Add("G21");
            gcode.Add("G90");
            // Process each layer
            foreach (var kvp in layers)
            {
                int k = kvp.Key;
                var layerBoxes = kvp.Value;
                double z = minZ + k * dz + dz * 0.5;
                gcode.Add($"; Layer {k} at Z={z:F3}");
                gcode.Add($"G1 Z{z:F3} F{feedRate}");
                // Generate outer perimeter (shell) for this layer
                var rectCurves = new List<Curve>();
                foreach (var b in layerBoxes)
                {
                    var ptsRect = new Polyline(new[]
                    {
                        new Point3d(b.X.Min, b.Y.Min, z),
                        new Point3d(b.X.Max, b.Y.Min, z),
                        new Point3d(b.X.Max, b.Y.Max, z),
                        new Point3d(b.X.Min, b.Y.Max, z),
                        new Point3d(b.X.Min, b.Y.Min, z)
                    });
                    rectCurves.Add(ptsRect.ToNurbsCurve());
                }
                double tol = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
                Curve[] unionCurves = Curve.CreateBooleanUnion(rectCurves, tol);
                if (unionCurves != null && unionCurves.Length > 0)
                {
                    var unionList = new List<Curve>(unionCurves);
                    unionList.Sort((c1, c2) =>
                    {
                        double area1 = AreaMassProperties.Compute(c1).Area;
                        double area2 = AreaMassProperties.Compute(c2).Area;
                        return area2.CompareTo(area1);
                    });
                    var shell = unionList[0];
                    _paths.Add(shell);
                    Polyline shellPoly;
                    if (shell.TryGetPolyline(out shellPoly))
                    {
                        var shellPts = shellPoly.ToArray();
                        int count = shellPts.Length;
                        if (shellPts[0].DistanceTo(shellPts[count - 1]) < tol) count--;
                        gcode.Add("; Perimeter");
                        gcode.Add($"G1 X{shellPts[0].X:F3} Y{shellPts[0].Y:F3} F{feedRate}");
                        for (int i = 1; i < count; i++)
                            gcode.Add($"G1 X{shellPts[i].X:F3} Y{shellPts[i].Y:F3} F{feedRate}");
                        gcode.Add($"G1 X{shellPts[0].X:F3} Y{shellPts[0].Y:F3} F{feedRate}");
                    }
                }
                // Sort by X then Y
                layerBoxes.Sort((b1, b2) =>
                {
                    double x1 = (b1.X.Min + b1.X.Max) * 0.5;
                    double y1 = (b1.Y.Min + b1.Y.Max) * 0.5;
                    double x2 = (b2.X.Min + b2.X.Max) * 0.5;
                    double y2 = (b2.Y.Min + b2.Y.Max) * 0.5;
                    int c = x1.CompareTo(x2);
                    return c != 0 ? c : y1.CompareTo(y2);
                });
                var pts = new List<Point3d>();
                foreach (var b in layerBoxes)
                {
                    double cx = (b.X.Min + b.X.Max) * 0.5;
                    double cy = (b.Y.Min + b.Y.Max) * 0.5;
                    gcode.Add($"G1 X{cx:F3} Y{cy:F3} F{feedRate}");
                    pts.Add(new Point3d(cx, cy, z));
                }
                if (pts.Count > 1)
                {
                    var poly = new Polyline(pts);
                    if (poly.IsValid)
                        _paths.Add(poly.ToNurbsCurve());
                }
            }
            DA.SetDataList(0, gcode);
            DA.SetDataList(1, _paths);
            ExpirePreview(true);
        }

        public override Guid ComponentGuid => new Guid("EC178F86-190A-48F6-B41A-C0C25851EE86");

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (_paths == null || _paths.Count == 0) return;
            foreach (var crv in _paths)
                args.Display.DrawCurve(crv, Color.Red, 2);
        }
    }
}