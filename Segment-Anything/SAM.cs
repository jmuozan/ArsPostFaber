using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if !WINDOWS
using System.Runtime.InteropServices;
#endif

namespace SAMViewer
{
    /// <summary>
    /// Segment Anything
    /// </summary>
    class SAM
    {
        public static SAM theSingleton = null;
        private InferenceSession mEncoder;
        private InferenceSession mDecoder;
        public float mask_threshold = 0.0f;
        private bool mReady = false;
        private string mModelPath = "";

        protected SAM()
        {
        }

        public static SAM Instance()
        {
            if (null == theSingleton)
            {
                theSingleton = new SAM();
            }
            return theSingleton;
        }

        /// <summary>
        /// Set model path (for PyTorch model or directory containing ONNX models)
        /// </summary>
        public void SetModelPath(string modelPath)
        {
            mModelPath = modelPath;
            // Reset ready state
            mReady = false;
            
            // Check if we need to convert PyTorch model to ONNX
            if (modelPath.EndsWith(".pth"))
            {
                Console.WriteLine($"Using PyTorch model: {modelPath}");
                // We'll try to convert when loading
            }
            else if (Directory.Exists(modelPath))
            {
                Console.WriteLine($"Using model directory: {modelPath}");
                // Will look for encoder-quant.onnx and decoder-quant.onnx in this directory
            }
            else
            {
                Console.WriteLine($"Using model path: {modelPath}");
            }
        }

        /// <summary>
        /// Convert PyTorch model to ONNX format
        /// </summary>
        private bool ConvertPyTorchToOnnx(string modelPath)
        {
            try
            {
                // Create a temporary Python script to convert the model
                string scriptPath = Path.Combine(Path.GetTempPath(), "convert_sam_to_onnx.py");
                string scriptContent = @"
import torch
import os
import sys
from segment_anything import sam_model_registry

def convert_sam_to_onnx(checkpoint_path, output_dir):
    # Convert a SAM PyTorch checkpoint to ONNX format
    print(f'Converting {checkpoint_path} to ONNX format...')
    
    # Determine model type from filename
    if 'vit_h' in checkpoint_path:
        model_type = 'vit_h'
    elif 'vit_l' in checkpoint_path:
        model_type = 'vit_l'
    elif 'vit_b' in checkpoint_path:
        model_type = 'vit_b'
    else:
        model_type = 'vit_b'  # Default to base model
    
    print(f'Using model type: {model_type}')
    
    # Load the model
    device = 'cuda' if torch.cuda.is_available() else 'cpu'
    sam = sam_model_registry[model_type](checkpoint=checkpoint_path)
    sam.to(device=device)
    
    # Save the image encoder
    dummy_image = torch.randn(1, 3, 1024, 1024, device=device)
    encoder_path = os.path.join(output_dir, 'encoder-quant.onnx')
    torch.onnx.export(
        sam.image_encoder,
        dummy_image,
        encoder_path,
        opset_version=17,
        input_names=['x'],
        output_names=['y'],
    )
    print(f'Saved encoder to {encoder_path}')
    
    # Save the mask decoder
    image_embeddings = torch.randn(1, 256, 64, 64, device=device)
    point_coords = torch.randint(low=0, high=1024, size=(1, 5, 2), device=device).float()
    point_labels = torch.randint(low=0, high=4, size=(1, 5), device=device)
    mask_input = torch.zeros(1, 1, 256, 256, device=device)
    has_mask_input = torch.zeros(1, dtype=torch.float, device=device)
    orig_im_size = torch.tensor([1024, 1024], dtype=torch.float, device=device)
    
    decoder = sam.mask_decoder
    decoder_path = os.path.join(output_dir, 'decoder-quant.onnx')
    torch.onnx.export(
        decoder,
        (
            image_embeddings,
            point_coords,
            point_labels,
            mask_input,
            has_mask_input,
            orig_im_size,
        ),
        decoder_path,
        opset_version=17,
        input_names=[
            'image_embeddings',
            'point_coords',
            'point_labels',
            'mask_input',
            'has_mask_input',
            'orig_im_size',
        ],
        output_names=['masks', 'iou_predictions'],
    )
    print(f'Saved decoder to {decoder_path}')
    return True

if __name__ == '__main__':
    if len(sys.argv) != 3:
        print('Usage: python convert_sam_to_onnx.py <checkpoint_path> <output_dir>')
        sys.exit(1)
        
    checkpoint_path = sys.argv[1]
    output_dir = sys.argv[2]
    
    # Ensure output directory exists
    os.makedirs(output_dir, exist_ok=True)
    
    success = convert_sam_to_onnx(checkpoint_path, output_dir)
    if success:
        print('Conversion completed successfully!')
    else:
        print('Conversion failed!')
        sys.exit(1)
";

                File.WriteAllText(scriptPath, scriptContent);
                
                // Create output directory for ONNX models
                string onnxDir = Path.Combine(Path.GetDirectoryName(modelPath), "onnx_models");
                Directory.CreateDirectory(onnxDir);
                
                // Run the conversion script
                Console.WriteLine("Converting PyTorch model to ONNX format...");
                Console.WriteLine("This may take a few minutes...");
                
                // Check if Python is available
                var pythonCheck = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                try
                {
                    using (var process = System.Diagnostics.Process.Start(pythonCheck))
                    {
                        string pythonVersion = process.StandardOutput.ReadToEnd().Trim();
                        process.WaitForExit();
                        Console.WriteLine($"Found {pythonVersion}");
                    }
                }
                catch
                {
                    Console.WriteLine("Python 3 not found. Please install Python 3 and the segment_anything package.");
                    return false;
                }
                
                // Run the conversion
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "python3",
                    Arguments = $"{scriptPath} \"{modelPath}\" \"{onnxDir}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (var process = System.Diagnostics.Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine($"Conversion failed: {error}");
                        return false;
                    }
                    
                    Console.WriteLine(output);
                }
                
