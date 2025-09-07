using System;
using Grasshopper.Kernel;

namespace ArsPostFaber
{
    /// <summary>
    /// This class provides assembly information for the ArsPostFaber Grasshopper plugin.
    /// </summary>
    public class ArsPostFaberInfo : GH_AssemblyInfo
    {
        public new const string AssemblyVersion = "1.0.0.0";
        public const string AssemblyFileVersion = "1.0.0.0";
        
        public override string Name => "ArsPostFaber";
        
        public override Guid Id => new Guid("559c2a24-6cde-4e4d-b55c-6e9d85cf39a8");
        
        public override string AuthorName => "Jorge Muyo";
        
        public override string AuthorContact => "https://github.com/jmuozan";
        
        public override string Description => "Digital Fabrication Tools for Grasshopper - Drawing, Slicing, Serial Control, and Photogrammetry";
    }
}