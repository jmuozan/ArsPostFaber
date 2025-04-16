using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace SAMViewer.Mac
{
    class MacProgram
    {
        static void Main(string[] args)
        {
            Console.WriteLine("SAM Video Segmentation for macOS");
            Console.WriteLine("==============================");
            
            try
            {
                if (args.Length == 0)
                {
                    Console.WriteLine("Usage:");
                    Console.WriteLine("  SAMVideoSegmentation video <path>   - Open a video for segmentation");
                    Console.WriteLine("  SAMVideoSegmentation video <path> -m <model-path> - Open video with specific model");
                    Console.WriteLine("  SAMVideoSegmentation help           - Show this help message");
                    return;
                }
                
                if (args[0] == "video")
                {
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: SAMVideoSegmentation video <path-to-video> [-m <path-to-model>]");
                        return;
                    }
                    
                    string videoPath = args[1];
                    string modelPath = null;
                    
                    // Check for model path
                    for (int i = 2; i < args.Length - 1; i++)
                    {
                        if (args[i] == "-m" || args[i] == "--model")
                        {
                            modelPath = args[i + 1];
                            break;
                        }
                    }
                    
                    if (!File.Exists(videoPath))
                    {
                        Console.WriteLine($"Error: Video file not found: {videoPath}");
                        return;
                    }
                    
                    Console.WriteLine($"Opening video: {videoPath}");
                    if (!string.IsNullOrEmpty(modelPath))
                    {
                        Console.WriteLine($"Using model: {modelPath}");
                    }
                    
                    try
                    {
                        // Ensure Python is available
                        ProcessStartInfo pythonCheck = new ProcessStartInfo
                        {
                            FileName = "python3",
                            Arguments = "--version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        
                        Process pythonProcess = Process.Start(pythonCheck);
                        string pythonVersion = pythonProcess.StandardOutput.ReadToEnd();
                        pythonProcess.WaitForExit();
                        
                        if (pythonProcess.ExitCode != 0)
                        {
                            Console.WriteLine("Error: Python 3 is not installed or not in PATH.");
                            Console.WriteLine("Please install Python 3 and ensure it's available in your PATH.");
                            return;
                        }
                        
                        Console.WriteLine($"Using Python: {pythonVersion.Trim()}");
                    
                        // Create video SAM instance
                        Console.WriteLine("Initializing SAM video processor...");
                        VideoSAM videoSam = new VideoSAM();
                        
                        // Set model path if provided
                        if (!string.IsNullOrEmpty(modelPath))
                        {
                            Console.WriteLine($"Setting model path: {modelPath}");
                            videoSam.SetModelPath(modelPath);
                        }
                        
                        // Open the video
                        Console.WriteLine($"Opening video: {videoPath}");
                        if (videoSam.OpenVideo(videoPath))
                        {
                            // Show web interface
                            Console.WriteLine("Launching web interface...");
                            videoSam.ShowWebInterface();
                            
                            Console.WriteLine("Web interface should now be open in your browser.");
                            Console.WriteLine("If not, manually open: http://localhost:8000");
                            Console.WriteLine("Press Enter to exit when done...");
                            Console.ReadLine();
                            
                            // Ask if user wants to save the segmented video
                            Console.Write("Save segmented video? (y/n): ");
                            string response = Console.ReadLine()?.Trim().ToLower() ?? "n";
                            
                            if (response == "y" || response == "yes")
                            {
                                string outputPath = Path.Combine(
                                    Path.GetDirectoryName(videoPath),
                                    Path.GetFileNameWithoutExtension(videoPath) + "_segmented" + Path.GetExtension(videoPath)
                                );
                                
                                Console.WriteLine($"Saving segmented video to: {outputPath}");
                                videoSam.SaveSegmentedVideo(outputPath);
                                Console.WriteLine("Video saved successfully!");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Failed to open video: {videoPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in SAM video processing: {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                    }
                }
                else if (args[0] == "help" || args[0] == "--help" || args[0] == "-h")
                {
                    Console.WriteLine("Usage:");
                    Console.WriteLine("  SAMVideoSegmentation video <path>                 - Open a video for segmentation");
                    Console.WriteLine("  SAMVideoSegmentation video <path> -m <model-path> - Open video with specific model");
                    Console.WriteLine("  SAMVideoSegmentation help                         - Show this help message");
                }
                else
                {
                    Console.WriteLine($"Unknown command: {args[0]}");
                    Console.WriteLine("Usage:");
                    Console.WriteLine("  SAMVideoSegmentation video <path>                 - Open a video for segmentation");
                    Console.WriteLine("  SAMVideoSegmentation video <path> -m <model-path> - Open video with specific model");
                    Console.WriteLine("  SAMVideoSegmentation help                         - Show this help message");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}