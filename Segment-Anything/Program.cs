using System;
using System.IO;
using System.Threading;

namespace SAMViewer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("SAM Video Segmentation");
            Console.WriteLine("======================");
            
            try
            {
                if (args.Length == 0)
                {
                    // Launch SAM Viewer UI
                    MainWindow window = new MainWindow();
                    Console.WriteLine("Launching SAM Viewer UI...");
                    
                    // Start UI thread
                    Thread uiThread = new Thread(() => {
                        // Create and start application
                        System.Windows.Application app = new System.Windows.Application();
                        app.Run(window);
                    });
                    uiThread.SetApartmentState(ApartmentState.STA);
                    uiThread.Start();
                    uiThread.Join();
                }
                else if (args[0] == "video")
                {
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: SAMViewer video <path-to-video>");
                        return;
                    }
                    
                    string videoPath = args[1];
                    
                    if (!File.Exists(videoPath))
                    {
                        Console.WriteLine($"Error: Video file not found: {videoPath}");
                        return;
                    }
                    
                    Console.WriteLine($"Opening video: {videoPath}");
                    
                    // Create video SAM instance
                    VideoSAM videoSam = new VideoSAM();
                    
                    // Open the video
                    if (videoSam.OpenVideo(videoPath))
                    {
                        // Show web interface
                        videoSam.ShowWebInterface();
                        
                        Console.WriteLine("Web interface opened. Press Enter to exit when done...");
                        Console.ReadLine();
                        
                        // Ask if user wants to save the segmented video
                        Console.Write("Save segmented video? (y/n): ");
                        string response = Console.ReadLine().Trim().ToLower();
                        
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
                }
                else if (args[0] == "help" || args[0] == "--help" || args[0] == "-h")
                {
                    ShowHelp();
                }
                else
                {
                    Console.WriteLine($"Unknown command: {args[0]}");
                    ShowHelp();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
        
        static void ShowHelp()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  SAMViewer                - Launch the SAM Viewer UI");
            Console.WriteLine("  SAMViewer video <path>   - Open a video for segmentation");
            Console.WriteLine("  SAMViewer help           - Show this help message");
        }
    }
}