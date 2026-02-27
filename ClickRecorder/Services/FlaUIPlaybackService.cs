using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;
using ClickRecorder.Models;

namespace ClickRecorder.Services
{
    /// <summary>
    /// Replays recorded actions using FlaUI UI Automation where possible,
    /// falling back to Win32 mouse_event when no element metadata is available.
    /// Captures full exception details (including call stack) for every failure.
    /// </summary>
    public sealed class FlaUIPlaybackService : IDisposable
    {
        // ── Win32 fallback ────────────────────────────────────────────────────
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }


        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy,
                                               uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;
        private const uint KEYEVENTF_KEYUP       = 0x0002;
        private const byte VK_RETURN             = 0x0D;
        private const uint GA_ROOT = 2;

        // ── Events ────────────────────────────────────────────────────────────
        public event EventHandler<StepResult>? StepCompleted;
        public event EventHandler?             PlaybackFinished;

        public bool IsPlaying { get; private set; }

        private readonly UIA3Automation    _automation = new();
        private CancellationTokenSource?  _cts;

        private const int ElementSearchTimeoutMs = 5000;
        private const int ElementSearchPollMs    = 200;

        // ── Public API ────────────────────────────────────────────────────────
        public async Task<TestSession> PlayAsync(
            List<ClickAction> actions,
            int    repeatCount      = 1,
            double speedMult        = 1.0,
            bool   stopOnError      = false,
            bool   takeScreenshots  = false)
        {
            if (IsPlaying) throw new InvalidOperationException("Already playing.");

            IsPlaying = true;
            _cts = new CancellationTokenSource();

            var session = new TestSession
            {
                StartedAt       = DateTime.Now,
                TotalActions    = actions.Count,
                RepeatCount     = repeatCount,
                SpeedMultiplier = speedMult
            };

            try
            {
                for (int r = 1; r <= repeatCount && !_cts.Token.IsCancellationRequested; r++)
                {
                    foreach (var action in actions)
                    {
                        if (_cts.Token.IsCancellationRequested) break;

                        // Honour recorded timing (scaled)
                        if (action.DelayAfterPrevious > TimeSpan.Zero)
                        {
                            int ms = (int)(action.DelayAfterPrevious.TotalMilliseconds / speedMult);
                            try { await Task.Delay(Math.Clamp(ms, 0, 30_000), _cts.Token); }
                            catch (OperationCanceledException) { break; }
                        }

                        var result = ExecuteStep(action, r);

                        if (takeScreenshots && result.Status == StepStatus.Failed)
                            result.ScreenshotPath = ScreenshotService.Capture(action.Id, r);

                        session.Steps.Add(result);
                        StepCompleted?.Invoke(this, result);

                        if (stopOnError && result.Status == StepStatus.Failed)
                            goto Finish;
                    }
                }

                Finish:;
            }
            finally
            {
                session.WasCancelled = _cts.IsCancellationRequested;
                session.FinishedAt   = DateTime.Now;
                IsPlaying            = false;
                PlaybackFinished?.Invoke(this, EventArgs.Empty);
            }

            return session;
        }

        public void Stop() => _cts?.Cancel();

        // ── Core step execution ───────────────────────────────────────────────
        private StepResult ExecuteStep(ClickAction action, int repeatIndex)
        {
            var result = new StepResult
            {
                StepId      = action.Id,
                RepeatIndex = repeatIndex,
                X           = action.X,
                Y           = action.Y,
                Kind        = action.Kind,
                Button      = action.Button.ToString(),
                TextToType  = action.TextToType,
                Element     = action.Element,
                ExecutedAt  = DateTime.Now
            };

            var sw = Stopwatch.StartNew();
            try
            {
                uint? effectiveTargetProcessId = ResolveEffectiveTargetProcessId(action);
                bool strictProcessScope = effectiveTargetProcessId.HasValue;

                if (strictProcessScope && !action.UseElementPlayback)
                {
                    throw new InvalidOperationException(
                        $"Step #{action.Id} is process-scoped (PID {action.TargetProcessId}) but has no usable UI element identity. " +
                        "Coordinate playback is blocked in strict mode to prevent clicks outside the attached process.");
                }

                if (action.Kind == ActionKind.TypeText)
                {
                    if (action.UseElementPlayback)
                    {
                        result.Mode = PlaybackMode.FlaUI;
                        TypeViaFlaUI(action, effectiveTargetProcessId);
                    }
                    else
                    {
                        result.Mode = PlaybackMode.Coordinates;
                        TypeViaCoordinates(action, effectiveTargetProcessId);
                    }
                }
                else if (action.UseElementPlayback)
                {
                    result.Mode = PlaybackMode.FlaUI;
                    ClickViaFlaUI(action, effectiveTargetProcessId);
                }
                else
                {
                    result.Mode = PlaybackMode.Coordinates;
                    ClickViaWin32(action, effectiveTargetProcessId);
                }
                result.Status = StepStatus.Success;
            }
            catch (Exception ex)
            {
                result.Status    = StepStatus.Failed;
                result.Exception = ExceptionDetail.FromException(ex);
            }
            finally
            {
                sw.Stop();
                result.Duration = sw.Elapsed;
            }

            return result;
        }

