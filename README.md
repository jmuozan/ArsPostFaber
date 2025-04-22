# crft

## Overview
The `crft` repository appears to be a project that integrates various components for tasks such as hand tracking, segmentation, and more. It includes scripts, libraries, and binaries for processing and analyzing data, likely related to computer vision or machine learning.

## Repository Structure

### Root Files
- **BitmapParameter.cs, EventArguments.cs, HandtrackComponent.cs, MeshComponent.cs, MockClasses.cs, ply-importerComponent.cs, SAMComponent.cs, WebcamComponent.cs, WebViewEditor.cs**: Core C# files implementing various functionalities.
- **crft.csproj, crft.sln**: Project and solution files for the .NET project.
- **LICENSE**: Licensing information.
- **README.md**: This file.
- **download_models.sh**: A script to download required models.
- **run_sam.sh**: A script to run SAM (Segment Anything Model).

### Directories

#### `backup/`
Contains backup scripts and files, such as:
- **launch_sam_ui.py**: Likely a Python script to launch a UI for SAM.
- **run_sam_direct.py**: A script to directly run SAM.
- **test_*.sh**: Various test scripts.

#### `bin/`
Compiled binaries and runtime files for Debug and Release configurations.

#### `lib/`
Contains native libraries, such as `libOpenCvSharpExtern.dylib`.

#### `mediapipe/`
Python scripts for hand tracking:
- **handtrack.py**: Main script for hand tracking.
- **process_hand_frame.py**: Processes individual hand frames.

#### `segmentanything/`
Scripts and configurations for the Segment Anything Model (SAM):
- **1_extract_frames.py, 2_segmenter.py, 3_masking_out.py, 4_reconstruction.py**: Steps for frame extraction, segmentation, masking, and reconstruction.
- **sam2/**: Contains SAM2-related scripts, configurations, and utilities.

#### `Properties/`
Contains project properties, such as `launchSettings.json`.

## Build Instructions
To build the project, use the following command:
```bash
$ dotnet build -clp:NoSummary crft.csproj
```

Alternatively, you can use the VS Code task labeled `build`.

## Usage
- Run `download_models.sh` to download necessary models.
- Use `run_sam.sh` to execute the Segment Anything Model.
- Explore the `mediapipe/` and `segmentanything/` directories for specific functionalities.

## License
Refer to the `LICENSE` file for licensing details.