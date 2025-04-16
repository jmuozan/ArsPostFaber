#!/usr/bin/env python3
# Simple script to view a video frame by frame

import cv2
import argparse

def main():
    parser = argparse.ArgumentParser(description='View a video frame by frame')
    parser.add_argument('video_path', help='Path to the video file')
    args = parser.parse_args()

    # Open the video file
    video = cv2.VideoCapture(args.video_path)

    if not video.isOpened():
        print(f"Error: Could not open video file: {args.video_path}")
        return

    # Get video properties
    frame_count = int(video.get(cv2.CAP_PROP_FRAME_COUNT))
    fps = video.get(cv2.CAP_PROP_FPS)
    width = int(video.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(video.get(cv2.CAP_PROP_FRAME_HEIGHT))

    print(f"Video info: {width}x{height}, {fps} FPS, {frame_count} frames")

    # Create window
    window_name = "Video Viewer"
    cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)
    cv2.resizeWindow(window_name, width, height)

    # Current frame
    current_frame = 0
    
    # Read the first frame
    ret, frame = video.read()
    
    if not ret:
        print("Error: Could not read the first frame")
        return

    print("\nControls:")
    print("  Next frame: Right arrow or Space")
    print("  Previous frame: Left arrow")
    print("  Jump forward 10 frames: Up arrow")
    print("  Jump backward 10 frames: Down arrow")
    print("  Quit: Q or Escape")

    while True:
        # Display frame number
        frame_with_info = frame.copy()
        cv2.putText(
            frame_with_info, 
            f"Frame: {current_frame}/{frame_count-1}",
            (20, 40), 
            cv2.FONT_HERSHEY_SIMPLEX, 
            1, 
            (0, 255, 0), 
            2
        )
        
        # Show frame
        cv2.imshow(window_name, frame_with_info)
        
        # Wait for key press
        key = cv2.waitKey(0) & 0xFF
        
        # Process key
        if key == ord('q') or key == 27:  # Q or Escape
            break
        elif key == 83 or key == 32:  # Right arrow or Space
            current_frame += 1
        elif key == 81:  # Left arrow
            current_frame = max(0, current_frame - 1)
        elif key == 82:  # Up arrow
            current_frame += 10
        elif key == 84:  # Down arrow
            current_frame = max(0, current_frame - 10)
        
        # Ensure frame index is valid
        current_frame = min(current_frame, frame_count - 1)
        
        # Set the position and read the frame
        video.set(cv2.CAP_PROP_POS_FRAMES, current_frame)
        ret, frame = video.read()
        
        if not ret:
            print(f"Error: Could not read frame {current_frame}")
            break

    # Release resources
    video.release()
    cv2.destroyAllWindows()

if __name__ == "__main__":
    main()