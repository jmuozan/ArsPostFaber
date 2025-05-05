using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Special;

namespace crft
{
    public class PortParam : GH_ValueList
    {
        public PortParam()
        {
            CreateAttributes();
            Name = "Serial Ports";
            Description = "List of available serial ports";
            ListItems.Clear();
        }

        public override string Name => "Serial Ports";
        public override string Description => "List of available serial ports on this computer";
        public override Guid ComponentGuid => new Guid("a7c92f3d-e3d9-4753-95c8-b92c9254ba36");
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        public void Update()
        {
            var selected = (SelectedItems.FirstOrDefault()?.Value as GH_String)?.Value;
            
            string[] ports;
            try
            {
                ports = System.IO.Ports.SerialPort.GetPortNames();
            }
            catch (PlatformNotSupportedException)
            {
                ports = GetUnixPortNames();
            }

            if (ports.SequenceEqual(ListItems.Select(item => ((GH_String)item.Value).Value)))
                return;

            ListItems.Clear();
            int selectedIndex = 0;

            if (selected != null)
            {
                selectedIndex = Array.FindIndex(ports, p => p.Equals(selected, StringComparison.OrdinalIgnoreCase));
                if (selectedIndex == -1 && ports.Length > 0)
                {
                    ListItems.Add(new GH_ValueListItem($"{selected} (disconnected)", $"\"{selected}\""));
                    selectedIndex = 0;
                }
            }

            foreach (var port in ports)
                ListItems.Add(new GH_ValueListItem(port, $"\"{port}\""));

        if (ListItems.Count > 0)
            ListItems[selectedIndex].Selected = true;
        }

        // Fallback for non-Windows platforms: prefer 'cu.*' devices for outgoing connections
        private static string[] GetUnixPortNames()
        {
            var list = new List<string>();
            // First try 'cu.*' call-out ports
            try
            {
                var filesCu = Directory.GetFiles("/dev", "cu.*");
                list.AddRange(filesCu.Select(Path.GetFileName));
            }
            catch { }
            if (list.Count > 0)
                return list.ToArray();
            // If no 'cu.*', fall back to 'tty.*'
            try
            {
                var filesTty = Directory.GetFiles("/dev", "tty.*");
                list.AddRange(filesTty.Select(Path.GetFileName));
            }
            catch { }
            return list.ToArray();
        }
    }
}