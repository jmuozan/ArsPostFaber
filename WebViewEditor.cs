using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#if WINDOWS
using System.Windows.Forms;
#else
using System.Threading;
using SAMforGrasshopper.AppKit;
using SAMforGrasshopper.Foundation;
using SAMforGrasshopper.WebKit;
using SAMforGrasshopper.CoreGraphics;
#endif

namespace SAMforGrasshopper
{
    /// <summary>
    /// Cross-platform WebView-based editor for SAM Video Component
    /// </summary>
    public class WebViewEditor : IDisposable
    {
        private bool _isDisposed = false;
        private string _tempDirectory;
        private string _htmlPath;
        private string _jsPath;
        private string _cssPath;
        
#if WINDOWS
        // Windows implementation would go here using WebView2 or similar
        private Form _form;
#else
        // macOS implementation using WebKit
        private NSWindow _window;
        private WKWebView _webView;
        private NSViewController _viewController;
        private NSWindowController _windowController;
        private AutoResetEvent _initCompletedEvent = new AutoResetEvent(false);
#endif

        // Event handlers for communication with the component
        public event EventHandler<PointEventArgs> PointAdded;
        public event EventHandler<BoxEventArgs> BoxAdded;
        public event EventHandler ClearRequested;
        public event EventHandler<NavigationEventArgs> FrameNavigationRequested;
        public event EventHandler ProcessAllRequested;

        // Current state
        private int _currentFrame = 0;
        private int _totalFrames = 0;
        private string _currentImageData = string.Empty;
        private string _currentMaskData = string.Empty;
        private bool _isInitialized = false;
        private string _videoPath = string.Empty;
        
