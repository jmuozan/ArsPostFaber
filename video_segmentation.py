import os
import numpy as np
import cv2
import torch
import matplotlib.pyplot as plt
from PIL import Image
import logging
from matplotlib.widgets import Button
import shutil
from hydra import compose
from hydra.utils import instantiate
from omegaconf import OmegaConf
import sam2
import gc
import traceback
import time
import json
from types import MethodType

# Environment settings
os.environ['PYTORCH_ENABLE_MPS_FALLBACK'] = '1'

# Device selection
if torch.cuda.is_available():
    device = torch.device("cuda")
elif torch.backends.mps.is_available():
    device = torch.device("mps")
else:
    device = torch.device("cpu")

print(f"Using device: {device}")

# Memory optimization settings
torch.set_grad_enabled(False)  # Disable gradient tracking completely

if device.type == "cuda":
    # Lower precision for efficiency while maintaining accuracy
    torch.autocast("cuda", dtype=torch.bfloat16).__enter__()
    # Enable TF32 for Ampere GPUs
    if torch.cuda.get_device_properties(0).major >= 8:
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
    # Set more aggressive GPU memory management
    torch.cuda.empty_cache()
    # Set lower GPU memory fraction if experiencing OOM errors
    os.environ['PYTORCH_CUDA_ALLOC_CONF'] = 'max_split_size_mb:128'

def build_sam2_video_predictor(
    config_file,
    ckpt_path=None,
    device="mps",
    mode="eval",
    hydra_overrides_extra=[],
    apply_postprocessing=True,
    **kwargs,
):
    # Use standard SAM2VideoPredictor without VOS optimizations
    hydra_overrides = [
        "++model._target_=sam2.sam2_video_predictor.SAM2VideoPredictor",
    ]

    if apply_postprocessing:
        hydra_overrides_extra = hydra_overrides_extra.copy()
        hydra_overrides_extra += [
            # Use only supported parameters
            "++model.sam_mask_decoder_extra_args.dynamic_multimask_via_stability=true",
            "++model.sam_mask_decoder_extra_args.dynamic_multimask_stability_delta=0.03",
            "++model.sam_mask_decoder_extra_args.dynamic_multimask_stability_thresh=0.95",
            "++model.binarize_mask_from_pts_for_mem_enc=true",
            "++model.fill_hole_area=12",
        ]
    hydra_overrides.extend(hydra_overrides_extra)

    # Read config directly from file instead of using Hydra search paths
    if os.path.isfile(config_file):
        cfg = OmegaConf.load(config_file)
        # Add overrides manually
        for override in hydra_overrides:
            if override.startswith("++"):
                key_path = override[2:].split("=")[0]
                value = override.split("=")[1]
                nested_keys = key_path.split(".")
                
                # Navigate to the correct position in the config
                current = cfg
                for i, key in enumerate(nested_keys[:-1]):
                    if key not in current:
                        current[key] = {}
                    current = current[key]
                
                # Set the value
                current[nested_keys[-1]] = value
    else:
        # Fallback to Hydra compose if file doesn't exist
        try:
            cfg = compose(config_name=config_file, overrides=hydra_overrides)
        except Exception as e:
            print(f"Error using Hydra compose: {e}")
            raise ValueError(f"Config file not found: {config_file}")
    
    OmegaConf.resolve(cfg)
    model = instantiate(cfg.model, _recursive_=True)
    _load_checkpoint(model, ckpt_path)
    model = model.to(device)
    if mode == "eval":
        model.eval()
    return model

def _load_checkpoint(model, ckpt_path):
    if ckpt_path is not None:
        map_location = torch.device('cpu')
        print(f"Loading checkpoint from: {ckpt_path}")
        
        try:
            sd = torch.load(ckpt_path, map_location=map_location, weights_only=True)["model"]
            print("Loaded checkpoint, applying to model...")
            
            missing_keys, unexpected_keys = model.load_state_dict(sd)
            
            del sd
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            gc.collect()
            
            if missing_keys:
                logging.error(missing_keys)
                raise RuntimeError()
            if unexpected_keys:
                logging.error(unexpected_keys)
                raise RuntimeError()
            logging.info("Loaded checkpoint successfully")
            
        except RuntimeError as e:
            print(f"Memory error loading checkpoint: {e}")
            print("Try running on a machine with more memory or using a smaller model.")
            raise

