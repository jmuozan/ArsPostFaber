using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace SAMforGrasshopper
{
    public class SAMforGHInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get
            {
                return "SAM for Grasshopper";
            }
        }
        public override Bitmap Icon
        {
            get
            {
                // Return a 24x24 pixel bitmap to represent this GHA library.
                return null;
            }
        }
        public override string Description
        {
            get
            {
                return "Implementation of Segment Anything Model (SAM) for Grasshopper";
            }
        }
        public override Guid Id
        {
            get
            {
                return new Guid("96afb589-d38a-42b2-a385-b6b82ef4b868");
            }
        }

        public override string AuthorName
        {
            get
            {
                return "SAM Implementer";
            }
        }
        public override string AuthorContact
        {
            get
            {
                return "https://github.com/facebookresearch/segment-anything";
            }
        }
    }
    
    public class SAMCategoryIcon : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            // No icon available yet
            // Grasshopper.Instances.ComponentServer.AddCategoryIcon("SAM", null);
            Grasshopper.Instances.ComponentServer.AddCategorySymbolName("SAM", 'S');
            return GH_LoadingInstruction.Proceed;
        }
    }
}