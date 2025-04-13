import os
import numpy as np
import os
os.environ['PYTORCH_ENABLE_MPS_FALLBACK'] = '1'
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
import time  # For timing operations
import json  # For saving/loading scene data
from types import MethodType



# Device selection
if torch.cuda.is_available():
    device = torch.device("cuda")
elif torch.backends.mps.is_available():
    device = torch.device("mps")
    # We'll handle MPS memory settings later based on user input
else:
    device = torch.device("cpu")

device = torch.device("mps")
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

# HF model mapping
HF_MODEL_ID_TO_FILENAMES = {
    "facebook/sam2-hiera-tiny": (
        "configs/sam2/sam2_hiera_t.yaml",
        "sam2_hiera_tiny.pt",
    ),
    "facebook/sam2-hiera-small": (
        "configs/sam2/sam2_hiera_s.yaml",
        "sam2_hiera_small.pt",
    ),
    "facebook/sam2-hiera-base-plus": (
        "configs/sam2/sam2_hiera_b+.yaml",
        "sam2_hiera_base_plus.pt",
    ),
    "facebook/sam2-hiera-large": (
        "configs/sam2/sam2_hiera_l.yaml",
        "sam2_hiera_large.pt",
    ),
    "facebook/sam2.1-hiera-tiny": (
        "configs/sam2.1/sam2.1_hiera_t.yaml",
        "sam2.1_hiera_tiny.pt",
    ),
    "facebook/sam2.1-hiera-small": (
        "configs/sam2.1/sam2.1_hiera_s.yaml",
        "sam2.1_hiera_small.pt",
    ),
    "facebook/sam2.1-hiera-base-plus": (
        "configs/sam2.1/sam2.1_hiera_b+.yaml",
        "sam2.1_hiera_base_plus.pt",
    ),
    "facebook/sam2.1-hiera-large": (
        "configs/sam2.1/sam2.1_hiera_l.yaml",
        "sam2.1_hiera_large.pt",
    ),
}

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
    # to maintain compatibility with your version
    hydra_overrides = [
        "++model._target_=sam2.sam2_video_predictor.SAM2VideoPredictor",
    ]

    if apply_postprocessing:
        hydra_overrides_extra = hydra_overrides_extra.copy()
        hydra_overrides_extra += [
            # Use only supported parameters for your version
            "++model.sam_mask_decoder_extra_args.dynamic_multimask_via_stability=true",
            "++model.sam_mask_decoder_extra_args.dynamic_multimask_stability_delta=0.03",
            "++model.sam_mask_decoder_extra_args.dynamic_multimask_stability_thresh=0.95",
            "++model.binarize_mask_from_pts_for_mem_enc=true",
            "++model.fill_hole_area=12",
            # Remove unsupported parameters
            # "++model.memory_encoder_extra_args.frequency_factor=1.0",
            # "++model.memory_encoder_extra_args.num_scale_levels=16",
        ]
    hydra_overrides.extend(hydra_overrides_extra)

    # Read config and init model
    cfg = compose(config_name=config_file, overrides=hydra_overrides)
    OmegaConf.resolve(cfg)
    model = instantiate(cfg.model, _recursive_=True)
    _load_checkpoint(model, ckpt_path)
    model = model.to(device)
    if mode == "eval":
        model.eval()
    return model

def _hf_download(model_id):
    from huggingface_hub import hf_hub_download

    config_name, checkpoint_name = HF_MODEL_ID_TO_FILENAMES[model_id]
    ckpt_path = hf_hub_download(repo_id=model_id, filename=checkpoint_name)
    return config_name, ckpt_path

def build_sam2_video_predictor_hf(model_id, **kwargs):
    config_name, ckpt_path = _hf_download(model_id)
    return build_sam2_video_predictor(
        config_file=config_name, ckpt_path=ckpt_path, **kwargs
    )

def _load_checkpoint(model, ckpt_path):
    if ckpt_path is not None:
        map_location = torch.device('cpu')
        print("Loading checkpoint from disk...")
        
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

def show_box(box, ax):
    x0, y0 = box[0], box[1]
    w, h = box[2] - box[0], box[3] - box[1]
    ax.add_patch(plt.Rectangle((x0, y0), w, h, edgecolor='green', facecolor=(0, 0, 0, 0), lw=2))

# Define the to() method for SAM2VideoPredictor instances
# Modified _predictor_to function that works with read-only device properties
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

