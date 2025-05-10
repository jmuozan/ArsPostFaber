using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Diagnostics;

// This is a standalone diagnostic tool to test 3D printer serial communication
// It implements multiple connection methods for comparison to help diagnose issues

namespace PrinterTester
{
    class Program
    {
        static int Main(string[] args)
        {
            Console.WriteLine("3D Printer Connection Diagnostic Tool");
            Console.WriteLine("=====================================");
            
            // List all available serial ports
            Console.WriteLine("\nDetecting serial ports...");
            List<string> availablePorts = new List<string>();
            
            // Method 1: System.IO.Ports
            try
            {
                string[] systemPorts = SerialPort.GetPortNames();
                Console.WriteLine($"System.IO.Ports detected {systemPorts.Length} ports:");
                foreach (string port in systemPorts)
                {
                    Console.WriteLine($"  - {port}");
                    if (!availablePorts.Contains(port))
                    {
                        availablePorts.Add(port);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error detecting System.IO.Ports: {ex.Message}");
            }
            
            // Method 2: Direct filesystem check for Unix systems
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    List<string> unixPorts = new List<string>();
                    
                    // Check for macOS typical ports
                    if (Directory.Exists("/dev"))
                    {
                        // macOS/FreeBSD: First priority is cu.* devices, which are for outgoing connections
                        try
                        {
                            string[] cuPorts = Directory.GetFiles("/dev", "cu.*");
                            foreach (string port in cuPorts)
                            {
                                string portName = Path.GetFileName(port);
                                unixPorts.Add(portName);
                                Console.WriteLine($"  - {portName} (cu.*)");
                            }
                        }
                        catch { /* Ignore */ }
                        
                        // Check for tty.* devices
                        try
                        {
                            string[] ttyPorts = Directory.GetFiles("/dev", "tty.*");
                            foreach (string port in ttyPorts)
                            {
                                string portName = Path.GetFileName(port);
                                unixPorts.Add(portName);
                                Console.WriteLine($"  - {portName} (tty.*)");
                            }
                        }
                        catch { /* Ignore */ }
                        
                        // Linux: ttyUSB* and ttyACM* devices (common for USB-to-serial adapters and Arduino)
                        try
                        {
                            string[] usbPorts = Directory.GetFiles("/dev", "ttyUSB*");
                            foreach (string port in usbPorts)
                            {
                                string portName = Path.GetFileName(port);
                                unixPorts.Add(portName);
                                Console.WriteLine($"  - {portName} (ttyUSB*)");
                            }
                            
                            string[] acmPorts = Directory.GetFiles("/dev", "ttyACM*");
                            foreach (string port in acmPorts)
                            {
                                string portName = Path.GetFileName(port);
                                unixPorts.Add(portName);
                                Console.WriteLine($"  - {portName} (ttyACM*)");
                            }
                        }
                        catch { /* Ignore */ }
                    }
                    
                    // Add unix ports to available ports
                    foreach (string port in unixPorts)
                    {
                        string fullPath = port.StartsWith("/dev/") ? port : "/dev/" + port;
                        if (!availablePorts.Contains(fullPath))
                        {
                            availablePorts.Add(fullPath);
                        }
                    }
                    
                    Console.WriteLine($"Filesystem check detected {unixPorts.Count} ports");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error detecting Unix ports: {ex.Message}");
                }
            }
            
            // Ask user to select port
            if (availablePorts.Count == 0)
            {
                Console.WriteLine("No serial ports detected. Please check your connections and try again.");
                return 1;
            }
            
            Console.WriteLine("\nAvailable ports:");
            for (int i = 0; i < availablePorts.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {availablePorts[i]}");
            }
            
            int selectedIndex = 0;
            while (selectedIndex < 1 || selectedIndex > availablePorts.Count)
            {
                Console.Write($"\nSelect port (1-{availablePorts.Count}): ");
                if (!int.TryParse(Console.ReadLine(), out selectedIndex))
                {
                    selectedIndex = 0;
                }
            }
            
            string selectedPort = availablePorts[selectedIndex - 1];
            Console.WriteLine($"Selected port: {selectedPort}");
            
            // Ask for baud rate
            Console.Write("\nEnter baud rate (default: 115200): ");
            string baudInput = Console.ReadLine();
            int baudRate = 115200;
            if (!string.IsNullOrEmpty(baudInput) && int.TryParse(baudInput, out int customBaud))
            {
                baudRate = customBaud;
            }
            
            Console.WriteLine($"Using baud rate: {baudRate}");
            
            // Test the connection
            Console.WriteLine("\nTesting connection with multiple methods...");
            
            // Method 1: System.IO.Ports direct connection
            Console.WriteLine("\n[METHOD 1] Testing with System.IO.Ports.SerialPort...");
            TestWithSystemIOPorts(selectedPort, baudRate);
            
            // Method 2: Manual file stream (for Unix systems)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine("\n[METHOD 2] Testing with direct file stream access...");
                TestWithFileStream(selectedPort, baudRate);
            }
            
