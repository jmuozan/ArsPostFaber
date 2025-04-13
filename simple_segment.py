import os
import numpy as np
import torch
import cv2
import matplotlib.pyplot as plt
from segment_anything_hq import SamPredictor, sam_model_registry
from tqdm import tqdm

# Set device
if torch.cuda.is_available():
    device = torch.device("cuda")
elif torch.backends.mps.is_available():
    device = torch.device("mps")
else:
    device = torch.device("cpu")

print(f"Using device: {device}")

# Parameters
video_path = "ceramics.mp4"
output_dir = "ceramics_segmentation"
keyframe_interval = 5
model_type = "vit_b"  # Options: vit_b, vit_l, vit_h
checkpoint = "sam_hq_vit_b.pth"

# Create output directories
os.makedirs(output_dir, exist_ok=True)
os.makedirs(os.path.join(output_dir, "masks"), exist_ok=True)
os.makedirs(os.path.join(output_dir, "overlays"), exist_ok=True)
os.makedirs(os.path.join(output_dir, "temp_frames"), exist_ok=True)

# Check for the checkpoint file
checkpoint_path = checkpoint
if not os.path.exists(checkpoint_path):
    print(f"Downloading {model_type} checkpoint...")
    if model_type == "vit_b":
        # Download the base model
        os.system("wget https://huggingface.co/lkeab/hq-sam/resolve/main/sam_hq_vit_b.pth")
    elif model_type == "vit_l":
        os.system("wget https://huggingface.co/lkeab/hq-sam/resolve/main/sam_hq_vit_l.pth")
    elif model_type == "vit_h":
        os.system("wget https://huggingface.co/lkeab/hq-sam/resolve/main/sam_hq_vit_h.pth")
    else:
        raise ValueError(f"Unknown model type: {model_type}")

# Extract frames from the video
def extract_frames(video_path, output_dir):
    temp_frames_dir = os.path.join(output_dir, "temp_frames")
    cap = cv2.VideoCapture(video_path)
    
    if not cap.isOpened():
        raise ValueError(f"Could not open video file: {video_path}")
    
    frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    fps = cap.get(cv2.CAP_PROP_FPS)
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    
    print(f"Video loaded: {frame_count} frames, {fps} FPS, {width}x{height}")
    
    frame_idx = 0
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        
        # Save frame as JPEG
        frame_path = os.path.join(temp_frames_dir, f"{frame_idx:06d}.jpg")
        cv2.imwrite(frame_path, frame)
        
        # Print progress
        if frame_idx % 100 == 0:
            print(f"Extracted {frame_idx}/{frame_count} frames")
        
        frame_idx += 1
    
    cap.release()
    print(f"Extracted {frame_idx} frames to {temp_frames_dir}")
    return temp_frames_dir, fps, width, height

# Initialize the SAM model
def initialize_model(model_type, checkpoint_path, device):
    print(f"Loading model {model_type} from {checkpoint_path}...")
    # Create the model first
    sam = sam_model_registry[model_type]()
    # Then load the checkpoint with the right map_location
    sam_checkpoint = torch.load(checkpoint_path, map_location=device)
    sam.load_state_dict(sam_checkpoint)
    sam.to(device=device)
    predictor = SamPredictor(sam)
    print("Model loaded successfully")
    return predictor

