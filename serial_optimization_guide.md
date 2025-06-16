# Serial Component Optimization Guide

This guide details how to optimize your Grasshopper Serial Control Component to achieve smooth G-code streaming performance similar to Repetier Host.

## Problem Analysis

Your current implementation has several performance bottlenecks:

1. **Excessive synchronization**: Sending `M400` after every command forces the printer to complete all moves before accepting new commands
2. **No buffer management**: Not utilizing the printer's internal command buffer efficiently
3. **Poor ACK handling**: Waiting for acknowledgment on every command without proper flow control
4. **Frequent UI updates**: Updating the solution after every command creates unnecessary overhead

## Required Changes

### 1. Add New Class Fields

Add these fields to your `SerialControlComponent` class:

```csharp
// Buffer management
private const int MAX_BUFFER_SIZE = 8; // Most 3D printers have 8-16 command buffer
private int _commandsInBuffer = 0;
private Queue<DateTime> _commandTimestamps = new Queue<DateTime>();

// Enhanced communication
private readonly object _responseLock = new object();
private int _expectedLineNumber = 0;
private bool _printerReady = false;
```

### 2. Replace PrintLoop Method

**Location**: `SerialControlComponent.cs`, line ~400 (approximately)

**Action**: Replace the entire `PrintLoop()` method with:

```csharp
/// <summary>
/// Improved background print loop with proper buffering and flow control
/// </summary>
private void PrintLoop()
{
    _commandsInBuffer = 0;
    _commandTimestamps.Clear();
    
    while (_currentLineIndex < _printCommands.Count)
    {
        // Wait until playing
        while (!_isPlaying)
        {
            Thread.Sleep(100);
            continue;
        }
        
        // Stop if port closed
        if (_serialPort == null || !_serialPort.IsOpen)
            break;

        var cmd = _printCommands[_currentLineIndex].Trim();
        
        try
        {
            // Check if we need to wait for buffer space
            if (_commandsInBuffer >= MAX_BUFFER_SIZE)
            {
                // Wait for ACK to free up buffer space
                if (_ackEvent.WaitOne(5000)) // 5 second timeout
                {
                    _commandsInBuffer--;
                    if (_commandTimestamps.Count > 0)
                        _commandTimestamps.Dequeue();
                }
                else
                {
                    _lastEvent = "Timeout waiting for printer response";
                    break;
                }
            }
            
            // Send command
            _status = $"Sending: {cmd}";
            
            // Use line numbers for critical commands
            if (IsBufferedCommand(cmd))
            {
                if (!SendCommandWithLineNumber(cmd, _expectedLineNumber))
                {
                    _lastEvent = $"Failed to send command: {cmd}";
                    break;
                }
                _expectedLineNumber++;
                _commandsInBuffer++;
                _commandTimestamps.Enqueue(DateTime.Now);
            }
            else
            {
                // For non-buffered commands, send without line numbers
                _serialPort.WriteLine(cmd);
                _ackEvent.WaitOne(2000); // Wait for immediate ACK
            }
            
            _currentLineIndex++;
            
            // Update UI less frequently for better performance
            if (_currentLineIndex % 5 == 0)
            {
                ExpireSolution(true);
            }
            
            // Small delay to prevent overwhelming the printer
            Thread.Sleep(10);
        }
        catch (Exception ex)
        {
            _lastEvent = $"Error: {ex.Message}";
            break;
        }
    }
    
    // Wait for all remaining commands to complete
    while (_commandsInBuffer > 0)
    {
        if (_ackEvent.WaitOne(1000))
        {
            _commandsInBuffer--;
        }
        else
        {
            break;
        }
    }
    
    if (_currentLineIndex >= _printCommands.Count)
    {
        _isPlaying = false;
        _lastEvent = "Print complete";
    }
    _printThread = null;
}
```

### 3. Add Helper Methods

**Location**: Add these methods to your `SerialControlComponent` class:

