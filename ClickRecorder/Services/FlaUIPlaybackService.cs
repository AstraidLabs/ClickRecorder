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

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy,
                                               uint dwData, int dwExtraInfo);

        private const uint MOUSEEVENTF_LEFTDOWN   = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP     = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN  = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP    = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP   = 0x0040;

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
                if (action.Kind == ActionKind.TypeText)
                {
                    if (action.UseElementPlayback)
                    {
                        result.Mode = PlaybackMode.FlaUI;
                        TypeViaFlaUI(action);
                    }
                    else
                    {
                        result.Mode = PlaybackMode.Coordinates;
                        TypeViaCoordinates(action);
                    }
                }
                else if (action.UseElementPlayback)
                {
                    result.Mode = PlaybackMode.FlaUI;
                    ClickViaFlaUI(action);
                }
                else
                {
                    result.Mode = PlaybackMode.Coordinates;
                    ClickViaWin32(action);
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

        private void TypeViaFlaUI(ClickAction action)
        {
            var id = action.Element!;
            var el = WaitForElement(id)
                     ?? throw new ElementNotFoundException(
                         $"UI element not found: {id.Selector} in window '{id.WindowTitle ?? id.ProcessName}'");

            string text = action.TextToType ?? string.Empty;
            try { el.Focus(); } catch { }

            if (el.Patterns.Value.IsSupported)
            {
                el.Patterns.Value.Pattern.SetValue(text);
                return;
            }

            Keyboard.Type(text);
        }

        private void TypeViaCoordinates(ClickAction action)
        {
            if (!SetCursorPos(action.X, action.Y))
                throw new InvalidOperationException(
                    $"SetCursorPos({action.X},{action.Y}) failed. Win32 error: {Marshal.GetLastWin32Error()}");

            Thread.Sleep(30);
            mouse_event(MOUSEEVENTF_LEFTDOWN, action.X, action.Y, 0, 0);
            Thread.Sleep(30);
            mouse_event(MOUSEEVENTF_LEFTUP, action.X, action.Y, 0, 0);
            Thread.Sleep(40);

            Keyboard.Type(action.TextToType ?? string.Empty);
        }

        // ── FlaUI element click ───────────────────────────────────────────────
        private void ClickViaFlaUI(ClickAction action)
        {
            var id  = action.Element!;
            var el  = WaitForElement(id)
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

        private AutomationElement? FindElement(ElementIdentity id)
        {
            // Strategy 1: search by AutomationId (most reliable)
            if (!string.IsNullOrEmpty(id.AutomationId))
            {
                var found = FindByAutomationId(id.AutomationId, id.ProcessName);
                if (found is not null) return found;
            }

            // Strategy 2: search by Name + ControlType
            if (!string.IsNullOrEmpty(id.Name))
            {
                var found = FindByName(id.Name, id.ControlType, id.ProcessName);
                if (found is not null) return found;
            }

            // Strategy 3: search by ClassName
            if (!string.IsNullOrEmpty(id.ClassName))
            {
                var found = FindByClassName(id.ClassName, id.ProcessName);
                if (found is not null) return found;
            }

            return null;
        }

        private AutomationElement? WaitForElement(ElementIdentity id)
        {
            var timeoutAt = DateTime.UtcNow.AddMilliseconds(ElementSearchTimeoutMs);
            while (DateTime.UtcNow < timeoutAt)
            {
                var found = FindElement(id);
                if (found is not null)
                    return found;

                Thread.Sleep(ElementSearchPollMs);
            }

            return FindElement(id);
        }

        private AutomationElement? FindByAutomationId(string automationId, string? processName)
        {
            foreach (var root in GetSearchRoots(processName))
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

        private AutomationElement? FindByName(string name, string? controlType, string? processName)
        {
            foreach (var root in GetSearchRoots(processName))
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

        private AutomationElement? FindByClassName(string className, string? processName)
        {
            foreach (var root in GetSearchRoots(processName))
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

        private List<AutomationElement> GetSearchRoots(string? processName)
        {
            var roots = new List<AutomationElement>();
            try
            {
                if (!string.IsNullOrEmpty(processName))
                {
                    var procs = System.Diagnostics.Process.GetProcessesByName(processName);
                    foreach (var p in procs)
                    {
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            try
                            {
                                roots.Add(_automation.FromHandle(p.MainWindowHandle));
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            // Always include desktop as final fallback
            try { roots.Add(_automation.GetDesktop()); } catch { }
            return roots;
        }

        // ── Win32 fallback click ──────────────────────────────────────────────
        private void ClickViaWin32(ClickAction action)
        {
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