# Generate automatic keypoints for selected frames
def generate_keypoints(frames_dir, num_keypoints=5, keyframe_interval=None):
    frame_files = [f for f in os.listdir(frames_dir) if f.endswith('.jpg')]
    frame_files.sort(key=lambda x: int(os.path.splitext(x)[0]))
    
    total_frames = len(frame_files)
    
    if keyframe_interval:
        # Use regular intervals
        keyframes = list(range(0, total_frames, keyframe_interval))
    else:
        # Or distribute evenly
        step = max(1, total_frames // num_keypoints)
        keyframes = list(range(0, total_frames, step))
    
    # Always include the first and last frame
    if 0 not in keyframes:
        keyframes.insert(0, 0)
    if total_frames - 1 not in keyframes:
        keyframes.append(total_frames - 1)
    
    keyframes = sorted(set(keyframes))
    keypoints = {}
    
    print(f"Selected {len(keyframes)} keyframes at indices: {keyframes}")
    
    # For each keyframe, generate points in the center area
    for idx in keyframes:
        if idx >= total_frames:
            continue
            
        frame_path = os.path.join(frames_dir, frame_files[idx])
        frame = cv2.imread(frame_path)
        h, w = frame.shape[:2]
        
        # Get the center of the frame
        center_x, center_y = w // 2, h // 2
        
        # Create a grid of 5 positive points (center and four quadrants)
        points = [
            # Positive points (on the object)
            [center_x, center_y, 1],  # Center point
            [center_x - w//8, center_y, 1],  # Left
            [center_x + w//8, center_y, 1],  # Right
            [center_x, center_y - h//8, 1],  # Top
            [center_x, center_y + h//8, 1],  # Bottom
            
            # Negative points (background)
            [50, 50, 0],  # Top-left corner
            [w-50, 50, 0],  # Top-right corner
            [50, h-50, 0],  # Bottom-left corner
            [w-50, h-50, 0]  # Bottom-right corner
        ]
        
        keypoints[idx] = points
    
    return keypoints

# Process a single frame with SAM
def process_frame(predictor, frame, points):
    # Separate points and labels
    if not points:
        return None
        
    coords = np.array([[p[0], p[1]] for p in points], dtype=np.float32)
    labels = np.array([p[2] for p in points], dtype=np.int32)
    
    # Set image
    predictor.set_image(frame)
    
    # Generate masks using points
    masks, scores, logits = predictor.predict(
        point_coords=coords,
        point_labels=labels,
        multimask_output=True
    )
    
    # Return the highest-scoring mask
    return masks[np.argmax(scores)]

# Process a video and save results
def process_video(video_path, output_dir, predictor, keyframe_interval=5):
    # Extract frames
    frames_dir, fps, width, height = extract_frames(video_path, output_dir)
    
    # Generate keypoints
    keypoints = generate_keypoints(frames_dir, keyframe_interval=keyframe_interval)
    
    # Sort frames
    frame_files = [f for f in os.listdir(frames_dir) if f.endswith('.jpg')]
    frame_files.sort(key=lambda x: int(os.path.splitext(x)[0]))
    
    # Dictionary to store masks
    masks = {}
    
    # Process keyframes first
    print("Processing keyframes...")
    for idx in tqdm(keypoints.keys()):
        frame_path = os.path.join(frames_dir, frame_files[idx])
        frame = cv2.imread(frame_path)
        
        # Process with SAM
        mask = process_frame(predictor, frame, keypoints[idx])
        
        # Store the mask
        masks[idx] = mask
        
        # Save the mask
        mask_filename = f"mask_{idx:06d}.png"
        mask_path = os.path.join(output_dir, "masks", mask_filename)
        cv2.imwrite(mask_path, mask.astype(np.uint8) * 255)
        
        # Create overlay
        overlay = frame.copy()
        overlay[mask] = overlay[mask] * 0.5 + np.array([0, 255, 0], dtype=np.uint8) * 0.5
        
        # Save overlay
        overlay_filename = f"overlay_{idx:06d}.jpg"
        overlay_path = os.path.join(output_dir, "overlays", overlay_filename)
        cv2.imwrite(overlay_path, overlay)
    
    # Interpolate masks for non-keyframes
    print("Interpolating masks for all frames...")
    for i in tqdm(range(len(frame_files))):
        if i in masks:
            continue  # Skip keyframes that already have masks
            
        # Find nearest keyframes
        prev_kf = max([k for k in keypoints.keys() if k < i], default=None)
        next_kf = min([k for k in keypoints.keys() if k > i], default=None)
        
        if prev_kf is None and next_kf is None:
            print(f"Error: No keyframes available for frame {i}")
            continue
            
        if prev_kf is None:
            # Use next keyframe mask if no previous
            mask = masks[next_kf]
        elif next_kf is None:
            # Use previous keyframe mask if no next
            mask = masks[prev_kf]
        else:
            # Linear interpolation between two masks
            alpha = (i - prev_kf) / (next_kf - prev_kf)
            mask_prev = masks[prev_kf].astype(np.float32)
            mask_next = masks[next_kf].astype(np.float32)
            mask = (alpha * mask_next + (1 - alpha) * mask_prev) > 0.5
        
        # Store the mask
        masks[i] = mask
        
        # Save mask
        mask_filename = f"mask_{i:06d}.png"
        mask_path = os.path.join(output_dir, "masks", mask_filename)
        cv2.imwrite(mask_path, mask.astype(np.uint8) * 255)
        
        # Load frame and create overlay
        frame_path = os.path.join(frames_dir, frame_files[i])
        frame = cv2.imread(frame_path)
        overlay = frame.copy()
        overlay[mask] = overlay[mask] * 0.5 + np.array([0, 255, 0], dtype=np.uint8) * 0.5
        
        # Save overlay
        overlay_filename = f"overlay_{i:06d}.jpg"
        overlay_path = os.path.join(output_dir, "overlays", overlay_filename)
        cv2.imwrite(overlay_path, overlay)
    
    # Create output video
    create_output_video(frames_dir, output_dir, masks, fps, width, height)

# Create output video with masks
def create_output_video(frames_dir, output_dir, masks, fps, width, height):
    fourcc = cv2.VideoWriter_fourcc(*'mp4v')
    output_video_path = os.path.join(output_dir, "segmentation_overlay.mp4")
    mask_video_path = os.path.join(output_dir, "segmentation_masks.mp4")
    
    # Create video writers
    out = cv2.VideoWriter(output_video_path, fourcc, fps, (width, height))
    mask_out = cv2.VideoWriter(mask_video_path, fourcc, fps, (width, height))
    
    # Get all frames
    frame_files = [f for f in os.listdir(frames_dir) if f.endswith('.jpg')]
    frame_files.sort(key=lambda x: int(os.path.splitext(x)[0]))
    
    print("Creating output videos...")
    for i in tqdm(range(len(frame_files))):
        frame_path = os.path.join(frames_dir, frame_files[i])
        frame = cv2.imread(frame_path)
        
        # Create mask frame
        mask_frame = np.zeros((height, width, 3), dtype=np.uint8)
        if i in masks:
            mask = masks[i]
            mask_frame[mask] = [255, 255, 255]
            
            # Create overlay
            overlay = frame.copy()
            overlay[mask] = overlay[mask] * 0.5 + np.array([0, 255, 0], dtype=np.uint8) * 0.5
        else:
            overlay = frame
        
        # Write frames
        out.write(overlay)
        mask_out.write(mask_frame)
    
    # Release video writers
    out.release()
    mask_out.release()
    print(f"Videos saved to {output_video_path} and {mask_video_path}")

if __name__ == "__main__":
    # Start processing
    print(f"Processing video: {video_path}")
    
    # Initialize model
    predictor = initialize_model(model_type, checkpoint_path, device)
    
    # Process video
    process_video(video_path, output_dir, predictor, keyframe_interval)
    
    print("Processing complete! Results saved to:", output_dir)