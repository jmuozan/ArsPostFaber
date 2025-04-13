using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;
using Grasshopper.GUI.Canvas;
using Rhino.Geometry;

namespace crft
{
    public class WebcamViewerComponent : GH_Component
    {
        private Bitmap _displayImage;

        public WebcamViewerComponent()
          : base("Camera Viewer", "CamView",
              "Displays the camera feed from WebcamComponent",
              "Display", "Preview")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // Use generic parameter for maximum compatibility
            pManager.AddGenericParameter("Image", "I", "Camera image to display", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // No outputs needed
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Start with diagnostic logging
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "WebcamViewerComponent starting...");
            
            // Log the current state for debugging
            string prevState = (_displayImage == null) ? "null" : $"{_displayImage.Width}x{_displayImage.Height}";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Previous image state: {prevState}");
            
            // DO NOT reset the display image automatically
            // This would cause flicker as the image disappears and reappears
            // Only replace it when we have a new one
            
            // Check if we're connected to any source
            if (Params.Input[0].SourceCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Not connected to any source");
                return;
            }
            
            // We're connected, try to get the input data
            
            // Try to get a direct Bitmap first (changed approach)
            Bitmap directBitmap = null;
            if (DA.GetData(0, ref directBitmap) && directBitmap != null)
            {
                try
                {
                    // Clone the bitmap to avoid threading issues
                    _displayImage = (Bitmap)directBitmap.Clone();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"SUCCESS: Got direct Bitmap: {_displayImage.Width}x{_displayImage.Height}");
                    
                    // Force immediate UI update
                    ExpirePreview(true);
                    
                    // Ensure our custom attribute class gets notified of the change
                    if (m_attributes != null && m_attributes is WebcamViewerComponentAttributes attrs)
                    {
                        attrs.ForceRedraw();
                    }
                    
                    // Force canvas refresh on UI thread to ensure display is updated
                    if (Grasshopper.Instances.ActiveCanvas != null)
                    {
                        Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() => 
                        {
                            // Force component redraw
                            OnDisplayExpired(true);
                            
                            // Invalidate the component's area on the canvas to ensure it redraws
                            if (this.Attributes != null)
                            {
                                Grasshopper.Instances.ActiveCanvas.Invalidate();
                            }
                            
                            // Force document solution
                            if (OnPingDocument() != null)
                            {
                                OnPingDocument().NewSolution(false);
                            }
                            
                            // Finally refresh the entire canvas 
                            Grasshopper.Instances.ActiveCanvas.Refresh();
                        }));
                    }
                    
