using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Runtime.InteropServices;
using ClickRecorder.Data.Entities;
using ClickRecorder.Models;
using ClickRecorder.Services;
using ClickRecorder.Views;

namespace ClickRecorder.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private readonly GlobalMouseHook _hook = new();
    private readonly FlaUIInspectorService _inspector = new();
    private readonly FlaUIPlaybackService _playback = new();
    private readonly DatabaseService _db = new();
    private readonly TestCaseService _tcSvc = new();
    private readonly JobSchedulerService _scheduler;

    private readonly List<ClickAction> _recorded = new();
    private readonly List<StepResult> _stepResults = new();
    private readonly List<ExceptionDetail> _globalExData = new();
    private TestCaseEditorWindow? _openEditor;
    private int? _currentSequenceId;
    private TestSession? _lastSession;
    private bool _isRecording;
    private bool _isAttachArmed;
    private IntPtr? _attachedWindowHandle;
    private uint? _attachedProcessId;
    private string? _attachedProcessName;
    private DateTime? _lastClickTime;
    private int _clickId;

    public ObservableCollection<string> Clicks { get; } = new();
    public ObservableCollection<string> Results { get; } = new();
    public ObservableCollection<string> GlobalExceptions { get; } = new();

    private string _statusText = "⏸  Idle";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private Brush _statusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6C7086"));
    public Brush StatusColor { get => _statusColor; set => SetProperty(ref _statusColor, value); }

    private string _footer = string.Empty;
    public string FooterText { get => _footer; set => SetProperty(ref _footer, value); }

    private string _recordCount = "0";
    public string RecordCount { get => _recordCount; set => SetProperty(ref _recordCount, value); }

    private string _attachButtonText = "🎯 Připojit aplikaci";
    public string AttachButtonText { get => _attachButtonText; set => SetProperty(ref _attachButtonText, value); }

    private string _attachedAppText = "Nejprve připoj cílovou aplikaci";
    public string AttachedAppText { get => _attachedAppText; set => SetProperty(ref _attachedAppText, value); }

    public bool CanRecord => true;

    public string RepeatText { get; set; } = "1";
    public string SpeedText { get; set; } = "1.0";
    public bool StopOnError { get; set; }
    public bool TakeScreenshots { get; set; }
    public string SequenceName { get; set; } = "Moje sekvence";
    public string TextToType { get; set; } = string.Empty;
    public string FlaUiStatus { get; set; } = "⚙ FlaUI: Připraveno";

    private string _okText = "✓  0";
    public string OkText { get => _okText; set => SetProperty(ref _okText, value); }
    private string _errText = "✗  0";
    public string ErrText { get => _errText; set => SetProperty(ref _errText, value); }
    private string _flaUiText = "⚙  0";
    public string FlaUiText { get => _flaUiText; set => SetProperty(ref _flaUiText, value); }
    private string _coordText = "🖱  0";
    public string CoordText { get => _coordText; set => SetProperty(ref _coordText, value); }
    private string _durationText = "";
    public string DurationText { get => _durationText; set => SetProperty(ref _durationText, value); }

    public MainWindowViewModel()
    {
        _hook.MouseClicked += OnMouseClicked;
        _playback.StepCompleted += OnStepCompleted;
        _playback.PlaybackFinished += OnPlaybackFinished;
        _scheduler = new JobSchedulerService(_db, _playback);
        _scheduler.JobFinished += (_, args) => FooterText = $"⏰ Job dokončen: {args.Job.Name} – {args.Message}";
        FooterText = "FlaUI inicializováno. Nahrávání je připravené pro kliknutí kdekoliv.";
    }

    public bool CanPlay => _recorded.Count > 0;

    public void StartRecord()
    {
        if (_isRecording) return;
        if (_isAttachArmed)
        {
            FooterText = "Nejdřív dokonči výběr cílové aplikace kliknutím mimo ClickRecorder.";
            return;
        }
        _isRecording = true;
        _hook.Start();
        SetStatus("🔴  Nahrávám", "#F38BA8");
        FooterText = _attachedProcessId.HasValue
            ? $"Nahrávám… Připojená aplikace: {_attachedProcessName ?? _attachedProcessId.Value.ToString()} (PID {_attachedProcessId})."
            : "Nahrávám… Klikej kdekoliv – FlaUI inspektuje každý element.";
    }

    public void StopRecord()
    {
        if (!_isRecording) return;
        _isRecording = false;
        _hook.Stop();
        SetStatus("⏸  Idle", "#6C7086");
    }

    public void ArmAttachToApplication()
    {
        if (_isAttachArmed)
        {
            _isAttachArmed = false;
            AttachButtonText = "🎯 Připojit aplikaci";
            FooterText = "Výběr cílové aplikace zrušen.";
            if (!_isRecording)
            {
                _hook.Stop();
            }
            return;
        }

        _hook.Start();
        _isAttachArmed = true;
        AttachButtonText = "❌ Zrušit výběr";
        FooterText = "Klikni do cílové aplikace – další klik nastaví omezení nahrávání.";
    }

    public void ClearAttachedApplication()
    {
        _attachedWindowHandle = null;
        _attachedProcessId = null;
        _attachedProcessName = null;
        OnPropertyChanged(nameof(CanRecord));
        AttachedAppText = "Nejprve připoj cílovou aplikaci";
        FooterText = "Omezení cílové aplikace zrušeno.";
    }

    public async Task PlayAsync()
    {
        if (_recorded.Count == 0) return;
        if (!int.TryParse(RepeatText, out int rep) || rep < 1) rep = 1;
        if (!double.TryParse(SpeedText.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double spd) || spd <= 0) spd = 1.0;

        Results.Clear();
        _stepResults.Clear();
        UpdateStats(0, 0, 0, 0);
        SetStatus("▶  Přehrávám", "#A6E3A1");
        FooterText = $"Přehrávám {_recorded.Count} kroků × {rep}× ...";

        _lastSession = await _playback.PlayAsync(new List<ClickAction>(_recorded), rep, spd, StopOnError, TakeScreenshots);
    }

    public void StopPlay() => _playback.Stop();

    public void AddTextStep()
    {
        if (string.IsNullOrWhiteSpace(TextToType))
        {
            FooterText = "Zadej text, který chceš vyplnit.";
            return;
        }

        var ts = DateTime.Now;
        var delay = _lastClickTime.HasValue ? ts - _lastClickTime.Value : TimeSpan.Zero;
        _lastClickTime = ts;

        var previous = _recorded.Count > 0 ? _recorded[^1] : null;
        int x = previous?.X ?? 0;
        int y = previous?.Y ?? 0;
        var element = previous?.Element;

        if (previous is null && GetCursorPos(out var pt))
        {
            x = pt.X;
            y = pt.Y;
            element = _inspector.InspectAt(x, y);
        }

        _clickId++;
        var action = new ClickAction
        {
            Id = _clickId,
            X = x,
            Y = y,
            Kind = ActionKind.TypeText,
            Button = ClickButton.Left,
            TextToType = TextToType,
            DelayAfterPrevious = delay,
            RecordedAt = ts,
            Element = element
        };

        _recorded.Add(action);
        Clicks.Add($"⌨ {action.Summary}");
        RecordCount = _recorded.Count.ToString();
        FooterText = "Textový krok přidán do sekvence.";
    }

    public void ClearRecording()
    {
        _recorded.Clear();
        Clicks.Clear();
        _clickId = 0;
        _lastClickTime = null;
        RecordCount = "0";
        FooterText = "Záznamy vymazány.";
    }

    public void SaveSequence()
    {
        if (_recorded.Count == 0) return;
        var name = string.IsNullOrWhiteSpace(SequenceName) ? $"Sekvence {DateTime.Now:dd.MM HH:mm}" : SequenceName.Trim();
        var seq = _db.SaveSequence(name, string.Empty, new List<ClickAction>(_recorded));
        _currentSequenceId = seq.Id;
        FooterText = $"💾 Sekvence '{name}' uložena (ID {seq.Id})";
    }

    public void OpenTestCases(Window owner)
    {
        var win = new TestCasesWindow(_tcSvc) { Owner = owner };
        win.LoadToRecorderRequested += (_, steps) => LoadSteps(steps, "📂 Načteno {0} kroků z Test Case");
        win.Show();
    }

    public void OpenJobs(Window owner)
    {
        var win = new JobsWindow(_db, _scheduler) { Owner = owner };
        win.SequenceLoadRequested += (_, steps) => LoadSteps(steps, "📂 Načteno {0} kroků ze sekvence");
        win.Show();
    }

    public void SaveAsTestCase(Window owner)
    {
        if (_recorded.Count == 0) return;
        string name = string.IsNullOrWhiteSpace(SequenceName) ? $"Test Case {DateTime.Now:dd.MM HH:mm}" : SequenceName.Trim();
        var editor = new TestCaseEditorWindow(_tcSvc, new List<ClickAction>(_recorded), name) { Owner = owner };
        editor.Saved += (_, _) => FooterText = "🧪 Test Case uložen";
        editor.Closed += (_, _) => { if (_openEditor == editor) _openEditor = null; };
        editor.Show();
        _openEditor = editor;
        FooterText = "🧪 Editor otevřen. Nahraj další kliknutí a klikni „➕ Přidat do Test Case“.";
    }

    public void AddToTestCase()
    {
        if (_recorded.Count == 0 || _openEditor is null || !_openEditor.IsVisible) return;
        string sectionName = string.IsNullOrWhiteSpace(SequenceName) ? $"Sekce {DateTime.Now:HH:mm}" : SequenceName.Trim();
        _openEditor.AddSection(sectionName, new List<ClickAction>(_recorded));
        _openEditor.Activate();
        FooterText = $"➕ {_recorded.Count} kliků přidáno jako sekce „{sectionName}“.";
    }

    public void ExportReport()
    {
        if (_lastSession is null) return;
        var path = ReportExporter.ExportHtml(_lastSession);
        FooterText = $"Report uložen: {path}";
    }

    public void NotifyGlobalException(ExceptionDetail detail)
    {
        _globalExData.Add(detail);
        var source = string.IsNullOrWhiteSpace(detail.Source) ? "Global" : detail.Source;
        GlobalExceptions.Add($"[{DateTime.Now:HH:mm:ss}] {source}: {detail.TypeName}: {detail.Message}");
    }

    private void LoadSteps(List<ClickAction> steps, string footerFmt)
    {
        _recorded.Clear();
        Clicks.Clear();
        _clickId = 0;
        _lastClickTime = null;
        foreach (var a in steps)
        {
            _clickId = Math.Max(_clickId, a.Id);
            var icon = a.Kind == ActionKind.TypeText ? "⌨ " : (a.UseElementPlayback ? "⚙ " : "🖱 ");
            Clicks.Add(icon + a.Summary);
            _recorded.Add(a);
        }
        RecordCount = _recorded.Count.ToString();
        FooterText = string.Format(footerFmt, _recorded.Count);
    }

    private void OnMouseClicked(object? sender, MouseHookEventArgs e)
    {
        if (_isAttachArmed)
        {
            _isAttachArmed = false;
            AttachButtonText = "🎯 Připojit aplikaci";
            _attachedWindowHandle = e.RootWindowHandle;
            _attachedProcessId = e.ProcessId;
            _attachedProcessName = ResolveProcessName(e.ProcessId);
            OnPropertyChanged(nameof(CanRecord));
            AttachedAppText = $"🎯 {_attachedProcessName ?? "Neznámý proces"} (PID {_attachedProcessId}, HWND 0x{e.RootWindowHandle.ToInt64():X})";
            FooterText = "Cílová aplikace nastavena (informativně). Nahrávání dál bere kliknutí kdekoliv.";
            if (!_isRecording)
            {
                _hook.Stop();
            }
            return;
        }

        var ts = e.Timestamp;
        var prevT = _lastClickTime;
        _lastClickTime = ts;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            var identity = _inspector.InspectAt(e.X, e.Y);
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (!_isRecording) return;
                _clickId++;
                var delay = prevT.HasValue ? ts - prevT.Value : TimeSpan.Zero;
                var action = new ClickAction
                {
                    Id = _clickId,
                    X = e.X,
                    Y = e.Y,
                    Button = e.Button switch
                    {
                        HookButton.Right => ClickButton.Right,
                        HookButton.Middle => ClickButton.Middle,
                        _ => ClickButton.Left
                    },
                    DelayAfterPrevious = delay,
                    RecordedAt = ts,
                    Element = identity
                };
                _recorded.Add(action);
                var icon = action.Kind == ActionKind.TypeText ? "⌨" : (action.UseElementPlayback ? "⚙" : "🖱");
                Clicks.Add($"{icon} {action.Summary}");
                RecordCount = _recorded.Count.ToString();
            });
        });
    }

    private static string? ResolveProcessName(uint processId)
    {
        try
        {
            if (processId == 0) return null;
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private void OnStepCompleted(object? sender, StepResult result)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _stepResults.Add(result);
            Results.Add(result.ToString());
            int ok = 0, err = 0, flaui = 0, coord = 0;
            foreach (var r in _stepResults)
            {
                if (r.Status == StepStatus.Success) ok++; else err++;
                if (r.Mode == PlaybackMode.FlaUI) flaui++; else coord++;
            }
            UpdateStats(ok, err, flaui, coord);
        });
    }

    private void OnPlaybackFinished(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_lastSession is not null)
            {
                _db.SaveSession(_lastSession, _currentSequenceId, trigger: "Manual");
                DurationText = $"⏱ {_lastSession.TotalDuration.TotalSeconds:F1}s";
                FooterText = $"Hotovo – ✓{_lastSession.SuccessCount} ✗{_lastSession.FailureCount}";
            }
            SetStatus("⏸  Idle", "#6C7086");
        });
    }

    private void SetStatus(string text, string hex)
    {
        StatusText = text;
        StatusColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    private void UpdateStats(int ok, int err, int flaui, int coord)
    {
        OkText = $"✓  {ok}";
        ErrText = $"✗  {err}";
        FlaUiText = $"⚙  {flaui}";
        CoordText = $"🖱  {coord}";
    }

    public void Dispose()
    {
        _scheduler.Stop();
        _scheduler.Dispose();
        _hook.Stop();
        _hook.Dispose();
        _playback.Stop();
        _playback.Dispose();
        _inspector.Dispose();
    }
}
