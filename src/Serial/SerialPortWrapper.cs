using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Runtime.InteropServices;
using System.Text;
using RJCP.IO.Ports;

namespace crft
{
    internal interface ISerialPort
    {
        bool IsOpen { get; }
        event Action<string> DataReceived;
        void Open();
        void Close();
        void WriteLine(string line);
        void ClearBuffers();
    }

    internal class UnixSerialPort : ISerialPort
    {
        private readonly string _path;
        private readonly int _baudRate;
        private FileStream _fs;
        private Thread _readThread;
        private bool _running;

        public bool IsOpen => _fs != null;
        public event Action<string> DataReceived;

        public UnixSerialPort(string path, int baudRate)
        {
            _path = path;
            _baudRate = baudRate;
        }

        public void Open()
        {
            // Configure port settings via stty
            var psi = new ProcessStartInfo("stty", $"-f {_path} {_baudRate} cs8 -cstopb -parenb raw")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var p = Process.Start(psi);
            p.WaitForExit();

            _fs = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            _running = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();
            
            // Give the stream a moment to initialize
            Thread.Sleep(300);
        }

        public void Close()
        {
            _running = false;
            try { _readThread?.Join(500); } catch { }
            _fs?.Close();
            _fs = null;
        }

        public void WriteLine(string line)
        {
            if (_fs == null) return;
            // Ensure proper line ending for 3D printers (CRLF)
            if (!line.EndsWith("\r\n"))
                line = line.TrimEnd() + "\r\n";
                
            var bytes = Encoding.ASCII.GetBytes(line);
            _fs.Write(bytes, 0, bytes.Length);
            _fs.Flush();
            
            // Log the actual bytes being sent (for debugging)
            Debug.WriteLine($"Sent bytes: {BitConverter.ToString(bytes)}");
        }
        
        public void ClearBuffers()
        {
            if (_fs == null) return;
            
            try
            {
                // Discard any data in the buffer
                byte[] buffer = new byte[1024];
                while (_fs.CanRead && _fs.Length > 0 && _fs.Position < _fs.Length)
                {
                    int bytesRead = _fs.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    Debug.WriteLine($"Cleared {bytesRead} bytes from input buffer");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing buffers: {ex.Message}");
            }
        }

        private void ReadLoop()
        {
            var sb = new StringBuilder();
            while (_running && _fs != null)
            {
                int b;
                try { b = _fs.ReadByte(); }
                catch { break; }
                if (b < 0) { Thread.Sleep(10); continue; }
                char c = (char)b;
                
                // For debugging: show ASCII value of received bytes
                Debug.WriteLine($"Received byte: {b} (ASCII: {c})");
                
                // Handle both CR and LF as line endings
                if (c == '\n' || c == '\r')
                {
                    if (sb.Length > 0)
                    {
                        var s = sb.ToString().Trim(); sb.Clear();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            DataReceived?.Invoke(s);
                        }
                    }
                    // Skip the next character if it's the second part of CRLF
                    if (c == '\r')
                    {
                        try 
                        {
                            int next = _fs.ReadByte();
                            if (next != '\n') // If not LF, put it back (theoretically)
                            {
                                // Can't really put it back, so add it to the buffer if it's valid
                                if (next >= 0)
                                    sb.Append((char)next);
                            }
                        }
                        catch { }
                    }
                }
                else sb.Append(c);
            }
        }
    }

    internal class WindowsSerialPort : ISerialPort
    {
        private readonly SerialPortStream _port;
        public bool IsOpen => _port != null && _port.IsOpen;
        public event Action<string> DataReceived;

        public WindowsSerialPort(string path, int baudRate)
        {
            _port = new SerialPortStream(path, baudRate)
            {
                Parity = RJCP.IO.Ports.Parity.None,
                DataBits = 8,
                StopBits = RJCP.IO.Ports.StopBits.One,
                Handshake = RJCP.IO.Ports.Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };
            
            _port.DataReceived += (s, e) =>
            {
                try 
                { 
                    var line = _port.ReadLine(); 
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        DataReceived?.Invoke(line.Trim());
                    }
                }
                catch { }
            };
        }

        public void Open() 
        {
            _port.Open();
            // Give the port time to initialize
            Thread.Sleep(300);
        }
        
        public void Close() => _port.Close();
        
        public void WriteLine(string line) 
        {
            // Ensure proper line ending for 3D printers (CRLF)
            if (!line.EndsWith("\r\n"))
                line = line.TrimEnd() + "\r\n";
                
            _port.Write(line);
            _port.Flush();
            
            // Log the actual bytes being sent (for debugging)
            byte[] bytes = Encoding.ASCII.GetBytes(line);
            Debug.WriteLine($"Sent bytes: {BitConverter.ToString(bytes)}");
        }
        
        public void ClearBuffers()
        {
            try
            {
                if (_port != null && _port.IsOpen)
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing buffers: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Wrapper for .NET System.IO.Ports.SerialPort for cross-platform communications (e.g., macOS).
    /// </summary>
    internal class DotNetSerialPort : ISerialPort
    {
        private readonly SerialPort _port;
        public bool IsOpen => _port != null && _port.IsOpen;
        public event Action<string> DataReceived;

        public DotNetSerialPort(string portPath, int baudRate)
        {
            _port = new SerialPort(portPath, baudRate, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One)
            {
                Handshake = System.IO.Ports.Handshake.None,
                NewLine = "\r\n",
                ReadTimeout = 500,
                WriteTimeout = 500
            };
            _port.DataReceived += OnDataReceived;
        }

        private void OnDataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            try
            {
                while (_port.BytesToRead > 0)
                {
                    string line = _port.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                        DataReceived?.Invoke(line.Trim());
                }
            }
            catch (TimeoutException) { }
            catch (Exception) { }
        }

        public void Open()
        {
            // Ensure DTR/RTS are low before opening
            _port.DtrEnable = false;
            _port.RtsEnable = false;
            _port.Open();
            Thread.Sleep(300);
            // Toggle DTR/RTS to reset printer on connect
            _port.DtrEnable = true;
            _port.RtsEnable = true;
            Thread.Sleep(1000);
            _port.DtrEnable = false;
            _port.RtsEnable = false;
            Thread.Sleep(100);
        }

        public void Close()
        {
            try { _port.Close(); } catch { }
        }

        public void WriteLine(string line)
        {
            if (!_port.IsOpen) return;
            if (!line.EndsWith("\r\n"))
                line = line.TrimEnd() + "\r\n";
            _port.Write(line);
            try { _port.BaseStream.Flush(); } catch { }
            var bytes = Encoding.ASCII.GetBytes(line);
            Debug.WriteLine($"Sent bytes: {BitConverter.ToString(bytes)}");
        }

        public void ClearBuffers()
        {
            try
            {
                if (_port.IsOpen)
                {
                    _port.DiscardInBuffer();
                    _port.DiscardOutBuffer();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing buffers: {ex.Message}");
            }
        }
    }
}