            // Command loop
            Console.WriteLine("\n\nEntering command mode. Enter G-code commands to send.");
            Console.WriteLine("Enter 'quit' to exit, 'test' to run automatic test sequence.");
            
            // Choose connection method
            Console.Write("Which method to use for interactive mode (1 or 2): ");
            string methodInput = Console.ReadLine();
            int method = 1;
            if (!string.IsNullOrEmpty(methodInput) && int.TryParse(methodInput, out int customMethod))
            {
                method = customMethod;
            }
            
            if (method == 1)
            {
                InteractiveCommandLoopMethod1(selectedPort, baudRate);
            }
            else
            {
                InteractiveCommandLoopMethod2(selectedPort, baudRate);
            }
            
            return 0;
        }
        
        // Method 1: Test with System.IO.Ports
        static void TestWithSystemIOPorts(string portName, int baudRate)
        {
            try
            {
                using (SerialPort port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
                {
                    port.ReadTimeout = 1000;
                    port.WriteTimeout = 1000;
                    port.NewLine = "\r\n";  // Explicit CRLF for 3D printers
                    
                    // Open the port
                    Console.WriteLine($"Opening port {portName}...");
                    port.Open();
                    Console.WriteLine("Port opened successfully");
                    
                    // Clear any buffered data
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();
                    
                    // Wait for initialization
                    Thread.Sleep(1000);
                    
                    // Send a test command (M115 - Firmware info)
                    Console.WriteLine("Sending test command (M115)...");
                    string command = "M115\r\n";
                    
                    // Show the exact bytes we're sending
                    byte[] commandBytes = Encoding.ASCII.GetBytes(command);
                    Console.WriteLine($"Sending bytes: {BitConverter.ToString(commandBytes)}");
                    
                    // Send the command
                    port.Write(commandBytes, 0, commandBytes.Length);
                    port.BaseStream.Flush();
                    
                    // Wait for response
                    Console.WriteLine("Waiting for response...");
                    Thread.Sleep(1000);
                    
                    // Read response
                    StringBuilder response = new StringBuilder();
                    try
                    {
                        // Read what's available in buffer
                        if (port.BytesToRead > 0)
                        {
                            byte[] buffer = new byte[port.BytesToRead];
                            int bytesRead = port.Read(buffer, 0, buffer.Length);
                            string responseText = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                            response.Append(responseText);
                            
                            // Show the exact bytes received
                            Console.WriteLine($"Received bytes: {BitConverter.ToString(buffer, 0, bytesRead)}");
                        }
                        else
                        {
                            response.Append("[No data received]");
                        }
                    }
                    catch (TimeoutException)
                    {
                        response.Append("[Timeout reading response]");
                    }
                    
                    Console.WriteLine($"Response: {response}");
                    
                    // Close the port
                    Console.WriteLine("Closing port...");
                    port.Close();
                    Console.WriteLine("Port closed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }
        
        // Method 2: Test with direct file stream (for Unix systems)
        static void TestWithFileStream(string portName, int baudRate)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine("This method is only available on Unix-like systems");
                return;
            }
            
            FileStream fs = null;
            
            try
            {
                // Configure port settings via stty
                Console.WriteLine("Configuring port with stty...");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "stty",
                    Arguments = $"-f {portName} {baudRate} cs8 -cstopb -parenb raw",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                
                Process process = Process.Start(psi);
                process.WaitForExit();
                
                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"stty error: {process.StandardError.ReadToEnd()}");
                    return;
                }
                
                // Open the port as a file stream
                Console.WriteLine($"Opening port {portName} as file stream...");
                fs = new FileStream(portName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                Console.WriteLine("File stream opened successfully");
                
                // Send a test command (M115 - Firmware info)
                Console.WriteLine("Sending test command (M115)...");
                string command = "M115\r\n";
                byte[] commandBytes = Encoding.ASCII.GetBytes(command);
                
                // Show the exact bytes we're sending
                Console.WriteLine($"Sending bytes: {BitConverter.ToString(commandBytes)}");
                
                // Send the command
                fs.Write(commandBytes, 0, commandBytes.Length);
                fs.Flush();
                
                // Wait for response
                Console.WriteLine("Waiting for response...");
                Thread.Sleep(1000);
                
                // Read response
                StringBuilder response = new StringBuilder();
                byte[] buffer = new byte[1024];
                
                // Check if data is available
                if (fs.CanRead)
                {
                    int bytesRead = fs.Read(buffer, 0, buffer.Length);
                    
                    if (bytesRead > 0)
                    {
                        string responseText = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                        response.Append(responseText);
                        
                        // Show the exact bytes received
                        Console.WriteLine($"Received bytes: {BitConverter.ToString(buffer, 0, bytesRead)}");
                    }
                    else
                    {
                        response.Append("[No data received]");
                    }
                }
                else
                {
                    response.Append("[Cannot read from stream]");
                }
                
                Console.WriteLine($"Response: {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }
            finally
            {
                // Close the port
                if (fs != null)
                {
                    Console.WriteLine("Closing file stream...");
                    fs.Close();
                    fs.Dispose();
                    Console.WriteLine("File stream closed");
                }
            }
        }
        
        // Interactive command loop using Method 1 (System.IO.Ports)
        static void InteractiveCommandLoopMethod1(string portName, int baudRate)
        {
            try
            {
                using (SerialPort port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One))
                {
                    port.ReadTimeout = 1000;
                    port.WriteTimeout = 1000;
                    port.NewLine = "\r\n";  // Explicit CRLF for 3D printers

                    // Open the port
                    Console.WriteLine($"Opening port {portName} for interactive mode...");
                    port.Open();
                    Console.WriteLine("Port opened successfully for interactive mode.");

                    // Clear any buffered data
                    port.DiscardInBuffer();
                    port.DiscardOutBuffer();

                    // Interactive loop
                    while (true)
                    {
                        Console.Write("> ");
                        string input = Console.ReadLine();
                        if (input == null) break;
                        if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("Exiting interactive mode.");
                            break;
                        }
                        if (input.Equals("test", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] testCommands = new[] { "M115", "M105", "G28" };
                            foreach (var cmd in testCommands)
                            {
                                SendCommandPort(port, cmd);
                            }
                            continue;
                        }
                        SendCommandPort(port, input);
                    }

                    port.Close();
                    Console.WriteLine("Port closed.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in interactive mode: {ex.Message}");
            }
        }

        // Helper for sending commands over SerialPort
        static void SendCommandPort(SerialPort port, string command)
        {
            string cmd = command.EndsWith("\r\n") ? command : command + "\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(cmd);
            Console.WriteLine($"Sending bytes: {BitConverter.ToString(bytes)}");
            port.Write(bytes, 0, bytes.Length);
            port.BaseStream.Flush();
            Thread.Sleep(500);

            StringBuilder response = new StringBuilder();
            try
            {
                while (port.BytesToRead > 0)
                {
                    byte[] buffer = new byte[port.BytesToRead];
                    int bytesRead = port.Read(buffer, 0, buffer.Length);
                    response.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                }
            }
            catch (TimeoutException) { }

            if (response.Length > 0)
                Console.WriteLine($"Response: {response}");
            else
                Console.WriteLine("[No response]");
        }

        // Interactive command loop using Method 2 (direct file stream)
        static void InteractiveCommandLoopMethod2(string portName, int baudRate)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine("Interactive Method2 is only supported on Unix-like systems.");
                return;
            }

            FileStream fs = null;
            try
            {
                // Configure port settings via stty
                Console.WriteLine("Configuring port with stty for interactive mode...");
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "stty",
                    Arguments = $"-f {portName} {baudRate} cs8 -cstopb -parenb raw",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        Console.WriteLine($"stty error: {process.StandardError.ReadToEnd()}");
                        return;
                    }
                }

                // Open the port as a file stream
                Console.WriteLine($"Opening port {portName} as file stream for interactive mode...");
                fs = new FileStream(portName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                Console.WriteLine("File stream opened successfully for interactive mode.");

                // Interactive loop
                while (true)
                {
                    Console.Write("> ");
                    string input = Console.ReadLine();
                    if (input == null) break;
                    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Exiting interactive mode.");
                        break;
                    }
                    if (input.Equals("test", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] testCommands = new[] { "M115", "M105", "G28" };
                        foreach (var cmd in testCommands)
                        {
                            SendCommandFile(fs, cmd);
                        }
                        continue;
                    }
                    SendCommandFile(fs, input);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in interactive mode (file stream): {ex.Message}");
            }
            finally
            {
                if (fs != null)
                {
                    fs.Close();
                    fs.Dispose();
                    Console.WriteLine("File stream closed.");
                }
            }
        }

        // Helper for sending commands over FileStream
        static void SendCommandFile(FileStream fs, string command)
        {
            string cmd = command.EndsWith("\r\n") ? command : command + "\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(cmd);
            Console.WriteLine($"Sending bytes: {BitConverter.ToString(bytes)}");
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush();
            Thread.Sleep(500);

            byte[] buffer = new byte[1024];
            int bytesRead = fs.Read(buffer, 0, buffer.Length);
            if (bytesRead > 0)
            {
                string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Response: {response}");
            }
            else
            {
                Console.WriteLine("[No response]");
            }
        }

    } // end class Program
} // end namespace PrinterTester