import cv2
import mediapipe as mp
import json
import sys

mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles
mp_hands = mp.solutions.hands

def process_image(image_path, output_path):
    image = cv2.imread(image_path)
    if image is None:
        print(f"Error: Unable to read image at {image_path}", file=sys.stderr)
        sys.exit(1)

    image_height, image_width, _ = image.shape
    results = hands.process(cv2.cvtColor(image, cv2.COLOR_BGR2RGB))

    if results.multi_hand_landmarks:
        landmarks_data = []
        for hand_landmarks in results.multi_hand_landmarks:
            hand_data = []
            for landmark in hand_landmarks.landmark:
                hand_data.append({
                    "x": landmark.x,
                    "y": landmark.y,
                    "z": landmark.z
                })
            landmarks_data.append(hand_data)

        with open(output_path, 'w') as f:
            json.dump(landmarks_data, f)
    else:
        with open(output_path, 'w') as f:
            json.dump([], f)

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python handtrack.py <input_image_path> <output_json_path>", file=sys.stderr)
        sys.exit(1)

    input_image_path = sys.argv[1]
    output_json_path = sys.argv[2]

    with mp_hands.Hands(
        static_image_mode=True,
        max_num_hands=2,
        min_detection_confidence=0.5
    ) as hands:
        process_image(input_image_path, output_json_path)