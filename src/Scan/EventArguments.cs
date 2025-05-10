using System;

namespace SAMforGrasshopper
{
    /// <summary>
    /// Event arguments for point interactions
    /// </summary>
    public class PointEventArgs : EventArgs
    {
        public float X { get; }
        public float Y { get; }
        public bool IsAdditive { get; }
        
        public PointEventArgs(float x, float y, bool isAdditive)
        {
            X = x;
            Y = y;
            IsAdditive = isAdditive;
        }
    }
    
    /// <summary>
    /// Event arguments for box interactions
    /// </summary>
    public class BoxEventArgs : EventArgs
    {
        public float X1 { get; }
        public float Y1 { get; }
        public float X2 { get; }
        public float Y2 { get; }
        
        public BoxEventArgs(float x1, float y1, float x2, float y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }
    }
    
    /// <summary>
    /// Event arguments for frame navigation
    /// </summary>
    public class NavigationEventArgs : EventArgs
    {
        public int FrameOffset { get; }
        
        public NavigationEventArgs(int frameOffset)
        {
            FrameOffset = frameOffset;
        }
    }
}