using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.GUI.Canvas;
using Rhino.Geometry;

namespace crft
{
    public class SegmentationComponent : GH_Component
    {
        internal Bitmap _inputFrame;
        internal Bitmap _segmentationResult;
        internal Bitmap _maskResult;
        private bool _isRecording = false;
        private bool _isPythonAvailable = false;
        private string _videoOutputPath;
        private string _tempImagesDir;
        private string _pythonPath;
        private CancellationTokenSource _cancellationSource;
        private Task _segmentationTask;
        private Process _pythonProcess;
        private int _frameCount = 0;
        private string _segmentationOutputDir;
        private Dictionary<string, string> _modelPaths;
        private string _selectedModel = "vit_b"; // Default model type
        internal readonly object _lockObject = new object();

        /// <summary>
        /// Initializes a new instance of the SegmentationComponent class.
        /// </summary>
        public SegmentationComponent()
          : base("Segmentation", "Seg",
              "Segments objects in the input video feed using Segment Anything",
              "Display", "Segmentation")
        {
            // Create temp directory for segmentation
            _tempImagesDir = Path.Combine(Path.GetTempPath(), "gh_segmentation_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempImagesDir);
            
            _segmentationOutputDir = Path.Combine(_tempImagesDir, "output");
            Directory.CreateDirectory(_segmentationOutputDir);
            
            _videoOutputPath = Path.Combine(_tempImagesDir, "video.mp4");

            // Initialize model paths
            _modelPaths = new Dictionary<string, string>
            {
                { "vit_b", Path.Combine(Path.GetTempPath(), "sam_hq_vit_b.pth") },
                { "vit_l", Path.Combine(Path.GetTempPath(), "sam_hq_vit_l.pth") },
                { "vit_h", Path.Combine(Path.GetTempPath(), "sam_hq_vit_h.pth") }
            };

            // Check for Python installation
            CheckForPython();
        }

        private void CheckForPython()
        {
            try
            {
                Process process = new Process();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    process.StartInfo.FileName = "python3";
                    process.StartInfo.Arguments = "--version";
                }
                else
                {
                    process.StartInfo.FileName = "python";
                    process.StartInfo.Arguments = "--version";
                }
                
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode == 0)
                {
                    _isPythonAvailable = true;
                    _pythonPath = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "python3" : "python";
                    
                    // Check for required packages
                    CheckPythonPackages();
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Python not found. Please install Python 3.x");
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to check Python: " + ex.Message);
            }
        }

