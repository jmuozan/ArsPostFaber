# Hand Detection with MediaPipe.NET - Complete Guide

This guide provides step-by-step instructions for setting up and running a hand detection application using MediaPipe.NET on a MacBook Pro with M1 chip (or any compatible system).

## Prerequisites

- .NET 6.0 SDK or newer (https://dotnet.microsoft.com/download)
- Visual Studio Code or any other code editor
- Basic terminal/command line knowledge

## Step 1: Set Up Your Project

1. Open Terminal and create a new directory for your project:

```bash
mkdir HandDetectionApp
cd HandDetectionApp
```

2. Create a new console application:

```bash
dotnet new console
```

3. Add the required NuGet packages:

```bash
dotnet add package MediaPipe.Net
dotnet add package MediaPipe.Net.Runtime.CPU
dotnet add package SixLabors.ImageSharp
```

## Step 2: Create the Program Code

Replace the contents of `Program.cs` with the following code:

```csharp
using System;
using System.IO;
using Mediapipe.Net.Framework;
using Mediapipe.Net.Framework.Format;
using Mediapipe.Net.Framework.Packets;
using Mediapipe.Net.Framework.Port;
using Mediapipe.Net.Framework.Protobuf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace HandDetectionApp
{
    class Program
    {
        // Hand detection graph configuration
        private const string handDetectionConfig = @"
input_stream: ""input_video""
output_stream: ""output_video""
output_stream: ""hand_landmarks""

# Throttles the images flowing downstream for flow control.
node {
  calculator: ""FlowLimiterCalculator""
  input_stream: ""input_video""
  input_stream: ""FINISHED:output_video""
  input_stream_info: {
    tag_index: ""FINISHED""
    back_edge: true
  }
  output_stream: ""throttled_input_video""
}

# Hand detection subgraph
node {
  calculator: ""HandDetectionCpu""
  input_stream: ""IMAGE:throttled_input_video""
  output_stream: ""DETECTIONS:hand_detections""
}

# Hand landmarks subgraph
node {
  calculator: ""HandLandmarkCpu""
  input_stream: ""IMAGE:throttled_input_video""
  input_stream: ""DETECTIONS:hand_detections""
  output_stream: ""LANDMARKS:hand_landmarks""
  output_stream: ""HANDEDNESS:handedness""
  output_stream: ""PALM_DETECTIONS:palm_detections""
  output_stream: ""HAND_ROIS_FROM_LANDMARKS:hand_rects_from_landmarks""
  output_stream: ""HAND_ROIS_FROM_PALM_DETECTIONS:hand_rects_from_palm_detections""
}";

        static void Main(string[] args)
        {
            Console.WriteLine("Starting hand detection...");
            
            if (args.Length == 0)
            {
                Console.WriteLine("Please provide a path to an image file.");
                Console.WriteLine("Usage: dotnet run -- /path/to/your/image.jpg");
                return;
            }
            
            string imagePath = args[0];
            if (!File.Exists(imagePath))
            {
                Console.WriteLine($"Cannot find image at path: {imagePath}");
                return;
            }
            
            try
            {
                // Create the MediaPipe graph
                var graph = new CalculatorGraph(handDetectionConfig);
                
                // Add output stream poller for the landmarks
                var landmarksStream = graph.AddOutputStreamPoller<NormalizedLandmarkList>("hand_landmarks").Value();
                var landmarksPacket = new NormalizedLandmarkListPacket();
                
                // Start the graph
                graph.StartRun().AssertOk();

                // Load and decode the image
                using var imageFile = File.OpenRead(imagePath);
                using var image = Image.Load<Rgba32>(imageFile);
                
                Console.WriteLine($"Processing image: {imagePath} ({image.Width}x{image.Height})");
                
                // Create an ImageFrame from the loaded image
                var imageFrame = CreateImageFrameFromImage(image);
                
                // Add the image to the graph
                long timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds() * 1000;
                using var timestampedImagePacket = new ImageFramePacket(imageFrame, new Timestamp(timestamp));
                graph.AddPacketToInputStream("input_video", timestampedImagePacket).AssertOk();
                
                // Wait until the graph is idle
                graph.WaitUntilIdle().AssertOk();
                
                // Get and process the landmarks
                if (landmarksStream.Next(landmarksPacket))
                {
                    var landmarks = landmarksPacket.Get();
                    Console.WriteLine($"Detected {landmarks.Landmark.Count} hand landmarks:");
                    
                    // Print out each landmark
                    for (int i = 0; i < landmarks.Landmark.Count; i++)
                    {
                        var landmark = landmarks.Landmark[i];
                        Console.WriteLine($"  Landmark {i}: X={landmark.X:F4}, Y={landmark.Y:F4}, Z={landmark.Z:F4}");
                    }
                }
                else
                {
                    Console.WriteLine("No hand landmarks detected in the image.");
                }
                
                // Shut down the graph
                graph.CloseAllPacketSources().AssertOk();
                graph.WaitUntilDone().AssertOk();
                
                Console.WriteLine("Hand detection completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during hand detection: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        
        private static ImageFrame CreateImageFrameFromImage(Image<Rgba32> image)
        {
            // Create a buffer for pixel data
            byte[] pixelData = new byte[image.Width * image.Height * 4]; // 4 bytes per pixel (RGBA)
            
            // Copy pixel data to the buffer
            image.CopyPixelDataTo(pixelData);
            
            // Create and return an ImageFrame
            return new ImageFrame(
                ImageFormat.Types.Format.Srgba, 
                image.Width, 
                image.Height, 
                image.Width * 4, // Width step (stride)
                pixelData
            );
        }
    }
}
```

## Step 3: Build and Run the Application

1. Build your application:

```bash
dotnet build
```

2. Run the application with a test image:

```bash
dotnet run -- /path/to/your/image.jpg
```

Replace `/path/to/your/image.jpg` with the actual path to an image you want to test. The image should preferably include a visible hand.

## Step 4: Understanding the Output

The application will process the image and output information about detected hand landmarks:

- Each hand has 21 landmarks representing different parts of the hand (knuckles, fingertips, etc.)
- For each landmark, the program outputs its X, Y, and Z coordinates:
  - X and Y coordinates are normalized to [0.0, 1.0] range (relative to the image width and height)
  - Z represents the depth, also normalized

## Example of a Successful Output

```
Starting hand detection...
Processing image: hand_image.jpg (1280x720)
Detected 21 hand landmarks:
  Landmark 0: X=0.4532, Y=0.6257, Z=0.0000  # Wrist
  Landmark 1: X=0.4631, Y=0.5842, Z=-0.0153  # Thumb base
  Landmark 2: X=0.4751, Y=0.5423, Z=-0.0243
  ...
  Landmark 20: X=0.3152, Y=0.5763, Z=-0.0187  # Pinky fingertip
Hand detection completed successfully.
```

## Optional: Webcam Hand Detection

If you'd like to use your webcam for real-time hand detection, you'll need to add another package to handle video input:

```bash
dotnet add package OpenCvSharp4
```

Then replace `Program.cs` with the following code:

```csharp
using System;
using System.IO;
using System.Threading;
using Mediapipe.Net.Framework;
using Mediapipe.Net.Framework.Format;
using Mediapipe.Net.Framework.Packets;
using Mediapipe.Net.Framework.Port;
using Mediapipe.Net.Framework.Protobuf;
using OpenCvSharp;

namespace HandDetectionApp
{
    class Program
    {
        // Hand detection graph configuration (same as before)
        private const string handDetectionConfig = @"
input_stream: ""input_video""
output_stream: ""output_video""
output_stream: ""hand_landmarks""

# Throttles the images flowing downstream for flow control.
node {
  calculator: ""FlowLimiterCalculator""
  input_stream: ""input_video""
  input_stream: ""FINISHED:output_video""
  input_stream_info: {
    tag_index: ""FINISHED""
    back_edge: true
  }
  output_stream: ""throttled_input_video""
}

# Hand detection subgraph
node {
  calculator: ""HandDetectionCpu""
  input_stream: ""IMAGE:throttled_input_video""
  output_stream: ""DETECTIONS:hand_detections""
}

# Hand landmarks subgraph
node {
  calculator: ""HandLandmarkCpu""
  input_stream: ""IMAGE:throttled_input_video""
  input_stream: ""DETECTIONS:hand_detections""
  output_stream: ""LANDMARKS:hand_landmarks""
  output_stream: ""HANDEDNESS:handedness""
  output_stream: ""PALM_DETECTIONS:palm_detections""
  output_stream: ""HAND_ROIS_FROM_LANDMARKS:hand_rects_from_landmarks""
  output_stream: ""HAND_ROIS_FROM_PALM_DETECTIONS:hand_rects_from_palm_detections""
}";

        static void Main(string[] args)
        {
            Console.WriteLine("Starting hand detection with webcam...");
            
            try
            {
                // Create the MediaPipe graph
                var graph = new CalculatorGraph(handDetectionConfig);
                
                // Add output stream poller for the landmarks
                var landmarksStream = graph.AddOutputStreamPoller<NormalizedLandmarkList>("hand_landmarks").Value();
                var landmarksPacket = new NormalizedLandmarkListPacket();
                
                // Start the graph
                graph.StartRun().AssertOk();
                
                // Open the webcam
                using var capture = new VideoCapture(0); // 0 = default camera
                if (!capture.IsOpened())
                {
                    Console.WriteLine("Failed to open webcam");
                    return;
                }
                
                // Configure webcam
                capture.Set(VideoCaptureProperties.FrameWidth, 640);
                capture.Set(VideoCaptureProperties.FrameHeight, 480);
                
                // Create window to display the video
                using var window = new Window("Hand Detection");
                
                // Process frames in a loop
                using var frame = new Mat();
                long frameTimestamp = 0;
                
                Console.WriteLine("Press ESC key to exit");
                
                while (true)
                {
                    // Capture a frame
                    if (!capture.Read(frame))
                    {
                        Console.WriteLine("Failed to read from webcam");
                        break;
                    }
                    
                    // Convert the OpenCV frame to MediaPipe ImageFrame
                    using var imageFrame = ConvertMatToImageFrame(frame);
                    
                    // Add the frame to the graph
                    frameTimestamp += 1;
                    using var timestampedImagePacket = new ImageFramePacket(imageFrame, new Timestamp(frameTimestamp));
                    graph.AddPacketToInputStream("input_video", timestampedImagePacket).AssertOk();
                    
                    // Process hand landmarks if available
                    if (landmarksStream.Next(landmarksPacket))
                    {
                        var landmarks = landmarksPacket.Get();
                        
                        // Draw landmarks on the frame
                        DrawLandmarks(frame, landmarks);
                        
                        Console.WriteLine($"Detected {landmarks.Landmark.Count} hand landmarks");
                    }
                    
                    // Display the frame
                    window.ShowImage(frame);
                    
                    // Exit if ESC key is pressed
                    int key = Cv2.WaitKey(1);
                    if (key == 27) // ESC key
                        break;
                }
                
                // Clean up
                graph.CloseAllPacketSources().AssertOk();
                graph.WaitUntilDone().AssertOk();
                
                Console.WriteLine("Hand detection completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during hand detection: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        
        private static ImageFrame ConvertMatToImageFrame(Mat mat)
        {
            // Convert BGR to RGBA
            using var rgbaMat = new Mat();
            Cv2.CvtColor(mat, rgbaMat, ColorConversionCodes.BGR2RGBA);
            
            // Get pixel data
            byte[] pixelData = new byte[rgbaMat.Width * rgbaMat.Height * 4];
            Marshal.Copy(rgbaMat.Data, pixelData, 0, pixelData.Length);
            
            // Create ImageFrame
            return new ImageFrame(
                ImageFormat.Types.Format.Srgba,
                rgbaMat.Width,
                rgbaMat.Height,
                rgbaMat.Width * 4, // Width step
                pixelData
            );
        }
        
        private static void DrawLandmarks(Mat frame, NormalizedLandmarkList landmarks)
        {
            // Draw each landmark as a circle
            foreach (var landmark in landmarks.Landmark)
            {
                int x = (int)(landmark.X * frame.Width);
                int y = (int)(landmark.Y * frame.Height);
                
                Cv2.Circle(frame, new Point(x, y), 5, new Scalar(0, 255, 0), -1);
            }
            
            // Connect landmarks with lines to show hand structure
            // These connections represent the hand skeleton
            int[][] connections = new int[][] {
                new int[] {0, 1}, new int[] {1, 2}, new int[] {2, 3}, new int[] {3, 4},  // Thumb
                new int[] {0, 5}, new int[] {5, 6}, new int[] {6, 7}, new int[] {7, 8},  // Index finger
                new int[] {0, 9}, new int[] {9, 10}, new int[] {10, 11}, new int[] {11, 12},  // Middle finger
                new int[] {0, 13}, new int[] {13, 14}, new int[] {14, 15}, new int[] {15, 16},  // Ring finger
                new int[] {0, 17}, new int[] {17, 18}, new int[] {18, 19}, new int[] {19, 20},  // Pinky
                new int[] {5, 9}, new int[] {9, 13}, new int[] {13, 17}, // Palm
            };
            
            foreach (var connection in connections) 
            {
                if (connection.Length == 2 && connection[0] < landmarks.Landmark.Count && connection[1] < landmarks.Landmark.Count) 
                {
                    var point1 = landmarks.Landmark[connection[0]];
                    var point2 = landmarks.Landmark[connection[1]];
                    
                    int x1 = (int)(point1.X * frame.Width);
                    int y1 = (int)(point1.Y * frame.Height);
                    int x2 = (int)(point2.X * frame.Width);
                    int y2 = (int)(point2.Y * frame.Height);
                    
                    Cv2.Line(frame, new Point(x1, y1), new Point(x2, y2), new Scalar(255, 0, 0), 2);
                }
            }
        }
    }
}
```

Don't forget to add the necessary imports at the top:

```csharp
using System.Runtime.InteropServices;
```

## Troubleshooting

### Common Issues and Solutions

1. **"Unable to load shared library" error**
   - Make sure you have the correct MediaPipe.Net.Runtime.CPU package installed
   - Check if you have any compatibility issues with your processor

2. **No hand landmarks detected**
   - Make sure your hand is clearly visible in the image
   - Try with a different image or adjust your webcam position
   - Ensure good lighting conditions

3. **Performance issues**
   - Try reducing image/video resolution
   - Close other resource-intensive applications
   - If using webcam, try reducing frame rate

4. **OpenCVSharp4 not found (for webcam version)**
   - Make sure you have correctly installed the OpenCvSharp4 package
   - You may need to install OpenCvSharp4.runtime.{your-platform} package as well

## Hand Landmark Reference

The hand landmarks detected by MediaPipe are numbered 0-20:

- Landmark 0: Wrist
- Landmarks 1-4: Thumb (from base to tip)
- Landmarks 5-8: Index finger (from base to tip)
- Landmarks 9-12: Middle finger (from base to tip)
- Landmarks 13-16: Ring finger (from base to tip)
- Landmarks 17-20: Pinky finger (from base to tip)

## Additional Notes

- The application uses CPU-based detection, which is more compatible but potentially slower than GPU-based detection
- For real production applications, you might want to implement error handling and validation more robustly
- MediaPipe.NET is based on Google's MediaPipe framework, which provides many other useful features beyond hand detection

## Resources

- MediaPipe official documentation: https://mediapipe.dev/
- MediaPipe.NET GitHub repository: https://github.com/vignetteapp/MediaPipe.NET
