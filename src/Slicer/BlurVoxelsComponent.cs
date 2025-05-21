using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace crft.Slicer
{
    /// <summary>
    /// Applies blur filter to voxel grid.
    /// Ported from t43/blur_voxels.c
    /// </summary>
    public class BlurVoxelsComponent : GH_Component
    {
        public BlurVoxelsComponent()
          : base("Blur Voxels", "VoxBlur",
              "Apply blur filter to voxel grid", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Voxels", "V", "Voxel grid as boxes", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Radius", "R", "Blur radius", GH_ParamAccess.item, 1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBoxParameter("Blurred Voxels", "BV", "Blurred voxel grid", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var boxes = new List<Box>();
            int radius = 1;
            if (!DA.GetDataList(0, boxes)) return;
            DA.GetData(1, ref radius);
            if (radius <= 0)
            {
                DA.SetDataList(0, boxes);
                return;
            }
            double dx = boxes[0].X.Max - boxes[0].X.Min;
            double dy = boxes[0].Y.Max - boxes[0].Y.Min;
            double dz = boxes[0].Z.Max - boxes[0].Z.Min;
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var b in boxes)
            {
                minX = Math.Min(minX, b.X.Min);
                minY = Math.Min(minY, b.Y.Min);
                minZ = Math.Min(minZ, b.Z.Min);
                maxX = Math.Max(maxX, b.X.Max);
                maxY = Math.Max(maxY, b.Y.Max);
                maxZ = Math.Max(maxZ, b.Z.Max);
            }
            var origin = new Point3d(minX, minY, minZ);
            var coords = new HashSet<(int, int, int)>();
            foreach (var b in boxes)
            {
                int i = (int)Math.Round((b.X.Min - origin.X) / dx);
                int j = (int)Math.Round((b.Y.Min - origin.Y) / dy);
                int k = (int)Math.Round((b.Z.Min - origin.Z) / dz);
                for (int di = -radius; di <= radius; di++)
                    for (int dj = -radius; dj <= radius; dj++)
                        for (int dk = -radius; dk <= radius; dk++)
                            coords.Add((i + di, j + dj, k + dk));
            }
            var outBoxes = new List<Box>();
            foreach (var (i, j, k) in coords)
            {
                double x0 = origin.X + i * dx;
                double y0 = origin.Y + j * dy;
                double z0 = origin.Z + k * dz;
                var box = new Box(Plane.WorldXY,
                    new Interval(x0, x0 + dx),
                    new Interval(y0, y0 + dy),
                    new Interval(z0, z0 + dz));
                outBoxes.Add(box);
            }
            DA.SetDataList(0, outBoxes);
        }

        public override Guid ComponentGuid => new Guid("1C35FE90-82C6-4037-83CC-99D623D6B5FB");
    }
}