def show_mask(mask, ax, obj_id=None, random_color=False, alpha=0.6):
    if random_color:
        color = np.concatenate([np.random.random(3), np.array([alpha])], axis=0)
    else:
        cmap = plt.get_cmap("tab10")
        cmap_idx = 0 if obj_id is None else obj_id
        color = np.array([*cmap(cmap_idx)[:3], alpha])
    h, w = mask.shape[-2:]
    mask_image = mask.reshape(h, w, 1) * color.reshape(1, 1, -1)
    ax.imshow(mask_image)

def show_points(coords, labels, ax, marker_size=200):
    pos_points = coords[labels==1]
    neg_points = coords[labels==0]
    if len(pos_points) > 0:
        ax.scatter(pos_points[:, 0], pos_points[:, 1], color='green', marker='*', s=marker_size, edgecolor='white', linewidth=1.25)
    if len(neg_points) > 0:
        ax.scatter(neg_points[:, 0], neg_points[:, 1], color='red', marker='*', s=marker_size, edgecolor='white', linewidth=1.25)

# Define the to() method for SAM2VideoPredictor instances
def _predictor_to(self, target_device):
    """Move the model to the target device without modifying the device property"""
    original_device = self.device
    
    try:
        print(f"Moving model from {original_device} to {target_device}...")
        # Instead of changing the device property, we directly move the model
        self.model = self.model.to(target_device)
        print(f"Model successfully moved to {target_device}")
        return True
    except Exception as e:
        print(f"Error moving model to {target_device}: {e}")
        return False

