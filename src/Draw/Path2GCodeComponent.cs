using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace crft
{
    /// <summary>
    /// Converts curves to G-Code commands for a plotter, lifting pen for travel moves.
    /// </summary>
    public class Path2GCodeComponent : GH_Component
    {
        public Path2GCodeComponent()
          : base("Path to G-Code", "Path2GCode",
                 "Converts curves to G-Code commands with pen-up travel moves", 
                 "crft", "Draw")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Curves to convert to G-Code", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Lift Height", "H", "Height to lift pen for travel moves", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Z Down", "Z", "Height to lower pen for drawing", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Feed Rate", "F", "Feed rate for drawing moves (mm/min)", GH_ParamAccess.item, 1000.0);
            pManager.AddNumberParameter("Travel Rate", "T", "Feed rate for travel moves (mm/min)", GH_ParamAccess.item, 3000.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("G-Code", "G", "Generated G-Code as a single multiline string", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var curveTree = new GH_Structure<GH_Curve>();
            DA.GetDataTree(0, out curveTree);
            double liftHeight = 1.0, zDown = 0.0, feedRate = 1000.0, travelRate = 3000.0;
            DA.GetData(1, ref liftHeight);
            DA.GetData(2, ref zDown);
            DA.GetData(3, ref feedRate);
            DA.GetData(4, ref travelRate);

            var lines = new List<string>();
            lines.Add("G28");
            lines.Add("G21");
            lines.Add("G90");
            // Initial pen lift (no feed rate)
            lines.Add(string.Format(CultureInfo.InvariantCulture, "G1 Z{0:0.###}", liftHeight));

            double currentZ = liftHeight;
            double currentX = 0.0, currentY = 0.0;

            foreach (var path in curveTree.Paths)
            {
                var branch = curveTree[path];
                foreach (var ghCrv in branch)
                {
                    var crv = ghCrv?.Value;
                    if (crv == null) continue;

                    // Sample curve into points
                    var pts = new List<Point3d>();
                    if (crv.TryGetPolyline(out Polyline poly) && poly.IsValid)
                        pts.AddRange(poly);
                    else
                    {
                        double length = crv.GetLength();
                        if (length <= 0) continue;
                        int segments = Math.Max(1, (int)Math.Ceiling(length / 1.0));
                        for (int i = 0; i <= segments; i++)
                        {
                            double dist = length * i / segments;
                            if (!crv.LengthParameter(dist, out double t)) continue;
                            pts.Add(crv.PointAt(t));
                        }
                    }
                    if (pts.Count < 1) continue;

                    // Travel to start
                    var start = pts[0];
                    if (currentZ != liftHeight)
                    {
                        // Pen lift (no feed rate)
                        lines.Add(string.Format(CultureInfo.InvariantCulture, "G1 Z{0:0.###}", liftHeight));
                        currentZ = liftHeight;
                    }
                    // Move to start position
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "G1 X{0:0.###} Y{1:0.###}", start.X, start.Y));
                    currentX = start.X; currentY = start.Y;
                    // Pen down for drawing
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "G1 Z{0:0.###}", zDown));
                    currentZ = zDown;

                    // Draw path
                    foreach (var pt in pts.Skip(1))
                    {
                        // Draw move (no feed rate)
                        lines.Add(string.Format(CultureInfo.InvariantCulture, "G1 X{0:0.###} Y{1:0.###}", pt.X, pt.Y));
                        currentX = pt.X; currentY = pt.Y;
                    }
                }
            }

            // Final pen lift (no feed rate)
            lines.Add(string.Format(CultureInfo.InvariantCulture, "G1 Z{0:0.###}", liftHeight));
            var gcodeText = string.Join("\n", lines);
            DA.SetData(0, gcodeText);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("A9E5C8D7-1234-4EE3-89AB-1C2D3E4F5A6B");
    }
}