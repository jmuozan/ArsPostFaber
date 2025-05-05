using System;
using System.Linq;
using System.Collections.Generic;
using RJCP.IO.Ports;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Special;
using System.IO;

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
            
            // Use cross-platform SerialPortStream to get available ports (fallback on Unix if native lib unavailable)
            string[] ports;
            try
            {
                ports = SerialPortStream.GetPortNames();
            }
            catch
            {
                ports = GetUnixPortNames();
            }

            if (ports.SequenceEqual(ListItems.Select(item => ((GH_String)item.Value).Value)))
                return;

            ListItems.Clear();
            int selectedIndex = 0;

            if (selected != null)
            {
                // Preserve disconnected selected item
                selectedIndex = Array.FindIndex(ports, p => p.Equals(selected, StringComparison.OrdinalIgnoreCase));
                if (selectedIndex == -1 && ports.Length > 0)
                {
                    var selLabel = selected;
                    if (selLabel.StartsWith("cu.") || selLabel.StartsWith("tty."))
                        selLabel = selLabel.Substring(selLabel.IndexOf('.') + 1);
                    ListItems.Add(new GH_ValueListItem($"{selLabel} (disconnected)", $"\"{selected}\""));
                    selectedIndex = 0;
                }
            }

            foreach (var port in ports)
            {
                var label = port;
                if (label.StartsWith("cu.") || label.StartsWith("tty."))
                    label = label.Substring(label.IndexOf('.') + 1);
                ListItems.Add(new GH_ValueListItem(label, $"\"{port}\""));
            }

        if (ListItems.Count > 0)
            ListItems[selectedIndex].Selected = true;
        }

        // Fallback for Unix-like systems (macOS, Linux) when native library is unavailable
        private static string[] GetUnixPortNames()
        {
            var list = new List<string>();
            try
            {
                var filesCu = Directory.GetFiles("/dev", "cu.*");
                list.AddRange(filesCu.Select(Path.GetFileName));
            }
            catch { }
            if (list.Count > 0)
                return list.ToArray();
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