def auto_keypoints(video_path, num_keypoints=5):
    """Automatically generate keypoints for segmentation based on frame differences
    
    This is a simple approach that selects frames with high motion/changes and
    places points in the center or regions of change.
    """
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"Could not open video file: {video_path}")
        return None
        
    frame_count = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    
    # Calculate frame step for evenly distributed keypoints
    frame_step = max(1, frame_count // (num_keypoints + 1))
    
    keypoints_dict = {}
    prev_frame = None
    
    # Simple center point generator for the first frame
    ret, first_frame = cap.read()
    if ret:
        h, w = first_frame.shape[:2]
        center_x, center_y = w//2, h//2
        
        # Create a grid of 5 positive points (center and four quadrants)
        points = [
            (center_x, center_y, 1),  # Center
            (center_x//2, center_y//2, 1),  # Top-left
            (center_x + center_x//2, center_y//2, 1),  # Top-right
            (center_x//2, center_y + center_y//2, 1),  # Bottom-left
            (center_x + center_x//2, center_y + center_y//2, 1)  # Bottom-right
        ]
        
        # Add negative points around the edges
        edge_points = [
            (20, 20, 0),  # Top-left corner
            (w-20, 20, 0),  # Top-right corner
            (20, h-20, 0),  # Bottom-left corner
            (w-20, h-20, 0)  # Bottom-right corner
        ]
        points.extend(edge_points)
        
        keypoints_dict[0] = points
        prev_frame = first_frame
    
    # Process remaining frames at regular intervals
    for i in range(1, num_keypoints):
        frame_idx = i * frame_step
        
        # Set position to desired frame
        cap.set(cv2.CAP_PROP_POS_FRAMES, frame_idx)
        ret, frame = cap.read()
        
        if not ret:
            break
            
        # Find regions of change if we have a previous frame
        if prev_frame is not None:
            # Convert to grayscale
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            prev_gray = cv2.cvtColor(prev_frame, cv2.COLOR_BGR2GRAY)
            
            # Calculate frame difference
            diff = cv2.absdiff(gray, prev_gray)
            
            # Threshold to find significant changes
            _, thresh = cv2.threshold(diff, 25, 255, cv2.THRESH_BINARY)
            
            # Find contours in the thresholded image
            contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            
            # Sort contours by area (largest first)
            contours = sorted(contours, key=cv2.contourArea, reverse=True)
            
            points = []
            
            # Add positive points at centers of largest contours
            for j in range(min(5, len(contours))):
                if cv2.contourArea(contours[j]) > 100:  # Minimum area threshold
                    M = cv2.moments(contours[j])
                    if M["m00"] != 0:
                        cx = int(M["m10"] / M["m00"])
                        cy = int(M["m01"] / M["m00"])
                        points.append((cx, cy, 1))  # Positive point
            
            # If no significant contours, use a grid of points
            if not points:
                h, w = frame.shape[:2]
                center_x, center_y = w//2, h//2
                
                points = [
                    (center_x, center_y, 1),  # Center
                    (center_x//2, center_y//2, 1),  # Top-left
                    (center_x + center_x//2, center_y//2, 1),  # Top-right
                    (center_x//2, center_y + center_y//2, 1),  # Bottom-left
                    (center_x + center_x//2, center_y + center_y//2, 1)  # Bottom-right
                ]
            
            # Add some negative points around the edges
            h, w = frame.shape[:2]
            edge_points = [
                (20, 20, 0),  # Top-left corner
                (w-20, 20, 0),  # Top-right corner
                (20, h-20, 0),  # Bottom-left corner
                (w-20, h-20, 0)  # Bottom-right corner
            ]
            points.extend(edge_points)
            
            keypoints_dict[frame_idx] = points
        
        prev_frame = frame
    
    cap.release()
    print(f"Generated keypoints for {len(keypoints_dict)} frames")
    return keypoints_dict

class VideoProcessor:
    def __init__(self, video_path, output_dir=None, keyframe_interval=10, 
                 propagation_steps=2, checkpoint_path='checkpoints/sam2.1_hiera_tiny.pt', 
                 config_path='checkpoints/sam2.1_hiera_t.yaml'):
        """Initialize the video processor with the specified checkpoints"""
        self.video_path = video_path
        
        # Set up output directory
        if output_dir is None:
            video_name = os.path.splitext(os.path.basename(video_path))[0]
            self.output_dir = f"{video_name}_segmentation"
        else:
            self.output_dir = output_dir
            
        self.mask_dir = os.path.join(self.output_dir, "masks")
        self.overlay_dir = os.path.join(self.output_dir, "overlays")
        
        # Create output directories
        os.makedirs(self.output_dir, exist_ok=True)
        os.makedirs(self.mask_dir, exist_ok=True)
        os.makedirs(self.overlay_dir, exist_ok=True)
        
        # Video processing parameters
        self.keyframe_interval = keyframe_interval
        self.propagation_steps = propagation_steps
        
        # Checkpoint paths
        self.checkpoint_path = checkpoint_path
        self.config_path = config_path
        
        # Initialize model
        print(f"Loading SAM2 model from {checkpoint_path} and {config_path}...")
        self.initialize_model()
        
        # Initialize video capture
        self.cap = cv2.VideoCapture(video_path)
        if not self.cap.isOpened():
            raise ValueError(f"Could not open video file: {video_path}")
            
        self.frame_count = int(self.cap.get(cv2.CAP_PROP_FRAME_COUNT))
        self.fps = self.cap.get(cv2.CAP_PROP_FPS)
        self.width = int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        self.height = int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        
        print(f"Video loaded: {self.frame_count} frames, {self.fps} FPS, {self.width}x{self.height}")
        
        # Processing state
        self.current_obj_id = 1
        self.scene_annotations = {}
        self.video_segments = {}

    def initialize_model(self):
        """Initialize the SAM2 model with specified checkpoints"""
        try:
            # Build model directly with given checkpoint and config
            self.predictor = build_sam2_video_predictor(
                config_file=self.config_path,
                ckpt_path=self.checkpoint_path,
                device=device
            )
            
            # Add the to() method to the predictor instance
            self.predictor.to = MethodType(_predictor_to, self.predictor)
            
            print("Model loaded successfully")
        except Exception as e:
            print(f"Error loading model: {e}")
            
            # Try CPU as fallback
            if device.type != "cpu":
                print("Trying to load model on CPU instead...")
                try:
                    self.predictor = build_sam2_video_predictor(
                        config_file=self.config_path,
                        ckpt_path=self.checkpoint_path,
                        device="cpu"
                    )
                    # Add the to() method
                    self.predictor.to = MethodType(_predictor_to, self.predictor)
                    print("Model loaded successfully on CPU")
                except Exception as cpu_e:
                    print(f"Error loading model on CPU as well: {cpu_e}")
                    raise
            else:
                raise

    def extract_frames_to_temp(self, temp_dir=None):
        """Extract frames from video to a temporary directory for SAM2 processing"""
        if temp_dir is None:
            temp_dir = os.path.join(self.output_dir, "temp_frames")
        
        os.makedirs(temp_dir, exist_ok=True)
        print(f"Extracting frames to temporary directory: {temp_dir}")
        
        # Reset video capture to beginning
        self.cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
        
        frame_idx = 0
        while True:
            ret, frame = self.cap.read()
            if not ret:
                break
                
            # Save frame as JPEG
            frame_path = os.path.join(temp_dir, f"{frame_idx:06d}.jpg")
            cv2.imwrite(frame_path, frame)
            
            # Print progress
            if frame_idx % 100 == 0:
                print(f"Extracted {frame_idx}/{self.frame_count} frames")
                
            frame_idx += 1
            
        print(f"Extracted {frame_idx} frames to {temp_dir}")
        return temp_dir

    def initialize_state(self, temp_frames_dir):
        """Initialize model state with the extracted frames"""
        print("Initializing model state with video frames...")
        self.inference_state = self.predictor.init_state(video_path=temp_frames_dir)
        self.predictor.reset_state(self.inference_state)
        print("Model state initialized successfully")
        return self.inference_state

    def add_points(self, frame_idx, points, labels):
        """Add annotation points to a specific frame"""
        if not points or not labels:
            print(f"No points provided for frame {frame_idx}")
            return None
            
        # Convert input points from list of (x, y, label) tuples to separate arrays
        if len(points) > 0 and len(points[0]) == 3:
            # Points format is [(x1, y1, label1), (x2, y2, label2), ...]
            points_array = np.array([[p[0], p[1]] for p in points], dtype=np.float32)
            labels_array = np.array([p[2] for p in points], dtype=np.int32)
        else:
            # Standard format with separate points and labels
            points_array = np.array(points, dtype=np.float32)
            labels_array = np.array(labels, dtype=np.int32)
        
        print(f"Adding {len(points_array)} points to frame {frame_idx}")
        
        with torch.no_grad():
            try:
                _, out_obj_ids, out_mask_logits = self.predictor.add_new_points_or_box(
                    inference_state=self.inference_state,
                    frame_idx=frame_idx,
                    obj_id=self.current_obj_id,
                    points=points_array,
                    labels=labels_array,
                )
                
                # Store the result
                mask = (out_mask_logits[0] > 0.0).cpu().numpy()
                
                if frame_idx not in self.video_segments:
                    self.video_segments[frame_idx] = {}
                    
                self.video_segments[frame_idx][self.current_obj_id] = mask
                return mask
            except Exception as e:
                print(f"Error adding points to frame {frame_idx}: {e}")
                import traceback
                traceback.print_exc()
                return None

    def propagate_in_video(self, start_idx, end_idx):
        """Propagate segmentation through video frames using keyframes"""
        print(f"Propagating segmentation from frame {start_idx} to {end_idx}")
        
        # Create keyframes at regular intervals
        keyframes = list(range(start_idx, end_idx, self.keyframe_interval))
        if end_idx - 1 not in keyframes:
            keyframes.append(end_idx - 1)
            
        print(f"Using {len(keyframes)} keyframes for propagation")
        
        for i in range(len(keyframes) - 1):
            kf_start = keyframes[i]
            kf_end = keyframes[i + 1] + 1  # +1 because ranges are exclusive at the end
            
            print(f"Processing segment from frame {kf_start} to {kf_end-1}")
            
            try:
                # Process multiple propagation passes for better results
                for pass_idx in range(self.propagation_steps):
                    with torch.no_grad():
                        for out_frame_idx, out_obj_ids, out_mask_logits in self.predictor.propagate_in_video(self.inference_state):
                            if kf_start <= out_frame_idx < kf_end:
                                # Save masks for this frame
                                for j, obj_id in enumerate(out_obj_ids):
                                    mask = (out_mask_logits[j] > 0.0).cpu().numpy()
                                    
                                    # Store the mask
                                    if out_frame_idx not in self.video_segments:
                                        self.video_segments[out_frame_idx] = {}
                                    self.video_segments[out_frame_idx][obj_id] = mask
                                
                                if out_frame_idx % 10 == 0:
                                    print(f"Processed frame {out_frame_idx} (pass {pass_idx+1}/{self.propagation_steps})")
                            
                            # Stop after kf_end frames
                            if out_frame_idx >= kf_end - 1:
                                break
                                
            except RuntimeError as e:
                # Handle memory errors
                if "out of memory" in str(e):
                    print(f"\nMemory error detected: {e}")
                    print("Creating a new predictor instance on CPU for this segment...")
                    
                    try:
                        # Create a new predictor on CPU
                        cpu_predictor = build_sam2_video_predictor(
                            config_file=self.config_path,
                            ckpt_path=self.checkpoint_path,
                            device="cpu"
                        )
                        
                        # Initialize new state with the frames
                        print("Initializing CPU predictor state...")
                        temp_frames_dir = self.inference_state.input_info.video_path
                        cpu_state = cpu_predictor.init_state(video_path=temp_frames_dir)
                        
                        # Find latest processed frame with a mask
                        latest_frame = None
                        for f in range(kf_start, -1, -1):
                            if f in self.video_segments and self.current_obj_id in self.video_segments[f]:
                                latest_frame = f
                                break
                        
                        # Re-apply points from the closest frame with annotations
                        if latest_frame is not None and latest_frame in self.scene_annotations:
                            points, labels = self.scene_annotations[latest_frame]
                            
                            if points and labels:
                                print(f"Re-applying {len(points)} points at frame {kf_start} from frame {latest_frame}")
                                points_array = np.array(points, dtype=np.float32)
                                labels_array = np.array(labels, dtype=np.int32)
                                
                                # Add points to CPU predictor
                                cpu_predictor.add_new_points_or_box(
                                    inference_state=cpu_state,
                                    frame_idx=kf_start,
                                    obj_id=self.current_obj_id,
                                    points=points_array,
                                    labels=labels_array,
                                )
                                
                                # Continue processing on CPU
                                for pass_idx in range(self.propagation_steps):
                                    for out_frame_idx, out_obj_ids, out_mask_logits in cpu_predictor.propagate_in_video(cpu_state):
                                        if kf_start <= out_frame_idx < kf_end:
                                            # Save masks
                                            for j, obj_id in enumerate(out_obj_ids):
                                                mask = (out_mask_logits[j] > 0.0).cpu().numpy()
                                                
                                                if out_frame_idx not in self.video_segments:
                                                    self.video_segments[out_frame_idx] = {}
                                                self.video_segments[out_frame_idx][obj_id] = mask
                                                
                                            if out_frame_idx % 5 == 0:
                                                print(f"Processed frame {out_frame_idx} on CPU (pass {pass_idx+1}/{self.propagation_steps})")
                                                
                                        if out_frame_idx >= kf_end - 1:
                                            break
                                            
                        # Clean up
                        del cpu_predictor
                        del cpu_state
                        gc.collect()
                        
                    except Exception as cpu_e:
                        print(f"Error in CPU processing: {cpu_e}")
                        traceback.print_exc()
                        
                        # Handle basic frame-by-frame interpolation
                        self._fallback_processing(kf_start, kf_end)
                else:
                    # Not a memory error
                    raise
    
    def _fallback_processing(self, start_idx, end_idx):
        """Basic fallback to interpolate frames when propagation fails"""
        print(f"Using fallback frame interpolation for frames {start_idx} to {end_idx-1}")
        
        # Find existing masks before and after this segment
        before_frames = [f for f in range(start_idx-1, -1, -1) 
                         if f in self.video_segments and self.current_obj_id in self.video_segments[f]]
        after_frames = [f for f in range(end_idx, self.frame_count) 
                        if f in self.video_segments and self.current_obj_id in self.video_segments[f]]
        
        before_frame = before_frames[0] if before_frames else None
        after_frame = after_frames[0] if after_frames else None
        
        # Process each frame in the gap
        for frame_idx in range(start_idx, end_idx):
            # If we already have a mask for this frame, skip it
            if frame_idx in self.video_segments and self.current_obj_id in self.video_segments[frame_idx]:
                continue
                
            # If we have masks before and after, linearly interpolate
            if before_frame is not None and after_frame is not None:
                before_mask = self.video_segments[before_frame][self.current_obj_id]
                after_mask = self.video_segments[after_frame][self.current_obj_id]
                
                # Linear interpolation weight
                alpha = (frame_idx - before_frame) / (after_frame - before_frame)
                
                # Interpolate (binary masks, so use a threshold)
                interp_mask = (alpha * after_mask + (1 - alpha) * before_mask) > 0.5
                
                # Store the interpolated mask
                if frame_idx not in self.video_segments:
                    self.video_segments[frame_idx] = {}
                self.video_segments[frame_idx][self.current_obj_id] = interp_mask
                
                if frame_idx % 10 == 0:
                    print(f"Created interpolated mask for frame {frame_idx}")
            
            # Otherwise use the closest mask
            elif before_frame is not None:
                mask = self.video_segments[before_frame][self.current_obj_id].copy()
                if frame_idx not in self.video_segments:
                    self.video_segments[frame_idx] = {}
                self.video_segments[frame_idx][self.current_obj_id] = mask
                
                if frame_idx % 10 == 0:
                    print(f"Using mask from frame {before_frame} for frame {frame_idx}")
            
            elif after_frame is not None:
                mask = self.video_segments[after_frame][self.current_obj_id].copy()
                if frame_idx not in self.video_segments:
                    self.video_segments[frame_idx] = {}
                self.video_segments[frame_idx][self.current_obj_id] = mask
                
                if frame_idx % 10 == 0:
                    print(f"Using mask from frame {after_frame} for frame {frame_idx}")
            
            else:
                print(f"Warning: No reference masks available for frame {frame_idx}")

    def save_results(self):
        """Save the segmentation results"""
        print("Saving segmentation results...")
        
        # Reset video capture to beginning
        self.cap.set(cv2.CAP_PROP_POS_FRAMES, 0)
        
        # Set up output video writer
        fourcc = cv2.VideoWriter_fourcc(*'mp4v')
        output_video_path = os.path.join(self.output_dir, "segmentation_overlay.mp4")
        out = cv2.VideoWriter(output_video_path, fourcc, self.fps, (self.width, self.height))
        
        # Create a video with masks
        mask_video_path = os.path.join(self.output_dir, "segmentation_masks.mp4")
        mask_out = cv2.VideoWriter(mask_video_path, fourcc, self.fps, (self.width, self.height))
        
        # Process each frame
        frame_idx = 0
        while True:
            ret, frame = self.cap.read()
            if not ret:
                break
                
            # Save original frame to disk
            frame_path = os.path.join(self.overlay_dir, f"frame_{frame_idx:06d}.jpg")
            cv2.imwrite(frame_path, frame)
            
            # Create overlay if we have a mask for this frame
            overlay = frame.copy()
            mask_frame = np.zeros((self.height, self.width, 3), dtype=np.uint8)
            
            if frame_idx in self.video_segments and self.current_obj_id in self.video_segments[frame_idx]:
                mask = self.video_segments[frame_idx][self.current_obj_id]
                
                # Ensure mask has correct dimensions
                if mask.shape[:2] != (self.height, self.width):
                    # Resize mask if needed
                    mask_h, mask_w = mask.shape[:2]
                    resized_mask = np.zeros((self.height, self.width), dtype=bool)
                    # Copy what fits
                    h = min(self.height, mask_h)
                    w = min(self.width, mask_w)
                    resized_mask[:h, :w] = mask[:h, :w]
                    mask = resized_mask
                
                # Save binary mask
                mask_path = os.path.join(self.mask_dir, f"mask_{frame_idx:06d}.png")
                mask_img = Image.fromarray((mask * 255).astype(np.uint8))
                mask_img.save(mask_path)
                
                # Apply overlay (green with transparency)
                overlay_color = np.array([0, 255, 0], dtype=np.uint8)  # Green
                alpha = 0.5  # Transparency
                
                # Create colored mask overlay
                for c in range(3):
                    overlay[:, :, c] = np.where(
                        mask, 
                        overlay[:, :, c] * (1 - alpha) + overlay_color[c] * alpha,
                        overlay[:, :, c]
                    )
                
                # Create mask frame for mask video (white mask on black background)
                mask_frame[mask] = [255, 255, 255]
            
            # Save overlay frame
            overlay_path = os.path.join(self.overlay_dir, f"overlay_{frame_idx:06d}.jpg")
            cv2.imwrite(overlay_path, overlay)
            
            # Add to output videos
            out.write(overlay)
            mask_out.write(mask_frame)
            
            # Print progress
            if frame_idx % 100 == 0:
                print(f"Saved {frame_idx}/{self.frame_count} frames")
                
            frame_idx += 1
        
        # Release resources
        out.release()
        mask_out.release()
        
        print(f"Results saved to {self.output_dir}")
        print(f"Output videos: {output_video_path} and {mask_video_path}")

    def process_video_with_keypoints(self, keypoints_dict):
        """Process the entire video using provided keypoints
        
        keypoints_dict: Dictionary mapping frame_idx -> [(x,y,label), ...] where 
                       label is 1 for positive, 0 for negative
        """
        print(f"Processing video with {len(keypoints_dict)} annotated keyframes")
        
        # Extract frames to temp directory
        temp_frames_dir = self.extract_frames_to_temp()
        
        # Initialize model state with frames
        self.initialize_state(temp_frames_dir)
        
        # Get all annotated frames in order
        annotated_frames = sorted(keypoints_dict.keys())
        if not annotated_frames:
            print("No annotated frames provided")
            return
        
        # Process first annotated frame
        first_frame = annotated_frames[0]
        keypoints = keypoints_dict[first_frame]
        
        # Split keypoints into points and labels
        points = []
        labels = []
        for x, y, label in keypoints:
            points.append([x, y])
            labels.append(label)
        
        # Store annotations for potential reuse
        self.scene_annotations[first_frame] = (points, labels)
        
        # Apply first frame's annotations
        mask = self.add_points(first_frame, points, labels)
        if mask is None:
            print("Failed to process first annotated frame")
            return
        
        # Process video in segments defined by annotated frames
        for i in range(len(annotated_frames)):
            current_frame = annotated_frames[i]
            
            # Define end of this segment
            next_frame = self.frame_count
            if i < len(annotated_frames) - 1:
                next_frame = annotated_frames[i + 1]
            
            # Skip if it's the same frame
            if current_frame == next_frame:
                continue
                
            print(f"Processing segment from frame {current_frame} to {next_frame-1}")
            
            # If not the first frame, apply its annotations
            if i > 0:
                keypoints = keypoints_dict[current_frame]
                points = []
                labels = []
                for x, y, label in keypoints:
                    points.append([x, y])
                    labels.append(label)
                
                # Store annotations
                self.scene_annotations[current_frame] = (points, labels)
                
                # Apply annotations
                mask = self.add_points(current_frame, points, labels)
                if mask is None:
                    print(f"Failed to process annotated frame {current_frame}")
                    # Try to continue with previous mask
                    continue
            
            # Propagate through this segment
            self.propagate_in_video(current_frame, next_frame)
            
            # Clean up memory
            gc.collect()
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
        
        # Save final results
        self.save_results()
        
        # Clean up temp directory
        print(f"Cleaning up temporary directory: {temp_frames_dir}")
        shutil.rmtree(temp_frames_dir)
        
        return True

def main():
    """Main function to run video processing with SAM2.1"""
    # Get input video path
    video_path = input("Enter the path to the input video file: ")
    if not os.path.exists(video_path):
        print(f"Error: Video file {video_path} does not exist")
        return
    
    # Define output directory
    video_name = os.path.splitext(os.path.basename(video_path))[0]
    output_dir = f"{video_name}_segmentation"
    
    # Define model checkpoint paths
    checkpoint_path = input("Enter the path to the checkpoint file (default: checkpoints/sam2.1_hiera_tiny.pt): ")
    if not checkpoint_path:
        checkpoint_path = "checkpoints/sam2.1_hiera_tiny.pt"
    
    config_path = input("Enter the path to the config file (default: checkpoints/sam2.1_hiera_t.yaml): ")
    if not config_path:
        config_path = "checkpoints/sam2.1_hiera_t.yaml"
    
    # Check if files exist
    if not os.path.exists(checkpoint_path):
        print(f"Error: Checkpoint file {checkpoint_path} does not exist")
        return
        
    if not os.path.exists(config_path):
        print(f"Error: Config file {config_path} does not exist")
        return
    
    # Ask about keyframe interval and propagation steps
    keyframe_interval = 5
    try:
        keyframe_input = input("Enter keyframe interval (how often to re-apply annotations, default: 5): ")
        if keyframe_input.strip():
            keyframe_interval = int(keyframe_input)
    except ValueError:
        print("Invalid value, using default of 5")
    
    propagation_steps = 2
    try:
        propagation_input = input("Enter propagation refinement steps (higher values improve quality but are slower, default: 2): ")
        if propagation_input.strip():
            propagation_steps = int(propagation_input)
    except ValueError:
        print("Invalid value, using default of 2")
    
    # Initialize the video processor
    processor = VideoProcessor(
        video_path=video_path,
        output_dir=output_dir,
        keyframe_interval=keyframe_interval,
        propagation_steps=propagation_steps,
        checkpoint_path=checkpoint_path,
        config_path=config_path
    )
    
    # Ask about automatic keypoints generation
    use_auto = input("Generate automatic keypoints? (y/n, default: y): ").lower().strip() != 'n'
    
    if use_auto:
        num_keypoints = 10
        try:
            keypoints_input = input("Enter number of keyframes to generate (default: 10): ")
            if keypoints_input.strip():
                num_keypoints = int(keypoints_input)
        except ValueError:
            print("Invalid value, using default of 10")
        
        # Generate automatic keypoints
        keypoints_dict = auto_keypoints(video_path, num_keypoints)
    else:
        # Manual keypoints would be implemented here
        print("Manual keypoint selection not implemented in this script.")
        print("Using default automatic keypoints instead.")
        keypoints_dict = auto_keypoints(video_path, 10)
    
    # Process the video
    if keypoints_dict:
        print("\nStarting video processing...")
        processor.process_video_with_keypoints(keypoints_dict)
        print("\nVideo processing complete!")
        print(f"Results saved to {output_dir}")
    else:
        print("No keypoints available. Cannot process video.")

if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        print(f"Error during processing: {e}")
        traceback.print_exc()