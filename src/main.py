import cv2
import mediapipe as mp

# Simple Mediapipe hand tracker that prints landmark coords
def main():
    cap = cv2.VideoCapture(0)
    hands = mp.solutions.hands.Hands(
        model_complexity=0,
        min_detection_confidence=0.5,
        min_tracking_confidence=0.5)
    drawing = mp.solutions.drawing_utils
    styles = mp.solutions.drawing_styles
    connections = mp.solutions.hands.HAND_CONNECTIONS
    try:
        while True:
            success, frame = cap.read()
            if not success:
                continue
            frame = cv2.flip(frame, 1)
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            results = hands.process(rgb)
            landmarks = []
            if results.multi_hand_landmarks:
                lm0 = results.multi_hand_landmarks[0]
                drawing.draw_landmarks(
                    frame, lm0, connections,
                    styles.get_default_hand_landmarks_style(),
                    styles.get_default_hand_connections_style())
                h, w, _ = frame.shape
                for i, lm in enumerate(lm0.landmark):
                    x = lm.x * w
                    y = lm.y * h
                    print(f"LM {i} {x} {y}", flush=True)
                xt = lm0.landmark[mp.solutions.hands.HandLandmark.THUMB_TIP].x * w
                yt = lm0.landmark[mp.solutions.hands.HandLandmark.THUMB_TIP].y * h
                xi = lm0.landmark[mp.solutions.hands.HandLandmark.INDEX_FINGER_TIP].x * w
                yi = lm0.landmark[mp.solutions.hands.HandLandmark.INDEX_FINGER_TIP].y * h
                print(f"PINCH {xt} {yt} {xi} {yi}", flush=True)
            # Optional: show camera window
            cv2.imshow('MediaPipe Hands', frame)
            if cv2.waitKey(1) & 0xFF == 27:
                break
    finally:
        hands.close()
        cap.release()
        cv2.destroyAllWindows()

if __name__ == '__main__':
    main()