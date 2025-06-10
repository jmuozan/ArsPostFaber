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

def image_to_colored_ascii(input_path, output_path, font_size=10, scale=1.0, invert=False):
    try:
        # Open the image
        img = Image.open(input_path)
        
        # Get original dimensions
        original_width, original_height = img.size
        
        # Resize the image while maintaining aspect ratio for ASCII conversion
        width = int(original_width / (font_size * 0.6 * scale))
        height = int(original_height / (font_size * scale))
        img_resized = img.resize((width, height), Image.LANCZOS)
        
        # Convert the image to RGBA
        img_rgba = img_resized.convert('RGBA')
        
        # Load a monospaced font
        try:
            font = ImageFont.truetype("Courier", font_size)
        except IOError:
            # Fall back to default if Courier is not available
            font = ImageFont.load_default()
        
        # Create a new image with white background for normal mode or transparent for inverted
        bg_color = (255, 255, 255, 255) if not invert else (255, 255, 255, 255)
        ascii_img = Image.new("RGBA", (original_width, original_height), color=bg_color)
        draw = ImageDraw.Draw(ascii_img)
        
        # Process each pixel and create colored ASCII art
        pixels = np.array(img_rgba)
        
        # Get the main foreground color from the image (non-transparent color)
        foreground_color = (0, 0, 0, 255)  # Default to black if no suitable color found
        
        # Pre-generate random characters for each position to ensure variety
        char_grid = []
        for y in range(height):
            row = []
            for x in range(width):
                row.append(get_random_char())
            char_grid.append(row)
        
        for y in range(height):
            for x in range(width):
                r, g, b, a = pixels[y, x]
                # Calculate position in the original-sized image
                pos_x = int(x * font_size * 0.6 * scale)
                pos_y = int(y * font_size * scale)
                
                # Get pre-generated random character for this position
                char = char_grid[y][x]
                
                if invert:
                    # Inverted mode: ASCII on transparent areas, black on non-transparent
                    if a < 128:  # Transparent area
                        # Use black for ASCII characters in transparent areas
                        draw.text((pos_x, pos_y), char, fill=(0, 0, 0, 255), font=font)
                    else:  # Non-transparent area
                        # Just fill with solid black
                        rect_size = int(font_size * 0.6 * scale)
                        draw.rectangle(
                            [(pos_x, pos_y), (pos_x + rect_size, pos_y + rect_size)], 
                            fill=(0, 0, 0, 255)
                        )
                else:
                    # Normal mode: ASCII on non-transparent areas
                    if a < 128:  # Skip transparent pixels
                        continue
                        
                    # Draw the character with the pixel's color
                    draw.text((pos_x, pos_y), char, fill=(r, g, b, a), font=font)
        
        # Save the resulting image
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
    parser.add_argument('--font-size', '-f', type=int, default=20, help='Font size for ASCII characters (default: 20)')
    parser.add_argument('--scale', '-s', type=float, default=3.0, help='Scale factor for the output (default: 3.0)')
    parser.add_argument('--invert', '-i', action='store_true', help='Invert the effect (ASCII on transparent areas instead of visible areas)')
    
    args = parser.parse_args()
    
    image_to_colored_ascii(args.input_image, args.output, args.font_size, args.scale, args.invert)

if __name__ == "__main__":
    main()