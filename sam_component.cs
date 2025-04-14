using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SAMforGrasshopper
{
    public class SAMComponent : GH_Component
    {
        private SAM _sam;
        private float[] _imageEmbedding;
        private Bitmap _currentImage;
        private readonly object _lockObject = new object();
        private bool _modelsLoaded = false;
        private string _modelPath;
        private List<PointPromotion> _pointPromotions = new List<PointPromotion>();
        private List<BoxPromotion> _boxPromotions = new List<BoxPromotion>();
        internal Bitmap _maskImage;
        private VideoProcessor _videoProcessor;
        private string _inputPath = string.Empty;
        private bool _isVideo = false;
        private int _currentFrame = 0;
        private PointEditorForm _pointEditorForm;
        private bool _showEditor = false;
        private string _originalModelFile;
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// Initializes a new instance of the SAMComponent class.
        /// </summary>
        public SAMComponent()
          : base("Segment Anything", "SAM",
              "Segment Anything Model for image and video segmentation",
              "Image", "Segmentation")
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Input Path", "I", "Path to the image or video to segment", GH_ParamAccess.item);
            pManager.AddTextParameter("Model Path", "M", "Path to the folder containing SAM model or leave empty to use the default model", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Show Editor", "E", "Show the point editor window", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Frame", "F", "Frame number for video (0 for images)", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Reset", "R", "Reset all points and masks", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Mask Image", "M", "Segmentation mask as bitmap", GH_ParamAccess.item);
            pManager.AddGenericParameter("Curves", "C", "Segmentation boundaries as curves", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Total Frames", "TF", "Total number of frames in video", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string inputPath = "";
            string modelPath = "";
            bool showEditor = false;
            int frameNumber = 0;
            bool reset = false;

            if (!DA.GetData(0, ref inputPath)) return;
            DA.GetData(1, ref modelPath);
            DA.GetData(2, ref showEditor);
            DA.GetData(3, ref frameNumber);
            DA.GetData(4, ref reset);

            // Check if input path exists
            if (!File.Exists(inputPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input path does not exist");
                return;
            }

            // Determine if input is video or image
            string extension = Path.GetExtension(inputPath).ToLower();
            bool isVideo = extension == ".mp4" || extension == ".avi" || extension == ".mov" || extension == ".wmv";

            // Check if path or type changed
            bool inputChanged = (inputPath != _inputPath) || (isVideo != _isVideo);
            
            // Initialize SAM if not already initialized or model path changed
            if (_sam == null || modelPath != _modelPath || reset)
            {
                _modelPath = modelPath;
                _originalModelFile = GetPyTorchModelPath();
                InitializeSAM();
            }

            // If the input changed or we're resetting
            if (inputChanged || reset)
            {
                _inputPath = inputPath;
                _isVideo = isVideo;
                _currentFrame = 0;
                _pointPromotions.Clear();
                _boxPromotions.Clear();
                
                // Cancel any ongoing processing
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Create video processor if it's a video
                if (isVideo)
                {
                    try
                    {
                        _videoProcessor = new VideoProcessor(inputPath);
                        _videoProcessor.Initialize();
                    }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error initializing video: " + ex.Message);
                        return;
                    }
                }
            }

            // Handle showing/hiding the point editor
            if (showEditor != _showEditor)
            {
                _showEditor = showEditor;
                if (showEditor)
                {
                    ShowPointEditor();
                }
                else if (_pointEditorForm != null)
                {
                    _pointEditorForm.Close();
                    _pointEditorForm = null;
                }
            }

            // Load the image or video frame
            if (_isVideo)
            {
                if (_videoProcessor != null)
                {
                    if (frameNumber != _currentFrame || reset)
                    {
                        _currentFrame = frameNumber;
                        try
                        {
                            LoadVideoFrame(frameNumber);
                        }
                        catch (Exception ex)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error loading video frame: " + ex.Message);
                            return;
                        }
                    }
                }
            }
            else if (inputChanged || reset)
            {
                try
                {
                    LoadImage(inputPath);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error loading image: " + ex.Message);
                    return;
                }
            }

            // Perform segmentation
            if (_currentImage != null && _imageEmbedding != null && (_pointPromotions.Count > 0 || _boxPromotions.Count > 0))
            {
                PerformSegmentation();
            }

            // Output mask image
            if (_maskImage != null)
            {
                DA.SetData(0, new GH_ObjectWrapper(_maskImage));
            }

            // Convert mask to curves
            if (_maskImage != null)
            {
                List<Curve> curves = ConvertMaskToCurves(_maskImage);
                DA.SetDataList(1, curves);
            }

            // Output total frames for video
            if (_isVideo && _videoProcessor != null)
            {
                DA.SetData(2, _videoProcessor.TotalFrames);
            }
            else
            {
                DA.SetData(2, 1); // Just one frame for images
            }
        }

        private string GetPyTorchModelPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string modelFile = Path.Combine(baseDirectory, "sam_vit_b_01ec64.pth");
            
            if (File.Exists(modelFile))
            {
                return modelFile;
            }
            
            // Try to find the model in the specified model path
            if (!string.IsNullOrEmpty(_modelPath) && File.Exists(Path.Combine(_modelPath, "sam_vit_b_01ec64.pth")))
            {
                return Path.Combine(_modelPath, "sam_vit_b_01ec64.pth");
            }
            
            return string.Empty;
        }

        private void InitializeSAM()
        {
            try
            {
                _sam = new SAM();
                
                // Set model path if provided
                if (!string.IsNullOrEmpty(_modelPath))
                {
                    _sam.SetModelPath(_modelPath);
                }
                
                // Check if PyTorch model exists and needs conversion
                if (!string.IsNullOrEmpty(_originalModelFile) && File.Exists(_originalModelFile))
                {
                    string encoderPath = Path.Combine(_modelPath, "encoder-quant.onnx");
                    string decoderPath = Path.Combine(_modelPath, "decoder-quant.onnx");
                    
                    if (!File.Exists(encoderPath) || !File.Exists(decoderPath))
                    {
                        // Convert PyTorch model to ONNX
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Converting PyTorch model to ONNX. This may take a few minutes...");
                        ConvertPyTorchToONNX(_originalModelFile, _modelPath);
                    }
                }
                
                // Load models on a background thread to avoid blocking the UI
                Task.Run(() => 
                {
                    try
                    {
                        _sam.LoadONNXModel();
                        _modelsLoaded = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading SAM models: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error initializing SAM: " + ex.Message);
            }
        }

        private void ConvertPyTorchToONNX(string pytorchPath, string outputDir)
        {
            try
            {
                // Ensure the output directory exists
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // Check if Python is installed
                string pythonExe = FindPythonExecutable();
                if (string.IsNullOrEmpty(pythonExe))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Python not found. Please install Python and try again.");
                    return;
                }
                
                // Create a temporary Python script to convert the model
                string scriptPath = Path.Combine(Path.GetTempPath(), "convert_sam_to_onnx.py");
                File.WriteAllText(scriptPath, GetPythonConversionScript());
                
                // Execute the Python script
                ProcessStartInfo psi = new ProcessStartInfo(pythonExe);
                psi.Arguments = $"\"{scriptPath}\" \"{pytorchPath}\" \"{outputDir}\"";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                
                Process process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode != 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to convert PyTorch model to ONNX: " + error);
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Successfully converted PyTorch model to ONNX.");
                }
                
                // Clean up
                File.Delete(scriptPath);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error converting model: " + ex.Message);
            }
        }

        private string FindPythonExecutable()
        {
            // First try the PATH environment variable
            string[] possibleNames = { "python", "python3" };
            foreach (string name in possibleNames)
            {
                string executable = FindExecutableInPath(name);
                if (!string.IsNullOrEmpty(executable))
                {
                    return executable;
                }
            }
            
            // Then try common installation locations
            string[] commonLocations = {
                @"C:\Python39\python.exe",
                @"C:\Python310\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Program Files\Python39\python.exe",
                @"C:\Program Files\Python310\python.exe",
                @"C:\Program Files\Python311\python.exe",
                @"C:\Program Files (x86)\Python39\python.exe",
                @"C:\Program Files (x86)\Python310\python.exe",
                @"C:\Program Files (x86)\Python311\python.exe"
            };
            
            foreach (string location in commonLocations)
            {
                if (File.Exists(location))
                {
                    return location;
                }
            }
            
            return string.Empty;
        }

        private string FindExecutableInPath(string name)
        {
            string pathVar = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVar))
                return string.Empty;
                
            foreach (string path in pathVar.Split(Path.PathSeparator))
            {
                try
                {
                    string fullPath = Path.Combine(path, name + ".exe");
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    // Skip invalid paths
                }
            }
            
            return string.Empty;
        }

        private string GetPythonConversionScript()
        {
            return @"
import sys
import torch
import os
from segment_anything import sam_model_registry

def convert_sam_to_onnx(checkpoint_path, output_dir):
    sam_model = sam_model_registry['vit_b'](checkpoint=checkpoint_path)
    
    # Convert model to ONNX
    onnx_encoder_path = os.path.join(output_dir, 'encoder-quant.onnx')
    onnx_decoder_path = os.path.join(output_dir, 'decoder-quant.onnx')
    
    # Set the model to evaluation mode
    sam_model.eval()
    
    # Export encoder
    image_embedding_size = 256
    encoder_input = torch.randn(1, 3, 1024, 1024, dtype=torch.float32)
    
    dynamic_axes = {
        'x': {0: 'batch', 2: 'height', 3: 'width'}
    }
    
    torch.onnx.export(
        sam_model.image_encoder,
        encoder_input,
        onnx_encoder_path,
        export_params=True,
        verbose=False,
        opset_version=17,
        do_constant_folding=True,
        input_names=['x'],
        output_names=['y'],
        dynamic_axes=dynamic_axes
    )
    
    # Export decoder
    decoder_input_embedding = torch.randn(1, image_embedding_size, 64, 64, dtype=torch.float32)
    decoder_input_point_coords = torch.randint(0, 1024, (1, 5, 2), dtype=torch.float32)
    decoder_input_point_labels = torch.randint(0, 4, (1, 5), dtype=torch.float32)
    decoder_input_mask_input = torch.zeros(1, 1, 256, 256, dtype=torch.float32)
    decoder_input_has_mask_input = torch.zeros(1, dtype=torch.float32)
    decoder_input_orig_im_size = torch.tensor([1024, 1024], dtype=torch.float32)
    
    decoder_inputs = (
        decoder_input_embedding,
        decoder_input_point_coords,
        decoder_input_point_labels,
        decoder_input_mask_input,
        decoder_input_has_mask_input,
        decoder_input_orig_im_size
    )
    
    torch.onnx.export(
        sam_model.mask_decoder,
        decoder_inputs,
        onnx_decoder_path,
        export_params=True,
        verbose=False,
        opset_version=17,
        do_constant_folding=True,
        input_names=[
            'image_embeddings',
            'point_coords',
            'point_labels',
            'mask_input',
            'has_mask_input',
            'orig_im_size'
        ],
        output_names=['masks', 'iou_predictions', 'low_res_masks']
    )
    
    print(f'Exported encoder to {onnx_encoder_path}')
    print(f'Exported decoder to {onnx_decoder_path}')

if __name__ == '__main__':
    if len(sys.argv) < 3:
        print('Usage: python convert_sam_to_onnx.py <checkpoint_path> <output_dir>')
        sys.exit(1)
        
    checkpoint_path = sys.argv[1]
    output_dir = sys.argv[2]
    
    try:
        convert_sam_to_onnx(checkpoint_path, output_dir)
        sys.exit(0)
    except Exception as e:
        print(f'Error: {e}', file=sys.stderr)
        sys.exit(1)
";
        }

        private void LoadImage(string imagePath)
        {
            try
            {
                // Load the image using OpenCV
                using (var cvImage = OpenCvSharp.Cv2.ImRead(imagePath, OpenCvSharp.ImreadModes.Color))
                {
                    if (cvImage.Empty())
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error loading image");
                        return;
                    }

                    // Store image
                    _currentImage = new Bitmap(cvImage.Width, cvImage.Height);
                    using (Graphics g = Graphics.FromImage(_currentImage))
                    {
                        // Convert OpenCV mat to Bitmap
                        OpenCvSharp.Mat bgrMat = new OpenCvSharp.Mat();
                        OpenCvSharp.Cv2.CvtColor(cvImage, bgrMat, OpenCvSharp.ColorConversionCodes.BGR2RGB);
                        
                        System.Drawing.Imaging.BitmapData bmpData = _currentImage.LockBits(
                            new Rectangle(0, 0, _currentImage.Width, _currentImage.Height),
                            System.Drawing.Imaging.ImageLockMode.WriteOnly,
                            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                        
                        // Copy data
                        IntPtr ptr = bmpData.Scan0;
                        byte[] rgbValues = new byte[bmpData.Stride * bmpData.Height];
                        Marshal.Copy(bgrMat.Data, rgbValues, 0, rgbValues.Length);
                        Marshal.Copy(rgbValues, 0, ptr, rgbValues.Length);
                        
                        _currentImage.UnlockBits(bmpData);
                    }
                    
                    // Update point editor if open
                    if (_pointEditorForm != null)
                    {
                        _pointEditorForm.UpdateImage(_currentImage);
                    }
                    
                    // Wait for models to load before computing embeddings
                    if (!_modelsLoaded)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Waiting for SAM models to load...");
                        int maxWaitTime = 30000; // 30 seconds
                        int waited = 0;
                        int waitInterval = 500;
                        
                        while (!_modelsLoaded && waited < maxWaitTime)
                        {
                            Thread.Sleep(waitInterval);
                            waited += waitInterval;
                        }
                        
                        if (!_modelsLoaded)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Timed out waiting for SAM models to load");
                            return;
                        }
                    }
                    
                    // Compute image embedding in a background task
                    Task.Run(() => {
                        try
                        {
                            lock (_lockObject)
                            {
                                _imageEmbedding = _sam.Encode(cvImage, cvImage.Width, cvImage.Height);
                            }
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Image loaded and encoded successfully");
                        }
                        catch (Exception ex)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error encoding image: " + ex.Message);
                        }
                    }, _cancellationTokenSource.Token);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error loading image: " + ex.Message);
            }
        }

        private void LoadVideoFrame(int frameNumber)
        {
            if (_videoProcessor == null)
                return;
                
            if (frameNumber < 0 || frameNumber >= _videoProcessor.TotalFrames)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Frame {frameNumber} out of range (0-{_videoProcessor.TotalFrames - 1})");
                return;
            }
            
            OpenCvSharp.Mat frame = _videoProcessor.GetFrame(frameNumber);
            if (frame.Empty())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to read frame from video");
                return;
            }
            
            // Convert frame to bitmap
            _currentImage = new Bitmap(frame.Width, frame.Height);
            using (Graphics g = Graphics.FromImage(_currentImage))
            {
                // Convert OpenCV mat to Bitmap
                OpenCvSharp.Mat bgrMat = new OpenCvSharp.Mat();
                OpenCvSharp.Cv2.CvtColor(frame, bgrMat, OpenCvSharp.ColorConversionCodes.BGR2RGB);
                
                System.Drawing.Imaging.BitmapData bmpData = _currentImage.LockBits(
                    new Rectangle(0, 0, _currentImage.Width, _currentImage.Height),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                
                // Copy data
                IntPtr ptr = bmpData.Scan0;
                byte[] rgbValues = new byte[bmpData.Stride * bmpData.Height];
                Marshal.Copy(bgrMat.Data, rgbValues, 0, rgbValues.Length);
                Marshal.Copy(rgbValues, 0, ptr, rgbValues.Length);
                
                _currentImage.UnlockBits(bmpData);
            }
            
            // Update point editor if open
            if (_pointEditorForm != null)
            {
                _pointEditorForm.UpdateImage(_currentImage);
            }
            
            // Compute image embedding
            Task.Run(() => {
                try
                {
                    lock (_lockObject)
                    {
                        _imageEmbedding = _sam.Encode(frame, frame.Width, frame.Height);
                    }
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Frame {frameNumber} loaded and encoded successfully");
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error encoding frame: " + ex.Message);
                }
            }, _cancellationTokenSource.Token);
            
            // Clean up
            frame.Dispose();
        }

        private void ShowPointEditor()
        {
            if (_pointEditorForm != null)
            {
                _pointEditorForm.Focus();
                return;
            }
            
            _pointEditorForm = new PointEditorForm();
            _pointEditorForm.PointAdded += PointEditorForm_PointAdded;
            _pointEditorForm.BoxAdded += PointEditorForm_BoxAdded;
            _pointEditorForm.ClearRequested += PointEditorForm_ClearRequested;
            
            if (_currentImage != null)
            {
                _pointEditorForm.UpdateImage(_currentImage);
            }
            
            _pointEditorForm.Show();
            _pointEditorForm.FormClosed += (s, e) => {
                _pointEditorForm = null;
                _showEditor = false;
                ExpireSolution(true);
            };
        }

        private void PointEditorForm_PointAdded(object sender, PointEventArgs e)
        {
            if (_currentImage == null)
                return;
                
            // Convert point from 0-1 range to image coordinates
            int x = (int)(e.X * _currentImage.Width);
            int y = (int)(e.Y * _currentImage.Height);
            
            // Create new point promotion
            PointPromotion promotion = new PointPromotion(e.IsAdditive ? OpType.ADD : OpType.REMOVE);
            promotion.X = x;
            promotion.Y = y;
            
            // Add to list
            _pointPromotions.Add(promotion);
            
            // Trigger solution update
            ExpireSolution(true);
        }

        private void PointEditorForm_BoxAdded(object sender, BoxEventArgs e)
        {
            if (_currentImage == null)
                return;
                
            // Convert coordinates from 0-1 range to image coordinates
            int x1 = (int)(e.X1 * _currentImage.Width);
            int y1 = (int)(e.Y1 * _currentImage.Height);
            int x2 = (int)(e.X2 * _currentImage.Width);
            int y2 = (int)(e.Y2 * _currentImage.Height);
            
            // Create new box promotion
            BoxPromotion promotion = new BoxPromotion();
            promotion.mLeftUp = new PointPromotion(OpType.ADD) { X = x1, Y = y1 };
            promotion.mRightBottom = new PointPromotion(OpType.ADD) { X = x2, Y = y2 };
            
            // Add to list
            _boxPromotions.Add(promotion);
            
            // Trigger solution update
            ExpireSolution(true);
        }

        private void PointEditorForm_ClearRequested(object sender, EventArgs e)
        {
            _pointPromotions.Clear();
            _boxPromotions.Clear();
            _maskImage = null;
            
            // Trigger solution update
            ExpireSolution(true);
        }

        private void PerformSegmentation()
        {
            try
            {
                // Combine promotions
                List<Promotion> promotions = new List<Promotion>();
                promotions.AddRange(_pointPromotions);
                promotions.AddRange(_boxPromotions);
                
                if (promotions.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No prompts provided for segmentation");
                    return;
                }
                
                // Check if we have embeddings
                if (_imageEmbedding == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Image embedding not yet computed");
                    return;
                }
                
                // Perform segmentation
                MaskData maskData = _sam.Decode(promotions, _imageEmbedding, _currentImage.Width, _currentImage.Height);
                
                if (maskData == null || maskData.mMask.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No mask data returned from segmentation");
                    return;
                }
                
                // Create mask image
                _maskImage = new Bitmap(_currentImage.Width, _currentImage.Height);
                
                using (Graphics g = Graphics.FromImage(_maskImage))
                {
                    // First draw the original image with reduced opacity
                    ColorMatrix matrix = new ColorMatrix();
                    matrix.Matrix33 = 0.5f; // 50% opacity
                    
                    ImageAttributes attributes = new ImageAttributes();
                    attributes.SetColorMatrix(matrix);
                    
                    g.DrawImage(_currentImage, 
                        new Rectangle(0, 0, _currentImage.Width, _currentImage.Height),
                        0, 0, _currentImage.Width, _currentImage.Height,
                        GraphicsUnit.Pixel, attributes);
                }
                
                // Fill the mask
                for (int y = 0; y < _currentImage.Height; y++)
                {
                    for (int x = 0; x < _currentImage.Width; x++)
                    {
                        int index = y * _currentImage.Width + x;
                        if (index < maskData.mMask.Count && maskData.mMask[index] > _sam.mask_threshold)
                        {
                            // Use a semi-transparent red overlay
                            Color originalColor = _maskImage.GetPixel(x, y);
                            Color overlayColor = Color.FromArgb(
                                128, 
                                Math.Min(255, originalColor.R + 100), 
                                Math.Max(0, originalColor.G - 50), 
                                Math.Max(0, originalColor.B - 50)
                            );
                            _maskImage.SetPixel(x, y, overlayColor);
                        }
                    }
                }
                
                // Update the point editor if open
                if (_pointEditorForm != null)
                {
                    _pointEditorForm.UpdateMask(_maskImage);
                }
                
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Segmentation completed successfully");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error performing segmentation: " + ex.Message);
            }
        }

        private List<Curve> ConvertMaskToCurves(Bitmap maskImage)
        {
            List<Curve> curves = new List<Curve>();
            
            try
            {
                // Convert mask to OpenCV format for contour detection
                OpenCvSharp.Mat mat = new OpenCvSharp.Mat(_currentImage.Height, _currentImage.Width, OpenCvSharp.MatType.CV_8UC1);
                
                // Fill the mat with mask data
                for (int y = 0; y < maskImage.Height; y++)
                {
                    for (int x = 0; x < maskImage.Width; x++)
                    {
                        Color pixel = maskImage.GetPixel(x, y);
                        // Check if this pixel is part of the mask (redder than original)
                        mat.At<byte>(y, x) = (byte)((pixel.R > pixel.G + 50) ? 255 : 0);
                    }
                }
                
                // Find contours
                OpenCvSharp.Point[][] contours;
                OpenCvSharp.HierarchyIndex[] hierarchy;
                OpenCvSharp.Cv2.FindContours(
                    mat,
                    out contours,
                    out hierarchy,
                    OpenCvSharp.RetrievalModes.Tree,
                    OpenCvSharp.ContourApproximationModes.ApproxSimple
                );
                
                // Convert contours to Rhino curves
                foreach (var contour in contours)
                {
                    if (contour.Length < 3)
                        continue;
                    
                    // Create polyline from contour points
                    List<Point3d> points = new List<Point3d>();
                    foreach (var pt in contour)
                    {
                        // Map to 0-1 range
                        double x = pt.X / (double)maskImage.Width;
                        double y = pt.Y / (double)maskImage.Height;
                        points.Add(new Point3d(x, y, 0));
                    }
                    
                    // Close the curve
                    points.Add(points[0]);
                    
                    // Create curve
                    Polyline polyline = new Polyline(points);
                    curves.Add(polyline.ToNurbsCurve());
                }
                
                mat.Dispose();
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error converting mask to curves: " + ex.Message);
            }
            
            return curves;
        }

        /// <summary>
        /// Create custom attributes for the component
        /// </summary>
        public override void CreateAttributes()
        {
            m_attributes = new SAMComponentAttributes(this);
        }

        /// <summary>
        /// Clean up resources when component is removed
        /// </summary>
        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                // Cancel any running tasks
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Cancel();
                }
                
                // Close the point editor
                if (_pointEditorForm != null)
                {
                    _pointEditorForm.Close();
                    _pointEditorForm = null;
                }
                
                // Dispose video processor
                if (_videoProcessor != null)
                {
                    _videoProcessor.Dispose();
                    _videoProcessor = null;
                }
            }
            
            base.DocumentContextChanged(document, context);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("df32f6a8-bc39-42b3-a258-0ae334f5a12e"); }
        }
    }

    /// <summary>
    /// This class provides visualization of the SAM component
    /// </summary>
    public class SAMComponentAttributes : Grasshopper.Kernel.Attributes.GH_ComponentAttributes
    {
        private readonly SAMComponent Owner;
        private System.Drawing.Rectangle PreviewBounds;
        private System.Drawing.RectangleF ComponentBounds;
        private const int PreviewWidth = 320;
        private const int PreviewHeight = 240;

        public SAMComponentAttributes(SAMComponent owner) : base(owner)
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
            int totalWidth = Math.Max(baseRectangle.Width, PreviewWidth + 20);
            
            // Make the component wider if needed to fit the preview
            if (totalWidth > baseRectangle.Width)
            {
                baseRectangle.Width = totalWidth;
            }
            
            // Add space for the preview below the standard component UI
            baseRectangle.Height += PreviewHeight + 10;
            
            // Calculate preview rectangle - centered horizontally
            PreviewBounds = new System.Drawing.Rectangle(
                baseRectangle.X + (baseRectangle.Width - PreviewWidth) / 2, // Center horizontally
                (int)(ComponentBounds.Bottom + 5), // Position below component area
                PreviewWidth,
                PreviewHeight
            );
            
            Bounds = baseRectangle;
        }

        protected override void Render(Grasshopper.GUI.Canvas.GH_Canvas canvas, System.Drawing.Graphics graphics, Grasshopper.GUI.Canvas.GH_CanvasChannel channel)
        {
            if (channel == Grasshopper.GUI.Canvas.GH_CanvasChannel.Objects)
            {
                // For objects channel (main component rendering)
                base.Render(canvas, graphics, channel);
                
                // Draw border for preview area
                graphics.DrawRectangle(Pens.DarkGray, PreviewBounds);
                
                // Draw the segmentation preview if available
                Bitmap maskImage = Owner._maskImage;
                if (maskImage != null)
                {
                    try
                    {
                        // Calculate proportional dimensions to maintain aspect ratio
                        float imageRatio = (float)maskImage.Width / maskImage.Height;
                        float previewRatio = (float)PreviewBounds.Width / PreviewBounds.Height;
                        
                        Rectangle targetRect;
                        
                        if (imageRatio > previewRatio)
                        {
                            // Image is wider than preview area - fit to width
                            float targetHeight = PreviewBounds.Width / imageRatio;
                            float yOffset = (PreviewBounds.Height - targetHeight) / 2;
                            targetRect = new Rectangle(
                                PreviewBounds.X,
                                (int)(PreviewBounds.Y + yOffset),
                                PreviewBounds.Width,
                                (int)targetHeight
                            );
                        }
                        else
                        {
                            // Image is taller than preview area - fit to height
                            float targetWidth = PreviewBounds.Height * imageRatio;
                            float xOffset = (PreviewBounds.Width - targetWidth) / 2;
                            targetRect = new Rectangle(
                                (int)(PreviewBounds.X + xOffset),
                                PreviewBounds.Y,
                                (int)targetWidth,
                                PreviewBounds.Height
                            );
                        }
                        
                        // Draw the mask image with proper aspect ratio
                        graphics.DrawImage(maskImage, targetRect);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error drawing preview: {ex.Message}");
                    }
                }
                else
                {
                    // Draw a message when no segmentation is available
                    StringFormat format = new StringFormat();
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    graphics.DrawString("No segmentation preview available", 
                        GH_FontServer.Standard, Brushes.DarkGray, PreviewBounds, format);
                }
            }
            else
            {
                // For all other channels, use default rendering
                base.Render(canvas, graphics, channel);
            }
        }
    }
    
    /// <summary>
    /// Class for video processing
    /// </summary>
    public class VideoProcessor : IDisposable
    {
        private string _videoPath;
        private OpenCvSharp.VideoCapture _videoCapture;
        private int _totalFrames;
        private double _fps;
        
        public int TotalFrames => _totalFrames;
        public double FPS => _fps;
        
        public VideoProcessor(string videoPath)
        {
            _videoPath = videoPath;
        }
        
        public void Initialize()
        {
            if (_videoCapture != null)
            {
                _videoCapture.Dispose();
            }
            
            _videoCapture = new OpenCvSharp.VideoCapture(_videoPath);
            if (!_videoCapture.IsOpened())
            {
                throw new Exception("Failed to open video file");
            }
            
            _totalFrames = (int)_videoCapture.Get(OpenCvSharp.VideoCaptureProperties.FrameCount);
            _fps = _videoCapture.Get(OpenCvSharp.VideoCaptureProperties.Fps);
        }
        
        public OpenCvSharp.Mat GetFrame(int frameNumber)
        {
            if (_videoCapture == null || !_videoCapture.IsOpened())
            {
                throw new Exception("Video not initialized");
            }
            
            if (frameNumber < 0 || frameNumber >= _totalFrames)
            {
                throw new ArgumentOutOfRangeException(nameof(frameNumber));
            }
            
            // Set position to the requested frame
            _videoCapture.Set(OpenCvSharp.VideoCaptureProperties.PosFrames, frameNumber);
            
            // Read the frame
            OpenCvSharp.Mat frame = new OpenCvSharp.Mat();
            if (!_videoCapture.Read(frame) || frame.Empty())
            {
                throw new Exception($"Failed to read frame {frameNumber}");
            }
            
            return frame;
        }
        
        public void Dispose()
        {
            if (_videoCapture != null)
            {
                _videoCapture.Dispose();
                _videoCapture = null;
            }
        }
    }
    
    /// <summary>
    /// Event arguments for point addition
    /// </summary>
    public class PointEventArgs : EventArgs
    {
        public float X { get; }
        public float Y { get; }
        public bool IsAdditive { get; }
        
        public PointEventArgs(float x, float y, bool isAdditive)
        {
            X = x;
            Y = y;
            IsAdditive = isAdditive;
        }
    }
    
    /// <summary>
    /// Event arguments for box addition
    /// </summary>
    public class BoxEventArgs : EventArgs
    {
        public float X1 { get; }
        public float Y1 { get; }
        public float X2 { get; }
        public float Y2 { get; }
        
        public BoxEventArgs(float x1, float y1, float x2, float y2)
        {
            X1 = Math.Min(x1, x2);
            Y1 = Math.Min(y1, y2);
            X2 = Math.Max(x1, x2);
            Y2 = Math.Max(y1, y2);
        }
    }
    
    /// <summary>
    /// Form for editing points and boxes
    /// </summary>
    public class PointEditorForm : Form
    {
        private PictureBox _pictureBox;
        private Panel _toolbarPanel;
        private RadioButton _addPointRadio;
        private RadioButton _removePointRadio;
        private RadioButton _addBoxRadio;
        private Button _clearButton;
        private Label _instructionsLabel;
        
        private Bitmap _displayImage;
        private Bitmap _originalImage;
        private Bitmap _maskImage;
        private bool _isDrawingBox = false;
        private System.Drawing.Point _boxStart;
        private System.Drawing.Point _boxEnd;
        
        public event EventHandler<PointEventArgs> PointAdded;
        public event EventHandler<BoxEventArgs> BoxAdded;
        public event EventHandler ClearRequested;
        
        public PointEditorForm()
        {
            InitializeComponents();
        }
        
        private void InitializeComponents()
        {
            this.Text = "SAM Point Editor";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            
            // Create toolbar panel
            _toolbarPanel = new Panel();
            _toolbarPanel.Dock = DockStyle.Top;
            _toolbarPanel.Height = 50;
            _toolbarPanel.BackColor = SystemColors.Control;
            this.Controls.Add(_toolbarPanel);
            
            // Add radio buttons for tool selection
            _addPointRadio = new RadioButton();
            _addPointRadio.Text = "Add Point (+)";
            _addPointRadio.Checked = true;
            _addPointRadio.Location = new System.Drawing.Point(10, 15);
            _addPointRadio.AutoSize = true;
            _toolbarPanel.Controls.Add(_addPointRadio);
            
            _removePointRadio = new RadioButton();
            _removePointRadio.Text = "Remove Point (-)";
            _removePointRadio.Location = new System.Drawing.Point(120, 15);
            _removePointRadio.AutoSize = true;
            _toolbarPanel.Controls.Add(_removePointRadio);
            
            _addBoxRadio = new RadioButton();
            _addBoxRadio.Text = "Add Box";
            _addBoxRadio.Location = new System.Drawing.Point(240, 15);
            _addBoxRadio.AutoSize = true;
            _toolbarPanel.Controls.Add(_addBoxRadio);
            
            // Add clear button
            _clearButton = new Button();
            _clearButton.Text = "Clear All";
            _clearButton.Location = new System.Drawing.Point(340, 12);
            _clearButton.AutoSize = true;
            _clearButton.Click += ClearButton_Click;
            _toolbarPanel.Controls.Add(_clearButton);
            
            // Add instructions label
            _instructionsLabel = new Label();
            _instructionsLabel.Text = "Click to add points or draw boxes for segmentation";
            _instructionsLabel.Location = new System.Drawing.Point(450, 15);
            _instructionsLabel.AutoSize = true;
            _toolbarPanel.Controls.Add(_instructionsLabel);
            
            // Create picture box for the image
            _pictureBox = new PictureBox();
            _pictureBox.Dock = DockStyle.Fill;
            _pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            _pictureBox.BackColor = Color.Black;
            _pictureBox.MouseDown += PictureBox_MouseDown;
            _pictureBox.MouseMove += PictureBox_MouseMove;
            _pictureBox.MouseUp += PictureBox_MouseUp;
            _pictureBox.Paint += PictureBox_Paint;
            this.Controls.Add(_pictureBox);
            
            // Handle keyboard shortcuts
            this.KeyPreview = true;
            this.KeyDown += PointEditorForm_KeyDown;
        }
        
        private void PointEditorForm_KeyDown(object sender, KeyEventArgs e)
        {
            // Keyboard shortcuts
            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1)
            {
                _addPointRadio.Checked = true;
            }
            else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2)
            {
                _removePointRadio.Checked = true;
            }
            else if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3)
            {
                _addBoxRadio.Checked = true;
            }
            else if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
            {
                ClearButton_Click(sender, e);
            }
        }
        
        private void ClearButton_Click(object sender, EventArgs e)
        {
            ClearRequested?.Invoke(this, EventArgs.Empty);
            
            // Reset display
            if (_originalImage != null)
            {
                lock (_pictureBox)
                {
                    _displayImage = new Bitmap(_originalImage);
                    _pictureBox.Image = _displayImage;
                    _maskImage = null;
                }
            }
        }
        
        private void PictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (_originalImage == null)
                return;
                
            if (_addBoxRadio.Checked)
            {
                _isDrawingBox = true;
                _boxStart = e.Location;
                _boxEnd = e.Location;
            }
            else
            {
                // Add or remove point
                float normalizedX = (float)e.X / _pictureBox.ClientSize.Width;
                float normalizedY = (float)e.Y / _pictureBox.ClientSize.Height;
                
                // Adjust for zoom mode
                if (_pictureBox.SizeMode == PictureBoxSizeMode.Zoom)
                {
                    // Calculate image display area within picturebox
                    float imageRatio = (float)_originalImage.Width / _originalImage.Height;
                    float boxRatio = (float)_pictureBox.Width / _pictureBox.Height;
                    
                    Rectangle imageRect;
                    if (imageRatio > boxRatio)
                    {
                        // Image is wider than picturebox
                        int displayHeight = (int)(_pictureBox.Width / imageRatio);
                        int margin = (_pictureBox.Height - displayHeight) / 2;
                        imageRect = new Rectangle(0, margin, _pictureBox.Width, displayHeight);
                    }
                    else
                    {
                        // Image is taller than picturebox
                        int displayWidth = (int)(_pictureBox.Height * imageRatio);
                        int margin = (_pictureBox.Width - displayWidth) / 2;
                        imageRect = new Rectangle(margin, 0, displayWidth, _pictureBox.Height);
                    }
                    
                    // If click is outside image area, ignore
                    if (!imageRect.Contains(e.Location))
                        return;
                        
                    // Normalize coordinates relative to image rect
                    normalizedX = (float)(e.X - imageRect.X) / imageRect.Width;
                    normalizedY = (float)(e.Y - imageRect.Y) / imageRect.Height;
                }
                
                // Ensure coordinates are in 0-1 range
                normalizedX = Math.Max(0, Math.Min(1, normalizedX));
                normalizedY = Math.Max(0, Math.Min(1, normalizedY));
                
                // Raise point added event
                PointAdded?.Invoke(this, new PointEventArgs(
                    normalizedX, 
                    normalizedY, 
                    _addPointRadio.Checked));
                
                // Draw point on the display image
                using (Graphics g = Graphics.FromImage(_displayImage))
                {
                    int x = (int)(normalizedX * _originalImage.Width);
                    int y = (int)(normalizedY * _originalImage.Height);
                    int radius = 5;
                    
                    Color color = _addPointRadio.Checked ? Color.Lime : Color.Red;
                    g.FillEllipse(new SolidBrush(color), x - radius, y - radius, radius * 2, radius * 2);
                    g.DrawEllipse(new Pen(Color.White, 1), x - radius, y - radius, radius * 2, radius * 2);
                }
                
                _pictureBox.Invalidate();
            }
        }
        
        private void PictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDrawingBox && _originalImage != null)
            {
                _boxEnd = e.Location;
                _pictureBox.Invalidate();
            }
        }
        
        private void PictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (_isDrawingBox && _originalImage != null)
            {
                _isDrawingBox = false;
                
                // Calculate normalized coordinates
                float normalizedX1, normalizedY1, normalizedX2, normalizedY2;
                
                if (_pictureBox.SizeMode == PictureBoxSizeMode.Zoom)
                {
                    // Calculate image display area within picturebox
                    float imageRatio = (float)_originalImage.Width / _originalImage.Height;
                    float boxRatio = (float)_pictureBox.Width / _pictureBox.Height;
                    
                    Rectangle imageRect;
                    if (imageRatio > boxRatio)
                    {
                        // Image is wider than picturebox
                        int displayHeight = (int)(_pictureBox.Width / imageRatio);
                        int margin = (_pictureBox.Height - displayHeight) / 2;
                        imageRect = new Rectangle(0, margin, _pictureBox.Width, displayHeight);
                    }
                    else
                    {
                        // Image is taller than picturebox
                        int displayWidth = (int)(_pictureBox.Height * imageRatio);
                        int margin = (_pictureBox.Width - displayWidth) / 2;
                        imageRect = new Rectangle(margin, 0, displayWidth, _pictureBox.Height);
                    }
                    
                    // Normalize coordinates relative to image rect
                    normalizedX1 = (float)(_boxStart.X - imageRect.X) / imageRect.Width;
                    normalizedY1 = (float)(_boxStart.Y - imageRect.Y) / imageRect.Height;
                    normalizedX2 = (float)(_boxEnd.X - imageRect.X) / imageRect.Width;
                    normalizedY2 = (float)(_boxEnd.Y - imageRect.Y) / imageRect.Height;
                }
                else
                {
                    normalizedX1 = (float)_boxStart.X / _pictureBox.ClientSize.Width;
                    normalizedY1 = (float)_boxStart.Y / _pictureBox.ClientSize.Height;
                    normalizedX2 = (float)_boxEnd.X / _pictureBox.ClientSize.Width;
                    normalizedY2 = (float)_boxEnd.Y / _pictureBox.ClientSize.Height;
                }
                
                // Ensure coordinates are in 0-1 range
                normalizedX1 = Math.Max(0, Math.Min(1, normalizedX1));
                normalizedY1 = Math.Max(0, Math.Min(1, normalizedY1));
                normalizedX2 = Math.Max(0, Math.Min(1, normalizedX2));
                normalizedY2 = Math.Max(0, Math.Min(1, normalizedY2));
                
                // Don't add tiny boxes
                float width = Math.Abs(normalizedX2 - normalizedX1);
                float height = Math.Abs(normalizedY2 - normalizedY1);
                if (width < 0.01f || height < 0.01f)
                    return;
                
                // Raise box added event
                BoxAdded?.Invoke(this, new BoxEventArgs(
                    normalizedX1, 
                    normalizedY1, 
                    normalizedX2, 
                    normalizedY2));
                
                // Draw box on the display image
                using (Graphics g = Graphics.FromImage(_displayImage))
                {
                    int x1 = (int)(Math.Min(normalizedX1, normalizedX2) * _originalImage.Width);
                    int y1 = (int)(Math.Min(normalizedY1, normalizedY2) * _originalImage.Height);
                    int x2 = (int)(Math.Max(normalizedX1, normalizedX2) * _originalImage.Width);
                    int y2 = (int)(Math.Max(normalizedY1, normalizedY2) * _originalImage.Height);
                    
                    g.DrawRectangle(new Pen(Color.Lime, 2), x1, y1, x2 - x1, y2 - y1);
                }
                
                _pictureBox.Invalidate();
            }
        }
        
        private void PictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (_isDrawingBox && _originalImage != null)
            {
                // Get screen coordinates
                int x1 = Math.Min(_boxStart.X, _boxEnd.X);
                int y1 = Math.Min(_boxStart.Y, _boxEnd.Y);
                int x2 = Math.Max(_boxStart.X, _boxEnd.X);
                int y2 = Math.Max(_boxStart.Y, _boxEnd.Y);
                
                // Draw box on screen
                e.Graphics.DrawRectangle(new Pen(Color.Lime, 2), x1, y1, x2 - x1, y2 - y1);
            }
        }
        
        public void UpdateImage(Bitmap image)
        {
            if (image == null)
                return;
                
            lock (_pictureBox)
            {
                // Store original image and create display copy
                _originalImage = image;
                _displayImage = new Bitmap(image);
                _pictureBox.Image = _displayImage;
            }
        }
        
        public void UpdateMask(Bitmap maskImage)
        {
            if (maskImage == null || _originalImage == null)
                return;
                
            lock (_pictureBox)
            {
                _maskImage = maskImage;
                
                // Create a new display image
                _displayImage = new Bitmap(_originalImage);
                
                // Overlay the mask
                using (Graphics g = Graphics.FromImage(_displayImage))
                {
                    // Draw the mask with transparency
                    ColorMatrix matrix = new ColorMatrix();
                    matrix.Matrix33 = 0.5f; // 50% opacity
                    
                    ImageAttributes attributes = new ImageAttributes();
                    attributes.SetColorMatrix(matrix);
                    
                    g.DrawImage(maskImage, 
                        new Rectangle(0, 0, _displayImage.Width, _displayImage.Height),
                        0, 0, maskImage.Width, maskImage.Height,
                        GraphicsUnit.Pixel, attributes);
                }
                
                _pictureBox.Image = _displayImage;
            }
        }
    }

    /// <summary>
    /// SAM class for image segmentation
    /// </summary>
    public class SAM
    {
        private InferenceSession _encoder;
        private InferenceSession _decoder;
        public float mask_threshold = 0.0f;
        private bool _ready = false;
        private string _modelPath = "";

        public SAM()
        {
        }

        public void SetModelPath(string path)
        {
            _modelPath = path;
        }

        /// <summary>
        /// Load the SAM ONNX models
        /// </summary>
        public void LoadONNXModel()
        {
            if (_encoder != null)
                _encoder.Dispose();

            if (_decoder != null)
                _decoder.Dispose();

            var options = new SessionOptions();
            options.EnableMemoryPattern = false;
            options.EnableCpuMemArena = false;

            string encoderPath = Path.Combine(_modelPath, "encoder-quant.onnx");
            if (!File.Exists(encoderPath))
            {
                throw new FileNotFoundException($"Encoder model not found at {encoderPath}");
            }
            _encoder = new InferenceSession(encoderPath, options);

            string decoderPath = Path.Combine(_modelPath, "decoder-quant.onnx");
            if (!File.Exists(decoderPath))
            {
                throw new FileNotFoundException($"Decoder model not found at {decoderPath}");
            }
            _decoder = new InferenceSession(decoderPath, options);
            
            _ready = true;
        }

        /// <summary>
        /// Encode an image
        /// </summary>
        public float[] Encode(OpenCvSharp.Mat image, int orgWid, int orgHei)
        {
            if (!_ready)
                throw new InvalidOperationException("SAM models are not loaded");
                
            Transforms transform = new Transforms(1024);

            float[] img = transform.ApplyImage(image, orgWid, orgHei);
            var tensor = new DenseTensor<float>(img, new[] { 1, 3, 1024, 1024 });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("x", tensor)
            };

            var results = _encoder.Run(inputs);
            var embedding = results.First().AsTensor<float>().ToArray();

            return embedding;
        }

        /// <summary>
        /// Decode prompts to get segmentation mask
        /// </summary>
        public MaskData Decode(List<Promotion> promotions, float[] embedding, int orgWid, int orgHei)
        {
            if (!_ready)
                throw new InvalidOperationException("SAM models are not loaded");

            var embedding_tensor = new DenseTensor<float>(embedding, new[] { 1, 256, 64, 64 });

            var bpmos = promotions.FindAll(e => e.mType == PromotionType.Box);
            var pproms = promotions.FindAll(e => e.mType == PromotionType.Point);
            int boxCount = bpmos.Count;
            int pointCount = pproms.Count;
            float[] promotion = new float[2 * (boxCount * 2 + pointCount)];
            float[] label = new float[boxCount * 2 + pointCount];
            
            for (int i = 0; i < boxCount; i++)
            {
                var input = bpmos[i].GetInput();
                for (int j = 0; j < input.Length; j++)
                {
                    promotion[4 * i + j] = input[j];
                }
                var la = bpmos[i].GetLable();
                for (int j = 0; j < la.Length; j++)
                {
                    label[2 * i + j] = la[j];
                }
            }

            for (int i = 0; i < pointCount; i++)
            {
                var input = pproms[i].GetInput();
                for (int j = 0; j < input.Length; j++)
                {
                    promotion[4 * boxCount + 2 * i + j] = input[j];
                }
                label[2 * boxCount + i] = pproms[i].GetLable()[0];
            }

            // Prepare inputs for the decoder
            int n_points = boxCount * 2 + pointCount;
            if (n_points == 0)
                return null;

            float[] originalSize = new float[] { orgHei, orgWid };
            float[] maskInput = new float[256 * 256];
            var has_mask_input = 0.0f;

            var point_coords = new DenseTensor<float>(new[] { 1, n_points, 2 });
            var point_labels = new DenseTensor<float>(new[] { 1, n_points });
            var mask_input = new DenseTensor<float>(new[] { 1, 1, 256, 256 });
            var has_mask_tensor = new DenseTensor<float>(new[] { 1 });
            var orig_im_size = new DenseTensor<float>(new[] { 2 });

            for (int i = 0; i < n_points; i++)
            {
                point_coords[0, i, 0] = promotion[2 * i];
                point_coords[0, i, 1] = promotion[2 * i + 1];
                point_labels[0, i] = label[i];
            }

            for (int i = 0; i < 256 * 256; i++)
            {
                mask_input[0, 0, i / 256, i % 256] = maskInput[i];
            }

            has_mask_tensor[0] = has_mask_input;
            orig_im_size[0] = originalSize[0];
            orig_im_size[1] = originalSize[1];

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image_embeddings", embedding_tensor),
                NamedOnnxValue.CreateFromTensor("point_coords", point_coords),
                NamedOnnxValue.CreateFromTensor("point_labels", point_labels),
                NamedOnnxValue.CreateFromTensor("mask_input", mask_input),
                NamedOnnxValue.CreateFromTensor("has_mask_input", has_mask_tensor),
                NamedOnnxValue.CreateFromTensor("orig_im_size", orig_im_size)
            };

            var results = _decoder.Run(inputs);
            var masks = results.Where(r => r.Name == "masks").First().AsTensor<float>();
            var iou = results.Where(r => r.Name == "iou_predictions").First().AsTensor<float>();
            
            // Convert low-res to full-res mask
            int height = orgHei;
            int width = orgWid;
            var mask = new List<float>();

            for (int h = 0; h < height; h++)
            {
                for (int w = 0; w < width; w++)
                {
                    float y = (float)h / height;
                    float x = (float)w / width;
                    
                    // Map coordinates to mask dimensions
                    int mask_y = (int)(y * 256);
                    int mask_x = (int)(x * 256);
                    
                    mask_y = Math.Max(0, Math.Min(255, mask_y));
                    mask_x = Math.Max(0, Math.Min(255, mask_x));
                    
                    mask.Add(masks[0, 0, mask_y, mask_x]);
                }
            }

            MaskData maskData = new MaskData();
            maskData.mMask = mask;
            maskData.mIou = iou[0, 0];
            
            return maskData;
        }
    }

    /// <summary>
    /// Enum for operation types
    /// </summary>
    public enum OpType
    {
        ADD,
        REMOVE
    }

    /// <summary>
    /// Promotion type enum
    /// </summary>
    public enum PromotionType
    {
        Point,
        Box
    }

    /// <summary>
    /// Base class for promotions
    /// </summary>
    public abstract class Promotion
    {
        public PromotionType mType;
        
        public abstract float[] GetInput();
        public abstract float[] GetLable();
    }

    /// <summary>
    /// Point promotion
    /// </summary>
    public class PointPromotion : Promotion
    {
        public int X;
        public int Y;
        public OpType OpType;
        
        public PointPromotion(OpType opType)
        {
            mType = PromotionType.Point;
            OpType = opType;
        }
        
        public override float[] GetInput()
        {
            return new float[] { X, Y };
        }
        
        public override float[] GetLable()
        {
            return new float[] { OpType == OpType.ADD ? 1.0f : 0.0f };
        }
    }

    /// <summary>
    /// Box promotion
    /// </summary>
    public class BoxPromotion : Promotion
    {
        public PointPromotion mLeftUp;
        public PointPromotion mRightBottom;
        
        public BoxPromotion()
        {
            mType = PromotionType.Box;
        }
        
        public override float[] GetInput()
        {
            return new float[] { mLeftUp.X, mLeftUp.Y, mRightBottom.X, mRightBottom.Y };
        }
        
        public override float[] GetLable()
        {
            return new float[] { 2.0f, 3.0f };
        }
    }

    /// <summary>
    /// Mask data
    /// </summary>
    public class MaskData
    {
        public List<float> mMask = new List<float>();
        public float mIou;
    }

    /// <summary>
    /// Image transformations
    /// </summary>
    public class Transforms
    {
        private int _targetSize;
        
        public Transforms(int targetSize)
        {
            _targetSize = targetSize;
        }
        
        public float[] ApplyImage(OpenCvSharp.Mat image, int origWidth, int origHeight)
        {
            // Resize image to target size
            OpenCvSharp.Mat resized = new OpenCvSharp.Mat();
            OpenCvSharp.Cv2.Resize(image, resized, new OpenCvSharp.Size(_targetSize, _targetSize));
            
            // Convert to RGB
            OpenCvSharp.Mat rgb = new OpenCvSharp.Mat();
            OpenCvSharp.Cv2.CvtColor(resized, rgb, OpenCvSharp.ColorConversionCodes.BGR2RGB);
            
            // Normalize pixel values
            float[] result = new float[_targetSize * _targetSize * 3];
            
            for (int y = 0; y < _targetSize; y++)
            {
                for (int x = 0; x < _targetSize; x++)
                {
                    var pixel = rgb.At<OpenCvSharp.Vec3b>(y, x);
                    
                    // Normalize to 0-1 range and reorder channels for the model
                    result[0 * _targetSize * _targetSize + y * _targetSize + x] = pixel[0] / 255.0f;
                    result[1 * _targetSize * _targetSize + y * _targetSize + x] = pixel[1] / 255.0f;
                    result[2 * _targetSize * _targetSize + y * _targetSize + x] = pixel[2] / 255.0f;
                }
            }
            
            resized.Dispose();
            rgb.Dispose();
            
            return result;
        }
    }
}
