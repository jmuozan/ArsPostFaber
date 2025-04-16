using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace SAMViewer
{
    public class WebInterface
    {
        private string _tempDirectory;
        private string _htmlPath;
        private string _jsPath;
        private string _cssPath;
        private Process _browserProcess;

        // Current state
        private int _currentFrame = 0;
        private int _totalFrames = 0;
        private string _currentImageData = string.Empty;
        private string _currentMaskData = string.Empty;
        private string _videoPath = string.Empty;
        private string _modelPath = string.Empty;
        private bool _modelLoaded = false;

        // Event handlers for communication with VideoSAM
        public event EventHandler<PointEventArgs> PointAdded;
        public event EventHandler<BoxEventArgs> BoxAdded;
        public event EventHandler ClearRequested;
        public event EventHandler<NavigationEventArgs> FrameNavigationRequested;
        public event EventHandler ProcessAllRequested;
        public event EventHandler<ModelEventArgs> ModelPathSelected;

        public WebInterface()
        {
            // Create temporary directory for web files
            _tempDirectory = Path.Combine(Path.GetTempPath(), "SAMVideoEditor_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDirectory);
            
            // Create HTML, JS, and CSS files
            CreateWebFiles();
            
            // Set up a simple HTTP server to handle AJAX requests
            StartHttpServer();
        }

        private void StartHttpServer()
        {
            // Implement a lightweight HTTP server for handling AJAX requests from the web interface
            // This could use HttpListener in C# or a simple Python server subprocess
            try
            {
                // Create a Python HTTP server in the temp directory
                string pythonScript = Path.Combine(_tempDirectory, "server.py");
                
                // Create the Python script
                File.WriteAllText(pythonScript, @"
import http.server
import socketserver
import json
import os
import sys
import base64
from urllib.parse import parse_qs, urlparse

# Set the port
PORT = 9000

# Store the current state
state = {
    'currentFrame': 0,
    'totalFrames': 0,
    'videoPath': '',
    'modelPath': '',
    'modelLoaded': False,
    'events': []
}

# Custom request handler
class SAMRequestHandler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        # Parse the URL
        parsed_url = urlparse(self.path)
        
        # Serve static files
        if parsed_url.path == '/':
            self.path = '/index.html'
            return http.server.SimpleHTTPRequestHandler.do_GET(self)
        
        # API endpoint to get state
        elif parsed_url.path == '/api/state':
            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            
            # Send the current state
            self.wfile.write(json.dumps(state).encode())
            return
        
        # API endpoint to poll for events
        elif parsed_url.path == '/api/events':
            self.send_response(200)
            self.send_header('Content-type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            
            # Get events and clear them
            events = state['events'].copy()
            state['events'] = []
            
            self.wfile.write(json.dumps(events).encode())
            return
        
        # Serve static files
        return http.server.SimpleHTTPRequestHandler.do_GET(self)
    
    def do_POST(self):
        # Parse the URL
        parsed_url = urlparse(self.path)
        
        # API endpoint to update state
        if parsed_url.path == '/api/state':
            content_length = int(self.headers['Content-Length'])
            post_data = self.rfile.read(content_length)
            
            try:
                # Update state with new values
                new_state = json.loads(post_data.decode('utf-8'))
                for key, value in new_state.items():
                    if key != 'events':  # Don't overwrite events
                        state[key] = value
                
                self.send_response(200)
                self.send_header('Content-type', 'application/json')
                self.send_header('Access-Control-Allow-Origin', '*')
                self.end_headers()
                self.wfile.write(json.dumps({'success': True}).encode())
            except Exception as e:
                self.send_response(400)
                self.send_header('Content-type', 'application/json')
                self.send_header('Access-Control-Allow-Origin', '*')
                self.end_headers()
                self.wfile.write(json.dumps({'error': str(e)}).encode())
            
            return
        
        # API endpoint to add event
        elif parsed_url.path == '/api/event':
            content_length = int(self.headers['Content-Length'])
            post_data = self.rfile.read(content_length)
            
            try:
                # Add event to the queue
                event = json.loads(post_data.decode('utf-8'))
                state['events'].append(event)
                
                # Write to event file for C# to process
                event_type = event.get('type', '')
                event_payload = event.get('payload', {})
                
                # Write to event file
                with open('event.txt', 'a') as f:
                    f.write(json.dumps({'type': event_type, 'payload': event_payload}) + '\n')
                
                self.send_response(200)
                self.send_header('Content-type', 'application/json')
                self.send_header('Access-Control-Allow-Origin', '*')
                self.end_headers()
                self.wfile.write(json.dumps({'success': True}).encode())
            except Exception as e:
                self.send_response(400)
                self.send_header('Content-type', 'application/json')
                self.send_header('Access-Control-Allow-Origin', '*')
                self.end_headers()
                self.wfile.write(json.dumps({'error': str(e)}).encode())
            
            return
        
        # Default response for unsupported paths
        self.send_response(404)
        self.send_header('Content-type', 'application/json')
        self.send_header('Access-Control-Allow-Origin', '*')
        self.end_headers()
        self.wfile.write(json.dumps({'error': 'Not found'}).encode())

# Run the server
os.chdir(os.path.dirname(os.path.abspath(__file__)))
with socketserver.TCPServer(('', PORT), SAMRequestHandler) as httpd:
    print(f'Server running at http://localhost:{PORT}')
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass
    httpd.server_close()
    print('Server stopped')
");
                
                // Start the Python HTTP server
                Console.WriteLine("Starting HTTP server with Python...");
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "python3";
                psi.Arguments = pythonScript;
                psi.WorkingDirectory = _tempDirectory;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                
                Process serverProcess = new Process();
                serverProcess.StartInfo = psi;
                serverProcess.OutputDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"HTTP Server: {e.Data}");
                    }
                };
                serverProcess.ErrorDataReceived += (sender, e) => {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"HTTP Server Error: {e.Data}");
                    }
                };
                
                try
                {
                    serverProcess.Start();
                    Console.WriteLine("HTTP server process started");
                    serverProcess.BeginOutputReadLine();
                    serverProcess.BeginErrorReadLine();
                    
                    // Wait a moment to ensure server has started
                    System.Threading.Thread.Sleep(1000);
                    
                    Console.WriteLine($"Server started successfully at: http://localhost:9000");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to start HTTP server: {ex.Message}");
                    Console.WriteLine("Please ensure Python 3 is installed and in your PATH");
                    throw;
                }
                
                // Start a background thread to monitor event.txt for events
                string eventFilePath = Path.Combine(_tempDirectory, "event.txt");
                
                // Create empty event file
                File.WriteAllText(eventFilePath, "");
                
                // Start a task to monitor the event file
                System.Threading.Tasks.Task.Run(() => {
                    using (FileSystemWatcher watcher = new FileSystemWatcher(_tempDirectory, "event.txt"))
                    {
                        watcher.NotifyFilter = NotifyFilters.LastWrite;
                        watcher.Changed += (s, e) => {
                            try
                            {
                                // Wait a moment for the file to be fully written
                                System.Threading.Thread.Sleep(100);
                                
                                // Read and process events
                                string[] lines = File.ReadAllLines(eventFilePath);
                                
                                // Clear the file
                                File.WriteAllText(eventFilePath, "");
                                
                                foreach (string line in lines)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;
                                    
                                    try
                                    {
                                        // Parse the event
                                        dynamic eventData = Newtonsoft.Json.JsonConvert.DeserializeObject(line);
                                        string eventType = eventData.type;
                                        dynamic payload = eventData.payload;
                                        
                                        // Handle different event types
                                        switch (eventType)
                                        {
                                            case "addPoint":
                                                float x = (float)payload.x;
                                                float y = (float)payload.y;
                                                bool isAdditive = (bool)payload.isAdditive;
                                                PointAdded?.Invoke(this, new PointEventArgs(x, y, isAdditive));
                                                break;
                                                
                                            case "addBox":
                                                float x1 = (float)payload.x1;
                                                float y1 = (float)payload.y1;
                                                float x2 = (float)payload.x2;
                                                float y2 = (float)payload.y2;
                                                BoxAdded?.Invoke(this, new BoxEventArgs(x1, y1, x2, y2));
                                                break;
                                                
                                            case "clearFrame":
                                                ClearRequested?.Invoke(this, EventArgs.Empty);
                                                break;
                                                
                                            case "navigateFrame":
                                                int offset = (int)payload.offset;
                                                FrameNavigationRequested?.Invoke(this, new NavigationEventArgs(offset));
                                                break;
                                                
                                            case "processAll":
                                                ProcessAllRequested?.Invoke(this, EventArgs.Empty);
                                                break;
                                                
                                            case "selectModel":
                                                string modelPath = (string)payload.modelPath;
                                                ModelPathSelected?.Invoke(this, new ModelEventArgs(modelPath));
                                                break;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error processing event: {ex.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error in file watcher: {ex.Message}");
                            }
                        };
                        
                        watcher.EnableRaisingEvents = true;
                        
                        // Keep the watcher alive
                        new System.Threading.ManualResetEvent(false).WaitOne();
                    }
                });
                
                Console.WriteLine("HTTP server started for SAM UI");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start HTTP server: {ex.Message}");
            }
        }

        public void Show()
        {
            try
            {
                // Just open the web interface in the default browser
                string url = $"http://localhost:9000";
                
                Console.WriteLine($"Attempting to open web browser to {url}");
                
                // Attempt to open browser
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // For macOS, use the 'open' command
                    Console.WriteLine("Detected macOS, using 'open' command");
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "open",
                        Arguments = url,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    _browserProcess = Process.Start(psi);
                    
                    // Wait for the process to complete
                    _browserProcess.WaitForExit();
                    string output = _browserProcess.StandardOutput.ReadToEnd();
                    string error = _browserProcess.StandardError.ReadToEnd();
                    
                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine($"Error opening browser: {error}");
                    }
                    
                    if (_browserProcess.ExitCode != 0)
                    {
                        Console.WriteLine($"Failed to open browser: Exit code {_browserProcess.ExitCode}");
                    }
                    else 
                    {
                        Console.WriteLine("Browser opened successfully");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // For Windows
                    Console.WriteLine("Detected Windows, using ProcessStartInfo with UseShellExecute");
                    _browserProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // For Linux, use xdg-open
                    Console.WriteLine("Detected Linux, using 'xdg-open' command");
                    _browserProcess = Process.Start("xdg-open", url);
                }
                else
                {
                    Console.WriteLine("Unknown operating system. Please open the URL manually: " + url);
                }
                
                Console.WriteLine($"Opened browser with URL: {url}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open browser: {ex.Message}");
                Console.WriteLine($"Please open this URL manually: http://localhost:9000");
            }
            
            // Regardless of browser launch success, print instructions
            Console.WriteLine("\n===== SAM VIDEO SEGMENTATION =====");
            Console.WriteLine("The web interface should now be open in your browser.");
            Console.WriteLine("If not, please open this URL manually: http://localhost:9000");
            Console.WriteLine("Press Enter to exit when you're done with the segmentation.");
            Console.WriteLine("=================================\n");
        }

        public void SetFrameInfo(int currentFrame, int totalFrames)
        {
            _currentFrame = currentFrame;
            _totalFrames = totalFrames;
            
            UpdateState();
        }

        public void UpdateImage(Mat image)
        {
            if (image == null || image.Empty())
                return;
                
            try
            {
                // Convert Mat to PNG byte array
                using (var ms = new MemoryStream())
                {
                    Cv2.ImEncode(".png", image, out byte[] buffer);
                    ms.Write(buffer, 0, buffer.Length);
                    
                    // Convert to base64 for embedding in the HTML
                    _currentImageData = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    
                    // Update the state
                    UpdateState();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating image: {ex.Message}");
            }
        }

        public void UpdateMask(Mat mask)
        {
            if (mask == null || mask.Empty())
                return;
                
            try
            {
                // Convert Mat to PNG byte array
                using (var ms = new MemoryStream())
                {
                    Cv2.ImEncode(".png", mask, out byte[] buffer);
                    ms.Write(buffer, 0, buffer.Length);
                    
                    // Convert to base64 for embedding in the HTML
                    _currentMaskData = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    
                    // Update the state
                    UpdateState();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating mask: {ex.Message}");
            }
        }

        public void SetVideoPath(string videoPath)
        {
            _videoPath = videoPath;
            
            UpdateState();
        }
        
        public void SetModelPath(string modelPath, bool isLoaded)
        {
            _modelPath = modelPath;
            _modelLoaded = isLoaded;
            
            UpdateState();
        }

        private void UpdateState()
        {
            try
            {
                // Create JSON state
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    currentFrame = _currentFrame,
                    totalFrames = _totalFrames,
                    imageData = _currentImageData,
                    maskData = _currentMaskData,
                    videoPath = _videoPath,
                    modelPath = _modelPath,
                    modelLoaded = _modelLoaded
                });
                
                // Write to state file for the HTTP server
                File.WriteAllText(Path.Combine(_tempDirectory, "state.json"), json);
                
                // Make HTTP request to update state
                using (System.Net.WebClient client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    string response = client.UploadString("http://localhost:9000/api/state", "POST", json);
                    Console.WriteLine($"Updated state: {response}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating state: {ex.Message}");
            }
        }

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
            
            // Create empty state.json file
            File.WriteAllText(Path.Combine(_tempDirectory, "state.json"), "{}");
            
            // Output the path for users
            Console.WriteLine($"SAM UI created at: http://localhost:9000");
        }

        private string GetHtmlContent()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>SAM Video Segmentation</title>
    <link rel=""stylesheet"" href=""style.css"">
</head>
<body>
    <div class=""editor-container"">
        <div class=""header"">
            <h1>SAM Video Segmentation Interface</h1>
            <div id=""videoPathInfo"" class=""video-path"">Loading video information...</div>
        </div>
        
        <div class=""model-selection"">
            <div class=""model-input"">
                <input type=""text"" id=""modelPathInput"" placeholder=""Enter path to model file (.pth) or ONNX models directory"">
                <button id=""selectModelBtn"" class=""select-btn"">Select Model</button>
            </div>
            <div id=""modelStatus"" class=""model-status"">No model loaded</div>
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
let lastEventPollTime = 0;
let modelPath = '';
let modelLoaded = false;

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
    document.getElementById('selectModelBtn').addEventListener('click', selectModel);
    
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
    
    // Start polling for state updates
    pollState();
}

// Poll for state updates
function pollState() {
    fetch('/api/state')
        .then(response => response.json())
        .then(data => {
            updateEditor(data);
            setTimeout(pollState, 500);  // Poll every 500ms
        })
        .catch(error => {
            console.error('Error polling state:', error);
            setTimeout(pollState, 2000);  // Try again in 2 seconds on error
        });
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
    if (!modelLoaded) {
        alert('Please select a model first');
        return;
    }
    
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

// Select model path
function selectModel() {
    const modelPathInput = document.getElementById('modelPathInput');
    const modelPath = modelPathInput.value.trim();
    
    if (!modelPath) {
        alert('Please enter a model path');
        return;
    }
    
    // Send to backend via API
    sendEvent('selectModel', {
        modelPath: modelPath
    });
    
    // Show loading
    const modelStatus = document.getElementById('modelStatus');
    modelStatus.textContent = 'Loading model...';
    modelStatus.className = 'model-status loading';
}

// Add a point
function addPoint(x, y, isAdditive) {
    // Ensure coordinates are in 0-1 range
    x = Math.max(0, Math.min(1, x));
    y = Math.max(0, Math.min(1, y));
    
    // Send to backend via API
    sendEvent('addPoint', {
        x: x,
        y: y,
        isAdditive: isAdditive
    });
    
    // Draw a temporary point indicator
    const color = isAdditive ? '#00ff00' : '#ff0000';
    drawPoint(x, y, color);
}

// Add a box
function addBox(x1, y1, x2, y2) {
    // Ensure coordinates are in 0-1 range
    x1 = Math.max(0, Math.min(1, x1));
    y1 = Math.max(0, Math.min(1, y1));
    x2 = Math.max(0, Math.min(1, x2));
    y2 = Math.max(0, Math.min(1, y2));
    
    // Send to backend via API
    sendEvent('addBox', {
        x1: x1,
        y1: y1,
        x2: x2,
        y2: y2
    });
    
    // Draw a temporary box indicator
    drawBox(x1, y1, x2, y2, '#00ff00');
}

// Clear the current frame
function clearFrame() {
    sendEvent('clearFrame', {});
}

// Navigate to another frame
function navigateFrame(offset) {
    sendEvent('navigateFrame', {
        offset: offset
    });
}

// Process all frames with current promotions
function processAllFrames() {
    if (!modelLoaded) {
        alert('Please select a model first');
        return;
    }
    
    // Show processing status
    document.getElementById('processingStatus').classList.remove('hidden');
    document.getElementById('processButton').disabled = true;
    
    // Send message to backend
    sendEvent('processAll', {});
}

// Send event to backend
function sendEvent(type, payload) {
    fetch('/api/event', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json'
        },
        body: JSON.stringify({
            type: type,
            payload: payload
        })
    })
    .then(response => response.json())
    .then(data => {
        console.log('Event sent successfully:', data);
    })
    .catch(error => {
        console.error('Error sending event:', error);
    });
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
    
    // Update frame info if changed
    if (data.currentFrame !== undefined && data.totalFrames !== undefined) {
        currentFrame = data.currentFrame;
        totalFrames = data.totalFrames;
        document.getElementById('frameCounter').textContent = `Frame: ${currentFrame} / ${totalFrames > 0 ? totalFrames - 1 : 0}`;
        
        // Update previous/next buttons state
        document.getElementById('prevFrameBtn').disabled = currentFrame <= 0;
        document.getElementById('nextFrameBtn').disabled = currentFrame >= totalFrames - 1;
    }
    
    // Update video path info if provided
    if (data.videoPath) {
        const pathInfo = document.getElementById('videoPathInfo');
        if (pathInfo) {
            pathInfo.textContent = `Video: ${data.videoPath}`;
            pathInfo.classList.remove('hidden');
        }
    }
    
    // Update model status if changed
    if (data.modelPath !== undefined) {
        modelPath = data.modelPath;
        modelLoaded = data.modelLoaded;
        
        const modelPathInput = document.getElementById('modelPathInput');
        const modelStatus = document.getElementById('modelStatus');
        
        modelPathInput.value = modelPath;
        
        if (modelLoaded) {
            modelStatus.textContent = `Model loaded: ${modelPath}`;
            modelStatus.className = 'model-status loaded';
        } else if (modelPath) {
            modelStatus.textContent = `Failed to load model: ${modelPath}`;
            modelStatus.className = 'model-status error';
        } else {
            modelStatus.textContent = 'No model loaded';
            modelStatus.className = 'model-status';
        }
    }
    
    // Load images if changed
    if (data.imageData && originalImage.src !== data.imageData) {
        console.log('Loading image data');
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
    }
    
    if (data.maskData && maskImage.src !== data.maskData) {
        console.log('Loading mask data');
        maskImage.onload = function() {
            // When mask is loaded, redraw canvas
            drawCanvas();
        };
        maskImage.src = data.maskData;
    } else if (!data.maskData && maskImage.src) {
        // Clear mask if no data
        maskImage.src = '';
        drawCanvas();
    }
    
    // Check if processing is complete
    if (!data.isProcessing && document.getElementById('processingStatus').classList.contains('hidden') === false) {
        document.getElementById('processingStatus').classList.add('hidden');
        document.getElementById('processButton').disabled = false;
    }
    
    // Update instructions
    updateInstructions();
}

