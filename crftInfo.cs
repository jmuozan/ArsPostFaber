using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace crft
{
  public class crftInfo : GH_AssemblyInfo
  {
    public override string Name => "crft Info";

    //Return a 24x24 pixel bitmap to represent this GHA library.
    public override Bitmap Icon => null;

    //Return a short string describing the purpose of this GHA library.
    public override string Description => "";

    public override Guid Id => new Guid("7addb38a-ab0d-4229-9e50-e7396787b462");

    //Return a string identifying you or your company.
    public override string AuthorName => "";

    //Return a string representing your preferred contact details.
    public override string AuthorContact => "";

    //Return a string representing the version.  This returns the same version as the assembly.
    public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
  }
}