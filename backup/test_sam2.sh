#!/bin/bash
# Test script for SAM2 execution

# Set paths
VIDEO_PATH="/Users/jorgemuyo/Desktop/crft/porcelain.mp4"
FRAMES_DIR="/var/folders/wv/_x9hjmys03x5gnbfl70ry2sr0000gn/T/frames_porcelain"
OUTPUT_DIR="/var/folders/wv/_x9hjmys03x5gnbfl70ry2sr0000gn/T/output_porcelain"

# Create directories
mkdir -p "$FRAMES_DIR"
mkdir -p "$OUTPUT_DIR"
mkdir -p "$FRAMES_DIR/segmentation_output/masks"

echo "Running frame extraction..."
/Users/jorgemuyo/.pyenv/versions/3.11.0/bin/python /Users/jorgemuyo/Desktop/crft/segmentanything/1_extract_frames.py "$VIDEO_PATH" "$FRAMES_DIR" -r 10

echo "Running segmentation UI..."
cd /Users/jorgemuyo/Desktop/crft/segmentanything
/Users/jorgemuyo/.pyenv/versions/3.11.0/bin/python /Users/jorgemuyo/Desktop/crft/segmentanything/2_segmenter.py

echo "Done"