// Update instructions based on current tool
function updateInstructions() {
    const instructions = document.getElementById('instructions');
    
    if (!modelLoaded) {
        instructions.textContent = 'Please select a model using the input field above';
        return;
    }
    
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

.model-selection {
    background-color: #2a2a2a;
    padding: 10px 15px;
    display: flex;
    flex-direction: column;
    gap: 8px;
    border-bottom: 1px solid #444;
}

.model-input {
    display: flex;
    gap: 10px;
}

#modelPathInput {
    flex: 1;
    padding: 8px 12px;
    border: 1px solid #555;
    border-radius: 4px;
    background-color: #333;
    color: #fff;
    font-size: 14px;
}

.select-btn {
    padding: 8px 12px;
    border: none;
    border-radius: 4px;
    background-color: #0066cc;
    color: white;
    cursor: pointer;
    transition: background-color 0.2s;
}

.select-btn:hover {
    background-color: #0055aa;
}

.model-status {
    font-size: 14px;
    color: #aaa;
}

.model-status.loaded {
    color: #4CAF50;
}

.model-status.loading {
    color: #FFC107;
}

.model-status.error {
    color: #F44336;
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
    }

    // Event args for point events
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

    // Event args for box events
    public class BoxEventArgs : EventArgs
    {
        public float X1 { get; }
        public float Y1 { get; }
        public float X2 { get; }
        public float Y2 { get; }

        public BoxEventArgs(float x1, float y1, float x2, float y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }
    }

    // Event args for frame navigation
    public class NavigationEventArgs : EventArgs
    {
        public int Offset { get; }

        public NavigationEventArgs(int offset)
        {
            Offset = offset;
        }
    }
    
    // Event args for model selection
    public class ModelEventArgs : EventArgs
    {
        public string ModelPath { get; }
        
        public ModelEventArgs(string modelPath)
        {
            ModelPath = modelPath;
        }
    }
}