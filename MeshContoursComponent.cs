using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace crft
{
    /// <summary>
    /// Computes slicing contours for a mesh or brep given layer heights.
    /// </summary>
    public class MeshContoursComponent : GH_Component
    {
        public MeshContoursComponent()
          : base("Mesh Contours", "Contours",
              "Compute slicing contours from mesh or brep",
              "crft", "Slice")
        { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGeometryParameter("Geometry", "G", "Mesh or Brep to slice", GH_ParamAccess.item);
            p.AddNumberParameter("First Layer Height", "F", "Height of the first layer (mm)", GH_ParamAccess.item, 0.2);
            p.AddNumberParameter("Layer Height", "L", "Layer height for subsequent layers (mm)", GH_ParamAccess.item, 0.2);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddCurveParameter("Contours", "C", "Contour curves per layer", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Retrieve inputs
            GeometryBase geom = null;
            double firstLayerHeight = 0.2;
            double layerHeight = 0.2;
            if (!DA.GetData(0, ref geom)) return;
            DA.GetData(1, ref firstLayerHeight);
            DA.GetData(2, ref layerHeight);
            if (geom == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid geometry input");
                return;
            }

            // Compute bounding box and layer count
            BoundingBox bbox = geom.GetBoundingBox(true);
            double minZ = bbox.Min.Z;
            double maxZ = bbox.Max.Z;
            int layerCount = 1 + (int)Math.Ceiling((maxZ - (minZ + firstLayerHeight)) / layerHeight);

            // Prepare output tree
            var contourTree = new GH_Structure<GH_Curve>();

            // Slice per layer
            for (int i = 0; i < layerCount; i++)
            {
                double z = minZ + firstLayerHeight + i * layerHeight;
                Plane slicePlane = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);
                Curve[] curves = null;
                if (geom is Mesh mesh)
                    curves = Mesh.CreateContourCurves(mesh, slicePlane);
                else if (geom is Brep brep)
                    curves = Brep.CreateContourCurves(brep, slicePlane);

                if (curves == null) continue;
                foreach (var c in curves)
                {
                    if (c == null) continue;
                    contourTree.Append(new GH_Curve(c), new GH_Path(i));
                }
            }

            DA.SetDataTree(0, contourTree);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("770FFD13-15BF-4EA1-9A49-87A63D2F3632");
    }
}