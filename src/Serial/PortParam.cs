using System;
using System.Linq;
using System.Collections.Generic;
using RJCP.IO.Ports;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Special;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace crft
{
    public class PortParam : GH_ValueList
    {
        private string _lastSelectedPort = string.Empty;
        private bool _debugMode = true;
        
        public PortParam()
        {
            CreateAttributes();
            Name = "Serial Ports";
            Description = "List of available serial ports";
            ListItems.Clear();
            // Populate port list immediately
            string[] ports;
            try
            {
                ports = System.IO.Ports.SerialPort.GetPortNames();
            }
            catch
            {
                ports = new string[0];
            }
            // Fallback for Unix/macOS if no ports found
            if (ports.Length == 0 && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var dev = "/dev";
                try
                {
                    var cu = Directory.GetFiles(dev, "cu.*");
                    ports = cu.Select(Path.GetFileName).ToArray();
                }
                catch { }
                if (ports.Length == 0)
                {
                    try
                    {
                        var tty = Directory.GetFiles(dev, "tty.*");
                        ports = tty.Select(Path.GetFileName).ToArray();
                    }
                    catch { }
                }
            }
            // Populate dropdown items
            if (ports.Length > 0)
            {
                foreach (var port in ports)
                {
                    ListItems.Add(new GH_ValueListItem(port, $"\"{port}\""));
                }
            }
            else
            {
                ListItems.Add(new GH_ValueListItem("<no ports>", "\"\""));
            }
            // Select first item by default
            if (ListItems.Count > 0)
                ListItems[0].Selected = true;
        }

        public override string Name => "Serial Ports";
        public override string Description => "List of available serial ports on this computer";
        public override Guid ComponentGuid => new Guid("a7c92f3d-e3d9-4753-95c8-b92c9254ba36");
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        public void Update()
        {
            // Store currently selected port
            var selected = (SelectedItems.FirstOrDefault()?.Value as GH_String)?.Value;
            if (!string.IsNullOrEmpty(selected))
            {
                _lastSelectedPort = selected;
                if (_debugMode)
                {
                    Debug.WriteLine($"PortParam: Last selected port was {_lastSelectedPort}");
                }
            }
            
            // Get available ports using multiple methods for redundancy
            string[] ports = GetPortsMultiMethod();
            
            if (_debugMode)
            {
                Debug.WriteLine($"PortParam: Found {ports.Length} ports: {string.Join(", ", ports)}");
            }

            // Only update if the list has changed
            if (ports.SequenceEqual(ListItems.Select(item => ((GH_String)item.Value).Value)))
            {
                if (_debugMode)
                {
                    Debug.WriteLine("PortParam: Port list unchanged, not updating");
                }
                return;
            }

            // Clear and rebuild list
            ListItems.Clear();
            int selectedIndex = 0;

            // If we had a previously selected port, try to preserve it
            if (!string.IsNullOrEmpty(_lastSelectedPort))
            {
                // Check if the previously selected port still exists
                selectedIndex = Array.FindIndex(ports, p => p.Equals(_lastSelectedPort, StringComparison.OrdinalIgnoreCase));
                
                // If not found, but might be disconnected, add it with a flag
                if (selectedIndex == -1 && ports.Length > 0)
                {
                    var selLabel = _lastSelectedPort;
                    // On macOS, strip cu./tty. prefix for display
                    if (selLabel.StartsWith("cu.") || selLabel.StartsWith("tty."))
                        selLabel = selLabel.Substring(selLabel.IndexOf('.') + 1);
                    
                    ListItems.Add(new GH_ValueListItem($"{selLabel} (disconnected)", $"\"{_lastSelectedPort}\""));
                    selectedIndex = 0;
                    
                    if (_debugMode)
                    {
                        Debug.WriteLine($"PortParam: Added disconnected port {_lastSelectedPort}");
                    }
                }
            }

            // Add all detected ports
            foreach (var port in ports)
            {
                var label = port;
                // On macOS, strip cu./tty. prefix for display
                if (label.StartsWith("cu.") || label.StartsWith("tty."))
                    label = label.Substring(label.IndexOf('.') + 1);
                
                ListItems.Add(new GH_ValueListItem(label, $"\"{port}\""));
                
                if (_debugMode)
                {
                    Debug.WriteLine($"PortParam: Added port {port} with label {label}");
                }
            }

            // Set selected item
            if (ListItems.Count > 0)
            {
                if (selectedIndex >= 0 && selectedIndex < ListItems.Count)
                {
                    ListItems[selectedIndex].Selected = true;
                    
                    if (_debugMode)
                    {
                        Debug.WriteLine($"PortParam: Selected port at index {selectedIndex}");
                    }
                }
                else
                {
                    // Default to first item if index out of range
                    ListItems[0].Selected = true;
                    
                    if (_debugMode)
                    {
                        Debug.WriteLine("PortParam: Selected first port by default");
                    }
                }
            }
        }

        /// <summary>
        /// Gets ports using multiple methods for maximum compatibility across platforms
        /// </summary>
        private string[] GetPortsMultiMethod()
        {
            List<string> allPorts = new List<string>();
            
            // Try cross-platform SerialPortStream first
            try
            {
                string[] streamPorts = SerialPortStream.GetPortNames();
                if (streamPorts.Length > 0)
                {
                    if (_debugMode)
                    {
                        Debug.WriteLine($"PortParam: SerialPortStream found {streamPorts.Length} ports");
                    }
                    allPorts.AddRange(streamPorts);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PortParam: SerialPortStream error: {ex.Message}");
            }
            
            // Try standard .NET SerialPort for Windows systems
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    string[] netPorts = System.IO.Ports.SerialPort.GetPortNames();
                    if (_debugMode)
                    {
                        Debug.WriteLine($"PortParam: System.IO.Ports found {netPorts.Length} ports");
                    }
                    // Add any ports not already in the list
                    foreach (var port in netPorts)
                    {
                        if (!allPorts.Contains(port, StringComparer.OrdinalIgnoreCase))
                        {
                            allPorts.Add(port);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PortParam: System.IO.Ports error: {ex.Message}");
                }
            }
            
            // Always try direct file system access on Unix systems (macOS/Linux)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    string[] unixPorts = GetUnixPortNames();
                    if (_debugMode)
                    {
                        Debug.WriteLine($"PortParam: Unix file system found {unixPorts.Length} ports");
                    }
                    // Add any ports not already in the list
                    foreach (var port in unixPorts)
                    {
                        if (!allPorts.Contains(port, StringComparer.OrdinalIgnoreCase))
                        {
                            allPorts.Add(port);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PortParam: Unix ports error: {ex.Message}");
                }
            }
            
            // Return a distinct list of ports
            return allPorts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>
        /// Fallback for Unix-like systems (macOS, Linux) using direct file system access
        /// </summary>
        private static string[] GetUnixPortNames()
        {
            var list = new List<string>();
            
            // macOS/FreeBSD: First priority is cu.* devices, which are for outgoing connections
            try
            {
                if (Directory.Exists("/dev"))
                {
                    var filesCu = Directory.GetFiles("/dev", "cu.*");
                    Debug.WriteLine($"Found {filesCu.Length} cu.* devices in /dev");
                    list.AddRange(filesCu.Select(Path.GetFileName));
                }
            }
            catch (Exception ex) 
            { 
                Debug.WriteLine($"Error listing cu.* devices: {ex.Message}");
            }
            
            // macOS: Second priority is tty.* devices 
            try
            {
                if (Directory.Exists("/dev"))
                {
                    var filesTty = Directory.GetFiles("/dev", "tty.*");
                    Debug.WriteLine($"Found {filesTty.Length} tty.* devices in /dev");
                    list.AddRange(filesTty.Select(Path.GetFileName));
                }
            }
            catch (Exception ex) 
            { 
                Debug.WriteLine($"Error listing tty.* devices: {ex.Message}");
            }
            
            // Linux: ttyUSB* and ttyACM* devices (common for USB-to-serial adapters and Arduino)
            try
            {
                if (Directory.Exists("/dev"))
                {
                    var filesUsbTty = Directory.GetFiles("/dev", "ttyUSB*");
                    Debug.WriteLine($"Found {filesUsbTty.Length} ttyUSB* devices in /dev");
                    list.AddRange(filesUsbTty.Select(Path.GetFileName));
                    
                    var filesAcmTty = Directory.GetFiles("/dev", "ttyACM*");
                    Debug.WriteLine($"Found {filesAcmTty.Length} ttyACM* devices in /dev");
                    list.AddRange(filesAcmTty.Select(Path.GetFileName));
                }
            }
            catch (Exception ex) 
            { 
                Debug.WriteLine($"Error listing ttyUSB/ACM devices: {ex.Message}");
            }
            
            // On some systems, we might need to prepend "/dev/" to make paths absolute
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].StartsWith("/dev/"))
                {
                    list[i] = "/dev/" + list[i];
                }
            }
            
            return list.ToArray();
        }
        
        /// <summary>
        /// Additional method to check if a specific device is connected using lsusb/ioreg (Unix/macOS)
        /// </summary>
        public static bool IsDeviceConnected(string vendorId, string productId)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows implementation would use different approach
                // Not implemented for simplicity
                return false;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS: use ioreg to check for USB devices
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "ioreg",
                        Arguments = "-p IOUSB -l",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using (Process p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        
                        // Check for vendor and product ID in the output
                        return output.Contains($"\"idVendor\" = {vendorId}") && 
                               output.Contains($"\"idProduct\" = {productId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking device connection: {ex.Message}");
                    return false;
                }
            }
            else // Linux and other Unix-like
            {
                // Linux: use lsusb to check for USB devices
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "lsusb",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using (Process p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        
                        // Check for vendor and product ID in the output (format: ID vendor:product)
                        return output.Contains($"ID {vendorId}:{productId}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking device connection: {ex.Message}");
                    return false;
                }
            }
        }
    }
}