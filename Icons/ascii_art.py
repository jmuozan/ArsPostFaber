import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import argparse
import random
import string

def get_random_char():
    # Use a mix of ASCII printable characters for variety
    # Excluding space to ensure visibility
    chars = string.ascii_letters + string.digits + string.punctuation
    return random.choice(chars)

def image_to_colored_ascii(input_path, output_path, font_size=10, scale=1.0):
    try:
        # Open the image
        img = Image.open(input_path)
        
        # Get original dimensions
        original_width, original_height = img.size
        
        # Resize the image while maintaining aspect ratio for ASCII conversion
        width = int(original_width / (font_size * 0.6 * scale))
        height = int(original_height / (font_size * scale))
        img_resized = img.resize((width, height), Image.LANCZOS)
        
        # Convert the image to RGBA mode to handle transparency
        img_resized = img_resized.convert('RGBA')
        
        # Load a monospaced font
        try:
            font = ImageFont.truetype("Courier", font_size)
        except IOError:
            # Fall back to default if Courier is not available
            font = ImageFont.load_default()
        
        # Create a new image with transparent background
        ascii_img = Image.new("RGBA", (original_width, original_height), color=(0, 0, 0, 0))
        draw = ImageDraw.Draw(ascii_img)
        
        # Create a color-to-character mapping to ensure consistency
        # This maps each unique color to a random character
        color_char_map = {}
        
        # Process each pixel and create colored ASCII art
        pixels = np.array(img_resized)
        for y in range(height):
            for x in range(width):
                r, g, b, a = pixels[y, x]
                
                # Skip fully transparent pixels
                if a == 0:
                    continue
                
                # Create a color key for the map (including alpha)
                color_key = (r, g, b, a)
                
                # Get or create a random character for this color
                if color_key not in color_char_map:
                    color_char_map[color_key] = get_random_char()
                
                char = color_char_map[color_key]
                
                # Calculate position in the original-sized image
                pos_x = int(x * font_size * 0.6 * scale)
                pos_y = int(y * font_size * scale)
                
                # Draw the character with the pixel's color (including alpha)
                draw.text((pos_x, pos_y), char, fill=color_key, font=font)
        
        # Save the resulting image with transparency
        ascii_img.save(output_path)
        
        print(f"Colored ASCII art saved to {output_path}")
        return True
    
    except Exception as e:
        print(f"Error: {e}")
        return False

def main():
    parser = argparse.ArgumentParser(description='Convert an image to colored ASCII art')
    parser.add_argument('input_image', help='Path to the input image')
    parser.add_argument('--output', '-o', default='ascii_output.png', help='Path to save the output image (default: ascii_output.png)')
    parser.add_argument('--font-size', '-f', type=int, default=20, help='Font size for ASCII characters (default: 12)')
    parser.add_argument('--scale', '-s', type=float, default=3.0, help='Scale factor for the output (default: 1.0)')
    
    args = parser.parse_args()
    
    image_to_colored_ascii(args.input_image, args.output, args.font_size, args.scale)

if __name__ == "__main__":
    main()