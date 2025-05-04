import cv2
import os
import argparse

def extract_frames(video_path, output_folder, frame_rate=None):
    """
    Extract frames from a video and save them as jpg files with simple numeric filenames
    
    Args:
        video_path (str): Path to the video file
        output_folder (str): Folder to save the extracted frames
        frame_rate (int, optional): Extract frames at specified fps. If None, extract all frames.
    """
    # Create output directory if it doesn't exist
    if not os.path.exists(output_folder):
        os.makedirs(output_folder)
        print(f"Created output directory: {output_folder}")
    
    # Open the video file
    video = cv2.VideoCapture(video_path)
    if not video.isOpened():
        print(f"Error: Could not open video file {video_path}")
        return
    
    # Get video properties
    fps = video.get(cv2.CAP_PROP_FPS)
    total_frames = int(video.get(cv2.CAP_PROP_FRAME_COUNT))
    duration = total_frames / fps if fps > 0 else 0
    
    print(f"Video FPS: {fps}")
    print(f"Total frames: {total_frames}")
    print(f"Duration: {duration:.2f} seconds")
    
    # Calculate frame extraction interval
    if frame_rate is not None and frame_rate < fps:
        interval = int(fps / frame_rate)
        print(f"Extracting at {frame_rate} fps (every {interval} frames)")
    else:
        interval = 1
        print("Extracting all frames")
    
    # Extract frames
    frame_count = 0
    saved_count = 0
    
    while True:
        success, frame = video.read()
        if not success:
            break
        
        if frame_count % interval == 0:
            # Save with just the frame number as filename
            frame_filename = os.path.join(output_folder, f"{saved_count}.jpg")
            cv2.imwrite(frame_filename, frame)
            saved_count += 1
            
            # Print progress every 100 frames
            if saved_count % 100 == 0:
                print(f"Saved {saved_count} frames...")
        
        frame_count += 1
    
    # Release resources
    video.release()
    print(f"Extraction complete. Saved {saved_count} frames to {output_folder}")

if __name__ == "__main__":
    # Set up command line arguments
    parser = argparse.ArgumentParser(description="Extract frames from a video file")
    parser.add_argument("video_path", help="Path to the video file")
    parser.add_argument("output_folder", help="Folder to save the extracted frames")
    parser.add_argument("-r", "--frame_rate", type=int, help="Extract frames at specified fps rate", default=None)
    
    args = parser.parse_args()
    
    # Run the extraction
    extract_frames(args.video_path, args.output_folder, args.frame_rate)