                    return;
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Error processing direct Bitmap: {ex.Message}");
                }
            }
                
            // Fallback: try to get a GH_Bitmap (original approach)
            GH_Bitmap bitmapGoo = null;
            if (DA.GetData(0, ref bitmapGoo) && bitmapGoo != null && bitmapGoo.Value != null)
            {
                try
                {
                    // Clone the bitmap to avoid threading issues
                    _displayImage = (Bitmap)bitmapGoo.Value.Clone();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"SUCCESS: Got GH_Bitmap: {_displayImage.Width}x{_displayImage.Height}");
                    
                    // Force immediate UI update
                    ExpirePreview(true);
                    
                    // Ensure our custom attribute class gets notified of the change
                    if (m_attributes != null && m_attributes is WebcamViewerComponentAttributes attrs)
                    {
                        attrs.ForceRedraw();
                    }
                    
                    // Force canvas refresh on UI thread to ensure display is updated
                    if (Grasshopper.Instances.ActiveCanvas != null)
                    {
                        Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() => 
                        {
                            // Force component redraw
                            OnDisplayExpired(true);
                            
                            // Invalidate the component's area on the canvas to ensure it redraws
                            if (this.Attributes != null)
                            {
                                Grasshopper.Instances.ActiveCanvas.Invalidate();
                            }
                            
                            // Force document solution
                            if (OnPingDocument() != null)
                            {
                                OnPingDocument().NewSolution(false);
                            }
                            
                            // Finally refresh the entire canvas 
                            Grasshopper.Instances.ActiveCanvas.Refresh();
                        }));
                    }
                    
                    return;
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Error processing GH_Bitmap: {ex.Message}");
                }
            }
            
            // If that didn't work, try with GH_ObjectWrapper
            GH_ObjectWrapper wrapper = null;
            if (DA.GetData(0, ref wrapper) && wrapper != null && wrapper.Value != null)
            {
                try
                {
                    // Check what's in the wrapper
                    var wrapperType = wrapper.Value.GetType().Name;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"Got wrapper with value type: {wrapperType}");
                    
                    // Try to extract bitmap from wrapper
                    if (wrapper.Value is Bitmap bitmap)
                    {
                        _displayImage = (Bitmap)bitmap.Clone();
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            $"Extracted bitmap from wrapper: {_displayImage.Width}x{_displayImage.Height}");
                        
                        // Force immediate UI update
                        ExpirePreview(true);
                        
                        // Ensure our custom attribute class gets notified of the change
                        if (m_attributes != null && m_attributes is WebcamViewerComponentAttributes attrs)
                        {
                            attrs.ForceRedraw();
                        }
                        
                        // Force canvas refresh on UI thread to ensure display is updated
                        if (Grasshopper.Instances.ActiveCanvas != null)
                        {
                            Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() => 
                            {
                                // Force component redraw
                                OnDisplayExpired(true);
                                
                                // Invalidate the component's area on the canvas to ensure it redraws
                                if (this.Attributes != null)
                                {
                                    Grasshopper.Instances.ActiveCanvas.Invalidate();
                                }
                                
                                // Force document solution
                                if (OnPingDocument() != null)
                                {
                                    OnPingDocument().NewSolution(false);
                                }
                                
                                // Finally refresh the entire canvas 
                                Grasshopper.Instances.ActiveCanvas.Refresh();
                            }));
                        }
                        
                        return;
                    }
                    else if (wrapper.Value is System.Drawing.Image img)
                    {
                        _displayImage = new Bitmap(img);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            $"Converted Image to Bitmap: {_displayImage.Width}x{_displayImage.Height}");
                        
                        // Force immediate UI update
                        ExpirePreview(true);
                        
                        // Ensure our custom attribute class gets notified of the change
                        if (m_attributes != null && m_attributes is WebcamViewerComponentAttributes attrs)
                        {
                            attrs.ForceRedraw();
                        }
                        
                        // Force canvas refresh on UI thread to ensure display is updated
                        if (Grasshopper.Instances.ActiveCanvas != null)
                        {
                            Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() => 
                            {
                                // Force component redraw
                                OnDisplayExpired(true);
                                
                                // Invalidate the component's area on the canvas to ensure it redraws
                                if (this.Attributes != null)
                                {
                                    Grasshopper.Instances.ActiveCanvas.Invalidate();
                                }
                                
                                // Force document solution
                                if (OnPingDocument() != null)
                                {
                                    OnPingDocument().NewSolution(false);
                                }
                                
                                // Finally refresh the entire canvas 
                                Grasshopper.Instances.ActiveCanvas.Refresh();
                            }));
                        }
                        
                        return;
                    }
                    else
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                            $"Wrapper value is not an image type: {wrapperType}");
                    }
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Error processing wrapper: {ex.Message}");
                }
            }
            
            // Last resort - try to get any object and see if we can cast it
            object obj = null;
            if (DA.GetData(0, ref obj) && obj != null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"Got object of type: {obj.GetType().Name}");
                
                try
                {
                    // Try different conversion methods
                    if (obj is Bitmap rawBitmap)
                    {
                        _displayImage = (Bitmap)rawBitmap.Clone();
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            $"Got Bitmap directly: {_displayImage.Width}x{_displayImage.Height}");
                    }
                    else if (obj is System.Drawing.Image image)
                    {
                        _displayImage = new Bitmap(image);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            $"Converted Image to Bitmap: {_displayImage.Width}x{_displayImage.Height}");
                    }
                    else if (obj is IGH_Goo goo)
                    {
                        // Try different types of casting with IGH_Goo
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                            $"Trying to cast IGH_Goo: {goo.TypeName}");
                        
                        // Try to cast to a bitmap
                        Bitmap bmp = null;
                        if (goo.CastTo(out bmp) && bmp != null)
                        {
                            _displayImage = (Bitmap)bmp.Clone();
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                                $"Successfully cast to Bitmap: {_displayImage.Width}x{_displayImage.Height}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Error converting to bitmap: {ex.Message}");
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                    "Could not get any data from input");
            }
            
            // If we still don't have a display image, create a debug placeholder
            if (_displayImage == null)
            {
                try
                {
                    // Create a diagnostic image with info about what's happening
                    _displayImage = new Bitmap(320, 240);
                    using (Graphics g = Graphics.FromImage(_displayImage))
                    {
                        g.Clear(Color.FromArgb(40, 40, 40));
                        using (Font titleFont = new Font("Arial", 11, FontStyle.Bold))
                        {
                            g.DrawString("Connection Issue", 
                                titleFont, Brushes.Yellow, new PointF(80, 30));
                        }
                        
                        using (Font detailFont = new Font("Arial", 9))
                        {
                            // Draw detailed information about what we tried
                            int y = 70;
                            g.DrawString("Connected but couldn't get bitmap data", 
                                detailFont, Brushes.White, new PointF(30, y));
                            y += 25;
                            
                            g.DrawString("Attempted:", 
                                detailFont, Brushes.White, new PointF(30, y));
                            y += 20;
                            
                            g.DrawString("- Direct GH_Bitmap access", 
                                detailFont, Brushes.LightGray, new PointF(40, y));
                            y += 20;
                            
                            g.DrawString("- GH_ObjectWrapper extraction", 
                                detailFont, Brushes.LightGray, new PointF(40, y));
                            y += 20;
                            
                            g.DrawString("- Generic object casting", 
                                detailFont, Brushes.LightGray, new PointF(40, y));
                            y += 30;
                            
                            g.DrawString("Check Webcam component for camera errors", 
                                detailFont, Brushes.White, new PointF(30, y));
                        }
                    }
                    
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, 
                        "Created diagnostic image - couldn't get bitmap data");
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, 
                        $"Error creating diagnostic image: {ex.Message}");
                }
            }
            
            // Force UI update for either our valid image or the placeholder
            if (_displayImage != null)
            {
                // Log the final result
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                    $"Final image state: {_displayImage.Width}x{_displayImage.Height}");
                
                // Force component to update its display
                ExpirePreview(true);
                ExpireSolution(true);
                
                // Force canvas refresh on UI thread to ensure display is updated
                if (Grasshopper.Instances.ActiveCanvas != null)
                {
                    Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() => 
                    {
                        // Force component redraw
                        OnDisplayExpired(true);
                        
                        // Invalidate the component's area on the canvas to ensure it redraws
                        if (this.Attributes != null)
                        {
                            Grasshopper.Instances.ActiveCanvas.Invalidate();
                        }
                        
                        // Force document solution
                        if (OnPingDocument() != null)
                        {
                            OnPingDocument().NewSolution(false);
                        }
                        
                        // Finally refresh the entire canvas 
                        Grasshopper.Instances.ActiveCanvas.Refresh();
                    }));
                }
            }
        }

        // Display logic - draw the image in component UI
        public override void CreateAttributes()
        {
            m_attributes = new WebcamViewerComponentAttributes(this);
        }

        // Make the property public for easier access from the attributes class
        // and use proper getter implementation to ensure it's always accessed correctly
        public Bitmap DisplayImage 
        {
            get 
            {
                return _displayImage;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("f2a5e1c8-4d6c-5c6b-be0d-c52c4f78894c"); }
        }
        
        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            // When the document is closing, dispose of our bitmap
            if (context == GH_DocumentContext.Close)
            {
                if (_displayImage != null)
                {
                    try
                    {
                        _displayImage.Dispose();
                        _displayImage = null;
                    }
                    catch { }
                }
            }
            
            base.DocumentContextChanged(document, context);
        }
        
        // Override Dispose without 'override' keyword since GH_Component doesn't have a virtual Dispose method
        public override void RemovedFromDocument(GH_Document document)
        {
            // Clean up resources when component is removed
            if (_displayImage != null)
            {
                try
                {
                    _displayImage.Dispose();
                    _displayImage = null;
                }
                catch { }
            }
            
            base.RemovedFromDocument(document);
        }
    }

    public class WebcamViewerComponentAttributes : Grasshopper.Kernel.Attributes.GH_ComponentAttributes
    {
        private RectangleF _displayBounds;
        private readonly WebcamViewerComponent _owner;
        private const int DisplayWidth = 320;
        private const int DisplayHeight = 240;
        private bool _needsRedraw = false;

        public WebcamViewerComponentAttributes(WebcamViewerComponent owner) : base(owner)
        {
            _owner = owner;
        }

        protected override void Layout()
        {
            base.Layout();
            
            var baseRect = GH_Convert.ToRectangle(Bounds);
            
            // Add space for the image preview
            baseRect.Height += DisplayHeight + 10;
            
            // Make sure the component is wide enough for the display
            if (baseRect.Width < DisplayWidth + 20)
            {
                baseRect.Width = DisplayWidth + 20;
            }
            
            // Calculate display bounds - centered in component
            _displayBounds = new RectangleF(
                baseRect.X + (baseRect.Width - DisplayWidth) / 2,
                baseRect.Y + 30, // Position below component header
                DisplayWidth,
                DisplayHeight);
            
            Bounds = baseRect;
            
            // Force immediate component update after layout changes
            if (Grasshopper.Instances.ActiveCanvas != null)
            {
                Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() => 
                {
                    // Force canvas to redraw our adjusted layout
                    Grasshopper.Instances.ActiveCanvas.Invalidate();
                }));
            }
        }

        public void ForceRedraw()
        {
            _needsRedraw = true;
            
            // Force immediate canvas redraw if we have an active canvas
            if (Grasshopper.Instances.ActiveCanvas != null)
            {
                Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() => 
                {
                    // Force a complete canvas redraw
                    Grasshopper.Instances.ActiveCanvas.Invalidate();
                    
                    // Then ensure the entire canvas is refreshed
                    Grasshopper.Instances.ActiveCanvas.Refresh();
                }));
            }
        }
        
        protected override void Render(GH_Canvas canvas, System.Drawing.Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                // First render the standard component
                base.Render(canvas, graphics, channel);
                
                // Save the graphics state before clipping
                System.Drawing.Drawing2D.GraphicsState savedState = graphics.Save();
                
                try
                {
                    // Force image display area to be completely cleared before each render
                    // This ensures no fragments of previous images remain
                    Rectangle clipRect = Rectangle.Round(_displayBounds);
                    graphics.SetClip(clipRect);
                    graphics.Clear(Color.FromArgb(25, 25, 25));
                    graphics.ResetClip();
                    
                    // Draw the display area border with a slightly thicker pen
                    using (Pen borderPen = new Pen(Color.FromArgb(80, 80, 80), 1.5f))
                    {
                        graphics.DrawRectangle(borderPen, _displayBounds.X, _displayBounds.Y, _displayBounds.Width, _displayBounds.Height);
                    }
                    
                    // Fill the background with a dark color
                    using (SolidBrush backgroundBrush = new SolidBrush(Color.FromArgb(30, 30, 30)))
                    {
                        graphics.FillRectangle(backgroundBrush, _displayBounds);
                    }
                }
                finally
                {
                    // Restore the graphics state after our operations
                    graphics.Restore(savedState);
                }
                
                // After rendering, reset the redraw flag if it was set
                if (_needsRedraw)
                {
                    _needsRedraw = false;
                    // Log that we've redrawn due to explicit request
                    _owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Redrawing component due to explicit redraw request");
                }
                
                // Debug - find out about the display image
                Bitmap image = null;
                string hasImageMessage = "DisplayImage is NULL";
                string displayState = "uninitialized";
                
                if (_owner.DisplayImage != null)
                {
                    hasImageMessage = $"DisplayImage is valid: {_owner.DisplayImage.Width}x{_owner.DisplayImage.Height}";
                    displayState = "available";
                    
                    // Explicitly log that we have an image for debugging
                    _owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, 
                        $"RENDER: DisplayImage exists with dimensions {_owner.DisplayImage.Width}x{_owner.DisplayImage.Height}");
                    
                    try
                    {
                        // Save graphics state before any transformations
                        System.Drawing.Drawing2D.GraphicsState savedImageState = graphics.Save();
                        
                        try
                        {
                            // Clone the image for rendering to avoid threading issues
                            image = (Bitmap)_owner.DisplayImage.Clone();
                            
                            // Set high quality rendering
                            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                            
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
                            
                            // First clear the background to ensure no previous frame artifacts
                            using (SolidBrush brush = new SolidBrush(Color.FromArgb(30, 30, 30)))
                            {
                                graphics.FillRectangle(brush, _displayBounds);
                            }
                            
                            // Draw a border around the image area for better visibility
                            using (Pen borderPen = new Pen(Color.FromArgb(60, 60, 60), 1f))
                            {
                                graphics.DrawRectangle(borderPen, 
                                    drawRect.X, drawRect.Y, drawRect.Width, drawRect.Height);
                            }
                            
                            // Draw the image
                            graphics.DrawImage(image, drawRect);
                            
                            // Force the canvas to update 
                            if (Grasshopper.Instances.ActiveCanvas != null)
                            {
                                // Use BeginInvoke to avoid cross-thread calls
                                Grasshopper.Instances.ActiveCanvas.BeginInvoke((Action)(() => {
                                    // Force a complete redraw
                                    Grasshopper.Instances.ActiveCanvas.Invalidate();
                                    Grasshopper.Instances.ActiveCanvas.Refresh();
                                }));
                            }
                        }
                        finally
                        {
                            // Restore the graphics state
                            graphics.Restore(savedImageState);
                        }
                        
                        // Draw a simple text display at top left for debugging
                        using (Font debugFont = new Font("Arial", 8))
                        {
                            graphics.DrawString($"Image: {image.Width}×{image.Height}", 
                                debugFont, Brushes.White, 
                                new PointF(_displayBounds.X + 5, _displayBounds.Y + 5));
                        }
                        
                        // Draw dimension overlay at bottom right
                        string dimensionText = $"{image.Width}×{image.Height}";
                        SizeF textSize = graphics.MeasureString(dimensionText, GH_FontServer.Small);
                        
                        // Draw text background
                        RectangleF textRect = new RectangleF(
                            _displayBounds.Right - textSize.Width - 6,
                            _displayBounds.Bottom - textSize.Height - 4, 
                            textSize.Width + 4, 
                            textSize.Height + 2);
                            
                        using (SolidBrush textBackBrush = new SolidBrush(Color.FromArgb(120, Color.Black)))
                        {
                            graphics.FillRectangle(textBackBrush, textRect);
                        }
                        
                        // Draw dimensions text
                        graphics.DrawString(dimensionText, GH_FontServer.Small, Brushes.White, 
                            _displayBounds.Right - textSize.Width - 4, 
                            _displayBounds.Bottom - textSize.Height - 3);
                            
                        displayState = "rendered";
                    }
                    catch (Exception ex)
                    {
                        // Draw error message if rendering fails
                        displayState = $"error: {ex.Message}";
                        var format = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        graphics.DrawString($"Render error: {ex.Message}", GH_FontServer.Small, Brushes.Red, _displayBounds, format);
                        
                        // Add diagnostic info
                        _owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error rendering image: {ex.Message}");
                    }
                    finally
                    {
                        // Always dispose the cloned image after rendering
                        if (image != null)
                        {
                            try 
                            { 
                                // Make sure we dispose of our bitmap properly
                                image.Dispose();
                                image = null;
                            } 
                            catch { }
                        }
                    }
                }
                else
                {
                    // No image is available, display a message with debug info
                    using (Font debugFont = new Font("Arial", 10))
                    {
                        // First draw the status
                        graphics.DrawString("Camera image not available", 
                            debugFont, Brushes.LightGray, 
                            new PointF(_displayBounds.X + 20, _displayBounds.Y + 50));
                            
                        // Draw status information                         
                        using (Font smallFont = new Font("Arial", 8))
                        {
                            graphics.DrawString(hasImageMessage, 
                                smallFont, Brushes.Gray, 
                                new PointF(_displayBounds.X + 20, _displayBounds.Y + 80));
                                
                            // Draw display state
                            graphics.DrawString($"Display state: {displayState}", 
                                smallFont, Brushes.Gray, 
                                new PointF(_displayBounds.X + 20, _displayBounds.Y + 100));
                                
                            // Add note about connections
                            graphics.DrawString("Check camera connection and input sources", 
                                smallFont, Brushes.Gray, 
                                new PointF(_displayBounds.X + 20, _displayBounds.Y + 120));
                                
                            // Add info about the component state
                            graphics.DrawString($"Component Active: {_owner.Phase}", 
                                smallFont, Brushes.Gray, 
                                new PointF(_displayBounds.X + 20, _displayBounds.Y + 140));
                        }
                    }
                }
            }
            else
            {
                // Use standard rendering for all other channels
                base.Render(canvas, graphics, channel);
            }
        }
    }
}