                // Update the model path to use the ONNX models
                mModelPath = onnxDir;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting PyTorch model to ONNX: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Load Segment Anything model
        /// </summary>
        public void LoadONNXModel()
        {
            if (mReady)
                return;
                
            try
            {
                if (this.mEncoder != null)
                    this.mEncoder.Dispose();

                if (this.mDecoder != null)
                    this.mDecoder.Dispose();

                var options = new SessionOptions();
                options.EnableMemoryPattern = false;
                options.EnableCpuMemArena = false;
                
                // Check if we need to convert PyTorch model
                if (!string.IsNullOrEmpty(mModelPath) && mModelPath.EndsWith(".pth"))
                {
                    if (!ConvertPyTorchToOnnx(mModelPath))
                    {
                        Console.WriteLine("Failed to convert PyTorch model. Using default ONNX models.");
                        mModelPath = "";
                    }
                }

                // Determine the path to models
                string modelsPath;
                if (!string.IsNullOrEmpty(mModelPath) && Directory.Exists(mModelPath))
                {
                    modelsPath = mModelPath;
                }
                else 
                {
                    // Use default location (executable directory)
                    modelsPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                }
                
                // Load encoder
                string encoderPath = Path.Combine(modelsPath, "encoder-quant.onnx");
                if (!File.Exists(encoderPath))
                {
                    Console.WriteLine($"Encoder model not found at: {encoderPath}");
                    Console.WriteLine("Please download the ONNX models or provide a PyTorch model file.");
                    return;
                }
                
                this.mEncoder = new InferenceSession(encoderPath, options);
                Console.WriteLine($"Loaded encoder model from: {encoderPath}");

                // Load decoder
                string decoderPath = Path.Combine(modelsPath, "decoder-quant.onnx");
                if (!File.Exists(decoderPath))
                {
                    Console.WriteLine($"Decoder model not found at: {decoderPath}");
                    this.mEncoder.Dispose();
                    this.mEncoder = null;
                    return;
                }
                
                this.mDecoder = new InferenceSession(decoderPath, options);
                Console.WriteLine($"Loaded decoder model from: {decoderPath}");
                
                this.mReady = true;
                Console.WriteLine("SAM model loaded successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading ONNX model: {ex.Message}");
                this.mReady = false;
                
                if (this.mEncoder != null)
                {
                    this.mEncoder.Dispose();
                    this.mEncoder = null;
                }
                
                if (this.mDecoder != null)
                {
                    this.mDecoder.Dispose();
                    this.mDecoder = null;
                }
            }
        }
        
        /// <summary>
        /// Check if model is loaded and ready
        /// </summary>
        public bool IsReady()
        {
            return mReady;
        }
        
        /// <summary>
        /// Segment Anything对图像进行编码
        /// </summary>
        public float[] Encode(Mat image, int orgWid, int orgHei)
        {
            if (!this.mReady)
            {
                LoadONNXModel();
                if (!this.mReady)
                {
                    Console.WriteLine("Model not loaded. Cannot encode image.");
                    return null;
                }
            }
            
            Transforms tranform = new Transforms(1024);

            float[] img = tranform.ApplyImage(image, orgWid, orgHei);          
            var tensor = new DenseTensor<float>(img, new[] { 1, 3, 1024, 1024 });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("x", tensor)
            };

