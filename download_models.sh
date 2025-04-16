#!/bin/bash

# SAM Model Download and Setup Script for macOS
# This script installs Python dependencies and downloads the SAM models

echo "SAM Model Setup Script"
echo "======================"

# Check if Python 3 is installed
if ! command -v python3 &> /dev/null; then
    echo "Error: Python 3 is not installed. Please install Python 3."
    exit 1
fi

# Install required packages
echo "Installing required Python packages..."
python3 -m pip install torch segment_anything onnx

# Create checkpoints directory if it doesn't exist
if [ ! -d "checkpoints" ]; then
    mkdir -p checkpoints
fi

# Ask which model to download
echo ""
echo "Which SAM model would you like to download?"
echo "1. ViT-B (Base, ~375MB) - Recommended for most users"
echo "2. ViT-L (Large, ~1.2GB) - Better quality but slower"
echo "3. ViT-H (Huge, ~2.5GB) - Best quality but very slow"
echo "4. Skip download (if you already have a model)"
read -p "Enter your choice (1-4): " model_choice

case $model_choice in
    1)
        MODEL_URL="https://dl.fbaipublicfiles.com/segment_anything/sam_vit_b_01ec64.pth"
        MODEL_FILE="sam_vit_b_01ec64.pth"
        ;;
    2)
        MODEL_URL="https://dl.fbaipublicfiles.com/segment_anything/sam_vit_l_0b3195.pth"
        MODEL_FILE="sam_vit_l_0b3195.pth"
        ;;
    3)
        MODEL_URL="https://dl.fbaipublicfiles.com/segment_anything/sam_vit_h_4b8939.pth"
        MODEL_FILE="sam_vit_h_4b8939.pth"
        ;;
    4)
        echo "Skipping model download."
        exit 0
        ;;
    *)
        echo "Invalid choice. Exiting."
        exit 1
        ;;
esac

# Download the model
echo ""
echo "Downloading $MODEL_FILE..."
echo "This may take some time depending on your internet connection."
curl -L $MODEL_URL -o "checkpoints/$MODEL_FILE"

if [ $? -ne 0 ]; then
    echo "Error: Failed to download the model. Please check your internet connection and try again."
    exit 1
fi

echo ""
echo "Download complete! Model saved to: checkpoints/$MODEL_FILE"
echo ""
echo "To use this model with the SAM Video Segmentation tool, run:"
echo "./segment_video.sh path/to/video.mp4 -m checkpoints/$MODEL_FILE"
echo ""