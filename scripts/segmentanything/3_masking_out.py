import os
import cv2
import numpy as np
import re

def extract_objects_with_masks():
    # Create output directory if it doesn't exist
    output_dir = "db"
    if not os.path.exists(output_dir):
        os.makedirs(output_dir)
    print(f"Created output directory: {output_dir}")

    # Paths
    frames_dir = "pottery"
    masks_dir = "pottery/segmentation_output/masks"

    # Get all mask files
    mask_files = sorted([f for f in os.listdir(masks_dir) if f.endswith(".png")])

    # Process each mask
    for mask_filename in mask_files:
        # Extract frame number from mask filename (frame_XXXX_obj_1.png)
        match = re.search(r'frame_(\d+)_obj', mask_filename)
        if not match:
            print(f"Couldn't parse frame number from {mask_filename}, skipping...")
            continue
        frame_num = int(match.group(1))

        # Find corresponding original image (looking for {frame_num}.jpg)
        orig_filename = f"{frame_num}.jpg"
        orig_path = os.path.join(frames_dir, orig_filename)

        # Check if original image exists
        if not os.path.exists(orig_path):
            print(f"Original image {orig_path} not found, skipping...")
            continue

        # Read original image and mask
        original_img = cv2.imread(orig_path)
        if original_img is None:
            print(f"Failed to read original image {orig_path}, skipping...")
            continue

        mask_path = os.path.join(masks_dir, mask_filename)
        mask = cv2.imread(mask_path, cv2.IMREAD_GRAYSCALE)
        if mask is None:
            print(f"Failed to read mask {mask_path}, skipping...")
            continue

        # Ensure mask is binary (0 or 255)
        _, mask = cv2.threshold(mask, 127, 255, cv2.THRESH_BINARY)
        
        # Resize mask to match original image dimensions if they're different
        if mask.shape[:2] != original_img.shape[:2]:
            mask = cv2.resize(mask, (original_img.shape[1], original_img.shape[0]), 
                             interpolation=cv2.INTER_NEAREST)

        # Convert to BGRA (add alpha channel)
        output_img = cv2.cvtColor(original_img, cv2.COLOR_BGR2BGRA)
        
        # Set alpha channel based on mask
        output_img[:, :, 3] = mask

        # Save the result
        output_path = os.path.join(output_dir, f"extracted_{frame_num:04d}.png")
        cv2.imwrite(output_path, output_img)
        print(f"Processed {mask_filename} -> {output_path}")

    print(f"Finished! Extracted objects saved to {output_dir}/")

if __name__ == "__main__":
    # Extract objects with masks at original size without resizing
    extract_objects_with_masks()