#!/bin/bash

# SAM Video Segmentation Script for macOS
# This script launches the SAM Video segmentation UI for a given video file

# Check if video path is provided
if [ "$#" -lt 1 ]; then
    echo "Usage: $0 <path-to-video> [-m <path-to-model>]"
    echo "Example: $0 ceramics.mp4 -m checkpoints/sam_vit_b_01ec64.pth"
    exit 1
fi

VIDEO_PATH="$1"
MODEL_PATH=""

echo "Processing video: $VIDEO_PATH"

# Check for model path
shift  # Remove first argument (video path)
while [ "$#" -gt 0 ]; do
    case "$1" in
        -m|--model)
            if [ -z "$2" ] || [[ "$2" == -* ]]; then
                echo "Error: Model path missing after -m flag"
                exit 1
            fi
            MODEL_PATH="$2"
            echo "Using model: $MODEL_PATH"
            shift 2  # Move past the flag and its value
            ;;
        *)
            echo "Unknown parameter: $1"
            shift  # Move past the unknown parameter
            ;;
    esac
done

# Check if the video file exists
if [ ! -f "$VIDEO_PATH" ]; then
    echo "Error: Video file not found: $VIDEO_PATH"
    exit 1
fi

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Check for dotnet
if ! command -v dotnet &> /dev/null; then
    echo "Error: dotnet is not installed. Please install .NET SDK."
    exit 1
fi

# Check for model file
if [ -n "$MODEL_PATH" ] && [ ! -f "$MODEL_PATH" ] && [ ! -d "$MODEL_PATH" ]; then
    echo "Error: Model file or directory not found: $MODEL_PATH"
    exit 1
fi

# Absolute path to video
VIDEO_PATH=$(realpath "$VIDEO_PATH" 2>/dev/null || echo "$VIDEO_PATH")

# Ensure Python dependencies are installed
echo "Checking Python dependencies..."
python3 -c "import torch, segment_anything, onnx" 2>/dev/null
if [ $? -ne 0 ]; then
    echo "Missing Python dependencies. Would you like to install them now? (y/n)"
    read -p "> " install_deps
    
    if [[ "$install_deps" == "y" || "$install_deps" == "Y" ]]; then
        python3 -m pip install torch segment_anything onnx
    else
        echo "Dependencies not installed. The application may not work correctly."
    fi
fi

# Build the Mac-specific project
echo "Building SAM Video segmentation project..."
cd "$SCRIPT_DIR/Segment-Anything"
dotnet build SAMViewer.Mac.csproj -c Release
if [ $? -ne 0 ]; then
    echo "Error: Build failed. Please check the error messages above."
    exit 1
fi

echo "Running SAM Video segmentation for: $VIDEO_PATH"

# Run with specific command line based on model presence
if [ -n "$MODEL_PATH" ]; then
    echo "Executing with model: $MODEL_PATH"
    MODEL_PATH=$(realpath "$MODEL_PATH" 2>/dev/null || echo "$MODEL_PATH")
    
    # Run with verbose output
    echo "Command: dotnet run --project SAMViewer.Mac.csproj -c Release -- video \"$VIDEO_PATH\" -m \"$MODEL_PATH\""
    dotnet run --project SAMViewer.Mac.csproj -c Release -- video "$VIDEO_PATH" -m "$MODEL_PATH"
else
    # No model specified - try to find one in the checkpoints directory
    if [ -d "$SCRIPT_DIR/checkpoints" ]; then
        # Look for .pth files in the checkpoints directory
        MODEL_FILES=("$SCRIPT_DIR"/checkpoints/*.pth)
        if [ ${#MODEL_FILES[@]} -gt 0 ] && [ -f "${MODEL_FILES[0]}" ]; then
            MODEL_PATH="${MODEL_FILES[0]}"
            echo "Found model: $MODEL_PATH"
            echo "Command: dotnet run --project SAMViewer.Mac.csproj -c Release -- video \"$VIDEO_PATH\" -m \"$MODEL_PATH\""
            dotnet run --project SAMViewer.Mac.csproj -c Release -- video "$VIDEO_PATH" -m "$MODEL_PATH"
        else
            # No model found
            echo "No model found in checkpoints directory. Running without model..."
            echo "Command: dotnet run --project SAMViewer.Mac.csproj -c Release -- video \"$VIDEO_PATH\""
            dotnet run --project SAMViewer.Mac.csproj -c Release -- video "$VIDEO_PATH"
        fi
    else
        # No checkpoints directory
        echo "No checkpoints directory found. Running without model..."
        echo "Command: dotnet run --project SAMViewer.Mac.csproj -c Release -- video \"$VIDEO_PATH\""
        dotnet run --project SAMViewer.Mac.csproj -c Release -- video "$VIDEO_PATH"
    fi
fi

# If we get here without opening a browser, there might be an issue
echo "Did the web interface open in your browser? If not, try manually opening: http://localhost:9000"