```csharp
/// <summary>
/// Determines if a command gets buffered by the printer
/// </summary>
private bool IsBufferedCommand(string command)
{
    var cmd = command.Trim().ToUpper();
    
    // Movement commands are buffered
    if (cmd.StartsWith("G0") || cmd.StartsWith("G1") || cmd.StartsWith("G2") || cmd.StartsWith("G3"))
        return true;
    
    // Some other commands that might be buffered
    if (cmd.StartsWith("G4")) // Dwell
        return true;
        
    // Temperature and status commands are usually not buffered
    if (cmd.StartsWith("M104") || cmd.StartsWith("M105") || cmd.StartsWith("M106") || 
        cmd.StartsWith("M107") || cmd.StartsWith("M109") || cmd.StartsWith("M190"))
        return false;
    
    // Most other M commands are buffered
    if (cmd.StartsWith("M"))
        return true;
    
    return false;
}

/// <summary>
/// Format command with line number and checksum for reliable transmission
/// </summary>
private string FormatCommandWithLineNumber(string command, int lineNumber)
{
    // Remove any existing line numbers and checksums
    command = command.Trim();
    int semicolonPos = command.IndexOf(';');
    if (semicolonPos >= 0)
        command = command.Substring(0, semicolonPos).Trim();
    
    // Add line number
    string formattedCommand = $"N{lineNumber} {command}";
    
    // Calculate checksum
    int checksum = 0;
    foreach (char c in formattedCommand)
    {
        checksum ^= c;
    }
    
    // Add checksum
    formattedCommand += $"*{checksum}";
    
    return formattedCommand;
}

/// <summary>
/// Enhanced command sending with line numbers and error recovery
/// </summary>
private bool SendCommandWithLineNumber(string command, int lineNumber, int maxRetries = 3)
{
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            string formattedCommand = FormatCommandWithLineNumber(command, lineNumber);
            _serialPort.WriteLine(formattedCommand);
            
            // Wait for ACK with timeout
            if (_ackEvent.WaitOne(3000)) // 3 second timeout
            {
                return true; // Success
            }
            else
            {
                _lastEvent = $"Timeout on line {lineNumber}, attempt {attempt + 1}";
                if (attempt < maxRetries - 1)
                {
                    Thread.Sleep(500); // Wait before retry
                }
            }
        }
        catch (Exception ex)
        {
            _lastEvent = $"Send error on line {lineNumber}: {ex.Message}";
            if (attempt < maxRetries - 1)
            {
                Thread.Sleep(500);
            }
        }
    }
    
    return false; // Failed after all retries
}

/// <summary>
/// Extract parameter value from G-code response
/// </summary>
private string ExtractParameter(string response, string parameter)
{
    var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    foreach (var part in parts)
    {
        if (part.StartsWith(parameter, StringComparison.OrdinalIgnoreCase))
        {
            return part.Substring(parameter.Length);
        }
    }
    return null;
}

/// <summary>
/// Extract line number from resend request
/// </summary>
private int ExtractResendLineNumber(string response)
{
    // Look for "N" parameter in resend response
    var nParam = ExtractParameter(response, "N");
    if (int.TryParse(nParam, out int lineNum))
        return lineNum;
    
    // Fallback: look for number after "resend" or "rs"
    var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    for (int i = 0; i < parts.Length - 1; i++)
    {
        if (parts[i].Equals("resend", StringComparison.OrdinalIgnoreCase) ||
            parts[i].Equals("rs", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(parts[i + 1], out int num))
                return num;
        }
    }
    
    return _currentLineIndex; // Default to current line
}
```

### 4. Update Connection Logic

**Location**: In your `SolveInstance` method, around line 200

**Action**: Replace the connection initialization block with:

```csharp
if (connect && !_lastConnect)
{
    try
    {
        // Initialize cross-platform serial port
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _serialPort = new WindowsSerialPort(portName, baudRate);
        }
        else
        {
            string device = portName.StartsWith("/") ? portName : Path.Combine("/dev", portName);
            _serialPort = new UnixSerialPort(device, baudRate);
        }
        
        // Initialize connection with proper handshake
        if (!InitializeConnection())
        {
            throw new Exception("Failed to initialize printer connection");
        }
        
        // Load commands for playback
        if (commandsList.Count > 0)
        {
            _printCommands = new List<string>(commandsList);
            _currentLineIndex = 0;
            _isPlaying = false;
            _ackEvent = new AutoResetEvent(false);
            _status = $"Loaded {_printCommands.Count} commands";
        }
        
        // Clear any previous preview edits on new connection
        _hasPreviewEdits = false;
        _editedPreviewPoints = null;
    }
    catch (Exception ex)
    {
        _lastEvent = $"Connection error: {ex.Message}";
    }
}
```

### 5. Add Enhanced Connection Initialization

**Location**: Add this method to your `SerialControlComponent` class:

