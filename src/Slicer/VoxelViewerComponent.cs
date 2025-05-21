using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace crft.Slicer
{
    /// <summary>
    /// Visualizes voxel grid as preview geometry.
    /// Ported from t43/voxel_viewer.c
    /// </summary>
    public class VoxelViewerComponent : GH_Component
    {
        private List<Box> _boxes = new List<Box>();
        public VoxelViewerComponent()
          : base("Voxel Viewer", "VoxView",
              "Visualize voxel grid", "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("Voxels", "V", "Voxels as box geometry", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // No outputs; geometry is previewed
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            _boxes.Clear();
            var boxes = new List<Box>();
            if (!DA.GetDataList(0, boxes)) return;
            _boxes.AddRange(boxes);
            ExpirePreview(true);
        }

        public override Guid ComponentGuid => new Guid("C3B7E5D2-3456-4E89-CDEF-234567890ABC");

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (_boxes == null || _boxes.Count == 0) return;
            var display = args.Display;
            foreach (var box in _boxes)
            {
                display.DrawBox(box, System.Drawing.Color.LimeGreen);
            }
        }
    }
}