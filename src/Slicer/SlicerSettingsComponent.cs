using System;
using Grasshopper.Kernel;

namespace crft
{
    /// <summary>
    /// Component to set slicer parameters.
    /// </summary>
    public class SlicerSettingsComponent : GH_Component
    {
        public SlicerSettingsComponent()
          : base("Slicer Settings", "SlicerSettings",
              "Sets parameters for slicing.",
              "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Layer Height", "H", "Layer height (mm)", GH_ParamAccess.item, 0.2);
            pManager.AddNumberParameter("Wall Offset", "WO", "Offset distance between shells (mm)", GH_ParamAccess.item, 0.4);
            pManager.AddIntegerParameter("Shells", "N", "Number of shells (perimeters)", GH_ParamAccess.item, 2);
            pManager.AddNumberParameter("Infill Spacing", "IS", "Spacing between infill lines (mm)", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Print Speed", "PS", "Speed for extrusion moves (mm/min)", GH_ParamAccess.item, 1500.0);
            pManager.AddNumberParameter("Nozzle Diameter", "ND", "Nozzle diameter (mm)", GH_ParamAccess.item, 0.4);
            pManager.AddNumberParameter("Bed Width", "BW", "Bed width (mm)", GH_ParamAccess.item, 200.0);
            pManager.AddNumberParameter("Bed Depth", "BD", "Bed depth (mm)", GH_ParamAccess.item, 200.0);
            pManager.AddNumberParameter("Max Segment Length", "ML", "Maximum segment length for curve approximation (mm) (0 = use nozzle diameter)", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Smoothing Angle", "SA", "Maximum angle change (deg) below which successive segments are merged (0 = off)", GH_ParamAccess.item, 0.0);
            // Make bed and smoothing parameters optional
            pManager[6].Optional = true;
            pManager[7].Optional = true;
            pManager[8].Optional = true;
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Settings", "S", "Slicer settings", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double layer = 0.2;
            double offset = 0.4;
            int shells = 2;
            double infill = 10.0;
            double speed = 1500.0;
            double nozzle = 0.4;
            double bedWidth = 200.0;
            double bedDepth = 200.0;
            double maxSegmentLength = 0.0;
            double smoothingAngle = 0.0;
            DA.GetData(0, ref layer);
            DA.GetData(1, ref offset);
            DA.GetData(2, ref shells);
            DA.GetData(3, ref infill);
            DA.GetData(4, ref speed);
            DA.GetData(5, ref nozzle);
            DA.GetData(6, ref bedWidth);
            DA.GetData(7, ref bedDepth);
            DA.GetData(8, ref maxSegmentLength);
            DA.GetData(9, ref smoothingAngle);
            var settings = new SlicerSettings
            {
                LayerHeight = layer,
                WallOffset = offset,
                NumShells = shells,
                InfillSpacing = infill,
                PrintSpeed = speed,
                NozzleDiameter = nozzle,
                BedWidth = bedWidth,
                BedDepth = bedDepth,
                MaxSegmentLength = maxSegmentLength,
                SmoothingAngle = smoothingAngle
            };
            DA.SetData(0, settings);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("D71C3A07-8D9C-4FEC-8C47-785E9506E793");
    }
}