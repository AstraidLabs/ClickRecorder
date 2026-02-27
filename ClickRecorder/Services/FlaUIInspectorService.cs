using System;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using ClickRecorder.Models;

namespace ClickRecorder.Services
{
    /// <summary>
    /// Uses FlaUI / UI Automation 3 to inspect the element currently under
    /// the mouse cursor and return a rich <see cref="ElementIdentity"/>.
    /// </summary>
    public sealed class FlaUIInspectorService : IDisposable
    {
        private readonly UIA3Automation _automation = new();

        /// <summary>
        /// Inspect the element at absolute screen coordinates.
        /// Returns null if no element could be found.
        /// </summary>
        public ElementIdentity? InspectAt(int x, int y)
        {
            try
            {
                var element = _automation.FromPoint(new System.Drawing.Point(x, y));
                if (element is null) return null;

                var identity = new ElementIdentity
                {
                    AutomationId = NullIfEmpty(element.AutomationId),
                    Name         = NullIfEmpty(element.Name),
                    ControlType  = element.ControlType.ToString(),
                    ClassName    = NullIfEmpty(element.ClassName),
                    ProcessName  = GetProcessName(element),
                    WindowTitle  = GetWindowTitle(element),
                    AncestorPath = BuildAncestorPath(element)
                };

                return identity;
            }
            catch
            {
                return null;   // inspection is best-effort – never crash recorder
            }
        }

        private static string? NullIfEmpty(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string? GetProcessName(AutomationElement element)
        {
            try
            {
                int pid = element.Properties.ProcessId.Value;
                return System.Diagnostics.Process.GetProcessById(pid).ProcessName;
            }
            catch { return null; }
        }

        private static string? GetWindowTitle(AutomationElement element)
        {
            try
            {
                // Walk up to find the top-level Window
                var cur = element;
                while (cur != null)
                {
                    if (cur.ControlType == FlaUI.Core.Definitions.ControlType.Window)
                        return NullIfEmpty(cur.Name);
                    cur = cur.Parent;
                }
                return null;
            }
            catch { return null; }
        }

        private static List<string> BuildAncestorPath(AutomationElement element)
        {
            var path = new List<string>();
            try
            {
                var cur = element.Parent;
                int limit = 8;
                while (cur != null && limit-- > 0)
                {
                    string seg = cur.ControlType.ToString();
                    string? name = NullIfEmpty(cur.Name);
                    if (name != null) seg += $"['{name}']";
                    path.Insert(0, seg);
                    if (cur.ControlType == FlaUI.Core.Definitions.ControlType.Window) break;
                    cur = cur.Parent;
                }
            }
            catch { /* best-effort */ }
            return path;
        }

        public void Dispose() => _automation.Dispose();
    }
}
