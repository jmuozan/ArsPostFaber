#!/usr/bin/env python3
"""
SAM2 UI Launcher
This script launches the SAM2 UI with video input instead of frames directory
"""

import os
import sys
import subprocess
import platform
import tempfile
import shutil
import argparse

def main():
    parser = argparse.ArgumentParser(description='Launch SAM2 UI with a video file')
    parser.add_argument('--video', help='Path to input video file', default=None)
    parser.add_argument('--frames', help='Path to existing frames directory', default=None)
    parser.add_argument('--backend', help='Matplotlib backend to use', default=None)
    args = parser.parse_args()
    
    print("SAM2 UI Launcher Starting")
    print(f"Python version: {sys.version}")
    print(f"Platform: {platform.platform()}")
    
    # Get the script directory
    script_dir = os.path.dirname(os.path.abspath(__file__))
    print(f"Script directory: {script_dir}")
    
    # Get paths to the scripts
    segmenter_script = os.path.join(script_dir, "segmentanything", "2_segmenter.py")
    extract_script = os.path.join(script_dir, "segmentanything", "1_extract_frames.py")
    
    # Check if scripts exist
    if not os.path.exists(segmenter_script):
        print(f"ERROR: Segmenter script not found at {segmenter_script}")
        sys.exit(1)
        
    if not os.path.exists(extract_script) and args.video:
        print(f"ERROR: Extract script not found at {extract_script}")
        sys.exit(1)
    
    # Set up environment
    os.environ["PYTHONPATH"] = f"{script_dir}:{os.environ.get('PYTHONPATH', '')}"
    os.environ["PYTORCH_ENABLE_MPS_FALLBACK"] = "1"
    
    # Try different matplotlib backends
    if args.backend:
        os.environ["MPLBACKEND"] = args.backend
    else:
        # Try to find a working backend
        print("Testing available Matplotlib backends...")
        backends_to_try = ["MacOSX", "Qt5Agg", "TkAgg", "wxAgg", "Agg"]
        
        for backend in backends_to_try:
            try:
                print(f"Trying {backend} backend...")
                import matplotlib
                matplotlib.use(backend)
                import matplotlib.pyplot as plt
                # Test if it works by creating a simple figure
                fig = plt.figure()
                plt.close(fig)
                print(f"Success! Using {backend} backend")
                os.environ["MPLBACKEND"] = backend
                break
            except Exception as e:
                print(f"Backend {backend} failed: {e}")
                continue
        else:
            print("WARNING: No GUI backend found for Matplotlib. Using fallback 'Agg' (non-interactive).")
            os.environ["MPLBACKEND"] = "Agg"
    
    # Check if we need to install tcl-tk
    if os.environ.get("MPLBACKEND") == "TkAgg":
        try:
            import tkinter
        except ImportError:
            print("\nTkinter is not available. Would you like to install tcl-tk? (y/n): ", end="")
            choice = input().lower()
            if choice == 'y':
                print("Installing tcl-tk...")
                subprocess.run(["brew", "install", "tcl-tk"], check=True)
                print("You may need to reinstall Python or link tcl-tk libraries.")
                print("Please see: https://stackoverflow.com/questions/60469202/unable-to-install-tkinter-with-pyenv-pythons-on-macos")
                sys.exit(1)
            else:
                print("Continuing without Tkinter...")
                os.environ["MPLBACKEND"] = "Agg"
    
    # Determine frames directory
    frames_dir = None
    if args.frames:
        # Use provided frames directory
        frames_dir = os.path.abspath(args.frames)
    elif args.video:
        # Extract frames from video
        video_path = os.path.abspath(args.video)
        video_name = os.path.splitext(os.path.basename(video_path))[0]
        frames_dir = os.path.join(tempfile.gettempdir(), f"frames_{video_name}")
        
        # Create frames directory
        os.makedirs(frames_dir, exist_ok=True)
        
        print(f"\n=== EXTRACTING FRAMES FROM VIDEO ===")
        print(f"Video: {video_path}")
        print(f"Frames directory: {frames_dir}")
        
        # Run extraction script
        extract_cmd = [sys.executable, extract_script, video_path, frames_dir, "-r", "10"]
        try:
            subprocess.run(extract_cmd, check=True)
            print(f"Frames extracted successfully to {frames_dir}")
        except subprocess.CalledProcessError as e:
            print(f"Error extracting frames: {e}")
            sys.exit(1)
    else:
        # Check if we have a frames directory in the project
        default_frames = os.path.join(script_dir, "frames")
        if os.path.exists(default_frames) and os.path.isdir(default_frames):
            frames_dir = default_frames
            print(f"Using default frames directory: {frames_dir}")
        else:
            print("ERROR: No video or frames directory provided, and no default frames directory found.")
            print("Please specify a video file with --video or a frames directory with --frames")
            sys.exit(1)
    
    # Check that frames directory exists and has files
    if not os.path.exists(frames_dir):
        print(f"ERROR: Frames directory {frames_dir} does not exist")
        sys.exit(1)
        
    jpg_files = [f for f in os.listdir(frames_dir) if f.lower().endswith(('.jpg', '.jpeg', '.png'))]
    if not jpg_files:
        print(f"ERROR: No image files found in {frames_dir}")
        sys.exit(1)
        
    print(f"Found {len(jpg_files)} image files in {frames_dir}")
    
    # Change working directory
    sam_dir = os.path.join(script_dir, "segmentanything")
    os.chdir(sam_dir)
    print(f"Changed working directory to: {os.getcwd()}")
    
    # Create a modified version of the segmenter script that doesn't ask for input
    modified_script_path = os.path.join(tempfile.gettempdir(), "modified_segmenter.py")
    
    with open(segmenter_script, 'r') as f:
        script_content = f.read()
    
    # Replace the input line with automatic frames directory
    modified_content = script_content.replace(
        'video_dir = input("Enter the directory containing video frames (e.g., ./ceramics_frames): ")',
        f'video_dir = "{frames_dir}"  # Auto-set by launcher'
    )
    
    # Also modify the matplotlib backend selection directly in the script
    modified_content = modified_content.replace(
        'import matplotlib.pyplot as plt',
        'import matplotlib\nmatplotlib.use(os.environ.get("MPLBACKEND", "Agg"))\nimport matplotlib.pyplot as plt'
    )
    
    # Add code to handle non-interactive backend
    if os.environ.get("MPLBACKEND") == "Agg":
        # If we're using the Agg backend (non-interactive), we need to modify the script to 
        # save images rather than display them interactively
        modified_content = modified_content.replace(
            'plt.show()',
            'plt.savefig(os.path.join(os.path.dirname(os.path.abspath(__file__)), "sam_preview.png"))\nprint("\\nNOTE: Using non-interactive backend. Preview saved to sam_preview.png")'
        )
    
    with open(modified_script_path, 'w') as f:
        f.write(modified_content)
        
    print("\n=== LAUNCHING SAM2 UI ===")
    print(f"Using frames directory: {frames_dir}")
    print(f"Using Matplotlib backend: {os.environ.get('MPLBACKEND', 'default')}")
    print("=======================================\n")
    
    # Launch the modified script
    try:
        # Run the script with a new process group so it can operate independently
        # This helps ensure the UI remains open even if the parent process is closed
        if os.environ.get("MPLBACKEND") == "MacOSX":
            print("Using MacOSX backend - opening SAM2 UI...")
            
            # For MacOSX backend, use a different approach
            # We'll create a mini-launcher script that runs the modified segmenter
            mini_launcher_path = os.path.join(tempfile.gettempdir(), "mini_launcher.py")
            with open(mini_launcher_path, 'w') as f:
                f.write(f'''#!/usr/bin/env python3
import os
import sys
import subprocess

os.environ["PYTHONPATH"] = "{os.environ["PYTHONPATH"]}"
os.environ["MPLBACKEND"] = "MacOSX"
os.environ["PYTORCH_ENABLE_MPS_FALLBACK"] = "1"

# Run the script
subprocess.run(["{sys.executable}", "{modified_script_path}"])
''')
            
            # Make it executable
            os.chmod(mini_launcher_path, 0o755)
            
            # Launch it in a separate process
            subprocess.Popen([sys.executable, mini_launcher_path], 
                            start_new_session=True,
                            stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE)
            
            print("SAM2 UI launched in a separate window.")
            
        else:
            # For other backends, run directly
            subprocess.run([sys.executable, modified_script_path], check=True)
            print("SAM2 UI completed.")
            
    except subprocess.CalledProcessError as e:
        print(f"Error running SAM2 UI: {e}")
    finally:
        # We don't remove the modified script right away in case the new process is using it
        # It will be cleaned up the next time the launcher runs
        
        # Tell the user where to find results
        output_dir = os.path.join(frames_dir, "segmentation_output")
        if os.path.exists(output_dir):
            print(f"\nResults saved to: {output_dir}")
            mask_dir = os.path.join(output_dir, "masks")
            if os.path.exists(mask_dir) and os.path.isdir(mask_dir):
                mask_files = os.listdir(mask_dir)
                if mask_files:
                    print(f"Generated masks: {len(mask_files)}")
                else:
                    print("No masks generated yet. Complete the segmentation process in the UI window.")

if __name__ == "__main__":
    main()