        private void CheckPythonPackages()
        {
            try
            {
                string[] requiredPackages = new string[] { "torch", "torchvision", "segment-anything-hq", "opencv-python", "numpy", "timm" };
                foreach (string package in requiredPackages)
                {
                    Process process = new Process();
                    process.StartInfo.FileName = _pythonPath;
                    process.StartInfo.Arguments = $"-c \"import {package}\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    process.WaitForExit();
                    
                    if (process.ExitCode != 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Package '{package}' not found. Installing...");
                        InstallPackage(package);
                    }
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to check Python packages: " + ex.Message);
            }
        }

        private void InstallPackage(string package)
        {
            try
            {
                Process process = new Process();
                process.StartInfo.FileName = _pythonPath;
                process.StartInfo.Arguments = $"-m pip install {package}";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Successfully installed {package}");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to install {package}: {error}");
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error installing {package}: {ex.Message}");
            }
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // Use standard generic parameter for maximum compatibility
            pManager.AddGenericParameter("Image", "I", "Input image frame from webcam", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Record", "R", "Start/stop recording and processing", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Model", "M", "Model size (0=tiny, 1=small, 2=large)", GH_ParamAccess.item, 0);
            
            // Make Record input optional to allow a toggle button
            pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Use generic parameters for better compatibility
            pManager.AddGenericParameter("Segmentation", "S", "Segmentation overlay image", GH_ParamAccess.item);
            pManager.AddGenericParameter("Mask", "M", "Segmentation mask image", GH_ParamAccess.item);
            pManager.AddTextParameter("VideoPath", "V", "Path to saved segmentation video", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object inputObj = null;
            bool record = false;
            int modelSize = 0;

            // Get data from inputs
            if (!DA.GetData(0, ref inputObj)) 
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No input image received");
                return;
            }
            
            if (!DA.GetData(1, ref record)) return;
            if (!DA.GetData(2, ref modelSize)) return;
            
            // Extract the bitmap from input object
            Bitmap inputImage = null;
            
            // Log what type of object we received
            string inputType = inputObj != null ? inputObj.GetType().FullName : "null";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Received input of type: {inputType}");
            
            try {
                // Handle our custom GH_Bitmap type
                if (inputObj is GH_Bitmap bitmapGoo)
                {
                    inputImage = bitmapGoo.Value;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Extracted bitmap from GH_Bitmap: {inputImage.Width}x{inputImage.Height}");
                }
                // Handle GH_ObjectWrapper
                else if (inputObj is Grasshopper.Kernel.Types.GH_ObjectWrapper wrapper)
                {
                    if (wrapper.Value is Bitmap wrappedBitmap)
                    {
                        inputImage = wrappedBitmap;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Extracted bitmap from GH_ObjectWrapper: {inputImage.Width}x{inputImage.Height}");
                    }
                    else
                    {
                        // Log what's in the wrapper
                        string wrappedType = wrapper.Value != null ? wrapper.Value.GetType().FullName : "null";
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Wrapper contains non-bitmap type: {wrappedType}");
                    }
                }
                // Direct bitmap - this is what the webcam component sends
                else if (inputObj is Bitmap bitmap)
                {
                    inputImage = bitmap;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Received direct bitmap: {inputImage.Width}x{inputImage.Height}");
                }
                // Try to extract from other GH_Goo types
                else if (inputObj is IGH_Goo goo)
                {
                    // Try to cast to bitmap directly 
                    Bitmap extractedBitmap = null;
                    if (goo.CastTo(out extractedBitmap) && extractedBitmap != null)
                    {
                        inputImage = extractedBitmap;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Cast IGH_Goo to Bitmap: {inputImage.Width}x{inputImage.Height}");
                    }
                    else
                    {
                        // Try alternate method
                        object target = null;
                        if (goo.CastTo<object>(out target) && target is Bitmap bmp)
                        {
                            inputImage = bmp;
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Cast IGH_Goo to object then to Bitmap: {inputImage.Width}x{inputImage.Height}");
                        }
                        else
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to cast {goo.TypeName} to Bitmap");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error extracting bitmap: {ex.Message}");
            }
            
            if (inputImage == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid input image. Expected a bitmap. Check the webcam component.");
                return;
            }

            // Map model size to model type
            string[] modelTypes = new string[] { "vit_b", "vit_l", "vit_h" };
            if (modelSize >= 0 && modelSize < modelTypes.Length)
            {
                _selectedModel = modelTypes[modelSize];
            }

            if (!_isPythonAvailable)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Python 3.x with required packages must be installed");
                return;
            }

            lock (_lockObject)
            {
                _inputFrame = inputImage;
            }

            if (record && !_isRecording)
            {
                StartRecording();
            }
            else if (!record && _isRecording)
            {
                StopRecording();
            }

            if (_segmentationResult != null)
            {
                // Use GH_ObjectWrapper for output - this works better with custom attributes
                DA.SetData(0, new Grasshopper.Kernel.Types.GH_ObjectWrapper(_segmentationResult));
            }

            if (_maskResult != null)
            {
                // Use GH_ObjectWrapper for output - this works better with custom attributes
                DA.SetData(1, new Grasshopper.Kernel.Types.GH_ObjectWrapper(_maskResult));
            }

            if (_isRecording || (!_isRecording && _frameCount > 0))
            {
                DA.SetData(2, _videoOutputPath);
            }
        }

