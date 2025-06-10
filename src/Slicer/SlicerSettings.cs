using System;

namespace crft
{
    /// <summary>
    /// Holds parameters for the slicer pipeline.
    /// </summary>
    public class SlicerSettings
    {
        public double LayerHeight { get; set; } = 0.2;
        public double WallOffset { get; set; } = 0.4;
        public int NumShells { get; set; } = 2;
        public double InfillSpacing { get; set; } = 10.0;
        public double PrintSpeed { get; set; } = 1500.0;
        public double NozzleDiameter { get; set; } = 0.4;
    }
}