        private void TypeViaFlaUI(ClickAction action, uint? targetProcessId)
        {
            var id = action.Element!;
            var el = WaitForElement(id, targetProcessId)
                     ?? throw new ElementNotFoundException(
                         $"UI element not found: {id.Selector} in window '{id.WindowTitle ?? id.ProcessName}'");

            string text = action.TextToType ?? string.Empty;
            try { el.Focus(); } catch { }

            if (el.Patterns.Value.IsSupported && !ContainsEnterToken(text))
            {
                el.Patterns.Value.Pattern.SetValue(text);
                return;
            }

            TypeTextWithSpecialKeys(text);
        }

        private void TypeViaCoordinates(ClickAction action, uint? targetProcessId)
        {
            EnsureCoordinateTargetsProcess(action, targetProcessId);

            if (!SetCursorPos(action.X, action.Y))
                throw new InvalidOperationException(
                    $"SetCursorPos({action.X},{action.Y}) failed. Win32 error: {Marshal.GetLastWin32Error()}");

            Thread.Sleep(30);
            mouse_event(MOUSEEVENTF_LEFTDOWN, action.X, action.Y, 0, 0);
            Thread.Sleep(30);
            mouse_event(MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, 0);
            Thread.Sleep(40);

            TypeTextWithSpecialKeys(action.TextToType ?? string.Empty);
        }

        private static bool ContainsEnterToken(string text) =>
            text.Contains(ClickAction.EnterToken, StringComparison.Ordinal);

        private static void TypeTextWithSpecialKeys(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int start = 0;
            while (start < text.Length)
            {
                int idx = text.IndexOf(ClickAction.EnterToken, start, StringComparison.Ordinal);
                if (idx < 0)
                {
                    Keyboard.Type(text[start..]);
                    return;
                }

                if (idx > start)
                {
                    Keyboard.Type(text[start..idx]);
                }

                SendEnterKey();
                start = idx + ClickAction.EnterToken.Length;
            }
        }

