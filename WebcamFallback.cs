using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace crft
{
    public class WebcamFallback : GH_Component
    {
        private Bitmap _sampleImage;
        private readonly Timer _timer;
        private readonly Random _random = new Random();

        public WebcamFallback()
          : base("WebcamFallback", "WCam",
              "Test webcam output component",
              "Display", "Testing")
        {
            // Create a simple test image
            _sampleImage = new Bitmap(640, 480);
            using (Graphics g = Graphics.FromImage(_sampleImage))
            {
                g.Clear(Color.DarkGray);
                g.FillEllipse(Brushes.Red, 100, 100, 200, 200);
                g.DrawString("Test Image", new Font("Arial", 24), Brushes.White, new PointF(220, 220));
            }

            // Create a timer to periodically expire the solution
            _timer = new Timer(OnTimerCallback, null, 0, 250);
        }

        private void OnTimerCallback(object state)
        {
            // Create some visual changes to the image
            using (Graphics g = Graphics.FromImage(_sampleImage))
            {
                int x = _random.Next(0, _sampleImage.Width - 50);
                int y = _random.Next(0, _sampleImage.Height - 50);
                g.FillEllipse(Brushes.Yellow, x, y, 50, 50);
            }

            // Force solution update
            Grasshopper.Instances.ActiveCanvas?.Invoke(new Action(() =>
            {
                ExpireSolution(true);
            }));
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Enable", "E", "Enable/disable", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Use generic parameter for better compatibility
            pManager.AddGenericParameter("Image", "I", "Test image output", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool enable = false;
            if (!DA.GetData(0, ref enable)) return;
            
            if (enable && _sampleImage != null)
            {
                DA.SetData(0, new Grasshopper.Kernel.Types.GH_ObjectWrapper(_sampleImage));
            }
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader) 
        {
            return base.Read(reader);
        }

        protected override void AppendAdditionalComponentMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            _timer?.Dispose();
            _sampleImage?.Dispose();
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return null; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("e4ec07f2-4f66-45c0-9876-8d9f82ac3f73"); }
        }
    }
}