        private void StartRecording()
        {
            try
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Starting segmentation process...");

                // Create fresh output directory
                if (Directory.Exists(_segmentationOutputDir))
                {
                    Directory.Delete(_segmentationOutputDir, true);
                }
                Directory.CreateDirectory(_segmentationOutputDir);
                Directory.CreateDirectory(Path.Combine(_segmentationOutputDir, "frames"));
                Directory.CreateDirectory(Path.Combine(_segmentationOutputDir, "masks"));
                Directory.CreateDirectory(Path.Combine(_segmentationOutputDir, "overlays"));

                // Reset frame count
                _frameCount = 0;

                // Create the Python segmentation script
                string scriptPath = CreatePythonScript();

                _cancellationSource = new CancellationTokenSource();
                _segmentationTask = Task.Run(() => SegmentationLoop(_cancellationSource.Token));

                _isRecording = true;
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error starting segmentation: " + ex.Message);
            }
        }

        private string CreatePythonScript()
        {
            string scriptPath = Path.Combine(_tempImagesDir, "segment.py");
            string scriptContent = @"
import os
import sys
import time
import numpy as np
import torch
import cv2
from segment_anything_hq import SamPredictor, sam_model_registry

# Set device
if torch.cuda.is_available():
    device = torch.device('cuda')
elif hasattr(torch, 'backends') and hasattr(torch.backends, 'mps') and torch.backends.mps.is_available():
    device = torch.device('mps')
else:
    device = torch.device('cpu')

print(f'Using device: {device}')

class RealTimeSegmenter:
    def __init__(self, input_dir, output_dir, model_type='vit_b', model_path=None):
        self.input_dir = input_dir
        self.output_dir = output_dir
        self.frames_dir = os.path.join(output_dir, 'frames')
        self.masks_dir = os.path.join(output_dir, 'masks')
        self.overlays_dir = os.path.join(output_dir, 'overlays')
        self.model_type = model_type
        self.model_path = model_path
        self.device = device
        self.predictor = None
        
        # Create output directories
        os.makedirs(self.frames_dir, exist_ok=True)
        os.makedirs(self.masks_dir, exist_ok=True)
        os.makedirs(self.overlays_dir, exist_ok=True)
        
        # Load model
        self.load_model()
    
    def load_model(self):
        print(f'Loading model {self.model_type}...')
        
        try:
            # Create the model first
            sam = sam_model_registry[self.model_type]()
            
            # Then load the checkpoint with the right map_location
            if self.model_path and os.path.exists(self.model_path):
                print(f'Loading checkpoint from {self.model_path}')
                sam_checkpoint = torch.load(self.model_path, map_location=self.device)
                sam.load_state_dict(sam_checkpoint)
            else:
                print('No checkpoint provided, using default weights')
            
            sam.to(device=self.device)
            self.predictor = SamPredictor(sam)
            print('Model loaded successfully')
        except Exception as e:
            print(f'Error loading model: {e}')
            sys.exit(1)
    
    def generate_keypoints(self, frame):
        # Generate points in the center area
        h, w = frame.shape[:2]
        
        # Get the center of the frame
        center_x, center_y = w // 2, h // 2
        
        # Create a grid of points (center and four quadrants)
        points = np.array([
            # Positive points (on the object)
            [center_x, center_y],  # Center point
            [center_x - w//8, center_y],  # Left
            [center_x + w//8, center_y],  # Right
            [center_x, center_y - h//8],  # Top
            [center_x, center_y + h//8],  # Bottom
        ])
        
        # Negative points (background)
        neg_points = np.array([
            [50, 50],  # Top-left corner
            [w-50, 50],  # Top-right corner
            [50, h-50],  # Bottom-left corner
            [w-50, h-50]  # Bottom-right corner
        ])
        
        # Labels (1 for positive, 0 for negative)
        pos_labels = np.ones(points.shape[0], dtype=np.int32)
        neg_labels = np.zeros(neg_points.shape[0], dtype=np.int32)
        
        # Combine points and labels
        all_points = np.vstack([points, neg_points])
        all_labels = np.hstack([pos_labels, neg_labels])
        
        return all_points, all_labels
    
    def process_frame(self, frame):
        # Generate keypoints
        points, labels = self.generate_keypoints(frame)
        
        # Set image in predictor
        self.predictor.set_image(frame)
        
        # Generate masks using points
        masks, scores, logits = self.predictor.predict(
            point_coords=points,
            point_labels=labels,
            multimask_output=True
        )
        
        # Get the highest-scoring mask
        best_mask = masks[np.argmax(scores)]
        
        # Create overlay
        overlay = frame.copy()
        overlay[best_mask] = overlay[best_mask] * 0.5 + np.array([0, 255, 0], dtype=np.uint8) * 0.5
        
        return best_mask, overlay
    
    def process_image(self, image_path, frame_number):
        # Read the input image
        frame = cv2.imread(image_path)
        if frame is None:
            print(f'Error: Could not read {image_path}')
            return None, None
        
        # Process the frame
        mask, overlay = self.process_frame(frame)
        
        # Save the original frame, mask, and overlay
        frame_output = os.path.join(self.frames_dir, f'frame_{frame_number:06d}.jpg')
        mask_output = os.path.join(self.masks_dir, f'mask_{frame_number:06d}.png')
        overlay_output = os.path.join(self.overlays_dir, f'overlay_{frame_number:06d}.jpg')
        
        cv2.imwrite(frame_output, frame)
        cv2.imwrite(mask_output, (mask * 255).astype(np.uint8))
        cv2.imwrite(overlay_output, overlay)
        
        return mask_output, overlay_output
    
    def run(self):
        frame_number = 0
        last_processed_image = None
        
        print('Starting segmentation process. Waiting for input images...')
        
        while True:
            # Look for the next input image
            files = sorted([f for f in os.listdir(self.input_dir) if f.endswith('.jpg') or f.endswith('.png')])
            
            if files:
                image_path = os.path.join(self.input_dir, files[0])
                
                # Skip if we've already processed this image
                if image_path == last_processed_image:
                    # Remove the file we've already processed
                    try:
                        os.remove(image_path)
                    except:
                        pass
                    time.sleep(0.05)
                    continue
                
                print(f'Processing frame {frame_number}: {image_path}')
                mask_path, overlay_path = self.process_image(image_path, frame_number)
                
                if mask_path and overlay_path:
                    # Print the paths so the C# component can pick them up
                    print(f'RESULT:{mask_path}|{overlay_path}')
                    sys.stdout.flush()
                    
                    # Remember this frame as processed
                    last_processed_image = image_path
                    frame_number += 1
                
                # Remove the file we've processed
                try:
                    os.remove(image_path)
                except:
                    pass
            
            # Check if there's a stop signal
            if os.path.exists(os.path.join(self.input_dir, 'STOP')):
                print('Stop signal detected. Exiting...')
                # Remove the stop signal
                try:
                    os.remove(os.path.join(self.input_dir, 'STOP'))
                except:
                    pass
                break
            
            # Short sleep to avoid hammering the file system
            time.sleep(0.05)
        
        # Create output video
        if frame_number > 0:
            self.create_output_video()
    
    def create_output_video(self):
        # Create output video from the overlay frames
        overlay_files = sorted([f for f in os.listdir(self.overlays_dir) if f.endswith('.jpg')])
        
        if not overlay_files:
            print('No frames to create video')
            return
        
        # Get frame properties from the first frame
        first_frame = cv2.imread(os.path.join(self.overlays_dir, overlay_files[0]))
        height, width, _ = first_frame.shape
        
        # Create the output video file
        fourcc = cv2.VideoWriter_fourcc(*'mp4v')
        video_path = os.path.join(self.output_dir, 'segmentation_video.mp4')
        out = cv2.VideoWriter(video_path, fourcc, 20.0, (width, height))
        
        # Write each frame to the video
        for file in overlay_files:
            frame = cv2.imread(os.path.join(self.overlays_dir, file))
            out.write(frame)
        
        out.release()
        print(f'Video created: {video_path}')
        
        # Create mask video as well
        mask_files = sorted([f for f in os.listdir(self.masks_dir) if f.endswith('.png')])
        
        if mask_files:
            mask_video_path = os.path.join(self.output_dir, 'mask_video.mp4')
            mask_out = cv2.VideoWriter(mask_video_path, fourcc, 20.0, (width, height))
            
            for file in mask_files:
                # Read the binary mask
                mask = cv2.imread(os.path.join(self.masks_dir, file), cv2.IMREAD_GRAYSCALE)
                
                # Convert to 3-channel
                mask_3channel = np.zeros((height, width, 3), dtype=np.uint8)
                mask_3channel[mask > 0] = [255, 255, 255]
                
                mask_out.write(mask_3channel)
            
            mask_out.release()
            print(f'Mask video created: {mask_video_path}')

if __name__ == '__main__':
    if len(sys.argv) < 3:
        print('Usage: python segment.py <input_dir> <output_dir> [model_type] [model_path]')
        sys.exit(1)
    
    input_dir = sys.argv[1]
    output_dir = sys.argv[2]
    model_type = sys.argv[3] if len(sys.argv) > 3 else 'vit_b'
    model_path = sys.argv[4] if len(sys.argv) > 4 else None
    
    segmenter = RealTimeSegmenter(input_dir, output_dir, model_type, model_path)
    segmenter.run()
";

            File.WriteAllText(scriptPath, scriptContent);
            return scriptPath;
        }

        private void SegmentationLoop(CancellationToken token)
        {
            try
            {
                string framesDir = Path.Combine(_tempImagesDir, "frames_input");
                Directory.CreateDirectory(framesDir);

                string scriptPath = Path.Combine(_tempImagesDir, "segment.py");
                string modelPath = _modelPaths[_selectedModel];

                // Start Python process
                _pythonProcess = new Process();
                _pythonProcess.StartInfo.FileName = _pythonPath;
                _pythonProcess.StartInfo.Arguments = $"\"{scriptPath}\" \"{framesDir}\" \"{_segmentationOutputDir}\" {_selectedModel} \"{modelPath}\"";
                _pythonProcess.StartInfo.UseShellExecute = false;
                _pythonProcess.StartInfo.RedirectStandardOutput = true;
                _pythonProcess.StartInfo.RedirectStandardError = true;
                _pythonProcess.StartInfo.CreateNoWindow = true;

                // Set up output handlers
                _pythonProcess.OutputDataReceived += (sender, e) => 
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Debug.WriteLine($"Python: {e.Data}");
                        
                        // Look for result paths in the output
                        if (e.Data.StartsWith("RESULT:"))
                        {
                            string[] paths = e.Data.Substring(7).Split('|');
                            if (paths.Length == 2)
                            {
                                string maskPath = paths[0];
                                string overlayPath = paths[1];
                                
                                try
                                {
                                    if (File.Exists(maskPath) && File.Exists(overlayPath))
                                    {
                                        lock (_lockObject)
                                        {
                                            // Load the mask and overlay images
                                            using (Bitmap maskImg = new Bitmap(maskPath))
                                            {
                                                if (_maskResult != null)
                                                {
                                                    _maskResult.Dispose();
                                                }
                                                _maskResult = new Bitmap(maskImg);
                                            }
                                            
                                            using (Bitmap overlayImg = new Bitmap(overlayPath))
                                            {
                                                if (_segmentationResult != null)
                                                {
                                                    _segmentationResult.Dispose();
                                                }
                                                _segmentationResult = new Bitmap(overlayImg);
                                            }
                                        }
                                        
                                        // Force UI update
                                        if (Grasshopper.Instances.ActiveCanvas != null)
                                        {
                                            Grasshopper.Instances.ActiveCanvas.Invoke(new Action(() => 
                                            {
                                                Grasshopper.Instances.ActiveCanvas.Invalidate();
                                            }));
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error loading result images: {ex.Message}");
                                }
                            }
                        }
                    }
                };
                
                _pythonProcess.ErrorDataReceived += (sender, e) => 
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Debug.WriteLine($"Python error: {e.Data}");
                    }
                };

                _pythonProcess.Start();
                _pythonProcess.BeginOutputReadLine();
                _pythonProcess.BeginErrorReadLine();

                // Main recording loop
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Grab the current input frame
                        Bitmap currentFrame = null;
                        lock (_lockObject)
                        {
                            if (_inputFrame != null)
                            {
                                currentFrame = (Bitmap)_inputFrame.Clone();
                            }
                        }

                        if (currentFrame != null)
                        {
                            // Save the frame to disk for Python to process
                            string framePath = Path.Combine(framesDir, $"frame_{_frameCount:D6}.jpg");
                            currentFrame.Save(framePath, System.Drawing.Imaging.ImageFormat.Jpeg);
                            _frameCount++;
                            
                            // Clean up the cloned frame
                            currentFrame.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in segmentation loop: {ex.Message}");
                    }

                    // Short sleep to avoid hammering the CPU
                    Thread.Sleep(50);
                }

                // Signal Python to stop
                File.WriteAllText(Path.Combine(framesDir, "STOP"), "");
                
                // Wait for Python to finish
                _pythonProcess.WaitForExit();
                
                // Make sure we have the final video path
                string videoFile = Path.Combine(_segmentationOutputDir, "segmentation_video.mp4");
                if (File.Exists(videoFile))
                {
                    _videoOutputPath = videoFile;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Segmentation error: {ex.Message}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Segmentation error: {ex.Message}");
            }
            finally
            {
                // Clean up Python process
                if (_pythonProcess != null && !_pythonProcess.HasExited)
                {
                    try
                    {
                        _pythonProcess.Kill();
                    }
                    catch { }
                }
            }
        }

        private void StopRecording()
        {
            try
            {
                if (_isRecording)
                {
                    // Cancel the segmentation task
                    _cancellationSource?.Cancel();
                    
                    // Reset flags
                    _isRecording = false;
                    
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Segmentation completed. Processed {_frameCount} frames.");
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error stopping segmentation: " + ex.Message);
            }
        }

        public override void CreateAttributes()
        {
            m_attributes = new SegmentationComponentAttributes(this);
        }

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            
            // Add a menu item to download model if needed
            ToolStripMenuItem downloadItem = new ToolStripMenuItem("Download Model Files") { ToolTipText = "Download required model files" };
            downloadItem.Click += (sender, e) => DownloadModelFiles();
            menu.Items.Add(downloadItem);
        }

        private void DownloadModelFiles()
        {
            Task.Run(() => 
            {
                try
                {
                    // Create Python script to download model files
                    string scriptPath = Path.Combine(_tempImagesDir, "download_models.py");
                    string scriptContent = @"
import os
import sys
import torch
from huggingface_hub import hf_hub_download

def download_models(output_dir):
    models = {
        'sam_hq_vit_b.pth': ('lkeab/hq-sam', 'sam_hq_vit_b.pth'),
        'sam_hq_vit_l.pth': ('lkeab/hq-sam', 'sam_hq_vit_l.pth'),
        'sam_hq_vit_h.pth': ('lkeab/hq-sam', 'sam_hq_vit_h.pth')
    }
    
    for filename, (repo_id, file_id) in models.items():
        output_path = os.path.join(output_dir, filename)
        if not os.path.exists(output_path):
            print(f'Downloading {filename}...')
            try:
                hf_hub_download(repo_id=repo_id, filename=file_id, local_dir=output_dir)
                print(f'Successfully downloaded {filename}')
            except Exception as e:
                print(f'Error downloading {filename}: {e}')
        else:
            print(f'{filename} already exists at {output_path}')

if __name__ == '__main__':
    output_dir = sys.argv[1] if len(sys.argv) > 1 else '.'
    download_models(output_dir)
";
                    File.WriteAllText(scriptPath, scriptContent);
                    
                    // Run the script
                    Process process = new Process();
                    process.StartInfo.FileName = _pythonPath;
                    process.StartInfo.Arguments = $"\"{scriptPath}\" \"{Path.GetTempPath()}\"";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.CreateNoWindow = true;
                    process.Start();
                    
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    
                    if (process.ExitCode == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Successfully downloaded model files");
                    }
                    else
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error downloading models: {error}");
                    }
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Error downloading models: {ex.Message}");
                }
            });
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            // Stop recording before serialization
            if (_isRecording)
            {
                StopRecording();
            }
            return base.Write(writer);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopRecording();
            
            // Clean up bitmaps
            lock (_lockObject)
            {
                if (_segmentationResult != null)
                {
                    _segmentationResult.Dispose();
                    _segmentationResult = null;
                }
                
                if (_maskResult != null)
                {
                    _maskResult.Dispose();
                    _maskResult = null;
                }
            }
            
            // Try to clean up temp directory
            try
            {
                if (Directory.Exists(_tempImagesDir))
                {
                    Directory.Delete(_tempImagesDir, true);
                }
            }
            catch
            {
                // Ignore errors when cleaning up
            }
            
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                StopRecording();
            }
            base.DocumentContextChanged(document, context);
        }

