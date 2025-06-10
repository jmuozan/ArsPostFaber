# Slicer Components

This document describes how to use the new Slicer components in **crft** to generate G-Code from a Brep or Mesh.

## Components

- **Slicer Settings** (Category: crft > Slicer)
  - Inputs:
    - Layer Height (H): layer thickness in mm (default 0.2)
    - Wall Offset (WO): distance between shells in mm (default 0.4)
    - Shells (N): number of perimeter shells (default 2)
    - Infill Spacing (IS): spacing between infill lines in mm (default 10)
    - Print Speed (PS): extrusion speed in mm/min (default 1500)
    - Nozzle Diameter (ND): nozzle diameter in mm (default 0.4)
  - Output:
    - Settings (S): a **SlicerSettings** object encapsulating the parameters

- **Slice Geometry** (Category: crft > Slicer)
  - Inputs:
    - Geometry (G): Brep or Mesh to slice
    - Settings (S): input from **Slicer Settings**
  - Output:
    - Layers (L): tree of curves, one branch per layer at successive Z heights

- **G-Code Generator** (Category: crft > Slicer)
  - Inputs:
    - Settings (S): from **Slicer Settings**
    - Layers (L): from **Slice Geometry**
    - Start: optional list of G-Code header lines
    - End: optional list of G-Code footer lines
  - Output:
    - G-Code (G): list of G-Code commands as strings

## Workflow
1. Place the **Slicer Settings** component and configure slicing parameters.
2. Connect your Brep or Mesh to the **Slice Geometry** component along with the **Settings** output.
3. The **Slice Geometry** component returns a tree of curves, one branch per layer.
4. Connect the **Settings** and **Layers** outputs to the **G-Code Generator**.
5. (Optional) Provide **Start** and **End** custom G-Code lines, or let defaults be used.
6. The **G-Code** output can be baked to a text panel or written to a file using a file writer component.

## Notes
- Mesh slicing uses RhinoCommon’s `Intersection.MeshPlane`, Brep slicing uses `Intersection.BrepPlane`.
- Infill generation is not yet implemented; only perimeter toolpaths are generated.
- Extrusion amount (E) is approximated as the travel distance; fine-tune as needed.