# Modified _propagate_chunk function that handles device switching differently
def _propagate_chunk(self, start_idx, end_idx):
    """Propagate within a chunk using keyframes with automatic fallback to CPU on memory errors"""
    # Calculate keyframes within this chunk
    if start_idx >= end_idx - 1:
        return  # Nothing to propagate
        
    # Create keyframes at regular intervals
    keyframes = list(range(start_idx, end_idx, self.keyframe_interval))
    if keyframes[-1] != end_idx - 1:
        keyframes.append(end_idx - 1)
        
    print(f"Propagating with {len(keyframes)} keyframes in chunk")
    
    for i in range(len(keyframes) - 1):
        kf_start = keyframes[i]
        kf_end = keyframes[i + 1] + 1  # +1 because ranges are exclusive at the end
        
        print(f"Processing keyframe segment {kf_start} to {kf_end-1}")
        
        # Try with current device first (MPS/CUDA)
        try:
            # For each keyframe, do multiple propagation passes
            for pass_idx in range(self.propagation_steps):
                with torch.no_grad():
                    for out_frame_idx, out_obj_ids, out_mask_logits in self.predictor.propagate_in_video(self.inference_state):
                        if kf_start <= out_frame_idx < kf_end:
                            # Save masks for this frame
                            current_frame_data = {
                                out_obj_id: (out_mask_logits[i] > 0.0).cpu().numpy()
                                for i, out_obj_id in enumerate(out_obj_ids)
                            }
                            self.save_single_frame_results(out_frame_idx, current_frame_data)
                            
                            if out_frame_idx % 10 == 0:
                                print(f"Processed frame {out_frame_idx} (pass {pass_idx+1}/{self.propagation_steps})")
                        
                        # Stop after kf_end frames
                        if out_frame_idx >= kf_end - 1:
                            break
        
        except RuntimeError as e:
            # Check if it's a memory error
            if "out of memory" in str(e) and self.enable_auto_fallback:
                print(f"\nMemory error detected: {e}")
                print("Creating a new predictor instance on CPU for this segment...")
                
                try:
                    # Create a new predictor on CPU
                    cpu_predictor = build_sam2_video_predictor_hf(
                        self.model_id,
                        device="cpu"
                    )
                    
                    # Add the to() method to the new CPU predictor instance
                    from types import MethodType
                    cpu_predictor.to = MethodType(_predictor_to, cpu_predictor)
                    
                    # Initialize new state and reset it
                    print("Initializing new CPU predictor state...")
                    cpu_state = cpu_predictor.init_state(video_path=self.video_dir)
                    
                    # Find the latest processed frame data
                    latest_frame = None
                    scene_id = self.get_scene_for_frame(kf_start)
                    
                    # Look for the closest frame with annotation points from the same scene
                    if scene_id is not None and scene_id in self.scene_annotations:
                        closest_frame_distance = float('inf')
                        closest_frame = None
                        closest_points = None
                        closest_labels = None
                        
                        for frame_str, (points, labels) in self.scene_annotations[scene_id].items():
                            frame_idx = int(frame_str)
                            if frame_idx <= kf_start and len(points) > 0:
                                distance = kf_start - frame_idx
                                if distance < closest_frame_distance:
                                    closest_frame_distance = distance
                                    closest_frame = frame_idx
                                    closest_points = points
                                    closest_labels = labels
                        
                        if closest_points and closest_labels:
                            print(f"Using {len(closest_points)} points from frame {closest_frame} to guide CPU processing")
                            points_array = np.array(closest_points, dtype=np.float32)
                            labels_array = np.array(closest_labels, dtype=np.int32)
                            
                            # Add points to CPU predictor
                            try:
                                # Make sure we're setting the right object ID
                                cpu_predictor.add_new_points_or_box(
                                    inference_state=cpu_state,
                                    frame_idx=kf_start,
                                    obj_id=self.current_obj_id,
                                    points=points_array,
                                    labels=labels_array,
                                )
                                
                                # Process on CPU with new predictor
                                for pass_idx in range(self.propagation_steps):
                                    with torch.no_grad():
                                        for out_frame_idx, out_obj_ids, out_mask_logits in cpu_predictor.propagate_in_video(cpu_state):
                                            if kf_start <= out_frame_idx < kf_end:
                                                # Save masks for this frame
                                                current_frame_data = {
                                                    out_obj_id: (out_mask_logits[i] > 0.0).cpu().numpy()
                                                    for i, out_obj_id in enumerate(out_obj_ids)
                                                }
                                                self.save_single_frame_results(out_frame_idx, current_frame_data)
                                                
                                                if out_frame_idx % 5 == 0:
                                                    print(f"Processed frame {out_frame_idx} on CPU (pass {pass_idx+1}/{self.propagation_steps})")
                                            
                                            # Stop after kf_end frames
                                            if out_frame_idx >= kf_end - 1:
                                                break
                            
                            except RuntimeError as cpu_inner_error:
                                # If there's an error with CPU too, we'll try manual frame-by-frame processing
                                print(f"Error in CPU processing: {cpu_inner_error}")
                                print("Trying manual frame-by-frame processing...")
                                
                                # Process each frame individually
                                for frame_idx in range(kf_start, kf_end):
                                    try:
                                        # Generate basic segmentation for unprocessed frames
                                        if frame_idx not in self.video_segments or self.current_obj_id not in self.video_segments[frame_idx]:
                                            # Check if we have annotations for this frame
                                            if str(frame_idx) in self.scene_annotations[scene_id]:
                                                points, labels = self.scene_annotations[scene_id][str(frame_idx)]
                                                points_array = np.array(points, dtype=np.float32)
                                                labels_array = np.array(labels, dtype=np.int32)
                                                
                                                print(f"Using direct annotations for frame {frame_idx}")
                                                
                                                # Create a mask shape matching the image
                                                # Use a full frame array assuming all frames are the same size
                                                img = Image.open(os.path.join(self.video_dir, self.frame_names[frame_idx]))
                                                h, w = img.size[1], img.size[0]
                                                
                                                # Create a dummy mask based on the points
                                                mask = np.zeros((h, w), dtype=np.bool_)
                                                
                                                # For each positive point, set a small region around it to True
                                                for i, (x, y) in enumerate(points):
                                                    if labels[i] == 1:  # Only use positive points
                                                        x, y = int(x), int(y)
                                                        # Set 30x30 pixel region around point
                                                        y1, y2 = max(0, y-15), min(h, y+15)
                                                        x1, x2 = max(0, x-15), min(w, x+15)
                                                        mask[y1:y2, x1:x2] = True
                                                
                                                # Save this mask
                                                self.video_segments[frame_idx] = {self.current_obj_id: mask}
                                                self.save_single_frame_results(frame_idx, {self.current_obj_id: mask})
                                                print(f"Created basic mask for frame {frame_idx}")
                                            
                                            # If we don't have annotations, use nearest neighbor
                                            else:
                                                # Find nearest frame with a mask
                                                nearest_frames = sorted(list(self.video_segments.keys()), 
                                                                      key=lambda f: abs(f - frame_idx))
                                                
                                                if nearest_frames:
                                                    nearest_frame = nearest_frames[0]
                                                    if self.current_obj_id in self.video_segments[nearest_frame]:
                                                        print(f"Using nearest mask from frame {nearest_frame} for frame {frame_idx}")
                                                        mask = self.video_segments[nearest_frame][self.current_obj_id].copy()
                                                        self.video_segments[frame_idx] = {self.current_obj_id: mask}
                                                        self.save_single_frame_results(frame_idx, {self.current_obj_id: mask})
                                    
                                    except Exception as frame_error:
                                        print(f"Error processing frame {frame_idx}: {frame_error}")
                        else:
                            print("Could not find any points to guide CPU processing")
                    
                    # Clean up CPU predictor to free memory
                    del cpu_predictor
                    del cpu_state
                    gc.collect()
                    
                except Exception as cpu_error:
                    print(f"Error in CPU processing: {cpu_error}")
                    traceback.print_exc()
                    
                    # Generate masks for all unprocessed frames
                    self._fallback_frame_processing(kf_start, kf_end)
            else:
                # Not a memory error or fallback disabled
                raise

def _fallback_frame_processing(self, start_idx, end_idx):
        """Manually process frames when both MPS and CPU processing fail"""
        print("Performing manual fallback processing for frames {start_idx} to {end_idx-1}")
        
        scene_id = self.get_scene_for_frame(start_idx)
        if scene_id is None:
            print(f"Warning: Frame {start_idx} is not part of any scene")
            return
            
        # Process each frame individually
        for frame_idx in range(start_idx, end_idx):
            try:
                # Check if this frame already has a mask
                if frame_idx in self.video_segments and self.current_obj_id in self.video_segments[frame_idx]:
                    continue
                    
                # Try to use annotations if available
                if scene_id in self.scene_annotations and str(frame_idx) in self.scene_annotations[scene_id]:
                    points, labels = self.scene_annotations[scene_id][str(frame_idx)]
                    
                    if points and labels:
                        # Create a basic mask from the positive points
                        img = Image.open(os.path.join(self.video_dir, self.frame_names[frame_idx]))
                        h, w = img.size[1], img.size[0]
                        
                        # Create a dummy mask
                        mask = np.zeros((h, w), dtype=np.bool_)
                        
                        # For each positive point, set a region around it
                        for i, (x, y) in enumerate(points):
                            if labels[i] == 1:  # Positive point
                                x, y = int(x), int(y)
                                # Create a circular region
                                radius = 30
                                y1, y2 = max(0, y-radius), min(h, y+radius)
                                x1, x2 = max(0, x-radius), min(w, x+radius)
                                
                                for iy in range(y1, y2):
                                    for ix in range(x1, x2):
                                        # Set circular region
                                        if ((ix-x)**2 + (iy-y)**2) <= radius**2:
                                            if 0 <= iy < h and 0 <= ix < w:
                                                mask[iy, ix] = True
                        
                        # Save this mask
                        if frame_idx not in self.video_segments:
                            self.video_segments[frame_idx] = {}
                        self.video_segments[frame_idx][self.current_obj_id] = mask
                        self.save_single_frame_results(frame_idx, {self.current_obj_id: mask})
                        print(f"Created basic mask for frame {frame_idx} using annotations")
                        continue
                
                # If no annotations, use the nearest available mask
                available_frames = sorted(
                    [f for f in self.video_segments.keys() 
                     if self.current_obj_id in self.video_segments[f]],
                    key=lambda f: abs(f - frame_idx)
                )
                
                if available_frames:
                    closest_frame = available_frames[0]
                    print(f"Using mask from frame {closest_frame} for frame {frame_idx}")
                    
                    # Copy the mask
                    if frame_idx not in self.video_segments:
                        self.video_segments[frame_idx] = {}
                    
                    self.video_segments[frame_idx][self.current_obj_id] = \
                        self.video_segments[closest_frame][self.current_obj_id].copy()
                        
                    self.save_single_frame_results(frame_idx, 
                                                 {self.current_obj_id: self.video_segments[frame_idx][self.current_obj_id]})
                else:
                    print(f"Warning: No mask available to use for frame {frame_idx}")
                    
            except Exception as e:
                print(f"Error in fallback processing for frame {frame_idx}: {e}")

