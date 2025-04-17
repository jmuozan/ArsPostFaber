#!/bin/bash

# Custom script to run SAM with all the required library paths set correctly

# Set workspace directory
WORKSPACE="/Users/jorgemuyo/Desktop/crft"
cd "$WORKSPACE" || exit 1

# Make sure .NET 8 is in the path
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"

# Copy native libraries to all possible locations
mkdir -p "$WORKSPACE/Segment-Anything/bin/Release/net8.0/runtimes/osx-arm64/native/"
mkdir -p "$WORKSPACE/Segment-Anything/bin/Release/net8.0/runtimes/osx-x64/native/"
mkdir -p "$WORKSPACE/Segment-Anything/bin/Release/net8.0/lib/native/"
mkdir -p "$WORKSPACE/Segment-Anything/bin/Release/net8.0/native/"

cp "$WORKSPACE/lib/native/libOpenCvSharpExtern.dylib" "$WORKSPACE/Segment-Anything/bin/Release/net8.0/"
cp "$WORKSPACE/lib/native/libOpenCvSharpExtern.dylib" "$WORKSPACE/Segment-Anything/bin/Release/net8.0/runtimes/osx-arm64/native/"
cp "$WORKSPACE/lib/native/libOpenCvSharpExtern.dylib" "$WORKSPACE/Segment-Anything/bin/Release/net8.0/runtimes/osx-x64/native/"
cp "$WORKSPACE/lib/native/libOpenCvSharpExtern.dylib" "$WORKSPACE/Segment-Anything/bin/Release/net8.0/lib/native/"
cp "$WORKSPACE/lib/native/libOpenCvSharpExtern.dylib" "$WORKSPACE/Segment-Anything/bin/Release/net8.0/native/"

# Set the library path
export DYLD_LIBRARY_PATH="$WORKSPACE/lib/native:$WORKSPACE/Segment-Anything/bin/Release/net8.0:$WORKSPACE/Segment-Anything/bin/Release/net8.0/lib/native:$WORKSPACE/Segment-Anything/bin/Release/net8.0/runtimes/osx-arm64/native:$WORKSPACE/Segment-Anything/bin/Release/net8.0/native:$DYLD_LIBRARY_PATH"

# Kill any running Python servers that might be using our ports
echo "Stopping any running Python servers..."
pkill -f "python3 .*server.py" 2>/dev/null || true

# Video path
if [ -z "$1" ]; then
    echo "Usage: $0 <path-to-video>"
    echo "Example: $0 ceramics.mp4"
    exit 1
fi

VIDEO_PATH="$1"
echo "Processing video: $VIDEO_PATH"

# Make sure the path is absolute
if [[ "$VIDEO_PATH" != /* ]]; then
    VIDEO_PATH="$WORKSPACE/$VIDEO_PATH"
fi

# Navigate to the Segment-Anything directory
cd "$WORKSPACE/Segment-Anything" || exit 1

# Build the project
echo "Building SAM Video segmentation project..."
dotnet build SAMViewer.Mac.csproj -c Release

# Run the application with more detailed error messages
echo "Running SAM with video: $VIDEO_PATH"
echo "Command: DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --project SAMViewer.Mac.csproj -c Release -- video \"$VIDEO_PATH\" -m \"$WORKSPACE/checkpoints/sam_vit_b_01ec64.pth\" --verbose"
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --project SAMViewer.Mac.csproj -c Release -- video "$VIDEO_PATH" -m "$WORKSPACE/checkpoints/sam_vit_b_01ec64.pth" --verbose

echo "Done!"