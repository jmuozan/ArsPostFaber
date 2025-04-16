#!/bin/bash

# Simple wrapper script with correct paths

# Get the directory of this script
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Run the script with the correct video and model paths
"$SCRIPT_DIR/segment_video.sh" "$SCRIPT_DIR/ceramics.mp4" -m "$SCRIPT_DIR/checkpoints/sam_vit_b_01ec64.pth"