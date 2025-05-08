
Improving dense reconstruction results for weakly textured surfaces

For scenes with weakly textured surfaces it can help to have a high resolution of the input images (--PatchMatchStereo.max_image_size) and a large patch window radius (--PatchMatchStereo.window_radius). You may also want to reduce the filtering threshold for the photometric consistency cost (--PatchMatchStereo.filter_min_ncc).
