#!/usr/bin/env python3

import os
import argparse
from skimage import io
from pyxelate import Pyx

def pixelate_image(input_path, output_path, factor=8, palette=8, dither="naive", alpha=0.6, upscale=1):
    """
    Pixelate an image with alpha channel support
    
    Parameters:
    -----------
    input_path : str
        Path to the input image
    output_path : str
        Path to save the output image
    factor : int
        Downsampling factor (higher = more pixelated)
    palette : int
        Number of colors in the output image
    dither : str
        Dithering method ("none", "naive", "bayer", "floyd", "atkinson")
        Note: For alpha images, "naive" is recommended
    alpha : float
        Alpha threshold (0.0-1.0) for transparency
    upscale : int
        Pixel size in the final image
    """
    # Load the image
    print(f"Loading image from {input_path}...")
    image = io.imread(input_path)
    
    # Check if image has alpha channel
    has_alpha = image.shape[2] == 4 if len(image.shape) > 2 else False
    if has_alpha:
        print("Alpha channel detected, using appropriate settings")
    
    # Create Pyx transformer
    print(f"Creating pixelated version with factor={factor}, palette={palette}, dither={dither}...")
    pyx = Pyx(
        factor=factor, 
        palette=palette, 
        dither=dither,
        alpha=alpha,
        upscale=upscale
    )
    
    # Fit and transform the image
    pyx.fit(image)
    pixelated = pyx.transform(image)
    
    # Save the result
    print(f"Saving pixelated image to {output_path}")
    io.imsave(output_path, pixelated)
    print("Done!")

def process_directory(input_dir, output_dir, **kwargs):
    """Process all images in a directory"""
    os.makedirs(output_dir, exist_ok=True)
    
    count = 0
    for filename in os.listdir(input_dir):
        # Check if file is an image
        if filename.lower().endswith(('.png', '.jpg', '.jpeg', '.tiff', '.bmp', '.gif')):
            input_path = os.path.join(input_dir, filename)
            output_path = os.path.join(output_dir, f"pixel_{filename}")
            
            try:
                pixelate_image(input_path, output_path, **kwargs)
                count += 1
            except Exception as e:
                print(f"Error processing {filename}: {e}")
    
    print(f"Processed {count} images")

def main():
    parser = argparse.ArgumentParser(description="Pixelate images with alpha channel support")
    parser.add_argument("input", help="Input image file or directory")
    parser.add_argument("output", help="Output image file or directory")
    parser.add_argument("--factor", type=int, default=8, help="Downsampling factor (default: 8)")
    parser.add_argument("--palette", type=int, default=8, help="Number of colors (default: 8)")
    parser.add_argument("--dither", type=str, default="naive", 
                        choices=["none", "naive", "bayer", "floyd", "atkinson"],
                        help="Dithering method (default: naive)")
    parser.add_argument("--alpha", type=float, default=0.6, 
                        help="Alpha threshold 0.0-1.0 (default: 0.6)")
    parser.add_argument("--upscale", type=int, default=1,
                        help="Pixel size in output (default: 1)")
    
    args = parser.parse_args()
    
    if os.path.isdir(args.input):
        if not os.path.isdir(args.output):
            os.makedirs(args.output, exist_ok=True)
        process_directory(
            args.input, 
            args.output,
            factor=args.factor,
            palette=args.palette,
            dither=args.dither,
            alpha=args.alpha,
            upscale=args.upscale
        )
    else:
        pixelate_image(
            args.input,
            args.output,
            factor=args.factor,
            palette=args.palette,
            dither=args.dither,
            alpha=args.alpha,
            upscale=args.upscale
        )

if __name__ == "__main__":
    main()