class InteractiveSegmenter:
    def __init__(self, video_dir, batch_size=1, preload_frames=False, model_id=None, 
                    keyframe_interval=10, propagation_steps=3, enable_auto_fallback=True,
                    use_tiny_model=False):
        """Enhanced initialization with better memory optimization"""
        self.video_dir = video_dir
        self.output_dir = os.path.join(video_dir, "segmentation_output")
        self.mask_dir = os.path.join(self.output_dir, "masks")
        self.overlay_dir = os.path.join(self.output_dir, "overlays")
        self.scenes_file = os.path.join(self.output_dir, "scenes.json")
        
        # Keyframe management for better tracking
        self.keyframe_interval = keyframe_interval  # Re-apply annotations every N frames
        self.propagation_steps = propagation_steps  # Number of propagation refinement steps
        
        # Memory optimization options
        self.batch_size = batch_size
        self.preload_frames = preload_frames
        self.enable_auto_fallback = enable_auto_fallback
        
        # Use tiny model if requested (much lower memory usage)
        if use_tiny_model:
            self.model_id = "facebook/sam2.1-hiera-tiny"
        else:
            self.model_id = model_id if model_id else "facebook/sam2.1-hiera-large"
        
        # Create output directories
        os.makedirs(self.output_dir, exist_ok=True)
        os.makedirs(self.mask_dir, exist_ok=True)
        os.makedirs(self.overlay_dir, exist_ok=True)
        
        # Get frame filenames
        self.frame_names = [
            p for p in os.listdir(video_dir)
            if os.path.splitext(p)[-1].lower() in [".jpg", ".jpeg"]
        ]
        self.frame_names.sort(key=lambda p: int(os.path.splitext(p)[0]))
        
        print(f"Found {len(self.frame_names)} frames in {video_dir}")
        
        # Scene management
        self.scenes = []
        self.current_scene = None
        self.scene_annotations = {}  # Maps scene_id -> {frame_idx: (points, labels)}
        
        # Try to load existing scene data
        if os.path.exists(self.scenes_file):
            try:
                with open(self.scenes_file, 'r') as f:
                    scene_data = json.load(f)
                    self.scenes = scene_data.get('scenes', [])
                    
                    # Convert string keys back to integers for scene_annotations
                    self.scene_annotations = {}
                    for scene_id_str, scene_annot in scene_data.get('annotations', {}).items():
                        scene_id = int(scene_id_str)
                        self.scene_annotations[scene_id] = {}
                        for frame_idx_str, (points, labels) in scene_annot.items():
                            self.scene_annotations[scene_id][frame_idx_str] = (points, labels)
                    
                    print(f"Loaded {len(self.scenes)} existing scenes")
            except Exception as e:
                print(f"Error loading scene data: {e}")
                self.scenes = []
                self.scene_annotations = {}
        
        # If we have scenes, set the current one
        if self.scenes:
            self.current_scene = 0  # Use the index of the first scene
            print(f"Current scene: {self.current_scene}")
        
        # We'll only cache the current frame to save memory
        self.frame_cache = {}
        
        # Print memory usage info
        try:
            mem = psutil.virtual_memory()
            print(f"System memory: {mem.available/1024/1024/1024:.1f}GB available out of {mem.total/1024/1024/1024:.1f}GB total")
        except ImportError:
            print("Could not import psutil to check memory usage")
        
        # Set MPS memory limits for Apple Silicon
        if device.type == "mps":
            # Remove any memory limits
            if "PYTORCH_MPS_HIGH_WATERMARK_RATIO" in os.environ:
                del os.environ["PYTORCH_MPS_HIGH_WATERMARK_RATIO"]
            print("MPS memory limitations disabled for better performance")
        
        # Make sure we free memory
        gc.collect()
        torch.cuda.empty_cache() if torch.cuda.is_available() else None
        
        # Initialize SAM2 model
        print(f"Loading SAM2 model ({self.model_id}) on {device} device...")
        
        try:
            # Use standard predictor without VOS optimizations for compatibility
            self.predictor = build_sam2_video_predictor_hf(
                self.model_id, 
                device=device
            )
            
            # Add the to() method to the predictor instance
            self.predictor.to = MethodType(_predictor_to, self.predictor)
            
            # Initialize with the first frame only to save memory
            print("Initializing model state...")
            self.inference_state = self.predictor.init_state(video_path=video_dir)
            self.predictor.reset_state(self.inference_state)
            print("Model loaded and state initialized successfully")
        except Exception as e:
            # Handle initialization failure
            print(f"Error loading model on {device}: {e}")
            
            if device.type == "mps":
                try_cpu = input("\nMPS device error. Would you like to try using CPU instead? (y/n, default: y): ").lower().strip()
                if try_cpu != "n":
                    print("Falling back to CPU. This will be slower but may avoid memory issues.")
                    cpu_device = torch.device("cpu")
                    try:
                        self.predictor = build_sam2_video_predictor_hf(self.model_id, device=cpu_device)
                        # Add the to() method to the predictor instance
                        self.predictor.to = MethodType(_predictor_to, self.predictor)
                        
                        print("Initializing model state on CPU...")
                        self.inference_state = self.predictor.init_state(video_path=video_dir)
                        self.predictor.reset_state(self.inference_state)
                        print("Model loaded and state initialized successfully on CPU")
                        
                        # Initialize UI components here (excluded for brevity)
                        return
                    except Exception as cpu_e:
                        print(f"Error loading model on CPU as well: {cpu_e}")
            
            # Provide troubleshooting tips
            print("\nTroubleshooting tips:")
            print("1. If you're getting out-of-memory errors, try:")
            print("   - Close other applications to free system memory")
            print("   - Try using a smaller model (sam2.1-hiera-tiny or sam2.1-hiera-small)")
            print("2. If you're getting invalid watermark ratio errors:")
            print("   - Your PyTorch MPS implementation may have different requirements")
            print("   - Try removing the PYTORCH_MPS_HIGH_WATERMARK_RATIO environment variable")
            print("3. If you're getting unexpected keyword arguments errors:")
            print("   - Your SAM2 version might be different from what this script expects")
            
            # Try one last attempt with tiny model
            if not use_tiny_model:
                print("\nTrying with tiny model as a last resort...")
                try:
                    self.model_id = "facebook/sam2.1-hiera-tiny"
                    self.predictor = build_sam2_video_predictor_hf(self.model_id, device=torch.device("cpu"))
                    # Add the to() method to the predictor instance
                    self.predictor.to = MethodType(_predictor_to, self.predictor)
                    
                    print("Initializing model state with tiny model on CPU...")
                    self.inference_state = self.predictor.init_state(video_path=video_dir)
                    self.predictor.reset_state(self.inference_state)
                    print("Model loaded and state initialized successfully with tiny model")
                    
                    # Continue initialization
                    # Note: UI component initialization would go here
                    return
                except Exception as tiny_e:
                    print(f"Error loading tiny model as well: {tiny_e}")
                    raise
            else:
                raise
        
        # Initialize interactive variables
        self.points = []  # List of (x, y) coordinates
        self.labels = []  # List of 0 or 1 (negative or positive)
        self.current_frame_idx = 0
        self.current_obj_id = 1
        self.point_mode = 1  # 1 for positive, 0 for negative
        self.video_segments = {}  # To store segmentation results
        
        # Store points for the current frame
        self.frame_points = {}  # Maps frame_idx -> (points, labels)
        
        # Figure setup
        self.fig, self.ax = plt.subplots(figsize=(12, 8))
        plt.subplots_adjust(bottom=0.3)  # More space for buttons
        
        # Add interaction buttons
        self.add_mode_button = Button(plt.axes([0.1, 0.05, 0.15, 0.06]), 'Pos/Neg Mode')
        self.add_mode_button.on_clicked(self.toggle_point_mode)
        
        self.next_frame_button = Button(plt.axes([0.3, 0.05, 0.15, 0.06]), 'Next Frame')
        self.next_frame_button.on_clicked(self.next_frame)
        
        self.prev_frame_button = Button(plt.axes([0.5, 0.05, 0.15, 0.06]), 'Prev Frame')
        self.prev_frame_button.on_clicked(self.prev_frame)
        
        self.process_button = Button(plt.axes([0.7, 0.05, 0.15, 0.06]), 'Process Video')
        self.process_button.on_clicked(self.process_video)
        
        # Clear button to reset points
        self.clear_button = Button(plt.axes([0.1, 0.13, 0.15, 0.06]), 'Clear Points')
        self.clear_button.on_clicked(self.clear_points)
        
        # Scene management buttons
        self.new_scene_button = Button(plt.axes([0.3, 0.13, 0.15, 0.06]), 'New Scene')
        self.new_scene_button.on_clicked(self.new_scene)
        
        self.prev_scene_button = Button(plt.axes([0.5, 0.13, 0.15, 0.06]), 'Prev Scene')
        self.prev_scene_button.on_clicked(self.prev_scene)
        
        self.next_scene_button = Button(plt.axes([0.7, 0.13, 0.15, 0.06]), 'Next Scene')
        self.next_scene_button.on_clicked(self.next_scene)
        
        # Process current scene button
        self.process_scene_button = Button(plt.axes([0.3, 0.21, 0.4, 0.06]), 'Process Current Scene')
        self.process_scene_button.on_clicked(self.process_current_scene)
        
        # Connect mouse event
        self.fig.canvas.mpl_connect('button_press_event', self.on_click)
        
        # Load first frame
        self.load_frame(self.current_frame_idx)
        plt.show()
    def optimized_init(self, video_dir, batch_size=1, preload_frames=False, model_id=None, 
                    keyframe_interval=10, propagation_steps=3, enable_auto_fallback=True,
                    use_tiny_model=False):
        """Enhanced initialization with better memory optimization"""
        self.video_dir = video_dir
        self.output_dir = os.path.join(video_dir, "segmentation_output")
        self.mask_dir = os.path.join(self.output_dir, "masks")
        self.overlay_dir = os.path.join(self.output_dir, "overlays")
        self.scenes_file = os.path.join(self.output_dir, "scenes.json")
        
        # Keyframe management for better tracking
        self.keyframe_interval = keyframe_interval  # Re-apply annotations every N frames
        self.propagation_steps = propagation_steps  # Number of propagation refinement steps
        
        # Memory optimization options
        self.batch_size = batch_size
        self.preload_frames = preload_frames
        self.enable_auto_fallback = enable_auto_fallback
        
        # Use tiny model if requested (much lower memory usage)
        if use_tiny_model:
            self.model_id = "facebook/sam2.1-hiera-tiny"
        else:
            self.model_id = model_id if model_id else "facebook/sam2.1-hiera-large"
        
        # Create output directories
        os.makedirs(self.output_dir, exist_ok=True)
        os.makedirs(self.mask_dir, exist_ok=True)
        os.makedirs(self.overlay_dir, exist_ok=True)
        
        # Get frame filenames
        self.frame_names = [
            p for p in os.listdir(video_dir)
            if os.path.splitext(p)[-1].lower() in [".jpg", ".jpeg"]
        ]
        self.frame_names.sort(key=lambda p: int(os.path.splitext(p)[0]))
        
        print(f"Found {len(self.frame_names)} frames in {video_dir}")
        
        # Scene management
        self.scenes = []
        self.current_scene = None
        self.scene_annotations = {}  # Maps scene_id -> {frame_idx: (points, labels)}
        
        # Try to load existing scene data
        if os.path.exists(self.scenes_file):
            try:
                with open(self.scenes_file, 'r') as f:
                    scene_data = json.load(f)
                    self.scenes = scene_data.get('scenes', [])
                    
                    # Convert string keys back to integers for scene_annotations
                    self.scene_annotations = {}
                    for scene_id_str, scene_annot in scene_data.get('annotations', {}).items():
                        scene_id = int(scene_id_str)
                        self.scene_annotations[scene_id] = {}
                        for frame_idx_str, (points, labels) in scene_annot.items():
                            self.scene_annotations[scene_id][frame_idx_str] = (points, labels)
                    
                    print(f"Loaded {len(self.scenes)} existing scenes")
            except Exception as e:
                print(f"Error loading scene data: {e}")
                self.scenes = []
                self.scene_annotations = {}
        
        # If we have scenes, set the current one
        if self.scenes:
            self.current_scene = 0  # Use the index of the first scene
            print(f"Current scene: {self.current_scene}")
        
        # We'll only cache the current frame to save memory
        self.frame_cache = {}
        
        # Print memory usage info
        try:
            mem = psutil.virtual_memory()
            print(f"System memory: {mem.available/1024/1024/1024:.1f}GB available out of {mem.total/1024/1024/1024:.1f}GB total")
        except ImportError:
            print("Could not import psutil to check memory usage")
        
        # Set MPS memory limits for Apple Silicon
        if device.type == "mps":
            # Remove any memory limits
            if "PYTORCH_MPS_HIGH_WATERMARK_RATIO" in os.environ:
                del os.environ["PYTORCH_MPS_HIGH_WATERMARK_RATIO"]
            print("MPS memory limitations disabled for better performance")
        
        # Make sure we free memory
        gc.collect()
        torch.cuda.empty_cache() if torch.cuda.is_available() else None
        
        # Initialize SAM2 model
        print(f"Loading SAM2 model ({self.model_id}) on {device} device...")
        
        try:
            # Use standard predictor without VOS optimizations for compatibility
            self.predictor = build_sam2_video_predictor_hf(
                self.model_id, 
                device=device
            )
            
            # Add the to() method to the predictor instance
            self.predictor.to = MethodType(_predictor_to, self.predictor)
            
            # Initialize with the first frame only to save memory
            print("Initializing model state...")
            self.inference_state = self.predictor.init_state(video_path=video_dir)
            self.predictor.reset_state(self.inference_state)
            print("Model loaded and state initialized successfully")
        except Exception as e:
            # Handle initialization failure
            print(f"Error loading model on {device}: {e}")
            
            if device.type == "mps":
                try_cpu = input("\nMPS device error. Would you like to try using CPU instead? (y/n, default: y): ").lower().strip()
                if try_cpu != "n":
                    print("Falling back to CPU. This will be slower but may avoid memory issues.")
                    cpu_device = torch.device("cpu")
                    try:
                        self.predictor = build_sam2_video_predictor_hf(self.model_id, device=cpu_device)
                        # Add the to() method to the predictor instance
                        self.predictor.to = MethodType(_predictor_to, self.predictor)
                        
                        print("Initializing model state on CPU...")
                        self.inference_state = self.predictor.init_state(video_path=video_dir)
                        self.predictor.reset_state(self.inference_state)
                        print("Model loaded and state initialized successfully on CPU")
                        
                        # Initialize UI components here (excluded for brevity)
                        return
                    except Exception as cpu_e:
                        print(f"Error loading model on CPU as well: {cpu_e}")
            
            # Provide troubleshooting tips
            print("\nTroubleshooting tips:")
            print("1. If you're getting out-of-memory errors, try:")
            print("   - Close other applications to free system memory")
            print("   - Try using a smaller model (sam2.1-hiera-tiny or sam2.1-hiera-small)")
            print("2. If you're getting invalid watermark ratio errors:")
            print("   - Your PyTorch MPS implementation may have different requirements")
            print("   - Try removing the PYTORCH_MPS_HIGH_WATERMARK_RATIO environment variable")
            print("3. If you're getting unexpected keyword arguments errors:")
            print("   - Your SAM2 version might be different from what this script expects")
            
            # Try one last attempt with tiny model
            if not use_tiny_model:
                print("\nTrying with tiny model as a last resort...")
                try:
                    self.model_id = "facebook/sam2.1-hiera-tiny"
                    self.predictor = build_sam2_video_predictor_hf(self.model_id, device=torch.device("cpu"))
                    # Add the to() method to the predictor instance
                    self.predictor.to = MethodType(_predictor_to, self.predictor)
                    
                    print("Initializing model state with tiny model on CPU...")
                    self.inference_state = self.predictor.init_state(video_path=video_dir)
                    self.predictor.reset_state(self.inference_state)
                    print("Model loaded and state initialized successfully with tiny model")
                    
                    # Continue initialization
                    # Note: UI component initialization would go here
                    return
                except Exception as tiny_e:
                    print(f"Error loading tiny model as well: {tiny_e}")
                    raise
            else:
                raise
        
        # Initialize interactive variables
        self.points = []  # List of (x, y) coordinates
        self.labels = []  # List of 0 or 1 (negative or positive)
        self.current_frame_idx = 0
        self.current_obj_id = 1
        self.point_mode = 1  # 1 for positive, 0 for negative
        self.video_segments = {}  # To store segmentation results
        
        # Store points for the current frame
        self.frame_points = {}  # Maps frame_idx -> (points, labels)
        
        # Initialize UI components here (excluded for brevity)
    
    def check_memory(self):
        """Check available memory and return True if we should use CPU instead"""
        if not self.enable_auto_fallback:
            return False
            
        if self.predictor.device.type != "mps" and self.predictor.device.type != "cuda":
            return False  # Already on CPU
            
        try:
            import psutil
            
            if self.predictor.device.type == "mps":
                # For MPS (Apple Silicon), check system memory
                mem = psutil.virtual_memory()
                available_gb = mem.available/1024/1024/1024
                
                # If less than 2GB available, use CPU
                if available_gb < 2:
                    print(f"Low system memory detected: {available_gb:.1f}GB available. Switching to CPU.")
                    return True
            
            elif self.predictor.device.type == "cuda":
                # For CUDA, check GPU memory
                try:
                    # Get free and total memory in GB
                    free_gpu_mem = torch.cuda.memory_reserved(0) - torch.cuda.memory_allocated(0)
                    free_gpu_mem_gb = free_gpu_mem / 1024 / 1024 / 1024
                    
                    # If less than 1GB free, use CPU
                    if free_gpu_mem_gb < 1:
                        print(f"Low GPU memory detected: {free_gpu_mem_gb:.1f}GB free. Switching to CPU.")
                        return True
                except:
                    # If can't check GPU memory, default to system memory check
                    mem = psutil.virtual_memory()
                    if mem.available < 2 * 1024 * 1024 * 1024:  # Less than 2GB
                        return True
        except:
            # If we can't check memory, just continue with current device
            pass
            
        return False
    
    def save_scene_data(self):
        """Save scene boundaries and annotations to disk"""
        # Convert scene_annotations to a serializable format
        serializable_annotations = {}
        for scene_id, scene_annot in self.scene_annotations.items():
            serializable_annotations[str(scene_id)] = {}
            for frame_idx, (points, labels) in scene_annot.items():
                serializable_annotations[str(scene_id)][frame_idx] = (points, labels)
        
        scene_data = {
            'scenes': self.scenes,
            'annotations': serializable_annotations
        }
        
        with open(self.scenes_file, 'w') as f:
            json.dump(scene_data, f)
        print(f"Saved scene data to {self.scenes_file}")
    
    def new_scene(self, event):
        """Create a new scene starting at the current frame"""
        if self.scenes and self.current_frame_idx <= self.scenes[-1]:
            print(f"Cannot create a new scene at frame {self.current_frame_idx} because it overlaps with existing scene")
            return
            
        scene_id = len(self.scenes)
        self.scenes.append(self.current_frame_idx)
        self.current_scene = scene_id
        self.scene_annotations[scene_id] = {}
        
        print(f"Created new scene {scene_id} starting at frame {self.current_frame_idx}")
        self.save_scene_data()
        self.clear_points(None)
        self.load_frame(self.current_frame_idx)
    
    def get_scene_for_frame(self, frame_idx):
        """Find which scene contains the given frame"""
        if not self.scenes:
            return None
            
        for i in range(len(self.scenes)-1):
            if self.scenes[i] <= frame_idx < self.scenes[i+1]:
                return i
                
        # Check if it's in the last scene
        if self.scenes[-1] <= frame_idx:
            return len(self.scenes) - 1
            
        return None
    
    def get_scene_range(self, scene_id):
        """Get the range of frames for a scene"""
        if scene_id is None or scene_id >= len(self.scenes):
            return (None, None)
            
        start = self.scenes[scene_id]
        end = len(self.frame_names)
        
        if scene_id < len(self.scenes) - 1:
            end = self.scenes[scene_id + 1]
            
        return (start, end)
    
    def prev_scene(self, event):
        """Move to the previous scene"""
        if not self.scenes:
            print("No scenes defined yet")
            return
            
        current_scene = self.get_scene_for_frame(self.current_frame_idx)
        if current_scene is None or current_scene <= 0:
            print("Already at the first scene")
            return
            
        target_scene = current_scene - 1
        self.current_scene = target_scene
        self.current_frame_idx = self.scenes[target_scene]
        print(f"Moving to previous scene {target_scene} at frame {self.current_frame_idx}")
        self.load_frame(self.current_frame_idx)
    
    def next_scene(self, event):
        """Move to the next scene"""
        if not self.scenes:
            print("No scenes defined yet")
            return
            
        current_scene = self.get_scene_for_frame(self.current_frame_idx)
        if current_scene is None or current_scene >= len(self.scenes) - 1:
            print("Already at the last scene")
            return
            
        target_scene = current_scene + 1
        self.current_scene = target_scene
        self.current_frame_idx = self.scenes[target_scene]
        print(f"Moving to next scene {target_scene} at frame {self.current_frame_idx}")
        self.load_frame(self.current_frame_idx)
    
    def clear_points(self, event):
        """Clear all points and reset"""
        self.points = []
        self.labels = []
        self.load_frame(self.current_frame_idx)
    
    def load_frame(self, frame_idx):
        self.ax.clear()
        self.current_frame_idx = frame_idx
        
        # Try to get frame from cache first
        if frame_idx in self.frame_cache:
            self.img = self.frame_cache[frame_idx]
        else:
            self.img = Image.open(os.path.join(self.video_dir, self.frame_names[frame_idx]))
            # Cache only current frame
            self.frame_cache = {frame_idx: self.img}
        
        self.ax.imshow(self.img)
        
        # Determine which scene we're in
        scene_id = self.get_scene_for_frame(frame_idx)
        scene_range = self.get_scene_range(scene_id)
        
        # Update title with current mode and scene information
        mode_text = "POSITIVE" if self.point_mode == 1 else "NEGATIVE"
        scene_text = f"Scene {scene_id}" if scene_id is not None else "No Scene"
        
        # Only add range info if we have valid range
        if scene_range[0] is not None and scene_range[1] is not None:
            scene_text += f" (Frames {scene_range[0]}-{scene_range[1]-1})"
        
        self.ax.set_title(f"Frame {frame_idx} - {mode_text} click mode - {scene_text}")
        
        # If this frame is in a scene and has annotations, load them
        self.points = []
        self.labels = []
        
        if scene_id is not None and scene_id in self.scene_annotations:
            scene_annot = self.scene_annotations[scene_id]
            if str(frame_idx) in scene_annot:
                self.points, self.labels = scene_annot[str(frame_idx)]
        
        # Show existing points if any
        if self.points and self.labels:
            points_array = np.array(self.points, dtype=np.float32)
            labels_array = np.array(self.labels, dtype=np.int32)
            show_points(points_array, labels_array, self.ax)
        
        # Show mask if the frame has been processed
        if frame_idx in self.video_segments and self.current_obj_id in self.video_segments[frame_idx]:
            show_mask(self.video_segments[frame_idx][self.current_obj_id], self.ax, obj_id=self.current_obj_id)
        
        plt.draw()
    
    def on_click(self, event):
        if event.inaxes != self.ax:
            return
        
        # Get the current scene
        scene_id = self.get_scene_for_frame(self.current_frame_idx)
        
        # Only allow annotations if we're in a defined scene
        if scene_id is None:
            print("Cannot add points: current frame is not part of any scene")
            return
        
        # Add point to the list
        x, y = event.xdata, event.ydata
        self.points.append([x, y])
        self.labels.append(self.point_mode)
        
        # Store points in scene annotations
        if scene_id not in self.scene_annotations:
            self.scene_annotations[scene_id] = {}
            
        self.scene_annotations[scene_id][str(self.current_frame_idx)] = (self.points.copy(), self.labels.copy())
        self.save_scene_data()
        
        # Process points
        self.process_points()
        
        # Refresh display
        self.load_frame(self.current_frame_idx)
    
    def toggle_point_mode(self, event):
        self.point_mode = 1 - self.point_mode  # Toggle between 1 and 0
        mode_text = "POSITIVE" if self.point_mode == 1 else "NEGATIVE"
        scene_id = self.get_scene_for_frame(self.current_frame_idx)
        scene_text = f"Scene {scene_id}" if scene_id is not None else "No Scene"
        self.ax.set_title(f"Frame {self.current_frame_idx} - {mode_text} click mode - {scene_text}")
        plt.draw()
    
    def next_frame(self, event):
        if self.current_frame_idx < len(self.frame_names) - 1:
            self.current_frame_idx += 1
            self.load_frame(self.current_frame_idx)
            torch.cuda.empty_cache() if torch.cuda.is_available() else None
    
    def prev_frame(self, event):
        if self.current_frame_idx > 0:
            self.current_frame_idx -= 1
            self.load_frame(self.current_frame_idx)
            torch.cuda.empty_cache() if torch.cuda.is_available() else None
    
    def process_points(self):
        """Process points for the current frame"""
        scene_id = self.get_scene_for_frame(self.current_frame_idx)
        if scene_id is None:
            print("Cannot process points: current frame is not part of any scene")
            return
            
        if not self.points:
            return
        
        # Convert to numpy arrays
        points_array = np.array(self.points, dtype=np.float32)
        labels_array = np.array(self.labels, dtype=np.int32)
        
        # Process with the model
        with torch.no_grad():
            _, out_obj_ids, out_mask_logits = self.predictor.add_new_points_or_box(
                inference_state=self.inference_state,
                frame_idx=self.current_frame_idx,
                obj_id=self.current_obj_id,
                points=points_array,
                labels=labels_array,
            )
        
        # Store the mask and convert to CPU + numpy immediately to free GPU memory
        if self.current_frame_idx not in self.video_segments:
            self.video_segments[self.current_frame_idx] = {}
        
        # Store the mask
        mask = (out_mask_logits[0] > 0.0).cpu().numpy()
        self.video_segments[self.current_frame_idx][self.current_obj_id] = mask
        
        # Clean up GPU memory
        torch.cuda.empty_cache() if torch.cuda.is_available() else None
        gc.collect()
    
    def process_current_scene(self, event):
        """Process only the current scene"""
        scene_id = self.get_scene_for_frame(self.current_frame_idx)
        if scene_id is None:
            print("Cannot process: current frame is not part of any scene")
            return
            
        print(f"Processing scene {scene_id}...")
        
        # Get scene range
        start_idx, end_idx = self.get_scene_range(scene_id)
        if start_idx is None or end_idx is None:
            print("Cannot determine scene boundaries")
            return
            
        # Check if we have any annotations for this scene
        if scene_id not in self.scene_annotations or not self.scene_annotations[scene_id]:
            print("No annotations found for this scene")
            return
            
        try:
            # Process this scene
            self._process_scene(scene_id, start_idx, end_idx)
            
            # Verify outputs
            output_files = os.listdir(self.mask_dir) + os.listdir(self.overlay_dir)
            if not output_files:
                print("\nWARNING: No output files were generated. Something went wrong during processing.")
            else:
                print(f"Scene {scene_id} processing complete! Results saved to {self.output_dir}")
                
        except Exception as e:
            print(f"Error processing scene {scene_id}: {e}")
            traceback.print_exc()
        finally:
            if device.type == "cuda":
                torch.cuda.empty_cache()
            gc.collect()
    
    def process_video(self, event):
        """Process all scenes in the video with improved device management"""
        if not self.scenes:
            print("No scenes defined. Please define scenes first.")
            return
            
        print("Processing all scenes in the video. This may take a while...")
        
        try:
            total_frames = len(self.frame_names)
            print(f"Total frames to process: {total_frames}")
            print(f"Total scenes to process: {len(self.scenes)}")
            
            # Process each scene separately
            for scene_id in range(len(self.scenes)):
                start_idx, end_idx = self.get_scene_range(scene_id)
                
                if scene_id not in self.scene_annotations or not self.scene_annotations[scene_id]:
                    print(f"Skipping scene {scene_id}: No annotations found")
                    continue
                    
                print(f"Processing scene {scene_id} (frames {start_idx}-{end_idx-1})...")
                
                try:
                    # Try processing with current device first
                    self._process_scene(scene_id, start_idx, end_idx)
                except RuntimeError as e:
                    # If it's a memory error, retry with a new CPU predictor
                    if "out of memory" in str(e) and self.enable_auto_fallback:
                        print(f"\nMemory error in scene {scene_id}: {e}")
                        print(f"Creating new CPU predictor for scene {scene_id}...")
                        
                        try:
                            # Create a new predictor on CPU
                            cpu_predictor = build_sam2_video_predictor_hf(
                                self.model_id,
                                device="cpu"
                            )
                            
                            # Add the to() method to the new predictor
                            from types import MethodType
                            cpu_predictor.to = MethodType(_predictor_to, cpu_predictor)
                            
                            # Save original predictor
                            original_predictor = self.predictor
                            
                            # Replace with CPU predictor
                            self.predictor = cpu_predictor
                            
                            # Initialize new state
                            self.inference_state = self.predictor.init_state(video_path=self.video_dir)
                            
                            # Re-process the scene on CPU
                            self._process_scene(scene_id, start_idx, end_idx)
                            
                            # Restore original predictor 
                            self.predictor = original_predictor
                            self.inference_state = self.predictor.init_state(video_path=self.video_dir)
                            
                            # Clean up CPU predictor
                            del cpu_predictor
                            gc.collect()
                            
                        except Exception as cpu_e:
                            print(f"Error processing on CPU as well: {cpu_e}")
                            traceback.print_exc()
                    else:
                        # Not a memory error or fallback disabled
                        print(f"Error processing scene {scene_id}: {e}")
                        traceback.print_exc()
                
                # Clean up memory between scenes
                gc.collect()
                if device.type == "cuda":
                    torch.cuda.empty_cache()
            
            print(f"All scenes processed! Results saved to {self.output_dir}")
            
        except Exception as e:
            print(f"Error during video processing: {e}")
            traceback.print_exc()
        finally:
            # Clean up memory
            if device.type == "cuda":
                torch.cuda.empty_cache()
            gc.collect()
    
    def _process_scene(self, scene_id, start_idx, end_idx):
        """Process a single scene"""
        if scene_id not in self.scene_annotations:
            print(f"No annotations for scene {scene_id}")
            return
            
        # Reset the inference state to start fresh for this scene
        print(f"Initializing model state for scene {scene_id}...")
        self.predictor.reset_state(self.inference_state)
        
        # Find the first annotated frame in this scene
        first_annotated_frame = None
        annotated_frames = []
        for frame_str in self.scene_annotations[scene_id]:
            frame_idx = int(frame_str)
            if start_idx <= frame_idx < end_idx:
                annotated_frames.append(frame_idx)
                
        if not annotated_frames:
            print(f"No annotations found in scene {scene_id}")
            return
            
        # Sort annotated frames
        annotated_frames.sort()
        first_annotated_frame = annotated_frames[0]
        
        print(f"Scene {scene_id} has {len(annotated_frames)} annotated frames")
        print(f"First annotated frame: {first_annotated_frame}")
        
        # Apply points to the first annotated frame
        points, labels = self.scene_annotations[scene_id][str(first_annotated_frame)]
        points_array = np.array(points, dtype=np.float32)
        labels_array = np.array(labels, dtype=np.int32)
        
        print(f"Setting initial annotation with {len(points)} points at frame {first_annotated_frame}")
        
        with torch.no_grad():
            _, out_obj_ids, out_mask_logits = self.predictor.add_new_points_or_box(
                inference_state=self.inference_state,
                frame_idx=first_annotated_frame,
                obj_id=self.current_obj_id,
                points=points_array,
                labels=labels_array,
            )
        
        # Save the first annotated frame result
        current_frame_data = {
            self.current_obj_id: (out_mask_logits[0] > 0.0).cpu().numpy()
        }
        self.save_single_frame_results(first_annotated_frame, current_frame_data)
        
        # Process frames in chunks defined by annotated frames
        for i in range(len(annotated_frames)):
            current_frame = annotated_frames[i]
            
            # Define the end of this chunk (either the next annotated frame or scene end)
            next_frame = end_idx
            if i < len(annotated_frames) - 1:
                next_frame = annotated_frames[i + 1]
            
            print(f"Processing chunk from frame {current_frame} to {next_frame-1}")
            
            # If we're not at the first frame, re-apply annotations
            if i > 0:
                points, labels = self.scene_annotations[scene_id][str(current_frame)]
                points_array = np.array(points, dtype=np.float32)
                labels_array = np.array(labels, dtype=np.int32)
                
                print(f"Applying {len(points)} points at frame {current_frame}")
                
                with torch.no_grad():
                    _, out_obj_ids, out_mask_logits = self.predictor.add_new_points_or_box(
                        inference_state=self.inference_state,
                        frame_idx=current_frame,
                        obj_id=self.current_obj_id,
                        points=points_array,
                        labels=labels_array,
                    )
                
                # Save this annotated frame result
                current_frame_data = {
                    self.current_obj_id: (out_mask_logits[0] > 0.0).cpu().numpy()
                }
                self.save_single_frame_results(current_frame, current_frame_data)
            
            # Now propagate to the rest of the frames in this chunk
            # Use keyframe-based propagation within the chunk
            self._propagate_chunk(current_frame, next_frame)
            
            # Clean up memory between chunks
            gc.collect()
            if device.type == "cuda":
                torch.cuda.empty_cache()
    
    # Replace the _propagate_chunk method with this corrected version
    def _propagate_chunk(self, start_idx, end_idx):
        """Propagate within a chunk using keyframes with automatic fallback to CPU on memory errors"""
        # Calculate keyframes within this chunk
        if start_idx >= end_idx - 1:
            return  # Nothing to propagate
            
        # Create keyframes at regular intervals
        keyframes = list(range(start_idx, end_idx, self.keyframe_interval))
        if keyframes[-1] != end_idx - 1:
            keyframes.append(end_idx - 1)
            
        print(f"Propagating with {len(keyframes)} keyframes in chunk")
        
        for i in range(len(keyframes) - 1):
            kf_start = keyframes[i]
            kf_end = keyframes[i + 1] + 1  # +1 because ranges are exclusive at the end
            
            print(f"Processing keyframe segment {kf_start} to {kf_end-1}")
            
            # Try with current device first (MPS/CUDA)
            try:
                # For each keyframe, do multiple propagation passes
                for pass_idx in range(self.propagation_steps):
                    with torch.no_grad():
                        for out_frame_idx, out_obj_ids, out_mask_logits in self.predictor.propagate_in_video(self.inference_state):
                            if kf_start <= out_frame_idx < kf_end:
                                # Save masks for this frame
                                current_frame_data = {
                                    out_obj_id: (out_mask_logits[i] > 0.0).cpu().numpy()
                                    for i, out_obj_id in enumerate(out_obj_ids)
                                }
                                self.save_single_frame_results(out_frame_idx, current_frame_data)
                                
                                if out_frame_idx % 10 == 0:
                                    print(f"Processed frame {out_frame_idx} (pass {pass_idx+1}/{self.propagation_steps})")
                            
                            # Stop after kf_end frames
                            if out_frame_idx >= kf_end - 1:
                                break
            
            except RuntimeError as e:
                # Check if it's a memory error
                if "out of memory" in str(e) and self.enable_auto_fallback:
                    print(f"\nMemory error detected: {e}")
                    print("Creating a new predictor instance on CPU for this segment...")
                    
                    try:
                        # Create a new predictor on CPU instead of trying to convert existing one
                        cpu_predictor = build_sam2_video_predictor_hf(
                            self.model_id,
                            device="cpu"
                        )
                        
                        # Initialize new state with the video
                        print("Initializing new CPU predictor state...")
                        cpu_state = cpu_predictor.init_state(video_path=self.video_dir)
                        
                        # Find the latest processed frame data
                        latest_frame = max([f for f in range(start_idx, kf_start + 1) if f in self.video_segments], default=None)
                        if latest_frame is not None and self.current_obj_id in self.video_segments[latest_frame]:
                            # If we have a previous mask, try to use it as a guide
                            print(f"Using mask from frame {latest_frame} to guide CPU processing")
                            mask = self.video_segments[latest_frame][self.current_obj_id]
                            
                            # Get annotation points for the closest annotated frame
                            scene_id = self.get_scene_for_frame(kf_start)
                            if scene_id is not None and scene_id in self.scene_annotations:
                                frame_annotations = []
                                for frame_str, (points, labels) in self.scene_annotations[scene_id].items():
                                    frame_idx = int(frame_str)
                                    if frame_idx <= kf_start:
                                        frame_annotations.append((frame_idx, points, labels))
                                
                                if frame_annotations:
                                    # Get closest annotated frame
                                    closest_frame, closest_points, closest_labels = max(frame_annotations, key=lambda x: x[0])
                                    
                                    if closest_points and closest_labels:
                                        print(f"Using points from frame {closest_frame} to guide CPU processing")
                                        points_array = np.array(closest_points, dtype=np.float32)
                                        labels_array = np.array(closest_labels, dtype=np.int32)
                                        
                                        # Add points to CPU predictor
                                        cpu_predictor.add_new_points_or_box(
                                            inference_state=cpu_state,
                                            frame_idx=kf_start,
                                            obj_id=self.current_obj_id,
                                            points=points_array,
                                            labels=labels_array,
                                        )
                        
                        # Process on CPU with new predictor
                        for pass_idx in range(self.propagation_steps):
                            with torch.no_grad():
                                for out_frame_idx, out_obj_ids, out_mask_logits in cpu_predictor.propagate_in_video(cpu_state):
                                    if kf_start <= out_frame_idx < kf_end:
                                        # Save masks for this frame
                                        current_frame_data = {
                                            out_obj_id: (out_mask_logits[i] > 0.0).cpu().numpy()
                                            for i, out_obj_id in enumerate(out_obj_ids)
                                        }
                                        self.save_single_frame_results(out_frame_idx, current_frame_data)
                                        
                                        if out_frame_idx % 5 == 0:  # More frequent updates on CPU
                                            print(f"Processed frame {out_frame_idx} on CPU (pass {pass_idx+1}/{self.propagation_steps})")
                                    
                                    # Stop after kf_end frames
                                    if out_frame_idx >= kf_end - 1:
                                        break
                        
                        # Clean up CPU predictor to free memory
                        del cpu_predictor
                        del cpu_state
                        gc.collect()
                        
                    except Exception as cpu_error:
                        print(f"Error processing on CPU as well: {cpu_error}")
                        traceback.print_exc()
                else:
                    # Not a memory error or fallback disabled
                    raise
    
    def save_single_frame_results(self, frame_idx, frame_data):
        """Save segmentation masks and overlays for a single frame with additional checks"""
        try:
            # Skip if invalid frame
            if frame_idx < 0 or frame_idx >= len(self.frame_names):
                print(f"Warning: Invalid frame index {frame_idx}")
                return
            
            # Load original frame
            img_path = os.path.join(self.video_dir, self.frame_names[frame_idx])
            if not os.path.exists(img_path):
                print(f"Warning: Image file not found: {img_path}")
                return
                
            img = Image.open(img_path)
            img_np = np.array(img)
            
            # Create overlay figure
            fig, ax = plt.subplots(figsize=(10, 6))
            ax.imshow(img_np)
            
            # Check if frame_data is valid
            if not frame_data:
                print(f"Warning: No mask data for frame {frame_idx}")
                
                # Create empty mask for tracking
                h, w = img_np.shape[:2]
                empty_mask = np.zeros((h, w), dtype=np.bool_)
                
                # Store it
                if frame_idx not in self.video_segments:
                    self.video_segments[frame_idx] = {}
                self.video_segments[frame_idx][self.current_obj_id] = empty_mask
                
                # Add scene information to the overlay
                scene_id = self.get_scene_for_frame(frame_idx)
                if scene_id is not None:
                    ax.text(10, 30, f"Scene {scene_id}", fontsize=12, color='white', 
                            bbox=dict(facecolor='black', alpha=0.5))
                
                # Save overlay
                overlay_filename = f"frame_{frame_idx:04d}_overlay.png"
                overlay_path = os.path.join(self.overlay_dir, overlay_filename)
                plt.axis('off')
                plt.tight_layout()
                plt.savefig(overlay_path, bbox_inches='tight', pad_inches=0)
                plt.close(fig)  # Close figure to free memory
                return
            
            # Add masks
            for obj_id, mask in frame_data.items():
                # Ensure mask has the right shape and format for PIL
                if mask.ndim != 2:
                    try:
                        mask = np.squeeze(mask)
                    except:
                        print(f"Warning: Could not reshape mask with shape {mask.shape} for frame {frame_idx}, object {obj_id}")
                        continue
                
                # Ensure mask has the expected dimensions
                if mask.ndim != 2:
                    print(f"Warning: Mask has unexpected shape {mask.shape} for frame {frame_idx}, object {obj_id}")
                    continue
                    
                # Ensure mask has correct size (resize if needed)
                img_h, img_w = img_np.shape[:2]
                mask_h, mask_w = mask.shape
                
                if mask_h != img_h or mask_w != img_w:
                    print(f"Warning: Mask size ({mask_h}x{mask_w}) doesn't match image size ({img_h}x{img_w}), resizing")
                    # Resize mask to match image
                    mask_resized = np.zeros((img_h, img_w), dtype=mask.dtype)
                    
                    # Copy the portion that fits
                    h = min(img_h, mask_h)
                    w = min(img_w, mask_w)
                    mask_resized[:h, :w] = mask[:h, :w]
                    mask = mask_resized
                
                # Save binary mask
                mask_filename = f"frame_{frame_idx:04d}_obj_{obj_id}.png"
                mask_path = os.path.join(self.mask_dir, mask_filename)
                
                # Use binary values (0 or 255) for the mask
                mask_img = Image.fromarray((mask * 255).astype(np.uint8))
                mask_img.save(mask_path)
                
                # Add to overlay
                show_mask(mask, ax, obj_id=obj_id)
            
            # Add scene information to the overlay
            scene_id = self.get_scene_for_frame(frame_idx)
            if scene_id is not None:
                ax.text(10, 30, f"Scene {scene_id}", fontsize=12, color='white', 
                        bbox=dict(facecolor='black', alpha=0.5))
            
            # Save overlay
            overlay_filename = f"frame_{frame_idx:04d}_overlay.png"
            overlay_path = os.path.join(self.overlay_dir, overlay_filename)
            plt.axis('off')
            plt.tight_layout()
            plt.savefig(overlay_path, bbox_inches='tight', pad_inches=0)
            plt.close(fig)  # Close figure to free memory
            
            # Force garbage collection periodically
            if frame_idx % 10 == 0:
                gc.collect()
                
        except Exception as e:
            print(f"Error saving results for frame {frame_idx}: {e}")
            traceback.print_exc()

