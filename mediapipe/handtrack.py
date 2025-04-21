#!/usr/bin/env python3
import cv2
import mediapipe as mp
import sys
import os

mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles
mp_hands = mp.solutions.hands

def process_single_image(input_path, output_path):
    """Process a single image for hand landmarks and write results to file"""
    print(f"Processing image: {input_path}", file=sys.stderr)
    print(f"Writing landmarks to: {output_path}", file=sys.stderr)
    
    # Read the input image
    image = cv2.imread(input_path)
    if image is None:
        print(f"Error: Could not read image from {input_path}", file=sys.stderr)
        return False
    
    # Get image dimensions for debugging
    h, w = image.shape[:2]
    print(f"Image dimensions: {w}x{h}", file=sys.stderr)
    
    # Convert to RGB (MediaPipe requires RGB)
    image_rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    
    # Process with MediaPipe - use extremely sensitive settings 
    with mp_hands.Hands(
        static_image_mode=False,  # False for tracking mode
        max_num_hands=2,
        min_detection_confidence=0.1,  # Very low threshold to catch almost any hand shape
        min_tracking_confidence=0.1,   # Very low threshold for tracking
        model_complexity=0) as hands:  # Lower complexity for better performance
        
        # Process the image
        results = hands.process(image_rgb)
        
        # Write landmarks to output file
        with open(output_path, 'w') as f:
            if results.multi_hand_landmarks:
                print(f"Number of hands detected: {len(results.multi_hand_landmarks)}", file=sys.stderr)
                
                # Get the first hand
                hand_landmarks = results.multi_hand_landmarks[0]
                
                # Save landmarks to file - normalized coordinates (0-1)
                for landmark in hand_landmarks.landmark:
                    f.write(f"{landmark.x},{landmark.y}\n")
                
                # Save a debug visualization image
                debug_image = image.copy()
                mp_drawing.draw_landmarks(
                    debug_image,
                    hand_landmarks,
                    mp_hands.HAND_CONNECTIONS,
                    mp_drawing_styles.get_default_hand_landmarks_style(),
                    mp_drawing_styles.get_default_hand_connections_style())
                
                # Save debug image with landmarks
                debug_path = output_path + ".debug.jpg"
                cv2.imwrite(debug_path, debug_image)
                print(f"Saved debug image to: {debug_path}", file=sys.stderr)
                
                return True
            else:
                print("No hands detected", file=sys.stderr)
                return False

def main():
    """Main function to handle CLI arguments"""
    if len(sys.argv) != 3:
        print("Usage: python handtrack.py <input_image_path> <output_landmarks_path>", file=sys.stderr)
        sys.exit(1)
    
    input_path = sys.argv[1]
    output_path = sys.argv[2]
    
    try:
        # Create output directory if needed
        output_dir = os.path.dirname(output_path)
        if output_dir and not os.path.exists(output_dir):
            os.makedirs(output_dir)
        
        # Process the image
        success = process_single_image(input_path, output_path)
        sys.exit(0 if success else 1)
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(2)

if __name__ == "__main__":
    main()
