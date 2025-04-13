using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Attributes;
using Grasshopper.GUI.Canvas;
using Rhino.Geometry;

namespace crft
{
    public class ImageDisplayComponent : GH_Component
    {
        private Bitmap _displayImage;

        public ImageDisplayComponent()
          : base("ImageDisplay", "ImgDisp",
              "Displays an image from any source",
              "Display", "Testing")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // Use generic parameter for better compatibility
            pManager.AddGenericParameter("Image", "I", "Image to display", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // No outputs
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get the generic input 
            object obj = null;
            if (!DA.GetData(0, ref obj)) return;

            // Try to extract a bitmap from the input
            _displayImage = null;
            
            try
            {
                if (obj is GH_Bitmap bitmapGoo)
                {
                    _displayImage = bitmapGoo.Value;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Received GH_Bitmap: {_displayImage.Width}x{_displayImage.Height}");
                }
                else if (obj is GH_ObjectWrapper wrapper && wrapper.Value is Bitmap bitmap)
                {
                    _displayImage = bitmap;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Received wrapped bitmap: {bitmap.Width}x{bitmap.Height}");
                }
                else if (obj is Bitmap directBitmap)
                {
                    _displayImage = directBitmap;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Received direct bitmap: {directBitmap.Width}x{directBitmap.Height}");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Input is not a bitmap. Type: {obj.GetType().Name}");
                    if (obj is IGH_Goo goo)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Goo type: {goo.TypeName}, description: {goo.TypeDescription}");
                    }
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error handling image: {ex.Message}");
            }
        }

        // Display logic - draw the image in component UI
        public override void CreateAttributes()
        {
            m_attributes = new ImageDisplayComponentAttributes(this);
        }

        internal Bitmap DisplayImage => _displayImage;

        public override Guid ComponentGuid
        {
            get { return new Guid("0a9e51c5-2d6c-4c6a-9e0d-b62c4f78893a"); }
        }
    }

    public class ImageDisplayComponentAttributes : Grasshopper.Kernel.Attributes.GH_ComponentAttributes
    {
        private RectangleF _displayBounds;
        private readonly ImageDisplayComponent _owner;
        private const int DisplayWidth = 320;
        private const int DisplayHeight = 240;

        public ImageDisplayComponentAttributes(ImageDisplayComponent owner) : base(owner)
        {
            _owner = owner;
        }

        protected override void Layout()
        {
            base.Layout();
            
            var baseRect = GH_Convert.ToRectangle(Bounds);
            
            // Add space for the image preview
            baseRect.Height += DisplayHeight + 10;
            
            // Calculate display bounds
            _displayBounds = new RectangleF(
                baseRect.X + (baseRect.Width - DisplayWidth) / 2,
                baseRect.Bottom - DisplayHeight - 5,
                DisplayWidth,
                DisplayHeight);
            
            Bounds = baseRect;
        }

        protected override void Render(GH_Canvas canvas, System.Drawing.Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            
            if (channel == GH_CanvasChannel.Objects)
            {
                // Draw the display area border
                graphics.DrawRectangle(Pens.DarkGray, _displayBounds.X, _displayBounds.Y, _displayBounds.Width, _displayBounds.Height);
                
                Bitmap image = _owner.DisplayImage;
                if (image != null)
                {
                    // Calculate proportional drawing dimensions
                    float aspectRatio = (float)image.Width / image.Height;
                    float displayRatio = _displayBounds.Width / _displayBounds.Height;
                    RectangleF drawRect;
                    
                    if (aspectRatio > displayRatio)
                    {
                        // Image is wider - fit to width
                        float height = _displayBounds.Width / aspectRatio;
                        float y = _displayBounds.Y + (_displayBounds.Height - height) / 2;
                        drawRect = new RectangleF(_displayBounds.X, y, _displayBounds.Width, height);
                    }
                    else
                    {
                        // Image is taller - fit to height
                        float width = _displayBounds.Height * aspectRatio;
                        float x = _displayBounds.X + (_displayBounds.Width - width) / 2;
                        drawRect = new RectangleF(x, _displayBounds.Y, width, _displayBounds.Height);
                    }
                    
                    // Draw the image
                    graphics.DrawImage(image, drawRect);
                }
                else
                {
                    // No image - draw a message
                    var format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    graphics.DrawString("No image available", GH_FontServer.Standard, Brushes.DarkGray, _displayBounds, format);
                }
            }
        }
    }
}