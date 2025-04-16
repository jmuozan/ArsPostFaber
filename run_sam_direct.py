#!/usr/bin/env python3
"""
SAM2 Direct Launcher
This is a minimal script that launches the SAM2 UI directly
"""

import os
import sys
import subprocess
import platform
import tempfile
import time

# Script must be run with a video path
if len(sys.argv) < 2:
    print("Usage: python run_sam_direct.py <video_path>")
    sys.exit(1)

video_path = os.path.abspath(sys.argv[1])
if not os.path.exists(video_path):
    print(f"Error: Video file {video_path} not found")
    sys.exit(1)

# Get the script directory
script_dir = os.path.dirname(os.path.abspath(__file__))
print(f"Script directory: {script_dir}")

# Set up environment 
os.environ["PYTHONPATH"] = f"{script_dir}:{os.environ.get('PYTHONPATH', '')}"
os.environ["PYTORCH_ENABLE_MPS_FALLBACK"] = "1"
os.environ["MPLBACKEND"] = "MacOSX"

# Get paths to scripts
extract_script = os.path.join(script_dir, "segmentanything", "1_extract_frames.py")
segmenter_script = os.path.join(script_dir, "segmentanything", "2_segmenter.py")

# Create frames directory
video_name = os.path.splitext(os.path.basename(video_path))[0]
frames_dir = os.path.join(tempfile.gettempdir(), f"frames_{video_name}")
os.makedirs(frames_dir, exist_ok=True)

# Extract frames
print(f"Extracting frames from {video_path} to {frames_dir}...")
extract_cmd = [sys.executable, extract_script, video_path, frames_dir, "-r", "10"]
subprocess.run(extract_cmd, check=True)

# Modify segmenter script to use frames directory automatically
print("Creating modified segmenter script...")
with open(segmenter_script, 'r') as f:
    script_content = f.read()

modified_content = script_content.replace(
    'video_dir = input("Enter the directory containing video frames (e.g., ./ceramics_frames): ")',
    f'video_dir = "{frames_dir}"  # Auto-set by launcher'
)

modified_script_path = os.path.join(tempfile.gettempdir(), "sam_modified_segmenter.py")
with open(modified_script_path, 'w') as f:
    f.write(modified_content)

# Change to segmentanything directory
os.chdir(os.path.join(script_dir, "segmentanything"))
print(f"Working directory: {os.getcwd()}")

# Launch the UI
print("Starting SAM2 UI...")
print("If the UI doesn't appear, try running this script directly from Terminal:")
print(f"python3 {os.path.abspath(__file__)} {video_path}")

# Execute the script
os.system(f'python3 "{modified_script_path}"')

# Keep Terminal window open
input("Press Enter to close...")