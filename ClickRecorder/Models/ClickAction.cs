using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClickRecorder.Models
{
    // ─── UI Element identity (captured via FlaUI) ─────────────────────────────

    public class ElementIdentity
    {
        /// <summary>Owning process id if available.</summary>
        public uint? ProcessId       { get; set; }

        /// <summary>AutomationId assigned by the app developer – most stable.</summary>
        public string? AutomationId   { get; set; }

        /// <summary>Visible label / Name property.</summary>
        public string? Name           { get; set; }

        /// <summary>ControlType: Button, Edit, MenuItem, …</summary>
        public string? ControlType    { get; set; }

        /// <summary>Win32 class name, e.g. "Button", "Edit", "Chrome_WidgetWin_1".</summary>
        public string? ClassName      { get; set; }

        /// <summary>Process that owns the window.</summary>
        public string? ProcessName    { get; set; }

        /// <summary>Window title of the parent top-level window.</summary>
        public string? WindowTitle    { get; set; }

        /// <summary>Ancestor chain: Window > Pane > Group > Button etc.</summary>
        public List<string> AncestorPath { get; set; } = new();

        /// <summary>Human-readable selector summary.</summary>
        public string Selector =>
            !string.IsNullOrEmpty(AutomationId) ? $"[AutomationId='{AutomationId}']"
            : !string.IsNullOrEmpty(Name)       ? $"[Name='{Name}' Type={ControlType}]"
            : !string.IsNullOrEmpty(ClassName)  ? $"[Class='{ClassName}']"
            : "(unknown element)";

        public bool IsUsable =>
            !string.IsNullOrEmpty(AutomationId) ||
            !string.IsNullOrEmpty(Name)         ||
            !string.IsNullOrEmpty(ClassName);

        public override string ToString() =>
            $"{ControlType ?? "?"} {Selector} in '{WindowTitle ?? ProcessName}'";
    }

    // ─── Single recorded action ───────────────────────────────────────────────

    public enum ClickButton { Left, Right, Middle, Double }
    public enum ActionKind { Click, TypeText }

    public class ClickAction
    {
        public const string EnterToken = "{ENTER}";

        public int          Id                  { get; set; }
        public int          X                   { get; set; }
        public int          Y                   { get; set; }
        public ActionKind   Kind                { get; set; } = ActionKind.Click;
        public ClickButton  Button              { get; set; }
        public string?      TextToType          { get; set; }
        public TimeSpan     DelayAfterPrevious  { get; set; }
        public DateTime     RecordedAt          { get; set; }
        public uint?        TargetProcessId     { get; set; }

        /// <summary>FlaUI-inspected element info. Null if element inspection failed.</summary>
        public ElementIdentity? Element         { get; set; }

        /// <summary>
        /// True = prefer element-based playback (FlaUI), false = force coordinate playback.
        /// </summary>
        public bool PreferElementPlayback { get; set; } = true;

        /// <summary>When true playback uses FlaUI element search; otherwise falls back to coords.</summary>
        public bool UseElementPlayback =>
            PreferElementPlayback && Element?.IsUsable == true;

        public string Summary =>
            Kind == ActionKind.TypeText
                ? $"#{Id:D3}  [TEXT] '{(TextToType ?? string.Empty).Replace(EnterToken, "↵")}'  +{DelayAfterPrevious.TotalMilliseconds:F0}ms"
            : UseElementPlayback
                ? $"#{Id:D3}  {Element!.Selector,-38}  +{DelayAfterPrevious.TotalMilliseconds:F0}ms"
                : $"#{Id:D3}  [{Button}] ({X},{Y})                            +{DelayAfterPrevious.TotalMilliseconds:F0}ms";

        public override string ToString() => Summary;
    }

    // ─── Exception detail with inner chain ───────────────────────────────────

    public class ExceptionDetail
    {
        public string           Type           { get; set; } = string.Empty;
        public string           Message        { get; set; } = string.Empty;
        public string           StackTrace     { get; set; } = string.Empty;
        public string           Source         { get; set; } = string.Empty;
        public ExceptionDetail? InnerException { get; set; }
        public DateTime         CapturedAt     { get; set; }

        public static ExceptionDetail FromException(Exception ex) => new()
        {
            Type           = ex.GetType().FullName ?? ex.GetType().Name,
            Message        = ex.Message,
            StackTrace     = ex.StackTrace ?? "(no stack trace)",
            Source         = ex.Source ?? string.Empty,
            CapturedAt     = DateTime.Now,
            InnerException = ex.InnerException is not null
                             ? FromException(ex.InnerException) : null
        };

        public string ShortType => Type.Split('.')[^1];

        public object TypeName { get; internal set; }

        public string FullDisplay()
        {
            var sb    = new StringBuilder();
            var cur   = this;
            int depth = 0;
            while (cur is not null)
            {
                if (depth > 0) { sb.AppendLine(); sb.AppendLine($"──── Inner Exception (depth {depth}) ────"); }
                sb.AppendLine($"Type    : {cur.Type}");
                sb.AppendLine($"Message : {cur.Message}");
                if (!string.IsNullOrEmpty(cur.Source))
                    sb.AppendLine($"Source  : {cur.Source}");
                sb.AppendLine($"Captured: {cur.CapturedAt:HH:mm:ss.fff}");
                sb.AppendLine();
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(cur.StackTrace);
                cur = cur.InnerException;
                depth++;
            }
            return sb.ToString();
        }
    }

    // ─── Step result ──────────────────────────────────────────────────────────

    public enum StepStatus { Success, Failed, Skipped }
    public enum PlaybackMode { FlaUI, Coordinates }

    public class StepResult
    {
        public int          StepId        { get; set; }
        public int          RepeatIndex   { get; set; }
        public int          X             { get; set; }
        public int          Y             { get; set; }
        public string       Button        { get; set; } = string.Empty;
        public ActionKind   Kind          { get; set; }
        public string?      TextToType    { get; set; }
        public StepStatus   Status        { get; set; }
        public PlaybackMode Mode          { get; set; }
        public ExceptionDetail? Exception { get; set; }
        public TimeSpan     Duration      { get; set; }
        public DateTime     ExecutedAt    { get; set; }
        public string?      ScreenshotPath { get; set; }
        public ElementIdentity? Element   { get; set; }

        public string StatusIcon => Status switch
        {
            StepStatus.Success => "✓",
            StepStatus.Failed  => "✗",
            _                  => "—"
        };

        public string ModeIcon => Mode == PlaybackMode.FlaUI ? "⚙" : "🖱";

        public override string ToString()
        {
            string rep = RepeatIndex > 1 ? $" [R{RepeatIndex}]" : "";
            string who = Kind == ActionKind.TypeText
                ? $"TEXT '{(TextToType ?? string.Empty).Replace(EnterToken, "↵")}'"
                : Element is not null ? Element.Selector : $"({X},{Y})";
            string err = Exception is not null ? $"  → {Exception.ShortType}: {Exception.Message}" : "";
            return $"{StatusIcon} {ModeIcon}  #{StepId:D3}{rep}  {who,-40}  {Duration.TotalMilliseconds:F0}ms{err}";
        }
    }

    // ─── Test session ─────────────────────────────────────────────────────────

    public class TestSession
    {
        public string   Id              { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();
        public DateTime StartedAt       { get; set; }
        public DateTime? FinishedAt     { get; set; }
        public List<StepResult>      Steps               { get; set; } = new();
        public List<ExceptionDetail> UnhandledExceptions { get; set; } = new();
        public int    TotalActions      { get; set; }
        public int    RepeatCount       { get; set; }
        public double SpeedMultiplier   { get; set; }
        public bool   WasCancelled      { get; set; }

        public int SuccessCount => Steps.Count(s => s.Status == StepStatus.Success);
        public int FailureCount => Steps.Count(s => s.Status == StepStatus.Failed);
        public int FlaUISteps   => Steps.Count(s => s.Mode == PlaybackMode.FlaUI);
        public int CoordSteps   => Steps.Count(s => s.Mode == PlaybackMode.Coordinates);
        public TimeSpan TotalDuration =>
            FinishedAt.HasValue ? FinishedAt.Value - StartedAt : TimeSpan.Zero;
    }
}
