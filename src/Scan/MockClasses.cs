using System;
using System.Collections.Generic;

#if !WINDOWS
namespace SAMforGrasshopper
{
    // Mock classes for platform-specific functionality
    // These enable the code to compile in a cross-platform way

    // SAM related classes
    public class SAM
    {
        public float mask_threshold = 0.0f;
        
        public void SetModelPath(string path) 
        { 
            Console.WriteLine($"[MOCK] SAM.SetModelPath: {path}");
        }
        
        public void LoadONNXModel() 
        { 
            Console.WriteLine("[MOCK] SAM.LoadONNXModel called - simulating successful model load");
        }
        
        public float[] Encode(OpenCvSharp.Mat image, int width, int height) 
        { 
            Console.WriteLine($"[MOCK] SAM.Encode: image size {width}x{height}");
            // Return dummy embedding
            return new float[256]; 
        }
        
        public MaskData Decode(List<Promotion> promotions, float[] embedding, int width, int height) 
        { 
            Console.WriteLine($"[MOCK] SAM.Decode: with {promotions.Count} promotions");
            // Return dummy mask
            MaskData mask = new MaskData();
            // Fill with dummy values (1 for entire image)
            for (int i = 0; i < width * height; i++)
            {
                mask.mMask.Add(1.0f);
            }
            return mask;
        }
    }
    
    public class MaskData
    {
        public List<float> mMask = new List<float>();
    }
    
    public class Promotion { }
    
    public class PointPromotion : Promotion
    {
        public PointPromotion(OpType type) { Type = type; }
        public OpType Type { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
    }
    
    public class BoxPromotion : Promotion
    {
        public PointPromotion mLeftUp { get; set; }
        public PointPromotion mRightBottom { get; set; }
    }
    
    public enum OpType
    {
        ADD,
        REMOVE
    }
    
    public class VideoProcessor : IDisposable
    {
        private string _path;
        private int _totalFrames = 100; // Mock 100 frames
        
        public VideoProcessor(string path) 
        { 
            _path = path;
            Console.WriteLine($"[MOCK] VideoProcessor created for path: {path}");
        }
        
        public void Initialize() 
        { 
            Console.WriteLine("[MOCK] VideoProcessor initialized");
        }
        
        public int TotalFrames { get { return _totalFrames; } }
        
        public OpenCvSharp.Mat GetFrame(int frameNumber) 
        { 
            Console.WriteLine($"[MOCK] Getting frame {frameNumber}");
            // Create a dummy color image (all blue)
            OpenCvSharp.Mat frame = new OpenCvSharp.Mat(480, 640, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(255, 0, 0));
            return frame; 
        }
        
        public void Dispose() 
        { 
            Console.WriteLine("[MOCK] VideoProcessor disposed");
        }
    }
    
    // WebKit mock classes for macOS platform
    
    namespace AppKit
    {
        public class NSApplication
        {
            public static NSApplication SharedApplication { get; } = new NSApplication();
            public void InvokeOnMainThread(Action action) { action?.Invoke(); }
            public void ActivateIgnoringOtherApps(bool activate) { }
        }
        
        public class NSWindow
        {
            public static string WillCloseNotification = "WillCloseNotification";
            
            public NSWindow(CoreGraphics.CGRect frame, NSWindowStyle style, NSBackingStore backingStore, bool flag) { }
            public string Title { get; set; }
            public NSViewController ContentViewController { get; set; }
            public void MakeKeyAndOrderFront(object sender) { }
            public void OrderOut(object sender) { }
            public void Close() { }
        }
        
        public class NSViewController : IDisposable
        {
            public NSView View { get; set; }
            public void Dispose() { }
        }
        
        public class NSView
        {
        }
        
        public class NSWindowController : IDisposable
        {
            public NSWindow Window { get; set; }
            public void Dispose() { }
        }
        
        public enum NSWindowStyle
        {
            Titled = 1,
            Closable = 2,
            Resizable = 4
        }
        
        public enum NSBackingStore
        {
            Buffered = 2
        }
    }
    
    namespace Foundation
    {
        public class ExportAttribute : Attribute
        {
            public ExportAttribute(string name) { }
        }
        
        public static class Export
        {
            public static ExportAttribute webView_didFinishNavigation(string name) => new ExportAttribute(name);
        }
        
        public class NSObject : IDisposable
        {
            public void Dispose() { }
        }
        
        public class NSNotificationCenter
        {
            public static NSNotificationCenter DefaultCenter { get; } = new NSNotificationCenter();
            public void AddObserver(string name, Action<NSNotification> handler) { }
        }
        
        public class NSNotification
        {
            public object Object { get; set; }
        }
        
        public class NSUrl
        {
            public NSUrl(string path, bool isDir) { }
        }
        
        public class NSString
        {
            public NSString(string s) { }
        }
        
        public class NSError
        {
            public string LocalizedDescription { get; } = "";
        }
        
        public class NSDictionary
        {
            public object this[string key] => null;
        }
    }
    
    namespace WebKit
    {
        public class WKWebView : AppKit.NSView, IDisposable
        {
            public WKWebView(CoreGraphics.CGRect frame, WKWebViewConfiguration config) { }
            public Foundation.NSObject NavigationDelegate { get; set; }
            public void LoadFileUrl(Foundation.NSUrl url, Foundation.NSUrl url2) { }
            public void EvaluateJavaScript(Foundation.NSString script, Action<Foundation.NSObject, Foundation.NSError> callback) { }
            public void Dispose() { }
        }
        
        public class WKWebViewConfiguration
        {
            public WKUserContentController UserContentController { get; set; }
        }
        
        public class WKUserContentController
        {
            public void AddScriptMessageHandler(IWKScriptMessageHandler handler, string name) { }
        }
        
        public interface IWKScriptMessageHandler { }
        
        public class WKScriptMessage
        {
            public string Name { get; } = "";
            public object Body { get; } = null;
        }
        
        public class WKNavigation { }
        
        public interface IWKNavigationDelegate { }
    }
    
    namespace CoreGraphics
    {
        public struct CGRect
        {
            public CGRect(float x, float y, float width, float height) { }
            public CGRect(double x, double y, double width, double height) { }
        }
    }
}
#endif