        private static void SendEnterKey()
        {
            keybd_event(VK_RETURN, 0, 0, 0);
            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, 0);
        }

        // ── FlaUI element click ───────────────────────────────────────────────
        private void ClickViaFlaUI(ClickAction action, uint? targetProcessId)
        {
            var id  = action.Element!;
            var el  = WaitForElement(id, targetProcessId)
                      ?? throw new ElementNotFoundException(
                             $"UI element not found: {id.Selector} " +
                             $"in window '{id.WindowTitle ?? id.ProcessName}'");

            // Scroll into view if supported
            try { el.Focus(); } catch { /* best-effort */ }

            switch (action.Button)
            {
                case ClickButton.Left:
                    el.Click();
                    break;
                case ClickButton.Right:
                    el.RightClick();
                    break;
                case ClickButton.Double:
                    el.DoubleClick();
                    break;
                case ClickButton.Middle:
                    // FlaUI doesn't have a middle-click helper – fall back to Mouse API
                    var rect = el.BoundingRectangle;
                    var x = (int)(rect.Left + rect.Width / 2);
                    var y = (int)(rect.Top + rect.Height / 2);
                    Mouse.MoveTo(x, y);
                    Thread.Sleep(30);
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, x, y, 0, 0);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_MIDDLEUP, x, y, 0, 0);
                    break;
            }
        }

        private AutomationElement? FindElement(ElementIdentity id, uint? targetProcessId)
        {
            // Strategy 1: search by AutomationId (most reliable)
            if (!string.IsNullOrEmpty(id.AutomationId))
            {
                var found = FindByAutomationId(id.AutomationId, id.ProcessName, targetProcessId);
                if (found is not null) return found;
            }

            // Strategy 2: search by Name + ControlType
            if (!string.IsNullOrEmpty(id.Name))
            {
                var found = FindByName(id.Name, id.ControlType, id.ProcessName, targetProcessId);
                if (found is not null) return found;
            }

            // Strategy 3: search by ClassName
            if (!string.IsNullOrEmpty(id.ClassName))
            {
                var found = FindByClassName(id.ClassName, id.ProcessName, targetProcessId);
                if (found is not null) return found;
            }

            return null;
        }

        private AutomationElement? WaitForElement(ElementIdentity id, uint? targetProcessId)
        {
            var timeoutAt = DateTime.UtcNow.AddMilliseconds(ElementSearchTimeoutMs);
            while (DateTime.UtcNow < timeoutAt)
            {
                var found = FindElement(id, targetProcessId);
                if (found is not null)
                    return found;

                Thread.Sleep(ElementSearchPollMs);
            }

            return FindElement(id, targetProcessId);
        }

        private AutomationElement? FindByAutomationId(string automationId, string? processName, uint? targetProcessId)
        {
            foreach (var root in GetSearchRoots(processName, targetProcessId))
            {
                try
                {
                    var cf = _automation.ConditionFactory;
                    var cond = cf.ByAutomationId(automationId);
                    var el = root.FindFirst(TreeScope.Descendants, cond);
                    if (el is not null) return el;
                }
                catch { /* try next root */ }
            }
            return null;
        }

        private AutomationElement? FindByName(string name, string? controlType, string? processName, uint? targetProcessId)
        {
            foreach (var root in GetSearchRoots(processName, targetProcessId))
            {
                try
                {
                    var cf = _automation.ConditionFactory;
                    ConditionBase cond = cf.ByName(name);
                    if (!string.IsNullOrEmpty(controlType) &&
                        Enum.TryParse<ControlType>(controlType, out var ct))
                    {
                        cond = cond.And(cf.ByControlType(ct));
                    }

                    var el = root.FindFirst(TreeScope.Descendants, cond);
                    if (el is not null) return el;
                }
                catch { /* try next root */ }
            }
            return null;
        }

        private AutomationElement? FindByClassName(string className, string? processName, uint? targetProcessId)
        {
            foreach (var root in GetSearchRoots(processName, targetProcessId))
            {
                try
                {
                    var cf   = _automation.ConditionFactory;
                    var cond = cf.ByClassName(className);
                    var el   = root.FindFirst(TreeScope.Descendants, cond);
                    if (el is not null) return el;
                }
                catch { /* try next root */ }
            }
            return null;
        }

        private List<AutomationElement> GetSearchRoots(string? processName, uint? targetProcessId)
        {
            var roots = new List<AutomationElement>();
            try
            {
                IEnumerable<System.Diagnostics.Process> procs = Enumerable.Empty<System.Diagnostics.Process>();
                if (targetProcessId.HasValue)
                {
                    try
                    {
                        procs = new[] { System.Diagnostics.Process.GetProcessById((int)targetProcessId.Value) };
                    }
                    catch
                    {
                        procs = Enumerable.Empty<System.Diagnostics.Process>();
                    }
                }
                else if (!string.IsNullOrEmpty(processName))
                {
                    procs = System.Diagnostics.Process.GetProcessesByName(processName);
                }

                foreach (var p in procs)
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    try
                    {
                        roots.Add(_automation.FromHandle(p.MainWindowHandle));
                    }
                    catch { }
                }
            }
            catch { }

            // In strict process mode never fall back to desktop-wide search.
            if (!targetProcessId.HasValue)
            {
                try { roots.Add(_automation.GetDesktop()); } catch { }
            }

            return roots;
        }


        private uint? ResolveEffectiveTargetProcessId(ClickAction action)
        {
            if (!action.TargetProcessId.HasValue)
            {
                return null;
            }

            uint targetProcessId = action.TargetProcessId.Value;
            if (IsProcessAlive(targetProcessId))
            {
                return targetProcessId;
            }

            string? processName = action.Element?.ProcessName;
            if (string.IsNullOrWhiteSpace(processName))
            {
                return targetProcessId;
            }

            var replacement = TryResolveProcessIdByName(processName, action.Element?.WindowTitle);
            return replacement ?? targetProcessId;
        }

        private static bool IsProcessAlive(uint processId)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                return !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static uint? TryResolveProcessIdByName(string processName, string? windowTitle)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(processName)
                .Where(p => p.MainWindowHandle != IntPtr.Zero);

            if (!string.IsNullOrWhiteSpace(windowTitle))
            {
                var byTitle = processes.FirstOrDefault(p =>
                {
                    try
                    {
                        return string.Equals(p.MainWindowTitle, windowTitle, StringComparison.Ordinal);
                    }
                    catch
                    {
                        return false;
                    }
                });

                if (byTitle is not null)
                {
                    return (uint)byTitle.Id;
                }
            }

            var first = processes.OrderByDescending(p =>
            {
                try { return p.StartTime; }
                catch { return DateTime.MinValue; }
            }).FirstOrDefault();

            return first is not null ? (uint)first.Id : null;
        }

        private static void EnsureCoordinateTargetsProcess(ClickAction action, uint? targetProcessId)
        {
            if (!targetProcessId.HasValue)
            {
                return;
            }

            var pt = new POINT { X = action.X, Y = action.Y };
            var window = WindowFromPoint(pt);
            var root = window != IntPtr.Zero ? GetAncestor(window, GA_ROOT) : IntPtr.Zero;
            _ = GetWindowThreadProcessId(root, out uint processId);

            if (processId != targetProcessId.Value)
            {
                throw new InvalidOperationException(
                    $"Coordinate target mismatch for step #{action.Id}: expected PID {targetProcessId}, but point ({action.X},{action.Y}) resolves to PID {processId}.");
            }
        }

        // ── Win32 fallback click ──────────────────────────────────────────────
        private void ClickViaWin32(ClickAction action, uint? targetProcessId)
        {
            EnsureCoordinateTargetsProcess(action, targetProcessId);

            if (!SetCursorPos(action.X, action.Y))
                throw new InvalidOperationException(
                    $"SetCursorPos({action.X},{action.Y}) failed. " +
                    $"Win32 error: {Marshal.GetLastWin32Error()}");

            Thread.Sleep(30);

            switch (action.Button)
            {
                case ClickButton.Left:
                    mouse_event(MOUSEEVENTF_LEFTDOWN,   action.X, action.Y, 0, 0);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_LEFTUP,     action.X, action.Y, 0, 0);
                    break;
                case ClickButton.Right:
                    mouse_event(MOUSEEVENTF_RIGHTDOWN,  action.X, action.Y, 0, 0);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_RIGHTUP,    action.X, action.Y, 0, 0);
                    break;
                case ClickButton.Double:
                    mouse_event(MOUSEEVENTF_LEFTDOWN,   action.X, action.Y, 0, 0);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_LEFTUP,     action.X, action.Y, 0, 0);
                    Thread.Sleep(80);
                    mouse_event(MOUSEEVENTF_LEFTDOWN,   action.X, action.Y, 0, 0);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_LEFTUP,     action.X, action.Y, 0, 0);
                    break;
                case ClickButton.Middle:
                    mouse_event(MOUSEEVENTF_MIDDLEDOWN, action.X, action.Y, 0, 0);
                    Thread.Sleep(50);
                    mouse_event(MOUSEEVENTF_MIDDLEUP,   action.X, action.Y, 0, 0);
                    break;
            }
        }

        public void Dispose() => _automation.Dispose();
    }

    // ─── Custom exception ─────────────────────────────────────────────────────

    public class ElementNotFoundException : Exception
    {
        public ElementNotFoundException(string message) : base(message) { }
    }
}
