using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

namespace crft
{
    /// <summary>
    /// Slices input geometry (Brep or Mesh) into layer curves.
    /// </summary>
    public class SliceGeometryComponent : GH_Component
    {
        public SliceGeometryComponent()
          : base("Slice Geometry", "SliceGeo",
              "Slices input geometry into layer curves.",
              "crft", "Slicer")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Geometry", "G", "Input Brep or Mesh to slice", GH_ParamAccess.item);
            pManager.AddGenericParameter("Settings", "S", "Slicer settings", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Settings", "S", "Slicer settings (pass-through)", GH_ParamAccess.item);
            pManager.AddCurveParameter("Layers", "L", "Sliced curves per layer", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper geoWrapper = null;
            GH_ObjectWrapper settingsWrapper = null;
            if (!DA.GetData(0, ref geoWrapper) || geoWrapper?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No input geometry.");
                return;
            }
            if (!DA.GetData(1, ref settingsWrapper) || !(settingsWrapper.Value is SlicerSettings settings))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No slicer settings.");
                return;
            }
            // Prepare Brep for slicing (only Brep input supported)
            var layers = new List<List<Curve>>();
            object geo = geoWrapper.Value;
            Brep sliceBrep = null;
            if (geo is GH_Brep ghBrep)
            {
                sliceBrep = ghBrep.Value;
            }
            else if (geo is Brep b)
            {
                sliceBrep = b;
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input geometry must be a Brep.");
                return;
            }
            if (sliceBrep == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid Brep input.");
                return;
            }
            // Compute bounds and slicing
            var bbox = sliceBrep.GetBoundingBox(true);
            double zmin = bbox.Min.Z;
            double zmax = bbox.Max.Z;
            int layerCount = (int)Math.Floor((zmax - zmin) / settings.LayerHeight) + 1;
            double tol = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
            for (int i = 0; i < layerCount; i++)
            {
                double z = zmin + i * settings.LayerHeight;
                var plane = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);
                Intersection.BrepPlane(sliceBrep, plane, tol, out Curve[] crvs, out Point3d[] pts);
                layers.Add(crvs != null ? new List<Curve>(crvs) : new List<Curve>());
            }
            // Build GH_Structure of layer curves
            var tree = new GH_Structure<GH_Curve>();
            for (int i = 0; i < layers.Count; i++)
            {
                var path = new GH_Path(i);
                foreach (var c in layers[i])
                    tree.Append(new GH_Curve(c), path);
            }
            // Output settings pass-through and layer curves
            DA.SetData(0, settings);
            DA.SetDataTree(1, tree);
        }

        protected override System.Drawing.Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("E8B49AC2-1F47-4A66-B1D8-F4A3D59CF3B1");
    }
}