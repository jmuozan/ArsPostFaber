using System;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace crft
{
    /// <summary>
    /// Component that edits and previews a mesh with vertex selection.
    /// </summary>
    public class MeshPreviewComponent : GH_Component
    {
        private Mesh _mesh;
        private Mesh _editedMesh;
        private bool _hasEdits;

        public MeshPreviewComponent()
          : base("Mesh Edit", "MeshEdit",
              "Edit and preview mesh with vertex selection", "crft", "Mesh")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Mesh to edit", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Edited mesh output", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh mesh = null;
            if (!DA.GetData(0, ref mesh) || mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No mesh provided.");
                return;
            }
            // Store a copy for preview and potential editing
            _mesh = mesh.DuplicateMesh();
            // Output edited mesh if available, else pass through input
            if (_hasEdits && _editedMesh != null)
                DA.SetData(0, _editedMesh);
            else
                DA.SetData(0, mesh);
        }

        public override void CreateAttributes()
        {
            m_attributes = new ComponentButton(
                this,
                () => "✎",
                ShowMeshEditDialog
            );
        }

        /// <summary>
        /// Show the mesh editor window.
        /// </summary>
        private void ShowMeshEditDialog()
        {
            if (_mesh == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No mesh available for editing.");
                return;
            }
            try
            {
                var form = new MeshEditForm(_mesh);
                form.Closed += (s, e) =>
                {
                    var edited = form.EditedMesh;
                    if (edited != null)
                    {
                        _editedMesh = edited;
                        _hasEdits = true;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Mesh updated.");
                        ExpireSolution(true);
                    }
                };
                form.Show();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error launching mesh editor: {ex.Message}");
            }
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("D1A2B3C4-5678-90AB-CDEF-1234567890AB");
    }
}