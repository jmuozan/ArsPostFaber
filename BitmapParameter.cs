using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace crft
{
    /// <summary>
    /// A custom parameter for System.Drawing.Bitmap objects
    /// </summary>
    public class Param_Bitmap : GH_PersistentParam<GH_Bitmap>
    {
        public Param_Bitmap()
          : base("Bitmap", "Bitmap", "A bitmap image parameter", "Display", "Image")
        {
        }

        public override Guid ComponentGuid => new Guid("5bc76e95-ddab-4a6e-a823-71c5a51b0c55");

        protected override GH_Bitmap InstantiateT() => new GH_Bitmap();

        // Required method implementations for abstract class
        protected override GH_GetterResult Prompt_Singular(ref GH_Bitmap value)
        {
            // No UI prompt for bitmap input, just return cancel
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Plural(ref List<GH_Bitmap> values)
        {
            // No UI prompt for bitmap input, just return cancel
            return GH_GetterResult.cancel;
        }

        // Register this param with Grasshopper
        public override void AppendAdditionalMenuItems(System.Windows.Forms.ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
        }
        
        // Optional: Override additional methods like Exposure, Description, etc.
        public override GH_Exposure Exposure => GH_Exposure.primary;
    }

    /// <summary>
    /// A GH_Goo wrapper for System.Drawing.Bitmap
    /// </summary>
    public class GH_Bitmap : GH_Goo<Bitmap>
    {
        public GH_Bitmap() { }

        public GH_Bitmap(Bitmap bitmap)
        {
            Value = bitmap;
        }

        public override bool IsValid => Value != null;

        public override string TypeName => "Bitmap";

        public override string TypeDescription => "Bitmap image data";

        public override IGH_Goo Duplicate()
        {
            if (Value == null) return new GH_Bitmap();
            return new GH_Bitmap((Bitmap)Value.Clone());
        }

        public override string ToString()
        {
            if (Value == null) return "Null Bitmap";
            return $"Bitmap: {Value.Width}x{Value.Height}";
        }

        public override bool CastFrom(object source)
        {
            if (source is Bitmap bitmap)
            {
                Value = bitmap;
                return true;
            }

            if (source is GH_ObjectWrapper wrapper)
            {
                if (wrapper.Value is Bitmap bmp)
                {
                    Value = bmp;
                    return true;
                }
            }

            return false;
        }

        public override bool CastTo<Q>(ref Q target)
        {
            // Cast to Bitmap
            if (typeof(Q).IsAssignableFrom(typeof(Bitmap)))
            {
                if (Value != null)
                {
                    object obj = Value;
                    target = (Q)obj;
                    return true;
                }
            }

            // Cast to GH_ObjectWrapper
            if (typeof(Q).IsAssignableFrom(typeof(GH_ObjectWrapper)))
            {
                if (Value != null)
                {
                    object wrapper = new GH_ObjectWrapper(Value);
                    target = (Q)wrapper;
                    return true;
                }
            }

            return false;
        }
    }
}