        /// <summary>
        /// Creates a new WebView-based editor
        /// </summary>
        public WebViewEditor()
        {
            // Create temporary directory for web files
            _tempDirectory = Path.Combine(Path.GetTempPath(), "SAMVideoEditor_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            
            // Create HTML, JS, and CSS files
            CreateWebFiles();
            
            // Initialize WebView
            Initialize();
        }
        
        /// <summary>
        /// Gets the temporary directory where HTML files are stored
        /// </summary>
        public string GetTempDirectory()
        {
            return _tempDirectory;
        }

        /// <summary>
        /// Initialize the WebView
        /// </summary>
        private void Initialize()
        {
#if WINDOWS
            // Windows implementation would go here
#else
            Console.WriteLine("WebViewEditor.Initialize() called");
            Console.WriteLine($"Using HTML file at: {_htmlPath}");
            
            if (!File.Exists(_htmlPath))
            {
                Console.WriteLine($"ERROR: HTML file does not exist: {_htmlPath}");
                Console.WriteLine("Contents of temp directory:");
                try
                {
                    if (Directory.Exists(_tempDirectory))
                    {
                        foreach (var file in Directory.GetFiles(_tempDirectory))
                        {
                            Console.WriteLine($" - {file}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Temp directory does not exist!");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error listing temp directory: {ex.Message}");
                }
            }
            
            // Initialize on the main thread
            NSApplication.SharedApplication.InvokeOnMainThread(() => {
                try
                {
                    Console.WriteLine("Creating WebView configuration...");
                    // Create web view configuration
                    WKWebViewConfiguration config = new WKWebViewConfiguration();
                    WKUserContentController userContentController = new WKUserContentController();
                    
                    Console.WriteLine("Adding message handlers...");
                    // Add message handlers for JavaScript to native communication
                    userContentController.AddScriptMessageHandler(new ScriptMessageHandler(this), "addPoint");
                    userContentController.AddScriptMessageHandler(new ScriptMessageHandler(this), "addBox");
                    userContentController.AddScriptMessageHandler(new ScriptMessageHandler(this), "clearFrame");
                    userContentController.AddScriptMessageHandler(new ScriptMessageHandler(this), "navigateFrame");
                    userContentController.AddScriptMessageHandler(new ScriptMessageHandler(this), "processAll");
                    
                    config.UserContentController = userContentController;
                    
                    Console.WriteLine("Creating WebView...");
                    // Create web view
                    CGRect frame = new CGRect(0, 0, 800, 600);
                    _webView = new WKWebView(frame, config);
                    _webView.NavigationDelegate = new WebViewDelegate(this);
                    
                    Console.WriteLine("Creating view controller...");
                    // Create view controller to host the web view
                    _viewController = new NSViewController();
                    _viewController.View = _webView;
                    
                    Console.WriteLine("Creating window...");
                    // Create window
                    _window = new NSWindow(
                        new CGRect(200, 200, 800, 600),
                        NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
                        NSBackingStore.Buffered,
                        false);
                    
                    _window.Title = "SAM Video Editor";
                    _window.ContentViewController = _viewController;
                    
                    Console.WriteLine("Creating window controller...");
                    // Create window controller
                    _windowController = new NSWindowController();
                    _windowController.Window = _window;
                    
                    Console.WriteLine("Setting up notification observers...");
                    // Handle window closing
                    NSNotificationCenter.DefaultCenter.AddObserver(
                        NSWindow.WillCloseNotification,
                        notification => {
                            if (notification.Object == _window)
                            {
                                // Notify component
                                NSApplication.SharedApplication.InvokeOnMainThread(() => {
                                    _isInitialized = false;
                                });
                            }
                        });
                    
                    Console.WriteLine("Loading HTML file...");
                    // Load the HTML file
                    NSUrl url = new NSUrl(_htmlPath, false);
                    _webView.LoadFileUrl(url, url);
                    
                    Console.WriteLine("WebView initialization successful!");
                    _isInitialized = true;
                    _initCompletedEvent.Set();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR initializing WebView: {ex.Message}");
                    Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                    _initCompletedEvent.Set();
                }
            });
            
            Console.WriteLine("Waiting for initialization to complete...");
            // Wait for initialization to complete
            _initCompletedEvent.WaitOne(5000);
            Console.WriteLine($"Initialization completed, successful: {_isInitialized}");
#endif
        }
        
        /// <summary>
        /// Show the editor window
        /// </summary>
        public void Show()
        {
#if WINDOWS
            // Windows implementation would go here
#else
            try {
                // Add debug output
                Console.WriteLine("WebViewEditor.Show() called");
                
                if (!_isInitialized)
                {
                    Console.WriteLine("WebView not initialized, initializing now...");
                    Initialize();
                }
                
                // Don't try to show the window in Rhino - it can crash
                // Instead, just provide the URL for the user to open
                string htmlPath = Path.Combine(_tempDirectory, "index.html");
                if (File.Exists(htmlPath))
                {
                    string url = $"file://{htmlPath}";
                    Console.WriteLine($"Open this URL in your browser: {url}");
                    
                    // Use OS-specific approach to open browser
                    try
                    {
                        System.Diagnostics.Process browserProcess = new System.Diagnostics.Process();
                        // On macOS, use 'open' command which will use default browser
                        browserProcess.StartInfo.FileName = "open";
                        browserProcess.StartInfo.Arguments = $"\"{url}\"";
                        browserProcess.StartInfo.UseShellExecute = false;
                        browserProcess.Start();
                        
                        Console.WriteLine("Browser opened with segmentation interface");
                    }
                    catch (Exception browserEx)
                    {
                        Console.WriteLine($"Could not open browser automatically: {browserEx.Message}. Use the Web Interface output URL instead.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in WebViewEditor.Show(): {ex.Message}");
            }
#endif
        }
        
        /// <summary>
        /// Hide the editor window
        /// </summary>
        public void Hide()
        {
#if WINDOWS
            // Windows implementation would go here
#else
            NSApplication.SharedApplication.InvokeOnMainThread(() => {
                _window.OrderOut(null);
            });
#endif
        }
        
        /// <summary>
        /// Close and dispose the editor
        /// </summary>
        public void Close()
        {
            Dispose();
        }
        
        /// <summary>
        /// Set the video path for the editor
        /// </summary>
        public void SetVideoPath(string videoPath)
        {
            _videoPath = videoPath;
            // Update web view with new video path
            UpdateWebView();
        }
        
        /// <summary>
        /// Update the current frame image
        /// </summary>
        public void UpdateImage(OpenCvSharp.Mat image)
        {
            if (image == null || image.Empty())
                return;
                
            try
            {
                // Convert Mat to PNG byte array
                using (var ms = new MemoryStream())
                {
                    OpenCvSharp.Cv2.ImEncode(".png", image, out byte[] buffer);
                    ms.Write(buffer, 0, buffer.Length);
                    
                    // Convert to base64 for embedding in the HTML
                    _currentImageData = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    
                    // Update the image in the web view
                    UpdateWebView();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error updating image: " + ex.Message);
            }
        }
        
        /// <summary>
        /// Update the mask overlay
        /// </summary>
        public void UpdateMask(OpenCvSharp.Mat mask)
        {
            if (mask == null || mask.Empty())
                return;
                
            try
            {
                // Convert Mat to PNG byte array
                using (var ms = new MemoryStream())
                {
                    OpenCvSharp.Cv2.ImEncode(".png", mask, out byte[] buffer);
                    ms.Write(buffer, 0, buffer.Length);
                    
                    // Convert to base64 for embedding in the HTML
                    _currentMaskData = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    
                    // Update the mask in the web view
                    UpdateWebView();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error updating mask: " + ex.Message);
            }
        }
        
        /// <summary>
        /// Set current frame information
        /// </summary>
        public void SetFrameInfo(int currentFrame, int totalFrames)
        {
            _currentFrame = currentFrame;
            _totalFrames = totalFrames;
            
            // Update the web view
            UpdateWebView();
        }
        
        /// <summary>
        /// Update the web view with current state
        /// </summary>
        private void UpdateWebView()
        {
#if WINDOWS
            // Windows implementation would go here
#else
            if (!_isInitialized)
                return;
                
            NSApplication.SharedApplication.InvokeOnMainThread(() => {
                Console.WriteLine($"Updating web view: Frame {_currentFrame}/{_totalFrames}, Image data length: {_currentImageData?.Length ?? 0}");
                
                string script = $@"
                    updateEditor({{
                        currentFrame: {_currentFrame},
                        totalFrames: {_totalFrames},
                        imageData: '{_currentImageData}',
                        maskData: '{_currentMaskData}',
                        videoPath: '{_videoPath}'
                    }});
                ";
                
                _webView.EvaluateJavaScript(new NSString(script), (NSObject result, NSError error) => {
                    if (error != null)
                    {
                        Console.WriteLine("Error updating web view: " + error.LocalizedDescription);
                    }
                    else
                    {
                        Console.WriteLine("Web view updated successfully");
                    }
                });
            });
#endif
        }
        
        /// <summary>
        /// Handle point added from web view
        /// </summary>
        internal void HandlePointAdded(float x, float y, bool isAdditive)
        {
            PointAdded?.Invoke(this, new PointEventArgs(x, y, isAdditive));
        }
        
        /// <summary>
        /// Handle box added from web view
        /// </summary>
        internal void HandleBoxAdded(float x1, float y1, float x2, float y2)
        {
            BoxAdded?.Invoke(this, new BoxEventArgs(x1, y1, x2, y2));
        }
        
        /// <summary>
        /// Handle clear requested from web view
        /// </summary>
        internal void HandleClearRequested()
        {
            ClearRequested?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Handle frame navigation requested from web view
        /// </summary>
        internal void HandleFrameNavigationRequested(int offset)
        {
            FrameNavigationRequested?.Invoke(this, new NavigationEventArgs(offset));
        }
        
        /// <summary>
        /// Handle process all frames requested from web view
        /// </summary>
        internal void HandleProcessAllRequested()
        {
            ProcessAllRequested?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Create the web files (HTML, JS, CSS)
        /// </summary>
        private void CreateWebFiles()
        {
            _htmlPath = Path.Combine(_tempDirectory, "index.html");
            _jsPath = Path.Combine(_tempDirectory, "script.js");
            _cssPath = Path.Combine(_tempDirectory, "style.css");
            
            // Create HTML file
            File.WriteAllText(_htmlPath, GetHtmlContent());
            
            // Create JS file
            File.WriteAllText(_jsPath, GetJavaScriptContent());
            
            // Create CSS file
            File.WriteAllText(_cssPath, GetCssContent());
            
            // Output the path for users to directly open in browser if necessary
            Console.WriteLine($"HTML interface created at: file://{_htmlPath}");
        }
        
        /// <summary>
        /// Get the HTML content for the editor
        /// </summary>
        private string GetHtmlContent()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>SAM Video Editor</title>
    <link rel=""stylesheet"" href=""style.css"">
</head>
<body>
    <div class=""editor-container"">
        <div class=""header"">
            <h1>SAM Video Segmentation Interface</h1>
            <div id=""videoPathInfo"" class=""video-path"">Loading video information...</div>
        </div>
        <div class=""toolbar"">
            <div class=""tool-group"">
                <button id=""addPointBtn"" class=""tool-btn active"">Add Point (+)</button>
                <button id=""removePointBtn"" class=""tool-btn"">Remove Point (-)</button>
                <button id=""addBoxBtn"" class=""tool-btn"">Add Box</button>
                <button id=""clearBtn"" class=""tool-btn"">Clear Frame</button>
            </div>
            <div class=""frame-navigation"">
                <button id=""prevFrameBtn"" class=""nav-btn"">◀ Previous</button>
                <span id=""frameCounter"">Frame: 0 / 0</span>
                <button id=""nextFrameBtn"" class=""nav-btn"">Next ▶</button>
            </div>
        </div>
        
        <div class=""canvas-container"">
            <canvas id=""imageCanvas"" width=""800"" height=""600""></canvas>
            <div id=""loading"" class=""loading hidden"">Loading...</div>
        </div>
        
        <div class=""status-bar"">
            <div id=""instructions"">Click to add points or draw boxes for segmentation</div>
            <div>
                <button id=""processButton"" class=""process-btn"">Process All Frames</button>
                <span id=""processingStatus"" class=""hidden"">Processing...</span>
            </div>
        </div>
    </div>
    
    <script src=""script.js""></script>
</body>
</html>";
        }
        
        /// <summary>
        /// Get the JavaScript content for the editor
        /// </summary>
        private string GetJavaScriptContent()
        {
            return @"// Canvas setup
const canvas = document.getElementById('imageCanvas');
const ctx = canvas.getContext('2d');
const toolButtons = document.querySelectorAll('.tool-btn');
const loading = document.getElementById('loading');

// Editor state
let currentTool = 'addPoint';
let currentFrame = 0;
let totalFrames = 0;
let isDrawingBox = false;
let boxStart = { x: 0, y: 0 };
let boxEnd = { x: 0, y: 0 };
let originalImage = new Image();
let maskImage = new Image();
let canvasWidth = 800;
let canvasHeight = 600;

// Initialize
function init() {
    // Set up button event listeners
    document.getElementById('addPointBtn').addEventListener('click', () => setTool('addPoint'));
    document.getElementById('removePointBtn').addEventListener('click', () => setTool('removePoint'));
    document.getElementById('addBoxBtn').addEventListener('click', () => setTool('addBox'));
    document.getElementById('clearBtn').addEventListener('click', clearFrame);
    document.getElementById('prevFrameBtn').addEventListener('click', () => navigateFrame(-1));
    document.getElementById('nextFrameBtn').addEventListener('click', () => navigateFrame(1));
    document.getElementById('processButton').addEventListener('click', processAllFrames);
    
    // Set up canvas event listeners
    canvas.addEventListener('mousedown', handleMouseDown);
    canvas.addEventListener('mousemove', handleMouseMove);
    canvas.addEventListener('mouseup', handleMouseUp);
    
    // Set up keyboard shortcuts
    document.addEventListener('keydown', handleKeyDown);
    
    // Disable context menu on canvas
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());
    
    // Initial draw
    drawCanvas();
}

// Set the active tool
function setTool(tool) {
    currentTool = tool;
    
    // Update active button
    toolButtons.forEach(btn => {
        btn.classList.remove('active');
        if (btn.id === tool + 'Btn') {
            btn.classList.add('active');
        }
    });
    
    // Update cursor based on tool
    if (tool === 'addBox') {
        canvas.style.cursor = 'crosshair';
    } else {
        canvas.style.cursor = 'pointer';
    }
    
    // Update instructions
    updateInstructions();
}

// Handle mouse down event
function handleMouseDown(e) {
    const rect = canvas.getBoundingClientRect();
    const x = (e.clientX - rect.left) / canvas.clientWidth;
    const y = (e.clientY - rect.top) / canvas.clientHeight;
    
    if (currentTool === 'addBox') {
        isDrawingBox = true;
        boxStart = { x, y };
        boxEnd = { x, y };
    } else {
        // Add point
        addPoint(x, y, currentTool === 'addPoint');
    }
}

// Handle mouse move event
function handleMouseMove(e) {
    if (!isDrawingBox) return;
    
    const rect = canvas.getBoundingClientRect();
    const x = (e.clientX - rect.left) / canvas.clientWidth;
    const y = (e.clientY - rect.top) / canvas.clientHeight;
    
    boxEnd = { x, y };
    drawCanvas();
}

// Handle mouse up event
function handleMouseUp(e) {
    if (!isDrawingBox) return;
    
    const rect = canvas.getBoundingClientRect();
    const x = (e.clientX - rect.left) / canvas.clientWidth;
    const y = (e.clientY - rect.top) / canvas.clientHeight;
    
    boxEnd = { x, y };
    isDrawingBox = false;
    
    // Add box if it has a meaningful size
    const width = Math.abs(boxEnd.x - boxStart.x);
    const height = Math.abs(boxEnd.y - boxStart.y);
    
    if (width > 0.01 && height > 0.01) {
        addBox(
            Math.min(boxStart.x, boxEnd.x),
            Math.min(boxStart.y, boxEnd.y),
            Math.max(boxStart.x, boxEnd.x),
            Math.max(boxStart.y, boxEnd.y)
        );
    }
    
    drawCanvas();
}

// Handle keyboard shortcuts
function handleKeyDown(e) {
    switch (e.key) {
        case '1':
            setTool('addPoint');
            break;
        case '2':
            setTool('removePoint');
            break;
        case '3':
            setTool('addBox');
            break;
        case 'ArrowLeft':
            navigateFrame(-1);
            break;
        case 'ArrowRight':
            navigateFrame(1);
            break;
        case 'Delete':
        case 'Backspace':
            clearFrame();
            break;
    }
}

// Add a point
function addPoint(x, y, isAdditive) {
    // Ensure coordinates are in 0-1 range
    x = Math.max(0, Math.min(1, x));
    y = Math.max(0, Math.min(1, y));
    
    // Send to native code
    try {
        window.webkit.messageHandlers.addPoint.postMessage({
            x: x,
            y: y,
            isAdditive: isAdditive
        });
        
        // Draw a temporary point indicator
        const color = isAdditive ? '#00ff00' : '#ff0000';
        drawPoint(x, y, color);
    } catch (error) {
        console.error('Failed to send addPoint message:', error);
    }
}

// Add a box
function addBox(x1, y1, x2, y2) {
    // Ensure coordinates are in 0-1 range
    x1 = Math.max(0, Math.min(1, x1));
    y1 = Math.max(0, Math.min(1, y1));
    x2 = Math.max(0, Math.min(1, x2));
    y2 = Math.max(0, Math.min(1, y2));
    
    // Send to native code
    try {
        window.webkit.messageHandlers.addBox.postMessage({
            x1: x1,
            y1: y1,
            x2: x2,
            y2: y2
        });
        
        // Draw a temporary box indicator
        drawBox(x1, y1, x2, y2, '#00ff00');
    } catch (error) {
        console.error('Failed to send addBox message:', error);
    }
}

// Clear the current frame
function clearFrame() {
    try {
        window.webkit.messageHandlers.clearFrame.postMessage({});
    } catch (error) {
        console.error('Failed to send clearFrame message:', error);
    }
}

// Navigate to another frame
function navigateFrame(offset) {
    try {
        window.webkit.messageHandlers.navigateFrame.postMessage({
            offset: offset
        });
    } catch (error) {
        console.error('Failed to send navigateFrame message:', error);
    }
}

// Process all frames with current promotions
function processAllFrames() {
    try {
        // Show processing status
        document.getElementById('processingStatus').classList.remove('hidden');
        document.getElementById('processButton').disabled = true;
        
        // Send message to native code
        window.webkit.messageHandlers.processAll.postMessage({});
    } catch (error) {
        console.error('Failed to send processAll message:', error);
        // Hide processing status in case of error
        document.getElementById('processingStatus').classList.add('hidden');
        document.getElementById('processButton').disabled = false;
    }
}

// Draw a point on the canvas
function drawPoint(x, y, color) {
    const radius = 5;
    const canvasX = x * canvas.width;
    const canvasY = y * canvas.height;
    
    ctx.beginPath();
    ctx.arc(canvasX, canvasY, radius, 0, Math.PI * 2);
    ctx.fillStyle = color;
    ctx.fill();
    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = 1;
    ctx.stroke();
}

// Draw a box on the canvas
function drawBox(x1, y1, x2, y2, color) {
    const canvasX1 = x1 * canvas.width;
    const canvasY1 = y1 * canvas.height;
    const canvasX2 = x2 * canvas.width;
    const canvasY2 = y2 * canvas.height;
    const width = canvasX2 - canvasX1;
    const height = canvasY2 - canvasY1;
    
    ctx.strokeStyle = color;
    ctx.lineWidth = 2;
    ctx.strokeRect(canvasX1, canvasY1, width, height);
}

// Draw the canvas with current state
function drawCanvas() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    
    // Draw original image if available
    if (originalImage.src) {
        ctx.drawImage(originalImage, 0, 0, canvas.width, canvas.height);
    }
    
    // Draw mask overlay if available
    if (maskImage.src) {
        ctx.globalAlpha = 0.5;
        ctx.drawImage(maskImage, 0, 0, canvas.width, canvas.height);
        ctx.globalAlpha = 1.0;
    }
    
    // Draw box preview while drawing
    if (isDrawingBox) {
        const width = (boxEnd.x - boxStart.x) * canvas.width;
        const height = (boxEnd.y - boxStart.y) * canvas.height;
        
        ctx.strokeStyle = '#00ff00';
        ctx.lineWidth = 2;
        ctx.strokeRect(
            boxStart.x * canvas.width,
            boxStart.y * canvas.height,
            width,
            height
        );
    }
}

// Update the editor with new data
function updateEditor(data) {
    console.log('Updating editor with data:', Object.keys(data).join(', '));
    
    // Update frame info
    currentFrame = data.currentFrame;
    totalFrames = data.totalFrames;
    document.getElementById('frameCounter').textContent = `Frame: ${currentFrame} / ${totalFrames > 0 ? totalFrames - 1 : 0}`;
    
    // Update previous/next buttons state
    document.getElementById('prevFrameBtn').disabled = currentFrame <= 0;
    document.getElementById('nextFrameBtn').disabled = currentFrame >= totalFrames - 1;
    
    // Update video path info if provided
    if (data.videoPath) {
        const pathInfo = document.getElementById('videoPathInfo');
        if (pathInfo) {
            pathInfo.textContent = `Video: ${data.videoPath}`;
            pathInfo.classList.remove('hidden');
        }
    }
    
    // Load images
    if (data.imageData) {
        console.log('Loading image data of length: ' + data.imageData.length);
        originalImage.onload = function() {
            // When image is loaded, draw canvas
            console.log('Image loaded successfully');
            drawCanvas();
            loading.classList.add('hidden');
        };
        originalImage.onerror = function(e) {
            console.error('Error loading image:', e);
            loading.classList.add('hidden');
        };
        originalImage.src = data.imageData;
        loading.classList.remove('hidden');
    } else {
        console.log('No image data provided');
    }
    
    if (data.maskData) {
        console.log('Loading mask data');
        maskImage.onload = function() {
            // When mask is loaded, redraw canvas
            drawCanvas();
        };
        maskImage.src = data.maskData;
    } else {
        // Clear mask if no data
        maskImage.src = '';
    }
    
    // Update instructions
    updateInstructions();
}

// Update instructions based on current tool
function updateInstructions() {
    const instructions = document.getElementById('instructions');
    switch (currentTool) {
        case 'addPoint':
            instructions.textContent = 'Click to add positive points (include in selection)';
            break;
        case 'removePoint':
            instructions.textContent = 'Click to add negative points (exclude from selection)';
            break;
        case 'addBox':
            instructions.textContent = 'Click and drag to draw a box around the object';
            break;
        default:
            instructions.textContent = 'Click to add points or draw boxes for segmentation';
    }
}

// Initialize when DOM is loaded
document.addEventListener('DOMContentLoaded', init);";
        }
        
        /// <summary>
        /// Get the CSS content for the editor
        /// </summary>
        private string GetCssContent()
        {
            return @"* {
    margin: 0;
    padding: 0;
    box-sizing: border-box;
    font-family: system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, 'Open Sans', 'Helvetica Neue', sans-serif;
}

body {
    background-color: #f0f0f0;
    overflow: hidden;
}

.editor-container {
    display: flex;
    flex-direction: column;
    height: 100vh;
    width: 100vw;
    background-color: #2c2c2c;
    color: #f0f0f0;
}

.header {
    background-color: #1e1e1e;
    padding: 10px 15px;
    display: flex;
    justify-content: space-between;
    align-items: center;
    border-bottom: 1px solid #444;
}

.header h1 {
    font-size: 18px;
    font-weight: 500;
}

.toolbar {
    display: flex;
    justify-content: space-between;
    padding: 10px;
    background-color: #333;
    border-bottom: 1px solid #444;
}

.tool-group {
    display: flex;
    gap: 8px;
}

.tool-btn, .nav-btn, .process-btn {
    padding: 8px 12px;
    border: none;
    border-radius: 4px;
    background-color: #555;
    color: #fff;
    cursor: pointer;
    transition: background-color 0.2s;
}

.tool-btn:hover, .nav-btn:hover, .process-btn:hover {
    background-color: #666;
}

.tool-btn.active {
    background-color: #007bff;
}

.process-btn {
    background-color: #28a745;
}

.process-btn:hover {
    background-color: #218838;
}

.nav-btn:disabled {
    background-color: #444;
    color: #888;
    cursor: not-allowed;
}

.frame-navigation {
    display: flex;
    align-items: center;
    gap: 10px;
}

#frameCounter {
    min-width: 100px;
    text-align: center;
}

.canvas-container {
    flex: 1;
    position: relative;
    background-color: #222;
    display: flex;
    justify-content: center;
    align-items: center;
    overflow: hidden;
}

canvas {
    max-width: 100%;
    max-height: 100%;
    object-fit: contain;
    cursor: pointer;
}

.status-bar {
    padding: 8px 16px;
    background-color: #333;
    border-top: 1px solid #444;
    display: flex;
    justify-content: space-between;
    align-items: center;
}

.video-path {
    color: #aaffaa;
    font-size: 14px;
}

#processingStatus {
    color: #ffaa00;
    margin-left: 10px;
}

.loading {
    position: absolute;
    top: 50%;
    left: 50%;
    transform: translate(-50%, -50%);
    background-color: rgba(0, 0, 0, 0.7);
    color: white;
    padding: 10px 20px;
    border-radius: 4px;
    font-size: 14px;
}

.hidden {
    display: none;
}";
        }
        
        /// <summary>
        /// Dispose the editor
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;
                
            _isDisposed = true;
            
#if WINDOWS
            // Windows implementation would go here
#else
            NSApplication.SharedApplication.InvokeOnMainThread(() => {
                if (_window != null)
                {
                    _window.Close();
                    _window = null;
                }
                
                if (_webView != null)
                {
                    _webView.Dispose();
                    _webView = null;
                }
                
                if (_viewController != null)
                {
                    _viewController.Dispose();
                    _viewController = null;
                }
                
                if (_windowController != null)
                {
                    _windowController.Dispose();
                    _windowController = null;
                }
            });
#endif
            
            // Clean up temporary files
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch
            {
                // Ignore errors when cleaning up temp files
            }
        }
    }
    
#if !WINDOWS
    /// <summary>
    /// WebKit message handler for communication from JavaScript to native code
    /// </summary>
    class ScriptMessageHandler : NSObject, IWKScriptMessageHandler
    {
        private readonly WebViewEditor _editor;
        
        public ScriptMessageHandler(WebViewEditor editor)
        {
            _editor = editor;
        }
        
        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            try
            {
                // Handle message based on name
                switch (message.Name)
                {
                    case "addPoint":
                        HandleAddPoint(message.Body as NSDictionary);
                        break;
                    case "addBox":
                        HandleAddBox(message.Body as NSDictionary);
                        break;
                    case "clearFrame":
                        _editor.HandleClearRequested();
                        break;
                    case "navigateFrame":
                        HandleNavigateFrame(message.Body as NSDictionary);
                        break;
                    case "processAll":
                        _editor.HandleProcessAllRequested();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error handling script message: " + ex.Message);
            }
        }
        
        private void HandleAddPoint(NSDictionary data)
        {
            if (data == null)
                return;
                
            float x = Convert.ToSingle(data["x"]);
            float y = Convert.ToSingle(data["y"]);
            bool isAdditive = Convert.ToBoolean(data["isAdditive"]);
            
            _editor.HandlePointAdded(x, y, isAdditive);
        }
        
        private void HandleAddBox(NSDictionary data)
        {
            if (data == null)
                return;
                
            float x1 = Convert.ToSingle(data["x1"]);
            float y1 = Convert.ToSingle(data["y1"]);
            float x2 = Convert.ToSingle(data["x2"]);
            float y2 = Convert.ToSingle(data["y2"]);
            
            _editor.HandleBoxAdded(x1, y1, x2, y2);
        }
        
        private void HandleNavigateFrame(NSDictionary data)
        {
            if (data == null)
                return;
                
            int offset = Convert.ToInt32(data["offset"]);
            
            _editor.HandleFrameNavigationRequested(offset);
        }
    }
    
    /// <summary>
    /// WebKit navigation delegate
    /// </summary>
    class WebViewDelegate : NSObject, IWKNavigationDelegate
    {
        private readonly WebViewEditor _editor;
        
        public WebViewDelegate(WebViewEditor editor)
        {
            _editor = editor;
        }
        
        // Use custom Export attribute for macOS
        public void DidFinishNavigation(WKWebView webView, WKNavigation navigation)
        {
            // Navigation completed - the page is loaded
            Console.WriteLine("WebView navigation completed");
        }
    }
#endif
}