if __name__ == "__main__":
    # Set the directory with the video frames
    video_dir = input("Enter the directory containing video frames (e.g., ./ceramics_frames): ")
    
    if not os.path.exists(video_dir):
        print(f"Error: Directory {video_dir} does not exist")
        exit(1)
    
    # Default model selection
    model_id = "facebook/sam2.1-hiera-large"
    use_tiny_model = False
    
    # Enable automatic CPU fallback by default
    enable_auto_fallback = True
    
    # Ask about MPS watermark ratio if on MPS device
    if device.type == "mps":
        print("\nYou're using an MPS device (Apple Silicon).")
        watermark_choice = input("Would you like to disable the MPS watermark ratio limit? This may help with memory errors. (y/n, default: y): ").lower().strip()
        if watermark_choice != "n":
            print("Disabling MPS watermark ratio limit.")
            # Remove the environment variable completely instead of setting it to 0.8
            if "PYTORCH_MPS_HIGH_WATERMARK_RATIO" in os.environ:
                del os.environ["PYTORCH_MPS_HIGH_WATERMARK_RATIO"]
            print("PyTorch MPS will now allocate memory as needed.")
        
        # Ask about automatic memory management option
        auto_fallback = input("\nEnable automatic CPU fallback on memory errors? (y/n, default: y): ").lower().strip()
        if auto_fallback == "n":
            enable_auto_fallback = False
            print("Automatic CPU fallback disabled. The script may crash if it runs out of memory.")
        else:
            print("Automatic CPU fallback enabled. The script will switch to CPU if memory runs out.")
    
    # Check available memory and suggest model options
    try:
        import psutil
        mem = psutil.virtual_memory()
        available_gb = mem.available/1024/1024/1024
        total_gb = mem.total/1024/1024/1024
        print(f"System memory: {available_gb:.1f}GB available out of {total_gb:.1f}GB total")
        
        if available_gb < 8 and device.type in ["mps", "cuda"]:
            print("\nWarning: Relatively low memory available.")
            print("You may experience out-of-memory errors with the large model.")
            use_large = input("Do you want to proceed with the large model anyway? (y/n, default: n): ").lower().strip() == 'y'
            
            if not use_large:
                model_options = {
                    "1": "facebook/sam2.1-hiera-tiny (lowest memory usage)",
                    "2": "facebook/sam2.1-hiera-small (better balance)",
                    "3": "facebook/sam2.1-hiera-base-plus (good accuracy)",
                    "4": "facebook/sam2.1-hiera-large (highest quality, high memory)",
                    "5": "Use CPU instead of GPU (slower but more memory available)"
                }
                
                print("\nPlease select a model size:")
                for key, value in model_options.items():
                    print(f"{key}: {value}")
                
                choice = input("Enter choice (1-5, default: 1): ").strip()
                if not choice:
                    choice = "1"
                    
                if choice == "1":
                    # Tiny model
                    model_id = "facebook/sam2.1-hiera-tiny"
                    use_tiny_model = True
                    print("Selected tiny model for lower memory usage")
                elif choice == "5":
                    print("Using CPU for processing instead of GPU")
                    device = torch.device("cpu")
                else:
                    model_id = model_options.get(choice, "facebook/sam2.1-hiera-small").split(" ")[0]
                    print(f"Selected model: {model_id}")
    except ImportError:
        # No psutil, proceed with default
        pass
    
    # Clear up memory before starting
    if torch.cuda.is_available():
        torch.cuda.empty_cache()
    gc.collect()
    
    # Ask for keyframe interval
    keyframe_interval = 5  # More frequent keyframes by default
    try:
        keyframe_input = input("\nEnter keyframe interval (how often to re-apply annotations, default: 5): ")
        if keyframe_input.strip():
            keyframe_interval = int(keyframe_input)
    except ValueError:
        print("Invalid value, using default of 5")
    
    # Ask for propagation steps
    propagation_steps = 2  # Reduced for better memory usage
    try:
        propagation_input = input("\nEnter propagation refinement steps (higher values improve quality but are slower, default: 2): ")
        if propagation_input.strip():
            propagation_steps = int(propagation_input)
    except ValueError:
        print("Invalid value, using default of 2")
    
    # Start the interactive segmenter
    print("\n=== HOW TO USE THIS SCENE-AWARE SEGMENTATION TOOL ===")
    print("SCENE MANAGEMENT:")
    print("1. First, use 'New Scene' to mark the start of each scene in your video")
    print("2. Use 'Prev Scene' and 'Next Scene' to navigate between scenes")
    print("3. Each scene is processed independently to handle different lighting/angles")
    
    print("\nANNOTATION:")
    print("4. Within each scene, click on the pottery piece to add positive points (green)")
    print("5. Use 'Pos/Neg Mode' to toggle to negative mode for excluding areas (red)")
    print("6. You can annotate multiple frames within each scene for better tracking")
    
    print("\nIMPORTANT IMPROVEMENT:")
    print("7. Add annotations to MULTIPLE frames in each scene for better results")
    print("   The more frames you annotate, the better the tracking will be")
    print("   Try to annotate at least every 5-10 frames or whenever the object changes position/appearance")
    
    print("\nPROCESSING:")
    print("8. Use 'Process Current Scene' to process only the current scene")
    print("9. Use 'Process Video' to process all scenes sequentially")
    print("10. Results will be saved to the 'segmentation_output' folder")
    
    print("\nIf you encounter memory errors or missing frames, the program will:")
    print("- Try to automatically switch to CPU processing")
    print("- Use fallback methods to create masks for all frames")
    print("- Create basic segmentation masks based on your annotations")
    
    print(f"\nCurrent settings:")
    print(f"- Model: {model_id}")
    print(f"- Device: {device}")
    print(f"- Keyframe interval: {keyframe_interval}")
    print(f"- Refinement steps: {propagation_steps}")
    print(f"- Auto memory management: {'Enabled' if enable_auto_fallback else 'Disabled'}")
    
    print("\nIMPORTANT: You MUST create at least one scene and add annotations before processing!")
    print("=========================\n")
    
    try:
        # Use batch processing and don't preload frames to save memory
        segmenter = InteractiveSegmenter(
            video_dir, 
            batch_size=1, 
            preload_frames=False, 
            model_id=model_id,
            keyframe_interval=keyframe_interval,
            propagation_steps=propagation_steps,
            enable_auto_fallback=enable_auto_fallback
        )
    except Exception as e:
        print(f"Error initializing segmenter: {e}")
        traceback.print_exc()
        
        # Last attempt with tiny model
        if not use_tiny_model:
            print("\nRetrying with tiny model as a last resort...")
            try:
                # Use default segmenter without arguments to match your class definition
                segmenter = InteractiveSegmenter()
                
                # Then set the attributes manually
                segmenter.video_dir = video_dir
                segmenter.keyframe_interval = keyframe_interval
                segmenter.propagation_steps = propagation_steps
                segmenter.enable_auto_fallback = enable_auto_fallback
                segmenter.model_id = model_id
                
            except Exception as e:
                print(f"Error initializing segmenter: {e}")
                traceback.print_exc()