#!/bin/bash
# Simple shell script to run SAM on a video

if [ $# -lt 1 ]; then
  echo "Usage: $0 <video_path> [framerate]"
  exit 1
fi

VIDEO_PATH="$1"
FRAMERATE="${2:-10}"  # Default to 10 fps if not provided
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PYTHON_CMD="python3"

# Check if video exists
if [ ! -f "$VIDEO_PATH" ]; then
  echo "Error: Video file not found: $VIDEO_PATH"
  exit 1
fi

echo "Running SAM2 on video: $VIDEO_PATH"
echo "Script directory: $SCRIPT_DIR"
echo "Extraction framerate: $FRAMERATE fps"

# Extract frames
VIDEO_NAME=$(basename "$VIDEO_PATH" | sed 's/\.[^.]*$//')
FRAMES_DIR="/tmp/frames_${VIDEO_NAME}"
mkdir -p "$FRAMES_DIR"

echo "Extracting frames to $FRAMES_DIR..."
$PYTHON_CMD "$SCRIPT_DIR/segmentanything/1_extract_frames.py" "$VIDEO_PATH" "$FRAMES_DIR" -r "$FRAMERATE"

# Create a modified version of the segmenter script
SEGMENTER_SCRIPT="$SCRIPT_DIR/segmentanything/2_segmenter.py"
MODIFIED_SCRIPT="/tmp/modified_segmenter.py"

echo "Creating modified segmenter script..."
cp "$SEGMENTER_SCRIPT" "$MODIFIED_SCRIPT"

# Replace the input line with our frames directory and set default values for all prompts
sed -i '' "s|video_dir = input(\"Enter the directory containing video frames (e.g., ./ceramics_frames): \")|video_dir = \"$FRAMES_DIR\"  # Auto-set by launcher|" "$MODIFIED_SCRIPT"

# Auto-answer prompts by modifying the script
# This will auto-answer the watermark ratio question with 'y'
sed -i '' "s|try_cpu = input(\"\\nMPS device error. Would you like to try using CPU instead? (y/n, default: y): \").lower().strip()|try_cpu = \"y\"|g" "$MODIFIED_SCRIPT"

# Auto-answer the fallback question with 'y'
sed -i '' "s|auto_fallback = input(\"\\nEnable automatic CPU fallback on memory errors? (y/n, default: y): \").lower().strip()|auto_fallback = \"y\"|g" "$MODIFIED_SCRIPT"

# Auto-answer the model size question with 'y'
sed -i '' "s|use_large = input(\"Do you want to proceed with the large model anyway? (y/n, default: n): \").lower().strip() == 'y'|use_large = True|g" "$MODIFIED_SCRIPT"

# Auto-set keyframe interval to 10
sed -i '' "s|keyframe_input = input(\"\\nEnter keyframe interval (how often to re-apply annotations, default: 10): \")|keyframe_input = \"\"|g" "$MODIFIED_SCRIPT"

# Auto-set propagation steps to 3
sed -i '' "s|propagation_input = input(\"\\nEnter propagation refinement steps (higher values improve quality but are slower, default: 3): \")|propagation_input = \"\"|g" "$MODIFIED_SCRIPT"

# Run the modified script
cd "$SCRIPT_DIR/segmentanything"
echo "Launching SAM2 UI with frames directory: $FRAMES_DIR"
echo "All prompts will be auto-answered with default values"

$PYTHON_CMD "$MODIFIED_SCRIPT"

# Check if segmentation was completed by looking for mask files
MASKS_DIR="$FRAMES_DIR/segmentation_output/masks"
OUTPUT_DIR="$FRAMES_DIR/masking_output"
mkdir -p "$OUTPUT_DIR"

if [ -d "$MASKS_DIR" ] && [ "$(ls -A "$MASKS_DIR" 2>/dev/null)" ]; then
    echo "Segmentation masks found. Applying masks to frames..."
    
    # Create a modified version of the masking script with the correct paths
    MASKING_SCRIPT="$SCRIPT_DIR/segmentanything/3_masking_out.py"
    MODIFIED_MASKING_SCRIPT="/tmp/modified_masking_out.py"
    
    # Copy the original script
    cp "$MASKING_SCRIPT" "$MODIFIED_MASKING_SCRIPT"
    
    # Replace the paths in the script
    sed -i '' "s|frames_dir = \"pottery\"|frames_dir = \"$FRAMES_DIR\"|g" "$MODIFIED_MASKING_SCRIPT"
    sed -i '' "s|masks_dir = \"pottery/segmentation_output/masks\"|masks_dir = \"$MASKS_DIR\"|g" "$MODIFIED_MASKING_SCRIPT"
    sed -i '' "s|output_dir = \"db\"|output_dir = \"$OUTPUT_DIR\"|g" "$MODIFIED_MASKING_SCRIPT"
    
    # Run the masking script
    echo "Running masking script..."
    $PYTHON_CMD "$MODIFIED_MASKING_SCRIPT"
    
    echo "Masking complete! Masked frames saved to: $OUTPUT_DIR"
else
    echo "No segmentation masks found. Please complete the segmentation process in the UI."
fi

echo "Processing complete!"
echo "Output saved to: $FRAMES_DIR/segmentation_output"
if [ -d "$OUTPUT_DIR" ] && [ "$(ls -A "$OUTPUT_DIR" 2>/dev/null)" ]; then
    echo "Masked frames saved to: $OUTPUT_DIR"
fi

echo "Press Enter to close..."
read