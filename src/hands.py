import sys
import subprocess

# Auto-install missing dependencies
required = ["mediapipe","opencv-python","numpy"]
try:
    import cv2
    import mediapipe as mp
    import numpy as np
except ModuleNotFoundError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "--user"] + required)
    import cv2
    import mediapipe as mp
    import numpy as np

import argparse

parser = argparse.ArgumentParser(description="Hand tracking utility")
parser.add_argument("--headless", action="store_true", help="Run in headless mode for event streaming")
args = parser.parse_args()

mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles
mp_hands = mp.solutions.hands

def calculate_distance(point1, point2):
    """Calculate Euclidean distance between two points"""
    return np.sqrt((point1.x - point2.x)**2 + (point1.y - point2.y)**2)

def get_pinch_color(distance, threshold=0.08):
    """Get color based on pinch distance - red when pinching, green when apart"""
    if distance < threshold:
        return (0, 0, 255)  # Red (BGR format) - pinching
    else:
        return (0, 255, 0)  # Green (BGR format) - not pinching

# For static images:
IMAGE_FILES = []
with mp_hands.Hands(
    static_image_mode=True,
    max_num_hands=1,
    min_detection_confidence=0.5) as hands:
  for idx, file in enumerate(IMAGE_FILES):
    # Read an image, flip it around y-axis for correct handedness output (see
    # above).
    image = cv2.flip(cv2.imread(file), 1)
    # Convert the BGR image to RGB before processing.
    results = hands.process(cv2.cvtColor(image, cv2.COLOR_BGR2RGB))
    # Print handedness and draw hand landmarks on the image.
    print('Handedness:', results.multi_handedness)
    if not results.multi_hand_landmarks:
      continue
    image_height, image_width, _ = image.shape
    annotated_image = image.copy()
    for hand_landmarks in results.multi_hand_landmarks:
      print('hand_landmarks:', hand_landmarks)
      print(
          f'Index finger tip coordinates: (',
          f'{hand_landmarks.landmark[mp_hands.HandLandmark.INDEX_FINGER_TIP].x * image_width}, '
          f'{hand_landmarks.landmark[mp_hands.HandLandmark.INDEX_FINGER_TIP].y * image_height})'
      )
      mp_drawing.draw_landmarks(
          annotated_image,
          hand_landmarks,
          mp_hands.HAND_CONNECTIONS,
          mp_drawing_styles.get_default_hand_landmarks_style(),
          mp_drawing_styles.get_default_hand_connections_style())
      
      # Draw line between thumb tip (4) and index finger tip (8)
      thumb_tip = hand_landmarks.landmark[mp_hands.HandLandmark.THUMB_TIP]
      index_tip = hand_landmarks.landmark[mp_hands.HandLandmark.INDEX_FINGER_TIP]
      
      # Calculate distance and get color
      distance = calculate_distance(thumb_tip, index_tip)
      color = get_pinch_color(distance)
      
      # Convert normalized coordinates to pixel coordinates
      thumb_pixel = (int(thumb_tip.x * image_width), int(thumb_tip.y * image_height))
      index_pixel = (int(index_tip.x * image_width), int(index_tip.y * image_height))
      
      # Draw the line
      cv2.line(annotated_image, thumb_pixel, index_pixel, color, 3)
      
      print(f'Pinch distance: {distance:.3f}, Color: {"Red (Pinching)" if distance < 0.08 else "Green (Apart)"}')
      
    cv2.imwrite(
        '/tmp/annotated_image' + str(idx) + '.png', cv2.flip(annotated_image, 1))
    # Draw hand world landmarks.
    if not results.multi_hand_world_landmarks:
      continue
    for hand_world_landmarks in results.multi_hand_world_landmarks:
      mp_drawing.plot_landmarks(
        hand_world_landmarks, mp_hands.HAND_CONNECTIONS, azimuth=5)

# For webcam input:
if args.headless:
    cap = cv2.VideoCapture(0)
    with mp_hands.Hands(
        model_complexity=0,
        max_num_hands=1,
        min_detection_confidence=0.5,
        min_tracking_confidence=0.5) as hands:
        while cap.isOpened():
            success, image = cap.read()
            if not success:
                continue
            image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
            results = hands.process(image)
            if results.multi_hand_landmarks:
                hand_landmarks = results.multi_hand_landmarks[0]
                thumb_tip = hand_landmarks.landmark[mp_hands.HandLandmark.THUMB_TIP]
                index_tip = hand_landmarks.landmark[mp_hands.HandLandmark.INDEX_FINGER_TIP]
                dist = calculate_distance(thumb_tip, index_tip)
                print(f"{thumb_tip.x} {thumb_tip.y} {index_tip.x} {index_tip.y} {dist}", flush=True)
    cap.release()
    sys.exit(0)

# For webcam input:
cap = cv2.VideoCapture(0)
with mp_hands.Hands(
    model_complexity=0,
    max_num_hands=1,
    min_detection_confidence=0.5,
    min_tracking_confidence=0.5) as hands:
  while cap.isOpened():
    success, image = cap.read()
    if not success:
      print("Ignoring empty camera frame.")
      # If loading a video, use 'break' instead of 'continue'.
      continue
    # To improve performance, optionally mark the image as not writeable to
    # pass by reference.
    image.flags.writeable = False
    image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    results = hands.process(image)
    # Draw the hand annotations on the image.
    image.flags.writeable = True
    image = cv2.cvtColor(image, cv2.COLOR_RGB2BGR)
    
    if results.multi_hand_landmarks:
      for hand_landmarks in results.multi_hand_landmarks:
        mp_drawing.draw_landmarks(
            image,
            hand_landmarks,
            mp_hands.HAND_CONNECTIONS,
            mp_drawing_styles.get_default_hand_landmarks_style(),
            mp_drawing_styles.get_default_hand_connections_style())
        
        # Draw line between thumb tip (4) and index finger tip (8)
        thumb_tip = hand_landmarks.landmark[mp_hands.HandLandmark.THUMB_TIP]
        index_tip = hand_landmarks.landmark[mp_hands.HandLandmark.INDEX_FINGER_TIP]
        
        # Calculate distance and get color
        distance = calculate_distance(thumb_tip, index_tip)
        color = get_pinch_color(distance)
        
        # Convert normalized coordinates to pixel coordinates
        image_height, image_width, _ = image.shape
        thumb_pixel = (int(thumb_tip.x * image_width), int(thumb_tip.y * image_height))
        index_pixel = (int(index_tip.x * image_width), int(index_tip.y * image_height))
        
        # Draw the line
        cv2.line(image, thumb_pixel, index_pixel, color, 3)
        
        # Optional: Display distance on screen
        cv2.putText(image, f'Distance: {distance:.3f}', (10, 30), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.7, color, 2)
    
    # Flip the image horizontally for a selfie-view display.
    cv2.imshow('MediaPipe Hands', cv2.flip(image, 1))
    if cv2.waitKey(5) & 0xFF == 27:
      break

cap.release()
cv2.destroyAllWindows()