        // No override for Dispose since GH_Component doesn't have one
        // We'll add cleanup in the Removed event instead

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("32e9c49a-7d4b-4e18-ac37-8c851fe11542"); }
        }
    }

    public class SegmentationComponentAttributes : Grasshopper.Kernel.Attributes.GH_ComponentAttributes
    {
        private readonly new SegmentationComponent Owner;
        private RectangleF PreviewBounds;
        private RectangleF MaskPreviewBounds;
        private RectangleF ComponentBounds;
        private const int PreviewWidth = 320;
        private const int PreviewHeight = 240;

        public SegmentationComponentAttributes(SegmentationComponent owner) : base(owner)
        {
            Owner = owner;
        }

        protected override void Layout()
        {
            // First calculate the standard component layout
            base.Layout();
            
            // Get the standard component bounds
            Rectangle baseRectangle = GH_Convert.ToRectangle(Bounds);
            
            // Remember the original component area for parameter positioning
            ComponentBounds = new RectangleF(
                baseRectangle.X, 
                baseRectangle.Y, 
                baseRectangle.Width, 
                baseRectangle.Height
            );
            
            // Determine width to fit the preview centered
            int totalWidth = Math.Max(baseRectangle.Width, 2 * PreviewWidth + 30);
            
            // Make the component wider if needed to fit both previews
            if (totalWidth > baseRectangle.Width)
            {
                baseRectangle.Width = totalWidth;
            }
            
            // Add space for the previews below the standard component UI
            baseRectangle.Height += PreviewHeight + 10;
            
            // Calculate segmentation preview rectangle - left side
            PreviewBounds = new RectangleF(
                baseRectangle.X + 10, 
                ComponentBounds.Bottom + 5, 
                PreviewWidth,
                PreviewHeight
            );
            
            // Calculate mask preview rectangle - right side
            MaskPreviewBounds = new RectangleF(
                baseRectangle.X + 20 + PreviewWidth, 
                ComponentBounds.Bottom + 5, 
                PreviewWidth,
                PreviewHeight
            );
            
            Bounds = baseRectangle;
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                // For objects channel (main component rendering)
                base.Render(canvas, graphics, channel);
                
                // Draw borders for preview areas
                graphics.DrawRectangle(Pens.DarkGray, PreviewBounds.X, PreviewBounds.Y, PreviewBounds.Width, PreviewBounds.Height);
                graphics.DrawRectangle(Pens.DarkGray, MaskPreviewBounds.X, MaskPreviewBounds.Y, MaskPreviewBounds.Width, MaskPreviewBounds.Height);
                
                // Draw labels for previews
                StringFormat format = new StringFormat();
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Near;
                
                graphics.DrawString("Segmentation", GH_FontServer.Small, Brushes.Black, 
                    new RectangleF(PreviewBounds.X, PreviewBounds.Y - 15, PreviewBounds.Width, 15), format);
                    
                graphics.DrawString("Mask", GH_FontServer.Small, Brushes.Black, 
                    new RectangleF(MaskPreviewBounds.X, MaskPreviewBounds.Y - 15, MaskPreviewBounds.Width, 15), format);
                
                // Draw the segmentation result
                Bitmap segmentation = null;
                Bitmap mask = null;
                
                // Get reference to segmentation result and mask in a thread-safe way
                object lockObj = Owner._lockObject;
                if (lockObj != null)
                {
                    lock (lockObj)
                    {
                        if (Owner._segmentationResult != null)
                        {
                            segmentation = Owner._segmentationResult;
                        }
                        
                        if (Owner._maskResult != null)
                        {
                            mask = Owner._maskResult;
                        }
                    }
                }
                
                if (segmentation != null)
                {
                    // Calculate proportional dimensions to maintain aspect ratio
                    float frameRatio = (float)segmentation.Width / segmentation.Height;
                    float previewRatio = PreviewBounds.Width / PreviewBounds.Height;
                    
                    RectangleF targetRect;
                    
                    if (frameRatio > previewRatio)
                    {
                        // Image is wider than preview area - fit to width
                        float targetHeight = PreviewBounds.Width / frameRatio;
                        float yOffset = (PreviewBounds.Height - targetHeight) / 2;
                        targetRect = new RectangleF(
                            PreviewBounds.X,
                            PreviewBounds.Y + yOffset,
                            PreviewBounds.Width,
                            targetHeight
                        );
                    }
                    else
                    {
                        // Image is taller than preview area - fit to height
                        float targetWidth = PreviewBounds.Height * frameRatio;
                        float xOffset = (PreviewBounds.Width - targetWidth) / 2;
                        targetRect = new RectangleF(
                            PreviewBounds.X + xOffset,
                            PreviewBounds.Y,
                            targetWidth,
                            PreviewBounds.Height
                        );
                    }
                    
                    // Draw the segmentation with proper aspect ratio
                    graphics.DrawImage(segmentation, targetRect);
                }
                else
                {
                    // Draw a message when no segmentation is available
                    format.LineAlignment = StringAlignment.Center;
                    graphics.DrawString("No segmentation available", GH_FontServer.Standard, Brushes.DarkGray, PreviewBounds, format);
                }
                
                // Draw the mask result
                if (mask != null)
                {
                    // Calculate proportional dimensions to maintain aspect ratio
                    float frameRatio = (float)mask.Width / mask.Height;
                    float previewRatio = MaskPreviewBounds.Width / MaskPreviewBounds.Height;
                    
                    RectangleF targetRect;
                    
                    if (frameRatio > previewRatio)
                    {
                        // Image is wider than preview area - fit to width
                        float targetHeight = MaskPreviewBounds.Width / frameRatio;
                        float yOffset = (MaskPreviewBounds.Height - targetHeight) / 2;
                        targetRect = new RectangleF(
                            MaskPreviewBounds.X,
                            MaskPreviewBounds.Y + yOffset,
                            MaskPreviewBounds.Width,
                            targetHeight
                        );
                    }
                    else
                    {
                        // Image is taller than preview area - fit to height
                        float targetWidth = MaskPreviewBounds.Height * frameRatio;
                        float xOffset = (MaskPreviewBounds.Width - targetWidth) / 2;
                        targetRect = new RectangleF(
                            MaskPreviewBounds.X + xOffset,
                            MaskPreviewBounds.Y,
                            targetWidth,
                            MaskPreviewBounds.Height
                        );
                    }
                    
                    // Draw the mask with proper aspect ratio
                    graphics.DrawImage(mask, targetRect);
                }
                else
                {
                    // Draw a message when no mask is available
                    format.LineAlignment = StringAlignment.Center;
                    graphics.DrawString("No mask available", GH_FontServer.Standard, Brushes.DarkGray, MaskPreviewBounds, format);
                }
            }
            else
            {
                // For all other channels, use default rendering
                base.Render(canvas, graphics, channel);
            }
        }
    }
}