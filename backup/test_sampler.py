#!/usr/bin/env python3
import os
import sys
import subprocess
import tempfile

def main():
    print("SAM2 Video Segmentation Tool Starting...")
    print(f"Arguments: {sys.argv}")
    
    if len(sys.argv) < 2:
        print("Usage: python test_sampler.py <video_file>")
        return
        
    video_path = sys.argv[1]
    print(f"Video path: {video_path}")
    
    if not os.path.exists(video_path):
        print(f"Error: Video file not found: {video_path}")
        return
    
    # Create temp directories
    video_name = os.path.splitext(os.path.basename(video_path))[0]
    frames_dir = os.path.join(tempfile.gettempdir(), f"frames_{video_name}")
    output_dir = os.path.join(tempfile.gettempdir(), f"output_{video_name}")
    masks_dir = os.path.join(frames_dir, "segmentation_output", "masks")
    
    # Create directories
    os.makedirs(frames_dir, exist_ok=True)
    os.makedirs(output_dir, exist_ok=True)
    os.makedirs(os.path.join(frames_dir, "segmentation_output"), exist_ok=True)
    os.makedirs(masks_dir, exist_ok=True)
    
    print(f"Created directories:")
    print(f"- Frames: {frames_dir}")
    print(f"- Output: {output_dir}")
    print(f"- Masks: {masks_dir}")
    
    # Get script paths
    script_dir = os.path.dirname(os.path.abspath(__file__))
    print(f"Script directory: {script_dir}")
    
    extract_script = os.path.join(script_dir, "segmentanything", "1_extract_frames.py")
    segment_script = os.path.join(script_dir, "segmentanything", "2_segmenter.py")
    mask_script = os.path.join(script_dir, "segmentanything", "3_masking_out.py")
    
    print(f"Extract script path: {extract_script}")
    print(f"Segment script path: {segment_script}")
    print(f"Mask script path: {mask_script}")
    
    # Check scripts exist
    if not all(os.path.exists(s) for s in [extract_script, segment_script, mask_script]):
        print("Error: Could not find all required scripts")
        for s in [extract_script, segment_script, mask_script]:
            print(f"- {s}: {'Found' if os.path.exists(s) else 'Not found'}")
        return
    else:
        print("All required scripts found!")
        
    # Set Python path to include the segmentanything directory
    os.environ["PYTHONPATH"] = f"{script_dir}:{os.environ.get('PYTHONPATH', '')}"
    print(f"PYTHONPATH set to: {os.environ['PYTHONPATH']}")
    
    # Extract frames
    print("\nStep 1: Extracting frames...")
    extract_cmd = [sys.executable, extract_script, video_path, frames_dir, "-r", "10"]
    try:
        subprocess.run(extract_cmd, check=True)
    except subprocess.CalledProcessError as e:
        print(f"Error running frame extraction: {e}")
        return
    
    # Run segmentation
    print("\nStep 2: Opening segmentation UI...")
    print("Please interact with the UI when it opens.")
    print("1. Use 'New Scene' to mark scene boundaries")
    print("2. Click to add positive points (green)")
    print("3. Toggle to negative mode if needed")
    print("4. Process using 'Process Current Scene' or 'Process Video'")
    
    # Change to script directory to find segmentation models
    os.chdir(os.path.join(script_dir, "segmentanything"))
    print(f"Changed working directory to: {os.getcwd()}")
    
    # Create temporary script with fixed paths for masking
    with tempfile.NamedTemporaryFile(mode='w', suffix='.py', delete=False) as tmp:
        mask_script_path = tmp.name
        
        with open(mask_script, 'r') as orig:
            content = orig.read()
            
        # Replace paths in the script
        content = content.replace('frames_dir = "pottery"', f'frames_dir = "{frames_dir}"')
        content = content.replace('masks_dir = "pottery/segmentation_output/masks"', f'masks_dir = "{masks_dir}"')
        content = content.replace('output_dir = "db"', f'output_dir = "{output_dir}"')
        
        tmp.write(content)
        print(f"Created temporary script with modified paths: {mask_script_path}")
    
    try:
        # Run segmentation UI directly to ensure it has access to terminal input/output
        print(f"Launching segmentation UI: {segment_script}")
        print(f"When prompted, enter this frames directory: {frames_dir}")
        
        # Create a temporary file to pass the frames directory to the segmentation script
        frames_file = os.path.join(tempfile.gettempdir(), "frames_dir.txt")
        with open(frames_file, 'w') as f:
            f.write(frames_dir)
        print(f"Created temporary file with frames directory: {frames_file}")
        
        # Launch the segmentation UI in the current terminal window
        print("\n\n===========================================================")
        print("STARTING SEGMENTATION UI - PLEASE INTERACT WITH THE INTERFACE")
        print("===========================================================\n")
        
        # Execute the script directly
        os.system(f'python3 "{segment_script}"')
        
        print("Segmentation UI process completed")
        
        # Apply masks
        print("\nStep 3: Applying masks to frames...")
        mask_cmd = [sys.executable, mask_script_path]
        subprocess.run(mask_cmd, check=True)
        
        print(f"\nProcessing complete\!")
        print(f"Output files saved to: {output_dir}")
        
    except Exception as e:
        print(f"Error during processing: {e}")
    finally:
        # Clean up temp script
        if os.path.exists(mask_script_path):
            os.remove(mask_script_path)

if __name__ == "__main__":
    main()
