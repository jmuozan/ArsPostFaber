# SAM Video Segmentation Troubleshooting

## Issues with segment_video.sh

The original `segment_video.sh` script has several issues on macOS:

1. It requires .NET 8.0 runtime, but you have .NET 9.0 installed
2. It has issues finding native libraries (OpenCvSharp and ONNX Runtime)
3. There may be issues with port conflicts for the web server

## Solutions

### Install .NET 8.0

The script requires .NET 8.0. We've installed it via Homebrew:

```bash
brew install dotnet@8
```

### Fix Library Issues

The native libraries need to be correctly loaded. We've created a custom script `run_fixed_sam.sh` that:

1. Sets up all necessary environment variables
2. Copies the native libraries to all possible locations
3. Uses a different port (9000 instead of 8000) to avoid conflicts
4. Runs with verbose error messages

### Alternative: Simple Video Viewer

If you're just trying to view the video, we've created a simple Python video viewer:

```bash
python3 view_video.py ceramics.mp4
```

This will display the video frame by frame with simple controls:
- Next frame: Right arrow or Space
- Previous frame: Left arrow
- Jump forward 10 frames: Up arrow
- Jump backward 10 frames: Down arrow
- Quit: Q or Escape

## Common Errors and Solutions

### .NET Runtime Error

If you see:
```
You must install or update .NET to run this application.
```

Make sure to set the environment variables:
```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec" 
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"
```

### Native Library Errors

If you see:
```
Error loading ONNX model: The type initializer for 'Microsoft.ML.OnnxRuntime.NativeMethods' threw an exception.
```

or

```
Error opening video: The type initializer for 'OpenCvSharp.Internal.NativeMethods' threw an exception.
```

These indicate issues with the native libraries. Make sure the library path is correctly set:

```bash
export DYLD_LIBRARY_PATH="/path/to/lib/native:$DYLD_LIBRARY_PATH"
```

### Port Already in Use

If you see:
```
OSError: [Errno 48] Address already in use
```

The Python HTTP server is trying to use a port that's already in use. We've modified the code to use port 9000 instead of 8000, which should help.

You can also kill any existing Python servers:
```bash
pkill -f "python3 .*server.py"
```