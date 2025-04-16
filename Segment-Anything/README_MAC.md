# SAM Video Segmentation for macOS

This is a macOS-compatible version of the Segment Anything Model (SAM) that supports video segmentation through a web interface.

## Features

- Video segmentation using Meta's Segment Anything Model (SAM)
- Web-based interface for drawing points and boxes to guide segmentation
- Support for PyTorch models with automatic conversion to ONNX
- Frame navigation and batch processing of all frames
- Saving segmented videos

## Requirements

- macOS
- .NET 8.0 or later
- Python 3.x with the following packages:
  - torch
  - segment_anything
  - onnx

## Usage

### Running from command line

```bash
./segment_video.sh path/to/video.mp4 -m path/to/model.pth
```

### Options

- `-m`, `--model`: Path to the SAM model file (PyTorch .pth or directory containing ONNX models)

## Model Information

SAM uses a ViT-based encoder and outputs masks based on user-provided prompts (points or boxes).

### Supported Models

- ViT-B (SAM ViT-Base)
- ViT-L (SAM ViT-Large)
- ViT-H (SAM ViT-Huge)

Model files can be downloaded from the [Segment Anything GitHub repository](https://github.com/facebookresearch/segment-anything).

## Web Interface

The application launches a web interface in your default browser:

- Click to add points for segmentation (green points indicate inclusion, red points indicate exclusion)
- Click and drag to create bounding boxes
- Use arrow keys to navigate between frames
- Click "Process All Frames" to apply current points/boxes to all frames

## Troubleshooting

If you encounter issues:

1. Ensure you have Python 3 installed
2. Install required Python packages: `pip install torch segment_anything onnx`
3. Check that your model file path is correct and the model is valid
4. For PyTorch models, ensure you have sufficient memory for conversion

## License

This application includes code from Meta's Segment Anything project, which is released under the Apache License 2.0.