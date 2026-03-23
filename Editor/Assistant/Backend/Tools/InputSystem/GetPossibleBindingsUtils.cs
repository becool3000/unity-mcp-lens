#if UNITY_AI_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Editor;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools.InputSystem
{
    static class GetPossibleBindingsUtils
    {
        internal const string k_FunctionName = "GetPossibleBindings";

        [Serializable]
        public class BindingEntry
        {
            [JsonProperty("path")]
            public string Path { get; set; }
            [JsonProperty("controlTypes")]
            public List<string> ControlTypes { get; set; } = new List<string>();
        }
        [Serializable]
        public class DeviceEntry
        {
            [JsonProperty("name")]
            public string Name { get; set; }
            [JsonProperty("bindings")]
            public List<BindingEntry> Bindings { get; set; } = new List<BindingEntry>();
        }

        public static List<DeviceEntry> BindingsByDevice()
        {
            var bindingList = new List<DeviceEntry>();
            var universalDevice = new DeviceEntry { Name = "Universal (Any)" };
            AddEntriesForControlUsages(universalDevice);
            bindingList.Add(universalDevice);

            foreach (var deviceLayout in EditorInputControlLayoutCache.allLayouts
                    .Where(x => x.isDeviceLayout && !x.isOverride && x.isGenericTypeOfDevice && !x.hideInUI)
                    .OrderBy(a => a.displayName))
            {
                AddDeviceTreeItems(bindingList, deviceLayout);
            }
            return bindingList;
        }

        static private void AddEntriesForControlUsages(DeviceEntry output, string device = "", string usage = "")
        {
            device = string.IsNullOrWhiteSpace(device) ? "*" : $"<{device}>";

            foreach (var usageAndLayouts in EditorInputControlLayoutCache.allUsages)
            {
                var entry = new BindingEntry();

                if (string.IsNullOrWhiteSpace(usage))
                {
                    entry.Path = $"\"{device}/{usageAndLayouts.Item1}\"";
                }
                else
                {
                    entry.Path = $"\"{device}{usage}/{usageAndLayouts.Item1}\"";
                }
                entry.ControlTypes.AddRange(usageAndLayouts.Item2);
                output.Bindings.Add(entry);
            }
        }
        static private void AddEntriesForControls(DeviceEntry output, InputControlLayout layout, string controlPathHeader = "")
        {
            if (string.IsNullOrWhiteSpace(controlPathHeader))
                controlPathHeader = $"<{layout.name}>";

            foreach (var control in layout.controls.OrderBy(a => a.name))
            {
                if (control.isModifyingExistingControl)
                    continue;

                AddControlEntryAndChildren(output, layout, control, controlPathHeader);
            }
        }

        static private void AddControlEntryAndChildren(DeviceEntry output, InputControlLayout layout, InputControlLayout.ControlItem control, string controlPathHeader)
        {
            for (var i = 0; i < (control.isArray ? control.arraySize : 1); ++i)
            {
                var name = control.isArray ? control.name + i : control.name;
                var controlName = $"{controlPathHeader}/{name}";
                var entry = new BindingEntry { Path = controlName };
                entry.ControlTypes.Add(control.layout);
                var controlLayout = EditorInputControlLayoutCache.TryGetLayout(control.layout);

                if (controlLayout != null)
                {
                    AddEntriesForControls(output, controlLayout, controlName);
                }
            }
        }

        static private void AddDeviceTreeItems(List<DeviceEntry> output, InputControlLayout layout)
        {
            // Find all layouts directly based on this one (ignoring overrides).
            var childLayouts = EditorInputControlLayoutCache.allLayouts
                .Where(x => x.isDeviceLayout && !x.isOverride && !x.hideInUI && x.baseLayouts.Contains(layout.name)).OrderBy(x => x.displayName);

            var deviceUsage = new DeviceEntry { Name = $"{layout.displayName} Usages" };

            // Add common usage variants (ie, lefthand, righthand) of the device
            if (layout.commonUsages.Count > 0)
            {
                foreach (var usage in layout.commonUsages)
                {
                    AddEntriesForControlUsages(deviceUsage, layout.name, usage);
                }
            }
            // The general version of the device
            AddEntriesForControlUsages(deviceUsage, layout.name);
            output.Add(deviceUsage);

            var deviceDirect = new DeviceEntry { Name = $"{layout.displayName} Direct" };
            output.Add(deviceDirect);
            AddEntriesForControls(deviceDirect, layout);
        }
    }
}
#endif
