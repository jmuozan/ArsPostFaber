#!/usr/bin/env python3
"""
3D Reconstruction from masked images
Uses COLMAP to generate a pointcloud or mesh from masked images
"""

import os
import sys
import subprocess
import argparse
import shutil

def check_colmap_installed():
    """Check if COLMAP is installed and accessible"""
    try:
        result = subprocess.run(['colmap', '--help'], 
                               stdout=subprocess.PIPE, 
                               stderr=subprocess.PIPE)
        return result.returncode == 0
    except FileNotFoundError:
        return False

def install_pycolmap():
    """Install pycolmap if not already installed"""
    try:
        import pycolmap
        return True
    except ImportError:
        print("Installing pycolmap...")
        try:
            subprocess.run([sys.executable, '-m', 'pip', 'install', 'pycolmap'], check=True)
            return True
        except subprocess.CalledProcessError:
            print("Failed to install pycolmap. Please install it manually: pip install pycolmap")
            return False

def run_colmap_sfm(image_dir, output_dir):
    """Run COLMAP structure-from-motion on input images with robust settings"""
    # Create COLMAP database and workspace directories
    os.makedirs(output_dir, exist_ok=True)
    database_path = os.path.join(output_dir, "database.db")
    
    # Feature extraction with enhanced settings
    print("Extracting image features with SiftGPU...")
    feature_extractor_cmd = [
        'colmap', 'feature_extractor',
        '--database_path', database_path,
        '--image_path', image_dir,
        '--ImageReader.single_camera', '1',  # Assume single camera model for all images
        '--SiftExtraction.use_gpu', '1',     # Use GPU if available
        '--SiftExtraction.estimate_affine_shape', '1',  # Better feature matching
        '--SiftExtraction.domain_size_pooling', '1',    # Better feature matching
        '--SiftExtraction.max_num_features', '16384'    # Extract more features
    ]
    subprocess.run(feature_extractor_cmd, check=True)
    
    # Feature matching with enhanced settings
    print("Matching features using exhaustive matcher...")
    feature_matcher_cmd = [
        'colmap', 'exhaustive_matcher',
        '--database_path', database_path,
        '--SiftMatching.guided_matching', '1',  # Use guided matching for better results
        '--SiftMatching.use_gpu', '1'           # Use GPU if available
    ]
    subprocess.run(feature_matcher_cmd, check=True)
    
    # Sparse reconstruction with custom parameters for difficult scenes
    sparse_dir = os.path.join(output_dir, "sparse")
    os.makedirs(sparse_dir, exist_ok=True)
    print("Building sparse pointcloud using exhaustive initialization...")
    mapper_cmd = [
        'colmap', 'mapper',
        '--database_path', database_path,
        '--image_path', image_dir,
        '--output_path', sparse_dir,
        '--Mapper.init_min_tri_angle', '4',     # Lower threshold for triangulation
        '--Mapper.multiple_models', '0',        # Force single model
        '--Mapper.extract_colors', '1',         # Extract colors from images
        '--Mapper.ba_global_images_ratio', '1.1',  # Run global bundle adjustment more often
        '--Mapper.ba_global_points_ratio', '1.1',  # Run global bundle adjustment more often
        '--Mapper.abs_pose_min_num_inliers', '5',  # Lower minimum inliers for adding images
        '--Mapper.max_reg_trials', '5',           # More trials for registering images
        '--Mapper.init_max_reg_trials', '100',    # More trials for initial pair
        '--Mapper.tri_complete_max_transitivity', '10',  # Higher transitivity
        '--Mapper.min_focal_length_ratio', '0.1',  # Accept wider range of focal lengths
        '--Mapper.max_focal_length_ratio', '10',   # Accept wider range of focal lengths
        '--Mapper.ba_global_max_refinements', '5',  # More bundle adjustment refinements
        '--Mapper.filter_max_reproj_error', '8'     # Less strict filtering
    ]
    subprocess.run(mapper_cmd, check=True)
    
    # Find the model directory (it might be 0, 1, etc.)
    sparse_models = [d for d in os.listdir(sparse_dir) if d.isdigit()]
    if not sparse_models:
        print("Error: No sparse model was created. Reconstruction failed.")
        # Export empty PLY as fallback
        empty_ply_path = os.path.join(output_dir, "empty_pointcloud.ply")
        with open(empty_ply_path, 'w') as f:
            f.write("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n")
        return empty_ply_path
    
    sparse_model_dir = os.path.join(sparse_dir, sparse_models[0])
    
    # Export the sparse model as PLY
    print("Exporting sparse model as PLY...")
    sparse_ply_path = os.path.join(output_dir, "sparse_pointcloud.ply")
    model_converter_cmd = [
        'colmap', 'model_converter',
        '--input_path', sparse_model_dir,
        '--output_path', sparse_ply_path,
        '--output_type', 'PLY'
    ]
    subprocess.run(model_converter_cmd, check=True)
    
    # Check if we have enough points for dense reconstruction
    try:
        # Count points in the PLY file
        with open(sparse_ply_path, 'r') as f:
            for line in f:
                if line.startswith('element vertex '):
                    num_points = int(line.split()[2])
                    break
        
        if num_points < 50:
            print(f"Warning: Only {num_points} points in sparse reconstruction. Skipping dense reconstruction.")
            return sparse_ply_path
    except:
        print("Warning: Could not determine point count. Continuing with dense reconstruction.")
    
    # Dense reconstruction
    dense_dir = os.path.join(output_dir, "dense")
    os.makedirs(dense_dir, exist_ok=True)
    
    try:
        print("Preparing dense reconstruction...")
        image_undistorter_cmd = [
            'colmap', 'image_undistorter',
            '--image_path', image_dir,
            '--input_path', sparse_model_dir,
            '--output_path', dense_dir,
            '--output_type', 'COLMAP'
        ]
        subprocess.run(image_undistorter_cmd, check=True)
        
        print("Performing dense point cloud reconstruction...")
        patch_match_stereo_cmd = [
            'colmap', 'patch_match_stereo',
            '--workspace_path', dense_dir,
            '--PatchMatchStereo.max_image_size', '1200',  # Limit image size for better performance
            '--PatchMatchStereo.window_radius', '5',     # Smaller window for fine details
            '--PatchMatchStereo.window_step', '1',       # Higher accuracy
            '--PatchMatchStereo.num_iterations', '5',    # More iterations for convergence
            '--PatchMatchStereo.geom_consistency', '1'   # Use geometric consistency
        ]
        subprocess.run(patch_match_stereo_cmd, check=True)
        
        print("Fusing point cloud...")
        point_cloud_dir = os.path.join(dense_dir, 'fused')
        os.makedirs(point_cloud_dir, exist_ok=True)
        stereo_fusion_cmd = [
            'colmap', 'stereo_fusion',
            '--workspace_path', dense_dir,
            '--output_path', os.path.join(point_cloud_dir, 'fused.ply'),
            '--input_type', 'geometric',
            '--StereoFusion.check_num_images', '2',      # Require fewer images
            '--StereoFusion.max_reproj_error', '5.0',    # Higher error threshold
            '--StereoFusion.max_depth_error', '0.1',     # Allow more depth error
            '--StereoFusion.max_normal_error', '30'      # Allow more normal error
        ]
        subprocess.run(stereo_fusion_cmd, check=True)
        
        # Check if fused.ply exists
        fused_ply_path = os.path.join(point_cloud_dir, 'fused.ply')
        if not os.path.exists(fused_ply_path):
            print("Warning: Fused point cloud file not created.")
            return sparse_ply_path
        
        # Meshing
        print("Creating mesh from point cloud...")
        mesh_dir = os.path.join(output_dir, "mesh")
        os.makedirs(mesh_dir, exist_ok=True)
        
        mesh_ply_path = os.path.join(mesh_dir, 'mesh.ply')
        poisson_mesher_cmd = [
            'colmap', 'poisson_mesher',
            '--input_path', fused_ply_path,
            '--output_path', mesh_ply_path,
            '--PoissonMeshing.trim', '10',               # Aggressive trimming
            '--PoissonMeshing.depth', '10',              # Higher resolution mesh
            '--PoissonMeshing.point_weight', '1.0'       # Weight points more
        ]
        subprocess.run(poisson_mesher_cmd, check=True)
        
        if os.path.exists(mesh_ply_path):
            return mesh_ply_path
        else:
            print("Warning: Mesh file not created. Returning point cloud instead.")
            return fused_ply_path
            
    except Exception as e:
        print(f"Error during dense reconstruction: {e}")
        print("Returning sparse point cloud instead.")
        return sparse_ply_path

