using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;
using Grasshopper.Kernel;
using Grasshopper.GUI.Canvas;
using Rhino.Geometry;

namespace MyNamespace
{
    public class WebcamComponent : GH_Component
    {
        internal Bitmap _currentFrame;
        private FilterInfoCollection _videoDevices;
        private VideoCaptureDevice _videoSource;
        private bool _isRunning = false;
        private Rectangle _previewRect;

        /// <summary>
        /// Initializes a new instance of the WebcamComponent class.
        /// </summary>
        public WebcamComponent()
          : base("Webcam", "Cam",
              "Captures and displays webcam video feed",
              "Display", "Preview")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Enable", "E", "Enable/disable webcam", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Device", "D", "Webcam device index", GH_ParamAccess.item, 0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Image", "I", "Current webcam frame as bitmap", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool enable = false;
            int deviceIndex = 0;

            if (!DA.GetData(0, ref enable)) return;
            if (!DA.GetData(1, ref deviceIndex)) return;

            if (enable && !_isRunning)
            {
                StartCamera(deviceIndex);
            }
            else if (!enable && _isRunning)
            {
                StopCamera();
            }

            if (_currentFrame != null)
            {
                DA.SetData(0, _currentFrame);
            }
        }

        private void StartCamera(int deviceIndex)
        {
            try
            {
                // Get available video devices
                _videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                
                if (_videoDevices.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No webcam devices found");
                    return;
                }

                // Make sure index is in range
                if (deviceIndex < 0 || deviceIndex >= _videoDevices.Count)
                {
                    deviceIndex = 0;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Using default camera. Available: {_videoDevices.Count}");
                }

                // Create video source
                _videoSource = new VideoCaptureDevice(_videoDevices[deviceIndex].MonikerString);
                _videoSource.NewFrame += OnNewFrame;
                _videoSource.Start();
                
                _isRunning = true;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error starting webcam: " + ex.Message);
            }
        }

        private void StopCamera()
        {
            if (_videoSource != null && _videoSource.IsRunning)
            {
                _videoSource.SignalToStop();
                _videoSource.NewFrame -= OnNewFrame;
                _videoSource = null;
            }
            _isRunning = false;
        }

        private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            // Make a copy of the frame since it will be disposed
            lock (this)
            {
                if (_currentFrame != null)
                {
                    _currentFrame.Dispose();
                }
                _currentFrame = (Bitmap)eventArgs.Frame.Clone();
            }

            // Force UI update
            Grasshopper.Instances.ActiveCanvas.Invalidate();
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
        }

        public override void CreateAttributes()
        {
            m_attributes = new WebcamComponentAttributes(this);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            // Stop camera before serialization
            if (_isRunning)
            {
                StopCamera();
            }
            return base.Write(writer);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopCamera();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                StopCamera();
            }
            base.DocumentContextChanged(document, context);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("dfeb6f18-ec49-41b2-ace1-02ae334f5f1e"); }
        }
    }

    public class WebcamComponentAttributes : Grasshopper.Kernel.Attributes.GH_ComponentAttributes
    {
        private WebcamComponent Owner;
        private RectangleF PreviewBounds;
        private const int PreviewWidth = 320;  // Default width of preview
        private const int PreviewHeight = 240; // Default height of preview

        public WebcamComponentAttributes(WebcamComponent owner) : base(owner)
        {
            Owner = owner;
        }

        protected override void Layout()
        {
            base.Layout();

            // Define a rectangle for the video preview below the standard component UI
            Rectangle baseRectangle = GH_Convert.ToRectangle(Bounds);
            
            // Add space for the preview area
            baseRectangle.Height += PreviewHeight + 10;

            // Calculate the preview rectangle
            PreviewBounds = new RectangleF(
                baseRectangle.X,
                baseRectangle.Y + baseRectangle.Height - PreviewHeight - 5,
                PreviewWidth,
                PreviewHeight
            );

            Bounds = baseRectangle;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel == GH_CanvasChannel.Objects)
            {
                // Draw border for preview area
                graphics.DrawRectangle(Pens.DarkGray, PreviewBounds.X, PreviewBounds.Y, PreviewBounds.Width, PreviewBounds.Height);

                Bitmap frame = null;
                lock (Owner)
                {
                    if (Owner._currentFrame != null)
                    {
                        frame = Owner._currentFrame;
                        // Draw the video frame
                        graphics.DrawImage(frame, PreviewBounds);
                    }
                    else
                    {
                        // Draw a message when no video is available
                        StringFormat format = new StringFormat();
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        graphics.DrawString("No webcam feed available", GH_FontServer.Standard, Brushes.DarkGray, PreviewBounds, format);
                    }
                }
            }
        }
    }
}