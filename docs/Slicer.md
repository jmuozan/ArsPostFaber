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
  - Outputs:
    - Settings (S): pass-through **SlicerSettings** object
    - Layers (L): tree of curves, one branch per layer at successive Z heights

- **Shell Geometry** (Category: crft > Slicer)
  - Inputs:
    - Settings (S): from **Slicer Settings**
    - Layers (L): from **Slice Geometry**
  - Outputs:
    - Settings (S): pass-through **SlicerSettings** object
    - Shells (C): shell offset curves per layer
    - Region (R): innermost region curves per layer

- **Infill Geometry** (Category: crft > Slicer)
  - Inputs:
    - Settings (S): from **Slicer Settings**
    - Region (R): from **Shell Geometry**
  - Outputs:
    - Settings (S): pass-through **SlicerSettings** object
    - Infill (I): infill curves per layer

- **G-Code Generator** (Category: crft > Slicer)
  - Inputs:
    - Settings (S): from **Slicer Settings**
    - Shells (C): from **Shell Geometry**
    - Infill (I): optional, from **Infill Geometry**
    - Start: optional list of G-Code header lines
    - End: optional list of G-Code footer lines


## Workflow
1. Place the **Slicer Settings** component and configure slicing parameters.
2. Connect your Brep or Mesh to the **Slice Geometry** component along with the **Settings** output.
3. The **Slice Geometry** component returns a tree of curves, one branch per layer.
4. Connect the **Settings** (S) and **Layers** (L) outputs of **Slice Geometry** to the respective inputs of the **G-Code Generator**.
5. (Optional) Provide **Start** and **End** custom G-Code lines, or let defaults be used.
6. The **G-Code** output can be baked to a text panel or written to a file using a file writer component.

## Notes
- Mesh slicing uses RhinoCommon’s `Intersection.MeshPlane`, Brep slicing uses `Intersection.BrepPlane`.
- Infill generation is not yet implemented; only perimeter toolpaths are generated.
- Extrusion amount (E) is approximated as the travel distance; fine-tune as needed.