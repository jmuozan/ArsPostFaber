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
        /// <summary>
        /// Width of the printer bed (mm).
        /// </summary>
        public double BedWidth { get; set; } = 200.0;
        /// <summary>
        /// Depth of the printer bed (mm).
        /// </summary>
        public double BedDepth { get; set; } = 200.0;
        /// <summary>
        /// Maximum segment length for curve approximation (mm). 0 = use nozzle diameter.
        /// </summary>
        public double MaxSegmentLength { get; set; } = 0.0;
        /// <summary>
        /// Window size for running average smoothing (number of points). 0 = off.
        /// </summary>
        public int SmoothingSamples { get; set; } = 0;
        /// <summary>
        /// Smoothing angle threshold (degrees). Segments whose direction changes are below this
        /// threshold will be merged to reduce small-step moves. 0 = no smoothing.
        /// </summary>
        public double SmoothingAngle { get; set; } = 0.0;
        /// <summary>
        /// Enable arc interpolation (G2/G3 commands)
        /// </summary>
        public bool UseArcInterpolation { get; set; } = true;
        /// <summary>
        /// Tolerance for arc fitting (mm)
        /// </summary>
        public double ArcTolerance { get; set; } = 0.05;
        /// <summary>
        /// Minimum segment length - merge shorter segments (mm)
        /// </summary>
        public double MinSegmentLength { get; set; } = 0.1;
        /// <summary>
        /// Junction deviation for cornering (mm)
        /// </summary>
        public double JunctionDeviation { get; set; } = 0.02;
        /// <summary>
        /// Enable adaptive feed rates for corners
        /// </summary>
        public bool UseAdaptiveFeedRates { get; set; } = true;
    }
}