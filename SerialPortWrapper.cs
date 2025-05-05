using System;
using System.Diagnostics;
using System.IO;
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
            var bytes = Encoding.ASCII.GetBytes(line + "\n");
            _fs.Write(bytes, 0, bytes.Length);
            _fs.Flush();
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
                if (c == '\n')
                {
                    var s = sb.ToString(); sb.Clear();
                    DataReceived?.Invoke(s);
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
            _port = new SerialPortStream(path, baudRate);
            _port.DataReceived += (s, e) =>
            {
                try { var line = _port.ReadLine(); DataReceived?.Invoke(line); }
                catch { }
            };
        }

        public void Open() => _port.Open();
        public void Close() => _port.Close();
        public void WriteLine(string line) => _port.WriteLine(line);
    }
}