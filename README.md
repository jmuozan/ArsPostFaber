# crft

## Overview
crft is a set of Grasshopper components for Rhino, forming a plugin that explores human-software-machine interaction (HMI). The components are designed as an experimental toolkit for bridging the physical and digital worlds, focusing on:

- **Atoms to Bits**: Mesh digitization workflows, enabling the conversion of physical objects into digital mesh representations.
- **Mesh Editing with Hand Detection**: Real-time mesh manipulation using hand tracking and gesture recognition, leveraging computer vision and AI.
- **Machine Live Control**: Components for live, interactive control of digital and physical systems, exploring new paradigms of HMI.

## Features
- Real-time camera and video capture integration.
- Hand tracking and gesture-based mesh editing.
- Mesh digitization and manipulation tools.
- Experimental interfaces for live machine control.

## Repository Structure
- **BitmapParameter.cs, EventArguments.cs, HandtrackComponent.cs, MeshComponent.cs, MockClasses.cs, ply-importerComponent.cs, SAMComponent.cs, WebcamComponent.cs, WebViewEditor.cs**: Core C# files implementing the Grasshopper components and plugin logic.
- **crft.csproj, crft.sln**: Project and solution files for .NET build.
- **download_models.sh**: Script to download required AI/ML models.
- **run_sam.sh**: Script to run the Segment Anything Model (SAM).
- **mediapipe/**: Python scripts for hand tracking and computer vision.
- **segmentanything/**: Scripts and configs for segmentation workflows.
- **backup/**: Utility scripts and test files.
- **bin/**: Compiled binaries and runtime files.
- **lib/**: Native libraries (e.g., OpenCV, ONNX Runtime).
- **Properties/**: Project configuration files.

## Build Instructions
To build the plugin, run:

```bash
dotnet build -clp:NoSummary crft.csproj
```

Or use the VS Code task labeled `build`.

## Usage
- Run `download_models.sh` to fetch required models.
- Use `run_sam.sh` for segmentation workflows.
- Load the compiled plugin (`crft.gha`) into Grasshopper for Rhino.
- Explore the provided Grasshopper components for mesh digitization, hand-based mesh editing, and live control experiments.

## License
See the `LICENSE` file for details.



## ToDo
- [ ] Implement Depth Maps for SfM 3D reconstruction
- [ ] 'mediapipe/hand_detection.py' implementation on HandtrackComponent.cs