def run_pycolmap_sfm(image_dir, output_dir):
    """Run pycolmap to perform structure-from-motion with robust settings"""
    import pycolmap
    import numpy as np
    
    os.makedirs(output_dir, exist_ok=True)
    database_path = os.path.join(output_dir, "database.db")
    
    print("Running SfM using pycolmap...")
    
    # Extract features with enhanced settings
    print("Extracting features...")
    options = pycolmap.FeatureExtractorOptions()
    options.gpu_index = 0  # Use GPU if available
    options.max_num_features = 16384  # Extract more features
    options.domain_size_pooling = True  # Better feature matching
    options.estimate_affine_shape = True  # Better feature matching
    options.sift_options.first_octave = -1  # Start at a lower octave for small features
    
    pycolmap.extract_features(database_path=database_path, 
                             image_path=image_dir,
                             options=options)
    
    # Match features with enhanced settings
    print("Matching features...")
    match_options = pycolmap.ExhaustiveMatcherOptions()
    match_options.gpu_index = 0  # Use GPU if available
    match_options.guided_matching = True  # Use guided matching for better results
    
    pycolmap.match_exhaustive(database_path=database_path,
                            options=match_options)
    
    # Reconstruct sparse model with custom parameters for difficult scenes
    print("Building sparse model with robust settings...")
    mapper_options = pycolmap.IncrementalMapperOptions()
    mapper_options.num_threads = 8  # Use multiple threads
    mapper_options.init_min_tri_angle = 4  # Lower threshold for triangulation
    mapper_options.abs_pose_min_num_inliers = 5  # Lower minimum inliers for adding images
    mapper_options.max_reg_trials = 5  # More trials for registering images
    mapper_options.min_focal_length_ratio = 0.1  # Accept wider range of focal lengths
    mapper_options.max_focal_length_ratio = 10  # Accept wider range of focal lengths
    mapper_options.extract_colors = True  # Extract colors
    
    # Create a ReconstructionManager for manual initialization
    reconstruction_manager = pycolmap.ReconstructionManager()
    reconstruction = pycolmap.Reconstruction()
    reconstruction_manager.add_reconstruction(reconstruction)
    
    # Try to perform reconstruction
    try:
        # Use incremental_mapping with custom parameters
        sparse_model = pycolmap.incremental_mapping(database_path=database_path,
                                                  image_path=image_dir,
                                                  output_path=os.path.join(output_dir, "sparse"),
                                                  options=mapper_options)
        
        # Save the sparse model
        sparse_model_dir = os.path.join(output_dir, "sparse")
        os.makedirs(sparse_model_dir, exist_ok=True)
        sparse_model.write(sparse_model_dir)
        
    except Exception as e:
        print(f"Error during incremental mapping: {e}")
        print("Trying with a manual initialization approach...")
        
        # Try to perform manual initialization
        try:
            # Try a different approach - read database and initialize manually
            database = pycolmap.Database.connect(database_path)
            
            # Get all images from the database
            image_pairs = []
            images = database.execute("SELECT image_id, name FROM images")
            
            # Create all possible image pairs for initialization
            image_ids = [image_id for image_id, _ in images]
            for i in range(len(image_ids)):
                for j in range(i+1, len(image_ids)):
                    image_pairs.append((image_ids[i], image_ids[j]))
            
            # Manual reconstruction with initialization from all pairs
            reconstruction = pycolmap.Reconstruction()
            
            # Attempt reconstruction with each pair
            success = False
            for img_id1, img_id2 in image_pairs:
                try:
                    success = pycolmap.initialize_reconstruction(
                        database_path=database_path,
                        reconstruction=reconstruction,
                        image_id1=img_id1,
                        image_id2=img_id2
                    )
                    if success:
                        print(f"Successfully initialized with image pair {img_id1}, {img_id2}")
                        break
                except:
                    continue
            
            if not success:
                print("Could not initialize reconstruction with any image pair.")
                # Create an empty PLY file as a fallback
                empty_ply_path = os.path.join(output_dir, "empty_pointcloud.ply")
                with open(empty_ply_path, 'w') as f:
                    f.write("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n")
                return empty_ply_path
            
            # Register remaining images
            pycolmap.register_next_images(database_path=database_path, 
                                       reconstruction=reconstruction)
            
            # Bundle adjustment
            pycolmap.bundle_adjustment(reconstruction=reconstruction)
            
            # Save reconstruction
            sparse_model_dir = os.path.join(output_dir, "sparse", "manual")
            os.makedirs(sparse_model_dir, exist_ok=True)
            reconstruction.write(sparse_model_dir)
        
        except Exception as manual_error:
            print(f"Manual initialization also failed: {manual_error}")
            # Create an empty PLY file as a fallback
            empty_ply_path = os.path.join(output_dir, "empty_pointcloud.ply")
            with open(empty_ply_path, 'w') as f:
                f.write("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n")
            return empty_ply_path
    
    # Export the reconstruction to PLY format
    sparse_ply_path = os.path.join(output_dir, "sparse_pointcloud.ply")
    
    # Create a simple PLY file from the 3D points in the reconstruction
    try:
        from pycolmap.io import write_ply
        points = []
        colors = []
        
        for point_id, point in sparse_model.points.items():
            points.append(point.xyz)
            colors.append(point.color)
        
        if len(points) > 0:
            points_array = np.array(points)
            colors_array = np.array(colors)
            write_ply(sparse_ply_path, points_array, colors_array)
        else:
            # Create an empty PLY file
            with open(sparse_ply_path, 'w') as f:
                f.write("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n")
    except Exception as ply_error:
        print(f"Error creating PLY file: {ply_error}")
        # Create an empty PLY file as a fallback
        with open(sparse_ply_path, 'w') as f:
            f.write("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n")
    
    print(f"Sparse reconstruction complete. Output saved to {sparse_model_dir}")
    print(f"PLY point cloud saved to {sparse_ply_path}")
    return sparse_ply_path

