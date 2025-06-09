#!/usr/bin/env python3
import sys
import subprocess

# Auto-install dependencies if missing
required = ["mediapipe", "opencv-python"]
try:
    import cv2
    import mediapipe as mp
except ModuleNotFoundError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "--user"] + required)
    import cv2
    import mediapipe as mp

import argparse

parser = argparse.ArgumentParser(description="Hand tracking utility (headless mode)")
parser.add_argument("--headless", action="store_true", help="Run in headless mode for event streaming")
args = parser.parse_args()

if args.headless:
    cap = cv2.VideoCapture(0)
    with mp.solutions.hands.Hands(
        model_complexity=0,
        max_num_hands=1,
        min_detection_confidence=0.5,
        min_tracking_confidence=0.5
    ) as hands:
        while cap.isOpened():
            success, frame = cap.read()
            if not success:
                continue
            image = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            results = hands.process(image)
            if results.multi_hand_landmarks:
                lm = results.multi_hand_landmarks[0]
                thumb = lm.landmark[mp.solutions.hands.HandLandmark.THUMB_TIP]
                index = lm.landmark[mp.solutions.hands.HandLandmark.INDEX_FINGER_TIP]
                dist = ((thumb.x - index.x)**2 + (thumb.y - index.y)**2) ** 0.5
                print(f"{thumb.x} {thumb.y} {index.x} {index.y} {dist}", flush=True)
    cap.release()
    sys.exit(0)