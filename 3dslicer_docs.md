 # 3D Slicer Workflow in Grasshopper
 
 This guide explains how to build a complete 3D slicing pipeline inside Grasshopper using the custom components under the `crft\\Slicer` namespace. The pipeline converts arbitrary geometry into printable layers or G-code via voxelization and marching-cubes.
 
 ## Prerequisites
 - Rhino 7 or 8 with Grasshopper
 - .NET 7.0 support (plug-in targets `net7.0`)
 - Add the `src/Slicer` folder to your Visual Studio solution and rebuild the `crft` plug-in.
 
 ## Component Overview
 All components live in the `crft\\Slicer` category. Each stub issues a warning until you port the corresponding C reference implementation from `t43/*.c`.
 
 | Component                   | Nickname   | Inputs                                                | Outputs                    |
 |-----------------------------|------------|-------------------------------------------------------|----------------------------|
 | **Mesh To Voxels**          | M2Vox      | Geometry (Mesh/Brep), Voxel Size                      | Voxels (List of Boxes)     |
 | **Blur Voxels**             | VoxBlur    | Voxels, Radius                                        | Blurred Voxels             |
 | **Accentuate Voxels**       | VoxAccent  | Voxels, Strength                                      | Accentuated Voxels         |
 | **Voxel Convert**           | VoxConv    | Voxel Input, Output Format                            | Voxel Output               |
 | **March Voxels**            | MarchVox   | Voxels, IsoValue                                      | Mesh                       |
 | **Voxel Viewer**            | VoxView    | Voxels                                               | (Preview only)             |
 | **Generate Support**        | SupGen     | Geometry, Overhang Angle                              | Support Geometry           |
 | **Voxels To GCode**         | Vox2GCode  | Voxels, Feed Rate                                     | GCode (List of Strings)    |
 
 ## Typical Pipeline
 1. **Load Geometry**: Reference a Brep or Mesh in Grasshopper.
 2. **Mesh To Voxels**: Convert raw geometry into a uniform voxel grid. Set the **Voxel Size** to control resolution.
 3. *(Optional)* **Blur Voxels** or **Accentuate Voxels**: Apply filters to smooth or sharpen the voxel representation.
 4. **March Voxels**: Run a marching-cubes algorithm on the voxel grid to produce a surface mesh.
 5. **Generate Support**: Analyze overhangs and extrude or mesh support structures.
 6. **Voxels To GCode**: Generate G-code paths directly from voxel grid for layer-by-layer printing.
 7. **Voxel Viewer**: Preview the voxel grid at any stage via Rhino display.
 
 ## Example Grasshopper Definition
 ```plaintext
 [Geometry] → [Mesh To Voxels] → [Blur Voxels] → [March Voxels] → [Voxels To GCode] → [GCode Panel]
                                   ↓
                          [Voxel Viewer]
 ```
 You can branch off the voxel grid to generate supports or convert formats:
 ```plaintext
 [Mesh To Voxels] → [March Voxels] → [Generate Support] → [Preview]
 ```
 
 ## Tips & Troubleshooting
 - Start with a coarse **Voxel Size** to validate your pipeline before increasing resolution.
 - Use **Voxel Convert** to import/export common voxel file formats (RAW, BINVOX, etc.).
 - If your marching-cubes mesh has holes, check your **IsoValue** threshold.
 - Preview intermediate voxel boxes to verify grid coverage.
 - Stub components display warnings; implement `SolveInstance` logic by porting C code from `t43/`.
 
 ## Next Steps
 - Port each `.c` file in `t43/` to C# using Rhino.Geometry operations:
   - Surface-plane intersections for voxelization
   - 3D convolutions for blur/accentuation
   - Standard marching-cubes lookup for `MarchVoxelsComponent`
   - G-code string formatting in `VoxelsToGCodeComponent`
 - Validate results by exporting meshes to STL and printing.
 
 For questions or contributions, see the `README.md` and contact the `crft` maintainers.