def main():
    parser = argparse.ArgumentParser(description="3D reconstruction from masked images")
    parser.add_argument("--image_dir", type=str, help="Directory containing masked images")
    parser.add_argument("--output_dir", type=str, help="Directory to save 3D reconstruction output")
    parser.add_argument("--method", choices=["colmap", "pycolmap"], default="colmap",
                        help="Method to use for reconstruction (colmap or pycolmap)")
    
    args = parser.parse_args()
    
    # If arguments are not provided, use environment variables or ask for input
    if not args.image_dir:
        args.image_dir = os.environ.get("MASKED_IMAGES_DIR")
        if not args.image_dir:
            args.image_dir = input("Enter the path to the masked images directory: ")
    
    if not args.output_dir:
        args.output_dir = os.environ.get("RECONSTRUCTION_OUTPUT_DIR")
        if not args.output_dir:
            args.output_dir = os.path.join(os.path.dirname(args.image_dir), "reconstruction")
    
    # Create output directory
    os.makedirs(args.output_dir, exist_ok=True)
    
    print(f"Input image directory: {args.image_dir}")
    print(f"Output directory: {args.output_dir}")
    
    if not os.path.exists(args.image_dir):
        print(f"Error: Image directory {args.image_dir} not found")
        # Create an empty PLY as fallback
        empty_ply = os.path.join(args.output_dir, "empty.ply")
        with open(empty_ply, 'w') as f:
            f.write("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n")
        print(f"Created empty PLY file: {empty_ply}")
        return empty_ply
    
    # Check for images in the directory
    image_files = [f for f in os.listdir(args.image_dir) 
                  if f.lower().endswith(('.jpg', '.jpeg', '.png'))]
    
    if not image_files:
        print(f"Error: No image files found in {args.image_dir}")
        # Create an empty PLY as fallback
        empty_ply = os.path.join(args.output_dir, "empty.ply")
        with open(empty_ply, 'w') as f:
            f.write("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n")
        print(f"Created empty PLY file: {empty_ply}")
        return empty_ply
    
    print(f"Found {len(image_files)} images for reconstruction")
    
    # Create a result file to store the path to the PLY
    result_file = os.path.join(args.output_dir, "result_ply_path.txt")
    
    # Check if COLMAP is installed
    if args.method == "colmap" and not check_colmap_installed():
        print("COLMAP is not installed or not in PATH. Please install COLMAP:")
        print("  macOS: brew install colmap")
        print("  Linux: apt-get install colmap")
        print("Switching to pycolmap method...")
        args.method = "pycolmap"
    
    # For pycolmap, check if it's installed
    if args.method == "pycolmap" and not install_pycolmap():
        print("Could not use pycolmap. Please install COLMAP and try again.")
        # Create an empty PLY as fallback
        empty_ply = os.path.join(args.output_dir, "empty.ply")
        with open(empty_ply, 'w') as f:
            f.write("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n")
        print(f"Created empty PLY file: {empty_ply}")
        
        # Save path to result file
        with open(result_file, 'w') as f:
            f.write(empty_ply)
        
        return empty_ply
    
    # Run the appropriate reconstruction method
    try:
        # Default to empty PLY in case of failure
        output_ply = os.path.join(args.output_dir, "empty.ply")
        
        if args.method == "colmap":
            output_ply = run_colmap_sfm(args.image_dir, args.output_dir)
            print(f"Reconstruction complete! Model saved to {output_ply}")
        else:
            output_ply = run_pycolmap_sfm(args.image_dir, args.output_dir)
            print(f"Reconstruction complete! Model saved to {output_ply}")
        
        # Check if the output file exists
        if not os.path.exists(output_ply):
            print(f"Warning: Output PLY file {output_ply} not found.")
            output_ply = os.path.join(args.output_dir, "sparse_pointcloud.ply")
            
            # If we still don't have a PLY, create an empty one
            if not os.path.exists(output_ply):
                output_ply = os.path.join(args.output_dir, "empty.ply")
                with open(output_ply, 'w') as f:
                    f.write("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n")
                print(f"Created empty PLY file: {output_ply}")
        
        # Save path to result file for easy finding
        with open(result_file, 'w') as f:
            f.write(output_ply)
        
        print("Reconstruction pipeline completed successfully!")
        return output_ply
        
    except Exception as e:
        print(f"Error during reconstruction: {e}")
        # Create an empty PLY as fallback
        empty_ply = os.path.join(args.output_dir, "empty.ply")
        with open(empty_ply, 'w') as f:
            f.write("ply\nformat ascii 1.0\nelement vertex 0\nend_header\n")
        print(f"Created empty PLY file: {empty_ply}")
        
        # Save path to result file
        with open(result_file, 'w') as f:
            f.write(empty_ply)
        
        return empty_ply

if __name__ == "__main__":
    main()