            var results = this.mEncoder.Run(inputs);
            var embedding = results.First().AsTensor<float>().ToArray();
            return embedding;
        }

        /// <summary>
        /// Segment Anything提示信息解码
        /// </summary>
        public MaskData Decode(List<Promotion> promotions, float[] embedding, int orgWid, int orgHei)
        {
            if (this.mReady == false)
            {
                Console.WriteLine("Image Embedding is not done!");
                return null;
            }

            var embedding_tensor = new DenseTensor<float>(embedding, new[] { 1, 256, 64, 64 });

            var bpmos = promotions.FindAll(e => e.mType == PromotionType.Box);
            var pproms = promotions.FindAll(e => e.mType == PromotionType.Point);
            int boxCount = promotions.FindAll(e => e.mType == PromotionType.Box).Count();
            int pointCount = promotions.FindAll(e => e.mType == PromotionType.Point).Count();
            float[] promotion = new float[2 * (boxCount * 2 + pointCount)];
            float[] label = new float[boxCount * 2 + pointCount];
            for (int i = 0; i < boxCount; i++)
            {
                var input = bpmos[i].GetInput();
                for (int j = 0; j < input.Count(); j++)
                {
                    promotion[4 * i + j] = input[j];
                }
                var la = bpmos[i].GetLable();
                for (int j = 0; j < la.Count(); j++)
                {
                    label[2 * i + j] = la[j];
                }
            }
            for (int i = 0; i < pointCount; i++)
            {
                var p = pproms[i].GetInput();
                for (int j = 0; j < p.Count(); j++)
                {
                    promotion[boxCount * 4 + 2 * i + j] = p[j];
                }
                var la = pproms[i].GetLable();
                for (int j = 0; j < la.Count(); j++)
                {
                    label[boxCount * 2 + i + j] = la[j];
                }
            }

            var point_coords_tensor = new DenseTensor<float>(promotion, new[] { 1, boxCount * 2 + pointCount, 2 });

            var point_label_tensor = new DenseTensor<float>(label, new[] { 1, boxCount * 2 + pointCount });

            float[] mask = new float[256 * 256];
            for (int i = 0; i < mask.Count(); i++)
            {
                mask[i] = 0;
            }
            var mask_tensor = new DenseTensor<float>(mask, new[] { 1, 1, 256, 256 });

            float[] hasMaskValues = new float[1] { 0 };
            var hasMaskValues_tensor = new DenseTensor<float>(hasMaskValues, new[] { 1 });

            float[] orig_im_size_values = { (float)orgHei, (float)orgWid };
            var orig_im_size_values_tensor = new DenseTensor<float>(orig_im_size_values, new[] { 2 });

            var decode_inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image_embeddings", embedding_tensor),
                NamedOnnxValue.CreateFromTensor("point_coords", point_coords_tensor),
                NamedOnnxValue.CreateFromTensor("point_labels", point_label_tensor),
                NamedOnnxValue.CreateFromTensor("mask_input", mask_tensor),
                NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskValues_tensor),
                NamedOnnxValue.CreateFromTensor("orig_im_size", orig_im_size_values_tensor)
            };
            MaskData md = new MaskData();
            var segmask = this.mDecoder.Run(decode_inputs).ToList();
            md.mMask = segmask[0].AsTensor<float>().ToArray().ToList();
            md.mShape = segmask[0].AsTensor<float>().Dimensions.ToArray();
            md.mIoU = segmask[1].AsTensor<float>().ToList();
            return md;
        }
        
        /// <summary>
        /// Process a video frame and return the mask
        /// </summary>
        public MaskData ProcessVideoFrame(Mat frame, List<Promotion> promotions)
        {
            if (frame == null || frame.Empty())
            {
                Console.WriteLine("Empty frame provided to ProcessVideoFrame");
                return null;
            }
            
            try
            {
                // Get image dimensions
                int width = frame.Width;
                int height = frame.Height;
                
                // Encode the image
                float[] embedding = Encode(frame, width, height);
                
                if (embedding == null)
                {
                    Console.WriteLine("Failed to encode frame");
                    return null;
                }
                
                // Decode with promotions
                MaskData maskData = Decode(promotions, embedding, width, height);
                
                return maskData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing video frame: {ex.Message}");
                return null;
            }
        }
    }
}