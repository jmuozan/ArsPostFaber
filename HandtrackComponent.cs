#pragma warning disable CA1416 // Suppress platform compatibility warnings

using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Newtonsoft.Json;
using System.IO;
using System.Diagnostics;

namespace crft
{
    // Hand landmarks data class to store tracking results
    public class HandLandmarks
    {
        public List<PointF> Points { get; set; } = new List<PointF>();
        public Bitmap DebugImage { get; set; }
        public bool HandDetected { get; set; } = false;

        public HandLandmarks Clone()
        {
            HandLandmarks clone = new HandLandmarks();
            clone.HandDetected = this.HandDetected;
            clone.Points = new List<PointF>(this.Points);

            if (this.DebugImage != null)
            {
                // Use a cross-platform method to clone the Bitmap
                clone.DebugImage = new Bitmap(this.DebugImage);
            }

            return clone;
        }

        public void Dispose()
        {
            if (DebugImage != null)
            {
                DebugImage.Dispose();
                DebugImage = null;
            }
        }
    }

    public class HandTrackingWebcamComponent : WebcamComponent
    {
        private HandLandmarks _currentHandLandmarks = new HandLandmarks();
        private Task _handTrackingTask = null;
        private bool _isProcessingHands = false;

        public HandTrackingWebcamComponent()
          : base()
        {
            this.Name = "Webcam Hand Tracking";
            this.NickName = "HandTrack";
            this.Description = "Captures webcam video feed and tracks hand landmarks.";
            this.Category = "Display";
            this.SubCategory = "Preview";
        }

        // Add a property to access the current frame from the base WebcamComponent
        protected Bitmap CurrentFrame
        {
            get
            {
                lock (_lockObject)
                {
                    return _currentFrame;
                }
            }
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            base.RegisterInputParams(pManager);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            base.RegisterOutputParams(pManager);
            pManager.AddGenericParameter("Hand Landmarks", "H", "Detected hand landmarks as points (0-1 normalized)", GH_ParamAccess.item);
            pManager.AddGenericParameter("Debug Image", "D", "Image with hand landmarks visualized", GH_ParamAccess.item);
        }

        // Add platform-specific checks and diagnostics
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            base.SolveInstance(DA);

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"[HandTrack] SolveInstance called. _isProcessingHands={_isProcessingHands}, _handTrackingTask?.IsCompleted={_handTrackingTask?.IsCompleted}");

            if (_isProcessingHands || (_handTrackingTask != null && !_handTrackingTask.IsCompleted))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"[HandTrack] Skipping: hand tracking task is running");
                DA.SetData(0, _currentHandLandmarks.Points);
                DA.SetData(1, _currentHandLandmarks.DebugImage);
                return;
            }

            _isProcessingHands = true;
            _handTrackingTask = Task.Run(() =>
            {
                try
                {
                    if (CurrentFrame != null)
                    {
                        string tempInputPath = Path.Combine(Path.GetTempPath(), "hand_input.jpg");
                        string tempOutputPath = Path.Combine(Path.GetTempPath(), "hand_output.json");
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"[HandTrack] Saving frame to: {tempInputPath}");
                        CurrentFrame.Save(tempInputPath, System.Drawing.Imaging.ImageFormat.Jpeg);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"[HandTrack] Frame saved. Running Python script...");

                        ProcessStartInfo startInfo = new ProcessStartInfo
                        {
                            FileName = "python3",
                            Arguments = $"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mediapipe", "handtrack.py")} {tempInputPath} {tempOutputPath}",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            Environment = { { "PYTHONHOME", "" }, { "PYTHONPATH", "" } }
                        };

                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"[HandTrack] Executing: {startInfo.FileName} {startInfo.Arguments}");
                        using (Process process = Process.Start(startInfo))
                        {
                            string output = process.StandardOutput.ReadToEnd();
                            string error = process.StandardError.ReadToEnd();
                            process.WaitForExit();
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"[HandTrack] Python exit code: {process.ExitCode}");
                            if (!string.IsNullOrWhiteSpace(output))
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"[HandTrack] Python stdout: {output}");
                            if (!string.IsNullOrWhiteSpace(error))
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"[HandTrack] Python stderr: {error}");
                            if (process.ExitCode != 0)
                            {
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"[HandTrack] Python script failed with exit code {process.ExitCode}");
                                return;
                            }
                        }

                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"[HandTrack] Checking for output file: {tempOutputPath}");
                        if (File.Exists(tempOutputPath))
                        {
                            string jsonContent = File.ReadAllText(tempOutputPath);
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"[HandTrack] Output file read. Content length: {jsonContent.Length}");
                            try
                            {
                                var landmarks = JsonConvert.DeserializeObject<List<List<PointF>>>(jsonContent);
                                if (landmarks != null && landmarks.Count > 0)
                                {
                                    _currentHandLandmarks.HandDetected = true;
                                    _currentHandLandmarks.Points = landmarks[0];
                                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"[HandTrack] Parsed {landmarks[0].Count} hand landmarks.");
                                    // Draw landmarks on a copy of the frame for debug image
                                    using (var debugBmp = new Bitmap(CurrentFrame))
                                    using (var g = Graphics.FromImage(debugBmp))
                                    {
                                        if (_currentHandLandmarks.Points.Count == 21)
                                        {
                                            Pen linePen = new Pen(Color.Lime, 3);
                                            Brush pointBrush = Brushes.Red;
                                            int[] palm = {0,1,2,3,4,0};
                                            for (int i = 0; i < palm.Length-1; i++)
                                                g.DrawLine(linePen, _currentHandLandmarks.Points[palm[i]], _currentHandLandmarks.Points[palm[i+1]]);
                                            int[][] fingers = new int[][] {
                                                new int[]{0,5,6,7,8},
                                                new int[]{0,9,10,11,12},
                                                new int[]{0,13,14,15,16},
                                                new int[]{0,17,18,19,20}
                                            };
                                            foreach (var finger in fingers)
                                                for (int i = 0; i < finger.Length-1; i++)
                                                    g.DrawLine(linePen, _currentHandLandmarks.Points[finger[i]], _currentHandLandmarks.Points[finger[i+1]]);
                                            foreach (var pt in _currentHandLandmarks.Points)
                                                g.FillEllipse(pointBrush, pt.X * debugBmp.Width - 5, pt.Y * debugBmp.Height - 5, 10, 10);
                                        }
                                        _currentHandLandmarks.DebugImage = new Bitmap(debugBmp);
                                    }
                                }
                                else
                                {
                                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "[HandTrack] No landmarks detected in JSON output.");
                                    _currentHandLandmarks.HandDetected = false;
                                    _currentHandLandmarks.DebugImage = null;
                                }
                            }
                            catch (Exception ex)
                            {
                                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"[HandTrack] Error parsing JSON output: {ex.Message}");
                            }
                        }
                        else
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "[HandTrack] No output file produced by Python script.");
                        }
                    }
                    else
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "[HandTrack] No current frame available for hand tracking.");
                    }
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"[HandTrack] Hand tracking error: {ex.Message}");
                }
                finally
                {
                    _isProcessingHands = false;
                }
            });

            DA.SetData(0, _currentHandLandmarks.Points);
            DA.SetData(1, _currentHandLandmarks.DebugImage);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        public override Guid ComponentGuid => new Guid("e07bf1f4-d31c-4479-9e25-fa234b4c0bb5");
    }
}

#pragma warning restore CA1416 // Re-enable platform compatibility warnings