```csharp
/// <summary>
/// Enhanced connection initialization with proper handshake
/// </summary>
private bool InitializeConnection()
{
    try
    {
        _responseLog.Clear();
        _printerReady = false;
        _expectedLineNumber = 0;
        
        // Setup enhanced data received handler
        SetupSerialPortHandlers();
        
        // Open port
        _serialPort.Open();
        
        // Clear any stray data
        _serialPort.ClearBuffers();
        
        // Wait for printer startup message
        DateTime startTime = DateTime.Now;
        while (!_printerReady && (DateTime.Now - startTime).TotalSeconds < 10)
        {
            Thread.Sleep(100);
        }
        
        if (!_printerReady)
        {
            // Printer might not send startup message, proceed anyway
            _printerReady = true;
        }
        
        // Reset line numbering
        _serialPort.WriteLine("M110 N0*125"); // Reset line number with checksum
        Thread.Sleep(500);
        
        // Send a test command to verify communication
        if (!SendCommandWithLineNumber("M115", 1)) // Get firmware info
        {
            throw new Exception("Failed to establish communication with printer");
        }
        
        _expectedLineNumber = 2; // Next line number after M115
        
        _lastEvent = $"Connected to printer successfully";
        return true;
    }
    catch (Exception ex)
    {
        _lastEvent = $"Connection failed: {ex.Message}";
        return false;
    }
}

/// <summary>
/// Setup enhanced serial port data handlers
/// </summary>
private void SetupSerialPortHandlers()
{
    _serialPort.DataReceived += data =>
    {
        lock (_responseLock)
        {
            _responseLog.Add(data);
            var trimmed = data.TrimStart();
            
            // Handle different response types
            if (trimmed.StartsWith("ok", StringComparison.OrdinalIgnoreCase))
            {
                // Check for line number in OK response
                if (trimmed.Contains("N") && int.TryParse(
                    ExtractParameter(trimmed, "N"), out int lineNum))
                {
                    // Verify line number matches expected
                    if (lineNum == _expectedLineNumber - 1) // ACK for previous command
                    {
                        _ackEvent?.Set();
                    }
                }
                else
                {
                    // Simple OK without line number
                    _ackEvent?.Set();
                }
                
                _printerReady = true;
            }
            else if (trimmed.StartsWith("start", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Contains("ready", StringComparison.OrdinalIgnoreCase))
            {
                _printerReady = true;
                _lastEvent = "Printer ready";
            }
            else if (trimmed.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("!!", StringComparison.OrdinalIgnoreCase))
            {
                _lastEvent = $"Printer error: {data}";
                _printerReady = false;
            }
            else if (trimmed.StartsWith("resend", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("rs", StringComparison.OrdinalIgnoreCase))
            {
                // Handle resend request
                int resendLine = ExtractResendLineNumber(trimmed);
                _lastEvent = $"Resend requested for line {resendLine}";
                // TODO: Implement resend logic if needed
            }
            else if (!trimmed.StartsWith("echo:busy", StringComparison.OrdinalIgnoreCase))
            {
                // Update last event for non-busy messages
                _lastEvent = $"Received: {data}";
            }
        }
    };
}
```

## Testing and Verification

### 1. Before Testing
- Backup your current `SerialControlComponent.cs` file
- Make sure your printer is properly connected and powered on

### 2. Test Procedure
1. Apply all changes above
2. Rebuild your Grasshopper plugin
3. Test with a simple G-code file (just a few movement commands)
4. Monitor the console/debug output for any error messages
5. Compare the smoothness with your previous implementation

### 3. Expected Results
- Faster G-code streaming
- Smoother movement without pauses between commands
- Better error handling and recovery
- Performance similar to Repetier Host

## Troubleshooting

### Common Issues

**"Timeout waiting for printer response"**
- Reduce `MAX_BUFFER_SIZE` from 8 to 4 or 6
- Check if your printer supports line numbers and checksums

**"Failed to establish communication"**
- Verify baud rate and port settings
- Check if printer is in the correct state (not printing, not in error)

**Commands still slow**
- Verify that `M400` commands are not being sent after every command
- Check that `IsBufferedCommand()` correctly identifies movement commands

### Debug Tips

1. Add debug output to see buffer utilization:
```csharp
Console.WriteLine($"Buffer: {_commandsInBuffer}/{MAX_BUFFER_SIZE}");
```

2. Monitor ACK timing:
```csharp
DateTime ackStart = DateTime.Now;
if (_ackEvent.WaitOne(5000))
{
    var ackTime = (DateTime.Now - ackStart).TotalMilliseconds;
    Console.WriteLine($"ACK received in {ackTime}ms");
}
```

## Performance Tuning

### Buffer Size Optimization
- Start with `MAX_BUFFER_SIZE = 8`
- If you get timeouts, reduce to 4 or 6
- If printer has larger buffer, you can increase to 12 or 16

### Timing Adjustments
- Adjust the 10ms delay in PrintLoop if needed
- Modify timeout values based on your printer's response time

### Line Number Protocol
- Some printers work better without line numbers
- You can disable line numbers by always using the simple `_serialPort.WriteLine(cmd)` approach

## Additional Optimizations

### Future Enhancements
1. **Implement resend logic** for better error recovery
2. **Add temperature monitoring** without blocking the main stream
3. **Implement emergency stop** functionality
4. **Add print progress estimation** based on buffer utilization

This guide should significantly improve your G-code streaming performance. The key is proper buffer management and eliminating unnecessary synchronization points that force the printer to wait between commands.