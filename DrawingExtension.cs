using System;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace crft
{
    /// <summary>
    /// Helper class to convert bitmaps to IGH_Goo
    /// </summary>
    public static class DrawingExtension
    {
        /// <summary>
        /// Create a Grasshopper displayable bitmap wrapper
        /// </summary>
        public static IGH_Goo ToGoo(this Bitmap bitmap)
        {
            if (bitmap == null)
                return null;
            
            // Create a copy of the bitmap to avoid threading issues
            Bitmap copy = new Bitmap(bitmap);
            return new GH_ObjectWrapper(copy);
        }
    }

    // Register all component parameters here
    public class GH_BitmapParam : Grasshopper.Kernel.GH_PersistentParam<crft.GH_Bitmap>
    {
        public GH_BitmapParam() : base("Bitmap", "Bitmap", "A bitmap image parameter", "Display", "Image") { }
        public override Grasshopper.Kernel.GH_Exposure Exposure => Grasshopper.Kernel.GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("E9A90A02-EFB4-4AD5-BE96-0A69B61E0430");
        protected override crft.GH_Bitmap InstantiateT() => new crft.GH_Bitmap();
        protected override Grasshopper.Kernel.GH_GetterResult Prompt_Singular(ref crft.GH_Bitmap value) => Grasshopper.Kernel.GH_GetterResult.cancel;
        protected override Grasshopper.Kernel.GH_GetterResult Prompt_Plural(ref System.Collections.Generic.List<crft.GH_Bitmap> values) => Grasshopper.Kernel.GH_GetterResult.cancel;
    }
}