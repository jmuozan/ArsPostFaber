using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Drawing;
using Rhino.Display;

namespace crft.Slicer
{
    /// <summary>
    /// Generates G-code from an ordered list of points.
    /// </summary>
    public class PathToGCodeComponent : GH_Component
    {
        private List<Curve> _paths = new List<Curve>();

        public PathToGCodeComponent()
          : base("Path To GCode", "Path2GCode",
              "Generate G-code from a list of points path", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "Ordered list of points to follow", GH_ParamAccess.list);
            pManager.AddNumberParameter("Feed Rate", "F", "Feed rate (mm/min)", GH_ParamAccess.item, 900.0);
            pManager.AddNumberParameter("Nozzle Dia", "N", "Nozzle diameter (mm)", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("Layer Height", "H", "Layer height (mm)", GH_ParamAccess.item, 1.5);
            pManager.AddNumberParameter("Filament Dia", "D", "Filament (material) diameter (mm)", GH_ParamAccess.item, 1.75);
            pManager.AddNumberParameter("Flow (%)", "Flow", "Extrusion flow percentage (0-100)", GH_ParamAccess.item, 100.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("GCode", "G", "Generated G-code lines", GH_ParamAccess.list);
            pManager.AddCurveParameter("Path", "C", "Preview path curve", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var pts = new List<Point3d>();
            double feedRate = 900.0;
            double nozzleDia = 3.0, layerHeight = 1.5, materialDia = 1.75, flowPct = 100.0;
            if (!DA.GetDataList(0, pts)) return;
            DA.GetData(1, ref feedRate);
            DA.GetData(2, ref nozzleDia);
            DA.GetData(3, ref layerHeight);
            DA.GetData(4, ref materialDia);
            DA.GetData(5, ref flowPct);
            _paths.Clear();
            var gcode = new List<string>();
            if (pts.Count == 0)
            {
                DA.SetDataList(0, gcode);
                DA.SetDataList(1, _paths);
                return;
            }
            double flow = flowPct * 0.01;
            double E = 0.0;
            var last = pts[0];
            gcode.Add("G21");
            gcode.Add("G90");
            gcode.Add("; Path start");
            gcode.Add($"G1 X{last.X:F3} Y{last.Y:F3} Z{last.Z:F3} E{E:F4} F{feedRate}");
            for (int i = 1; i < pts.Count; i++)
            {
                var p = pts[i];
                double dx = p.X - last.X;
                double dy = p.Y - last.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double vol = nozzleDia * layerHeight * dist * flow;
                double dE = vol / (Math.PI * Math.Pow(materialDia * 0.5, 2.0));
                E += dE;
                gcode.Add($"G1 X{p.X:F3} Y{p.Y:F3} Z{p.Z:F3} E{E:F4} F{feedRate}");
                last = p;
            }
            var pline = new Polyline(pts);
            if (pline.IsValid) _paths.Add(pline.ToNurbsCurve());
            DA.SetDataList(0, gcode);
            DA.SetDataList(1, _paths);
            ExpirePreview(true);
        }

        public override Guid ComponentGuid => new Guid("D51AB373-570A-4DFF-AD54-8EB36636F4AC");

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (_paths == null || _paths.Count == 0) return;
            foreach (var crv in _paths)
                args.Display.DrawCurve(crv, Color.Blue, 2);
        }
    }
}