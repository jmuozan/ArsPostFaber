using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace SAMViewer
{
    public class VideoSAM
    {
        private string _videoPath;
        private VideoCapture _videoCapture;
        private double _fps;
        private int _totalFrames;
        private int _currentFrameIndex = 0;
        private Mat _currentFrame;
        private List<Promotion> _currentPromotions = new List<Promotion>();
        private SAM _sam;
        private Dictionary<int, MaskData> _processedFrames = new Dictionary<int, MaskData>();
        private bool _isProcessing = false;
        private CancellationTokenSource _processingCancellationSource;
        private object _lockObject = new object();
        private WebInterface _webInterface;
        private string _currentModelPath = "";

        public VideoSAM()
        {
            _sam = SAM.Instance();
            _sam.mask_threshold = 0.0f;
            
            // Initialize web interface
            InitializeWebInterface();
        }

        private void InitializeWebInterface()
        {
            try
            {
                _webInterface = new WebInterface();
                
                // Register event handlers
                _webInterface.PointAdded += (sender, args) => 
                {
                    AddPoint(args.X, args.Y, args.IsAdditive);
                };
                
                _webInterface.BoxAdded += (sender, args) => 
                {
                    AddBox(args.X1, args.Y1, args.X2, args.Y2);
                };
                
                _webInterface.ClearRequested += (sender, args) => 
                {
                    ClearPromotions();
                };
                
                _webInterface.FrameNavigationRequested += (sender, args) => 
                {
                    NavigateFrames(args.Offset);
                };
                
                _webInterface.ProcessAllRequested += (sender, args) => 
                {
                    ProcessAllFrames();
                };
                
                _webInterface.ModelPathSelected += (sender, args) => 
                {
                    SetModelPath(args.ModelPath);
                };
                
                Console.WriteLine("Web interface initialized");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing web interface: {ex.Message}");
            }
        }
        
        public void SetModelPath(string modelPath)
        {
            try
            {
                _currentModelPath = modelPath;
                Console.WriteLine($"Setting model path: {modelPath}");
                
                // Check if the path exists
                if (!File.Exists(modelPath) && !Directory.Exists(modelPath))
                {
                    Console.WriteLine($"Model path does not exist: {modelPath}");
                    _webInterface.SetModelPath(modelPath, false);
                    return;
                }
                
                // Set the model path in SAM
                _sam.SetModelPath(modelPath);
                
                // Try to load the model
                _sam.LoadONNXModel();
                
                // Check if model was loaded successfully
                bool isLoaded = _sam.IsReady();
                
                _webInterface.SetModelPath(modelPath, isLoaded);
                
                if (isLoaded)
                {
                    Console.WriteLine($"Model loaded successfully: {modelPath}");
                    
                    // Re-process the current frame if it exists
                    if (_currentFrame != null && _currentPromotions.Count > 0)
                    {
                        ProcessCurrentFrame();
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to load model: {modelPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting model path: {ex.Message}");
                _webInterface.SetModelPath(modelPath, false);
            }
        }

        public bool OpenVideo(string videoPath)
        {
            try
            {
                // Clean up any existing video
                CloseVideo();
                
                _videoPath = videoPath;
                _videoCapture = new VideoCapture(videoPath);
                
                if (!_videoCapture.IsOpened())
                {
                    Console.WriteLine($"Failed to open video: {videoPath}");
                    return false;
                }
                
                // Get video properties
                _fps = _videoCapture.Fps;
                _totalFrames = (int)_videoCapture.FrameCount;
                
                Console.WriteLine($"Opened video: {videoPath}");
                Console.WriteLine($"FPS: {_fps}, Total frames: {_totalFrames}");
                
                // Read the first frame
                _currentFrameIndex = 0;
                GetFrame(_currentFrameIndex);
                
                // Update the web interface
                UpdateWebInterface();
                
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error opening video: {ex.Message}");
                return false;
            }
        }

        public void CloseVideo()
        {
            try
            {
                if (_videoCapture != null && _videoCapture.IsOpened())
                {
                    _videoCapture.Release();
                    _videoCapture.Dispose();
                    _videoCapture = null;
                }
                
                _currentFrame?.Dispose();
                _currentFrame = null;
                _processedFrames.Clear();
                _currentPromotions.Clear();
                _currentFrameIndex = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing video: {ex.Message}");
            }
        }

        public Mat GetFrame(int frameIndex)
        {
            if (_videoCapture == null || !_videoCapture.IsOpened())
                return null;
                
            try
            {
                // Validate frame index
                frameIndex = Math.Max(0, Math.Min(_totalFrames - 1, frameIndex));
                
                // If the requested frame is not the current frame, seek to it
                if (_videoCapture.PosFrames != frameIndex)
                {
                    _videoCapture.Set(VideoCaptureProperties.PosFrames, frameIndex);
                }
                
                // Read the frame
                Mat frame = new Mat();
                if (_videoCapture.Read(frame))
                {
                    // Update current frame and index
                    _currentFrame?.Dispose();
                    _currentFrame = frame;
                    _currentFrameIndex = frameIndex;
                    
                    return _currentFrame;
                }
                else
                {
                    frame.Dispose();
                    Console.WriteLine($"Failed to read frame {frameIndex}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting frame {frameIndex}: {ex.Message}");
                return null;
            }
        }

        public void AddPoint(float x, float y, bool isAdditive)
        {
            if (_currentFrame == null)
                return;
                
            if (!_sam.IsReady())
            {
                Console.WriteLine("Cannot add point: Model not loaded");
                return;
            }
                
            try
            {
                // Create promotion object
                PointPromotion promotion = new PointPromotion(isAdditive ? OpType.ADD : OpType.REMOVE);
                
                // Convert normalized coordinates to image coordinates
                promotion.X = (int)(x * _currentFrame.Width);
                promotion.Y = (int)(y * _currentFrame.Height);
                
                // Transform for SAM
                Transforms ts = new Transforms(1024);
                PointPromotion transformed = ts.ApplyCoords(promotion, _currentFrame.Width, _currentFrame.Height);
                
                // Add to current promotions
                _currentPromotions.Add(transformed);
                
                // Process current frame and update
                ProcessCurrentFrame();
                
                Console.WriteLine($"Added point at ({x}, {y}), isAdditive: {isAdditive}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding point: {ex.Message}");
            }
        }

        public void AddBox(float x1, float y1, float x2, float y2)
        {
            if (_currentFrame == null)
                return;
                
            if (!_sam.IsReady())
            {
                Console.WriteLine("Cannot add box: Model not loaded");
                return;
            }
                
            try
            {
                // Create promotion object
                BoxPromotion promotion = new BoxPromotion();
                
                // Convert normalized coordinates to image coordinates
                promotion.mLeftUp.X = (int)(x1 * _currentFrame.Width);
                promotion.mLeftUp.Y = (int)(y1 * _currentFrame.Height);
                promotion.mRightBottom.X = (int)(x2 * _currentFrame.Width);
                promotion.mRightBottom.Y = (int)(y2 * _currentFrame.Height);
                
                // Transform for SAM
                Transforms ts = new Transforms(1024);
                BoxPromotion transformed = ts.ApplyBox(promotion, _currentFrame.Width, _currentFrame.Height);
                
                // Add to current promotions
                _currentPromotions.Add(transformed);
                
                // Process current frame and update
                ProcessCurrentFrame();
                
                Console.WriteLine($"Added box from ({x1}, {y1}) to ({x2}, {y2})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding box: {ex.Message}");
            }
        }

        public void ClearPromotions()
        {
            try
            {
                _currentPromotions.Clear();
                
                // Clear the processed mask for current frame
                if (_processedFrames.ContainsKey(_currentFrameIndex))
                {
                    _processedFrames.Remove(_currentFrameIndex);
                }
                
                // Update the web interface
                UpdateWebInterface();
                
                Console.WriteLine("Cleared all promotions");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing promotions: {ex.Message}");
            }
        }

        public void NavigateFrames(int offset)
        {
            try
            {
                int newFrameIndex = _currentFrameIndex + offset;
                newFrameIndex = Math.Max(0, Math.Min(_totalFrames - 1, newFrameIndex));
                
                if (newFrameIndex != _currentFrameIndex)
                {
                    // Get the new frame
                    GetFrame(newFrameIndex);
                    
                    // Update the web interface
                    UpdateWebInterface();
                    
                    Console.WriteLine($"Navigated to frame {newFrameIndex}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error navigating frames: {ex.Message}");
            }
        }

        private void ProcessCurrentFrame()
        {
            if (_currentFrame == null || _currentPromotions.Count == 0)
                return;
            
            if (!_sam.IsReady())
            {
                Console.WriteLine("Cannot process frame: Model not loaded");
                return;
            }
                
            try
            {
                // Process the frame with SAM
                MaskData maskData = _sam.ProcessVideoFrame(_currentFrame, _currentPromotions);
                
                if (maskData != null)
                {
                    // Store the processed mask
                    if (_processedFrames.ContainsKey(_currentFrameIndex))
                    {
                        _processedFrames[_currentFrameIndex] = maskData;
                    }
                    else
                    {
                        _processedFrames.Add(_currentFrameIndex, maskData);
                    }
                    
                    // Update the web interface
                    UpdateWebInterface();
                    
                    Console.WriteLine($"Processed frame {_currentFrameIndex}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing current frame: {ex.Message}");
            }
        }

        public void ProcessAllFrames()
        {
            if (_isProcessing)
            {
                // Cancel any ongoing processing
                _processingCancellationSource?.Cancel();
                return;
            }
            
            if (_currentFrame == null || _currentPromotions.Count == 0)
            {
                Console.WriteLine("Cannot process all frames: No current frame or promotions");
                return;
            }
            
            if (!_sam.IsReady())
            {
                Console.WriteLine("Cannot process all frames: Model not loaded");
                return;
            }
            
            try
            {
                _isProcessing = true;
                _processingCancellationSource = new CancellationTokenSource();
                CancellationToken token = _processingCancellationSource.Token;
                
                Task.Run(() => 
                {
                    try
                    {
                        Console.WriteLine("Starting to process all frames...");
                        
                        // Save current frame index to restore later
                        int startFrameIndex = _currentFrameIndex;
                        
                        // Process each frame
                        for (int i = 0; i < _totalFrames; i++)
                        {
                            if (token.IsCancellationRequested)
                            {
                                Console.WriteLine("Processing cancelled");
                                break;
                            }
                            
                            // Get the frame
                            Mat frame = GetFrame(i);
                            
                            if (frame != null)
                            {
                                // Process the frame with SAM
                                MaskData maskData = _sam.ProcessVideoFrame(frame, _currentPromotions);
                                
                                if (maskData != null)
                                {
                                    lock (_lockObject)
                                    {
                                        // Store the processed mask
                                        if (_processedFrames.ContainsKey(i))
                                        {
                                            _processedFrames[i] = maskData;
                                        }
                                        else
                                        {
                                            _processedFrames.Add(i, maskData);
                                        }
                                    }
                                    
                                    Console.WriteLine($"Processed frame {i}/{_totalFrames}");
                                    
                                    // Update the web interface periodically
                                    if (i % 10 == 0 || i == _totalFrames - 1)
                                    {
                                        UpdateWebInterface();
                                    }
                                }
                            }
                        }
                        
                        // Restore the starting frame
                        GetFrame(startFrameIndex);
                        UpdateWebInterface();
                        
                        Console.WriteLine("Finished processing all frames");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during batch processing: {ex.Message}");
                    }
                    finally
                    {
                        _isProcessing = false;
                    }
                }, token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting batch processing: {ex.Message}");
                _isProcessing = false;
            }
        }

        private void UpdateWebInterface()
        {
            if (_webInterface == null || _currentFrame == null)
                return;
                
            try
            {
                // Create mask visualization if available
                Mat maskVisualization = null;
                if (_processedFrames.TryGetValue(_currentFrameIndex, out MaskData maskData))
                {
                    maskVisualization = CreateMaskVisualization(maskData);
                }
                
                // Update the web interface
                _webInterface.SetFrameInfo(_currentFrameIndex, _totalFrames);
                _webInterface.UpdateImage(_currentFrame);
                
                if (maskVisualization != null)
                {
                    _webInterface.UpdateMask(maskVisualization);
                    maskVisualization.Dispose();
                }
                
                if (!string.IsNullOrEmpty(_videoPath))
                {
                    _webInterface.SetVideoPath(_videoPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating web interface: {ex.Message}");
            }
        }

        private Mat CreateMaskVisualization(MaskData maskData)
        {
            if (maskData == null || _currentFrame == null)
                return null;
                
            try
            {
                // Create a visualization of the mask
                Mat maskVis = new Mat(_currentFrame.Size(), MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
                
                // Get the mask data
                float[] maskArray = maskData.mMask.ToArray();
                
                // Create colored overlay for mask
                for (int y = 0; y < _currentFrame.Height; y++)
                {
                    for (int x = 0; x < _currentFrame.Width; x++)
                    {
                        int index = y * _currentFrame.Width + x;
                        if (index < maskArray.Length && maskArray[index] > _sam.mask_threshold)
                        {
                            // Use semi-transparent red for the mask
                            maskVis.Set(y, x, new Vec4b(0, 0, 255, 128));
                        }
                    }
                }
                
                return maskVis;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating mask visualization: {ex.Message}");
                return null;
            }
        }

        public void ShowWebInterface()
        {
            _webInterface?.Show();
        }

        public void SaveSegmentedVideo(string outputPath)
        {
            if (_videoCapture == null || !_videoCapture.IsOpened() || _processedFrames.Count == 0)
            {
                Console.WriteLine("Cannot save video: No video opened or no frames processed");
                return;
            }
            
            try
            {
                // Create video writer
                int width = (int)_videoCapture.Get(VideoCaptureProperties.FrameWidth);
                int height = (int)_videoCapture.Get(VideoCaptureProperties.FrameHeight);
                
                using (VideoWriter writer = new VideoWriter(outputPath, FourCC.MP4V, _fps, new Size(width, height)))
                {
                    Console.WriteLine($"Saving segmented video to {outputPath}...");
                    
                    // Save current frame index to restore later
                    int startFrameIndex = _currentFrameIndex;
                    
                    // Process each frame
                    for (int i = 0; i < _totalFrames; i++)
                    {
                        // Get the frame
                        Mat frame = GetFrame(i);
                        
                        if (frame != null)
                        {
                            // Apply mask if available
                            if (_processedFrames.TryGetValue(i, out MaskData maskData))
                            {
                                Mat masked = ApplyMaskToFrame(frame, maskData);
                                writer.Write(masked);
                                masked.Dispose();
                            }
                            else
                            {
                                writer.Write(frame);
                            }
                            
                            if (i % 10 == 0 || i == _totalFrames - 1)
                            {
                                Console.WriteLine($"Saved frame {i}/{_totalFrames}");
                            }
                        }
                    }
                    
                    // Restore the starting frame
                    GetFrame(startFrameIndex);
                    
                    Console.WriteLine("Finished saving segmented video");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving segmented video: {ex.Message}");
            }
        }

        private Mat ApplyMaskToFrame(Mat frame, MaskData maskData)
        {
            if (maskData == null || frame == null)
                return frame.Clone();
                
            try
            {
                // Create output frame
                Mat output = frame.Clone();
                
                // Get the mask data
                float[] maskArray = maskData.mMask.ToArray();
                
                // Apply colored overlay for mask
                for (int y = 0; y < frame.Height; y++)
                {
                    for (int x = 0; x < frame.Width; x++)
                    {
                        int index = y * frame.Width + x;
                        if (index < maskArray.Length && maskArray[index] > _sam.mask_threshold)
                        {
                            // Add red tint to the segmented region
                            Vec3b pixel = frame.Get<Vec3b>(y, x);
                            output.Set(y, x, new Vec3b(
                                (byte)Math.Min(255, (int)pixel[0]),
                                (byte)Math.Min(255, (int)(pixel[1] * 0.5)),
                                (byte)Math.Min(255, (int)(pixel[2] * 0.5))
                            ));
                        }
                    }
                }
                
                return output;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error applying mask to frame: {ex.Message}");
                return frame.Clone();
            }
        }
    }
}