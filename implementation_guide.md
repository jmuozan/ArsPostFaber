# Segment Anything Model (SAM) for Grasshopper

This guide explains how to implement and use the Segment Anything Model in Grasshopper.

## Prerequisites

To use the SAM component in Grasshopper, you need:

1. Rhinoceros 6/7 with Grasshopper
2. Visual Studio 2019 or later with C# support
3. The SAM ONNX models:
   - `encoder-quant.onnx`
   - `decoder-quant.onnx`

## Setting Up the Environment

### Step 1: Create a new Class Library project in Visual Studio

1. Open Visual Studio
2. Create a new project
3. Select "Class Library (.NET Framework)" and use .NET Framework 4.8
4. Name the project "SAMforGrasshopper"

### Step 2: Add necessary references

Add the following NuGet packages:
- Microsoft.ML.OnnxRuntime (version 1.15.1 or later)
- OpenCvSharp4 (version 4.8.0 or later)
- OpenCvSharp4.runtime.win (version 4.8.0 or later)

Add references to Grasshopper and Rhino DLLs:
- RhinoCommon.dll
- Grasshopper.dll
- GH_IO.dll

These can be found in the Rhino installation directory, typically:
`C:\Program Files\Rhino 7\System\`

### Step 3: Implement the code

Create the necessary classes as shown in the provided code examples:
- SAMComponent.cs - The main Grasshopper component
- SAM.cs - The implementation of the Segment Anything Model
- Supporting classes for transformations and data structures

### Step 4: Build the project

Build the solution in Visual Studio.

## Installing the Component

1. Open Rhino and Grasshopper
2. In Grasshopper, go to File > Special Folders > Components Folder
3. Copy your compiled DLL (from `bin/Debug` or `bin/Release`) to this folder
4. Restart Grasshopper

## Using the Component

1. The SAM component will be available in the "Image > Segmentation" tab
2. Connect the following inputs:
   - Image Path: Path to the image you want to segment
   - Model Path: Path to the folder containing the SAM ONNX models
   - Points: Points on the image to guide segmentation (0-1 range)
   - Boxes: Bounding boxes to guide segmentation (0-1 range)
   - Add/Remove: True to add mask, False to remove mask
   - Reload: True to reload the image and reset segmentation

3. Outputs:
   - Mask Image: Bitmap of the segmentation mask
   - Curves: Curves representing the boundaries of the segmentation

## Downloading SAM ONNX Models

The original SAM models are in PyTorch format and need to be converted to ONNX.

You can obtain pre-converted ONNX models from:
- The GitHub repository release section
- The WeChat public account mentioned in the README.md by replying with "SAM"

Alternatively, you can convert the models yourself:
1. Clone the original SAM repository: https://github.com/facebookresearch/segment-anything
2. Use the script `scripts/export_onnx_model.py` to convert the models
3. Place the resulting ONNX files in a directory accessible to the component

## Troubleshooting

Common issues:
- **Missing ONNX Models**: Ensure the encoder-quant.onnx and decoder-quant.onnx files are in the specified model path
- **Wrong Image Format**: Make sure the image is in a standard format (PNG, JPG)
- **Memory Issues**: For large images, try reducing the image size before loading or use a more powerful machine
- **Missing Dependencies**: Ensure all the required DLLs are in the same directory as your component

If you encounter errors, check the component message panel in Grasshopper for detailed error information.
