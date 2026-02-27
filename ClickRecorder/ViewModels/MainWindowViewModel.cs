using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
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
    private readonly GlobalKeyboardHook _keyboardHook = new();
    private readonly FlaUIInspectorService _inspector = new();
    private readonly FlaUIPlaybackService _playback = new();
    private readonly ApplicationLauncherService _appLauncher = new();
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
    private bool _isPlaying;
    private bool _captureByElement = true;
    private readonly StringBuilder _typedBuffer = new();
    private DateTime? _lastTypedAt;

    public ObservableCollection<string> Clicks { get; } = new();
    public ObservableCollection<StepResult> Results { get; } = new();
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

    private string _attachedAppText = "Žádná aplikace není připojená.";
    public string AttachedAppText { get => _attachedAppText; set => SetProperty(ref _attachedAppText, value); }

    public bool CanRecord => _attachedProcessId.HasValue && !_isAttachArmed;

    public string RepeatText { get; set; } = "1";
    public string SpeedText { get; set; } = "1.0";
    public bool StopOnError { get; set; }
    public bool TakeScreenshots { get; set; }
    public string SequenceName { get; set; } = "Moje sekvence";
    public string SequenceDescription { get; set; } = string.Empty;
    public string TextToType { get; set; } = string.Empty;
    public string FlaUiStatus { get; set; } = "⚙ FlaUI: Připraveno";
    public string AppToLaunch { get; set; } = string.Empty;

    public bool CaptureByElement
    {
        get => _captureByElement;
        set
        {
            if (!SetProperty(ref _captureByElement, value)) return;
            if (value)
            {
                OnPropertyChanged(nameof(CaptureByCoordinates));
            }
        }
    }

    public bool CaptureByCoordinates
    {
        get => !CaptureByElement;
        set
        {
            if (value == CaptureByCoordinates) return;
            CaptureByElement = !value;
            OnPropertyChanged(nameof(CaptureByCoordinates));
        }
    }

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

    private int _selectedStepIndex = -1;
    private string _selectedStepName = string.Empty;
    private string _selectedStepDescription = string.Empty;

    public string SelectedStepName
    {
        get => _selectedStepName;
        set => SetProperty(ref _selectedStepName, value);
    }

    public string SelectedStepDescription
    {
        get => _selectedStepDescription;
        set => SetProperty(ref _selectedStepDescription, value);
    }

    public MainWindowViewModel()
    {
        _hook.MouseClicked += OnMouseClicked;
        _keyboardHook.KeyPressed += OnKeyPressed;
        _playback.StepCompleted += OnStepCompleted;
        _playback.PlaybackFinished += OnPlaybackFinished;
        _scheduler = new JobSchedulerService(_db, _playback);
        _scheduler.JobFinished += (_, args) => FooterText = $"⏰ Job dokončen: {args.Job.Name} – {args.Message}";
        FooterText = "FlaUI inicializováno. Nejprve připoj cílovou aplikaci, teprve potom můžeš nahrávat.";
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (!SetProperty(ref _isPlaying, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanPlay));
            OnPropertyChanged(nameof(CanStopPlayback));
        }
    }

    public bool CanPlay => _recorded.Count > 0 && !IsPlaying;
    public bool CanStopPlayback => IsPlaying;

    public void StartRecord()
    {
        if (_isRecording) return;
        if (_isAttachArmed)
        {
            FooterText = "Nejdřív dokonči výběr cílové aplikace kliknutím mimo ClickRecorder.";
            return;
        }
        if (!CanRecord)
        {
            FooterText = "Nejdřív připoj cílovou aplikaci přes tlačítko 🎯 Připojit aplikaci.";
            return;
        }
        _isRecording = true;
        _hook.Start();
        _keyboardHook.Start();
        SetStatus("🔴  Nahrávám", "#F38BA8");
        FooterText = _attachedProcessId.HasValue
            ? $"Nahrávám… Připojená aplikace: {_attachedProcessName ?? _attachedProcessId.Value.ToString()} (PID {_attachedProcessId})."
            : CaptureByElement
                ? "Nahrávám… Režim: UI element (FlaUI)."
                : "Nahrávám… Režim: souřadnice myši.";
    }

    public void StopRecord()
    {
        if (!_isRecording) return;
        _isRecording = false;
        FlushTypingBuffer(DateTime.Now);
        _hook.Stop();
        _keyboardHook.Stop();
        SetStatus("⏸  Idle", "#6C7086");
    }

    public void ArmAttachToApplication()
    {
        if (_isAttachArmed)
        {
            _isAttachArmed = false;
            AttachButtonText = "🎯 Připojit aplikaci";
            OnPropertyChanged(nameof(CanRecord));
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
        OnPropertyChanged(nameof(CanRecord));
        FooterText = "Klikni kdekoliv do cílové aplikace a připojí se.";
    }

    public void ClearAttachedApplication()
    {
        _attachedWindowHandle = null;
        _attachedProcessId = null;
        _attachedProcessName = null;
        OnPropertyChanged(nameof(CanRecord));
        AttachedAppText = "Žádná aplikace není připojená.";
        FooterText = "Omezení cílové aplikace zrušeno.";
    }

    public async Task PlayAsync()
    {
        if (!CanPlay) return;
        if (!int.TryParse(RepeatText, out int rep) || rep < 1) rep = 1;
        if (!double.TryParse(SpeedText.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out double spd) || spd <= 0) spd = 1.0;

        Results.Clear();
        _stepResults.Clear();
        UpdateStats(0, 0, 0, 0);
        SetStatus("▶  Přehrávám", "#A6E3A1");
        FooterText = $"Přehrávám {_recorded.Count} kroků × {rep}× ...";

        IsPlaying = true;
        try
        {
            _lastSession = await _playback.PlayAsync(new List<ClickAction>(_recorded), rep, spd, StopOnError, TakeScreenshots);
        }
        finally
        {
            IsPlaying = false;
        }
    }

    public void StopPlay() => _playback.Stop();

    public void LaunchApplication()
    {
        var result = _appLauncher.Launch(AppToLaunch);
        FooterText = result.Success ? $"🚀 {result.Message}" : $"⚠ {result.Message}";
    }

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
        uint? targetProcessId = previous?.TargetProcessId ?? _attachedProcessId;

        if (previous is null && GetCursorPos(out var pt))
        {
            x = pt.X;
            y = pt.Y;
            element = _inspector.InspectAt(x, y);
            if (_attachedProcessId.HasValue && element?.ProcessId != _attachedProcessId)
            {
                element = null;
            }
        }

        _clickId++;
        var action = new ClickAction
        {
            Id = _clickId,
            StepName = $"Krok {_clickId}",
            X = x,
            Y = y,
            Kind = ActionKind.TypeText,
            Button = ClickButton.Left,
            TextToType = TextToType,
            DelayAfterPrevious = delay,
            RecordedAt = ts,
            Element = element,
            TargetProcessId = targetProcessId,
            PreferElementPlayback = CaptureByElement
        };

        _recorded.Add(action);
        RefreshRecordedList();
        RecordCount = _recorded.Count.ToString();
        OnPropertyChanged(nameof(CanPlay));
        FooterText = "Textový krok přidán do sekvence.";
    }

    public void ClearRecording()
    {
        _recorded.Clear();
        Clicks.Clear();
        _typedBuffer.Clear();
        _lastTypedAt = null;
        _clickId = 0;
        _lastClickTime = null;
        _selectedStepIndex = -1;
        SelectedStepName = string.Empty;
        SelectedStepDescription = string.Empty;
        RecordCount = "0";
        OnPropertyChanged(nameof(CanPlay));
        FooterText = "Záznamy vymazány.";
    }

    public void SaveSequence()
    {
        if (_recorded.Count == 0) return;
        var name = string.IsNullOrWhiteSpace(SequenceName) ? $"Sekvence {DateTime.Now:dd.MM HH:mm}" : SequenceName.Trim();
        var description = SequenceDescription?.Trim() ?? string.Empty;
        var seq = _db.SaveSequence(name, description, new List<ClickAction>(_recorded));
        _currentSequenceId = seq.Id;
        FooterText = $"💾 Sekvence '{name}' uložena (ID {seq.Id})";
    }

    public void SelectStep(int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= _recorded.Count)
        {
            _selectedStepIndex = -1;
            SelectedStepName = string.Empty;
            SelectedStepDescription = string.Empty;
            return;
        }

        _selectedStepIndex = selectedIndex;
        var step = _recorded[selectedIndex];
        SelectedStepName = step.StepName;
        SelectedStepDescription = step.StepDescription;
    }

    public void ApplyStepDetails()
    {
        if (_selectedStepIndex < 0 || _selectedStepIndex >= _recorded.Count)
        {
            return;
        }

        var step = _recorded[_selectedStepIndex];
        step.StepName = SelectedStepName?.Trim() ?? string.Empty;
        step.StepDescription = SelectedStepDescription?.Trim() ?? string.Empty;
        RefreshRecordedList();
        FooterText = $"✏️ Upraven {_selectedStepIndex + 1}. krok v sekvenci.";
    }

    public int MoveSelectedStep(int direction)
    {
        if (_selectedStepIndex < 0 || _selectedStepIndex >= _recorded.Count)
        {
            return -1;
        }

        var nextIndex = _selectedStepIndex + direction;
        if (nextIndex < 0 || nextIndex >= _recorded.Count)
        {
            return _selectedStepIndex;
        }

        (_recorded[_selectedStepIndex], _recorded[nextIndex]) = (_recorded[nextIndex], _recorded[_selectedStepIndex]);
        RefreshRecordedList();
        _selectedStepIndex = nextIndex;
        FooterText = $"↕️ Změněno pořadí kroku na pozici {_selectedStepIndex + 1}.";
        return _selectedStepIndex;
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
        _clickId = 0;
        _lastClickTime = null;
        foreach (var a in steps)
        {
            _clickId = Math.Max(_clickId, a.Id);
            _recorded.Add(a);
        }
        RefreshRecordedList();
        RecordCount = _recorded.Count.ToString();
        OnPropertyChanged(nameof(CanPlay));
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
            AttachedAppText = $"🎯 {_attachedProcessName ?? "Neznámý proces"} · PID {_attachedProcessId}";
            FooterText = "Cílová aplikace nastavena. Nahrávání i přehrávání bude omezené pouze na tento proces.";
            if (!_isRecording)
            {
                _hook.Stop();
            }
            return;
        }

        if (_attachedProcessId.HasValue && e.ProcessId != _attachedProcessId.Value)
        {
            return;
        }

        FlushTypingBuffer(e.Timestamp);

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
                    StepName = $"Krok {_clickId}",
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
                    Element = identity,
                    TargetProcessId = _attachedProcessId,
                    PreferElementPlayback = CaptureByElement
                };
                _recorded.Add(action);
                RefreshRecordedList();
                RecordCount = _recorded.Count.ToString();
                OnPropertyChanged(nameof(CanPlay));
            });
        });
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (!_isRecording || _isAttachArmed)
        {
            return;
        }

        if (_attachedProcessId.HasValue && e.ProcessId != _attachedProcessId.Value)
        {
            return;
        }

        if (e.IsBackspace)
        {
            if (_typedBuffer.Length > 0)
            {
                _typedBuffer.Length--;
                _lastTypedAt = e.Timestamp;
            }
            return;
        }

        if (e.IsEnter)
        {
            _typedBuffer.Append(Environment.NewLine);
            _lastTypedAt = e.Timestamp;
            return;
        }

        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        _typedBuffer.Append(e.Text);
        _lastTypedAt = e.Timestamp;
    }

    private void FlushTypingBuffer(DateTime timestamp)
    {
        if (_typedBuffer.Length == 0)
        {
            _lastTypedAt = null;
            return;
        }

        var previous = _recorded.Count > 0 ? _recorded[^1] : null;
        int x = previous?.X ?? 0;
        int y = previous?.Y ?? 0;
        var element = previous?.Element;
        uint? targetProcessId = previous?.TargetProcessId ?? _attachedProcessId;

        if (previous is null && GetCursorPos(out var pt))
        {
            x = pt.X;
            y = pt.Y;
            element = _inspector.InspectAt(x, y);
            if (_attachedProcessId.HasValue && element?.ProcessId != _attachedProcessId)
            {
                element = null;
            }
        }

        _clickId++;
        var text = _typedBuffer.ToString();
        var action = new ClickAction
        {
            Id = _clickId,
            StepName = $"Krok {_clickId}",
            X = x,
            Y = y,
            Kind = ActionKind.TypeText,
            Button = ClickButton.Left,
            TextToType = text,
            DelayAfterPrevious = _lastClickTime.HasValue ? timestamp - _lastClickTime.Value : TimeSpan.Zero,
            RecordedAt = _lastTypedAt ?? timestamp,
            Element = element,
            TargetProcessId = targetProcessId,
            PreferElementPlayback = CaptureByElement
        };

        _recorded.Add(action);
        RefreshRecordedList();
        _typedBuffer.Clear();
        _lastTypedAt = null;
        _lastClickTime = timestamp;
        RecordCount = _recorded.Count.ToString();
        OnPropertyChanged(nameof(CanPlay));
    }

    private void RefreshRecordedList()
    {
        Clicks.Clear();
        foreach (var action in _recorded)
        {
            var icon = action.Kind == ActionKind.TypeText ? "⌨" : (action.UseElementPlayback ? "⚙" : "🖱");
            Clicks.Add($"{icon} {action.Summary}");
        }
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
            Results.Add(result);
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
                int flauiSteps = _lastSession.Steps.Count(s => s.Mode == PlaybackMode.FlaUI);
                bool allSuccessful = _lastSession.FailureCount == 0 && _lastSession.Steps.Count > 0;

                FooterText = $"Hotovo – ✓{_lastSession.SuccessCount} ✗{_lastSession.FailureCount}";
                if (allSuccessful && flauiSteps == _lastSession.Steps.Count)
                {
                    FooterText += " (FlaUI režim běží bez viditelného pohybu myši)";
                }
            }
            IsPlaying = false;
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
        _keyboardHook.Stop();
        _keyboardHook.Dispose();
        _playback.Stop();
        _playback.Dispose();
        _inspector.Dispose();
    }
}
