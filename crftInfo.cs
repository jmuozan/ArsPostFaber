using System;
using Grasshopper.Kernel;

namespace crft
{
    /// <summary>
    /// This class should be used to store any string, global, or static constants.
    /// </summary>
    public class crftInfo : GH_AssemblyInfo
    {
        public new const string AssemblyVersion = "0.1.0.0";
        public const string AssemblyFileVersion = "0.1.0.0";
        
        public override string Name => "CRFT Tools";
        
        public override Guid Id => new Guid("559c2a24-6cde-4e4d-b55c-6e9d85cf39a8");
        
        public override string AuthorName => "Jorge Muyo";
        
        public override string AuthorContact => "https://github.com/jmuozan";
        
        // No constructor-based registration - we'll register in the component classes
    }
}