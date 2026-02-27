using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClickRecorder.Models;
using ClickRecorder.Data.Entities;
using ClickRecorder.Services;
using ClickRecorder.Views;

namespace ClickRecorder
{
    public partial class MainWindow : Window
    {
        // ── Services ──────────────────────────────────────────────────────────
        private readonly GlobalMouseHook        _hook      = new();
        private readonly FlaUIInspectorService  _inspector  = new();
        private readonly FlaUIPlaybackService   _playback   = new();
        private readonly DatabaseService        _db         = new();
        private readonly TestCaseService       _tcSvc      = new();
        private          JobSchedulerService?   _scheduler;
        private          int?                   _currentSequenceId;
        private          TestCaseEditorWindow?  _openEditor;   // currently open TC editor

        // ── Data ──────────────────────────────────────────────────────────────
        private readonly List<ClickAction>            _recorded    = new();
        private readonly ObservableCollection<string> _results     = new();
        private readonly ObservableCollection<string> _globalExUI  = new();
        private readonly List<StepResult>             _stepResults = new();
        private readonly List<ExceptionDetail>        _globalExData = new();

        private TestSession? _lastSession;
        private bool         _isRecording;
        private DateTime?    _lastClickTime;
        private int          _clickId;

        // ── Init ──────────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();

            ResultList.ItemsSource   = _results;
            GlobalExList.ItemsSource = _globalExUI;

            _hook.MouseClicked         += OnMouseClicked;
            _playback.StepCompleted    += OnStepCompleted;
            _playback.PlaybackFinished += OnPlaybackFinished;

            // Init DB & scheduler
            _scheduler = new JobSchedulerService(_db, _playback);
            _scheduler.JobFinished += (_, args) => Dispatcher.Invoke(() =>
                Footer($"⏰ Job dokončen: {args.Job.Name} – {args.Message}"));

            // Confirm FlaUI is ready
            TxtFlaUIStatus.Text = "⚙ FlaUI: Připraveno";
            Footer("FlaUI inicializováno. Klikni ⏺ Nahrát a začni klikat kdekoliv.");
        }

        // ── Recording ─────────────────────────────────────────────────────────

        private void BtnRecord_Click(object sender, RoutedEventArgs e)
        {
            if (_isRecording) return;
            _isRecording = true;
            _hook.Start();
            BtnRecord.IsEnabled     = false;
            BtnStopRecord.IsEnabled = true;
            BtnPlay.IsEnabled       = false;
            SetStatus("🔴  Nahrávám", "#F38BA8");
            Footer("Nahrávám… Klikej kdekoliv – FlaUI inspektuje každý element.");
        }

        private void BtnStopRecord_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRecording) return;
            _isRecording = false;
            _hook.Stop();
            BtnRecord.IsEnabled     = true;
            BtnStopRecord.IsEnabled = false;
            BtnPlay.IsEnabled           = _recorded.Count > 0;
            BtnSaveSequence.IsEnabled   = _recorded.Count > 0;
            BtnSaveAsTestCase.IsEnabled = _recorded.Count > 0;
            UpdateAddToTcButton();
            SetStatus("⏸  Idle", "#6C7086");
            int withEl = 0;
            foreach (var a in _recorded) if (a.UseElementPlayback) withEl++;
            Footer($"Nahráno {_recorded.Count} kliknutí. {withEl}× s FlaUI elementem, " +
                   $"{_recorded.Count - withEl}× pouze souřadnice.");
        }

        private void OnMouseClicked(object? sender, MouseHookEventArgs e)
        {
            // Inspect element synchronously on a background thread,
            // then marshal result to UI thread.
            var ts    = e.Timestamp;
            var prevT = _lastClickTime;
            _lastClickTime = ts;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                // FlaUI inspection (can take ~50-200ms)
                var identity = _inspector.InspectAt(e.X, e.Y);

                Dispatcher.Invoke(() =>
                {
                    if (!_isRecording) return;

                    _clickId++;
                    var delay = prevT.HasValue ? ts - prevT.Value : TimeSpan.Zero;

                    var action = new ClickAction
                    {
                        Id                 = _clickId,
                        X                  = e.X,
                        Y                  = e.Y,
                        Button             = e.Button switch {
                            HookButton.Right  => ClickButton.Right,
                            HookButton.Middle => ClickButton.Middle,
                            _                 => ClickButton.Left
                        },
                        DelayAfterPrevious = delay,
                        RecordedAt         = ts,
                        Element            = identity
                    };

                    _recorded.Add(action);

                    string icon = action.UseElementPlayback ? "⚙" : "🖱";
                    ClickList.Items.Add($"{icon} {action.Summary}");
                    ClickList.ScrollIntoView(ClickList.Items[^1]);
                    TxtRecordCount.Text = _recorded.Count.ToString();

                    string elInfo = identity is not null
                        ? $" → {identity.Selector}"
                        : " (no element)";
                    Footer($"#{_clickId}  [{e.Button}] ({e.X},{e.Y}){elInfo}");
                });
            });
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _recorded.Clear();
            ClickList.Items.Clear();
            _clickId       = 0;
            _lastClickTime = null;
            TxtRecordCount.Text = "0";
            BtnPlay.IsEnabled   = false;
            ClearInspector();
            Footer("Záznamy vymazány.");
        }

        // ── Playback ──────────────────────────────────────────────────────────

        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            if (_recorded.Count == 0) { Info("Žádná kliknutí k přehrání."); return; }

            if (!int.TryParse(TxtRepeat.Text, out int rep) || rep < 1) rep = 1;
            if (!double.TryParse(TxtSpeed.Text.Replace(",", "."),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double spd) || spd <= 0) spd = 1.0;

            _results.Clear();
            _stepResults.Clear();
            ClearExPane();
            BtnPlay.IsEnabled     = false;
            BtnRecord.IsEnabled   = false;
            BtnStopPlay.IsEnabled = true;
            BtnExport.IsEnabled   = false;
            UpdateStats(0, 0, 0, 0);
            SetStatus("▶  Přehrávám", "#A6E3A1");
            Footer($"Přehrávám {_recorded.Count} kroků × {rep}× ...");

            _lastSession = await _playback.PlayAsync(
                new List<ClickAction>(_recorded),
                repeatCount:     rep,
                speedMult:       spd,
                stopOnError:     ChkStopOnError.IsChecked == true,
                takeScreenshots: ChkScreenshots.IsChecked == true);
        }

        private void BtnStopPlay_Click(object sender, RoutedEventArgs e) => _playback.Stop();

        private void OnStepCompleted(object? sender, StepResult result)
        {
            Dispatcher.Invoke(() =>
            {
                _stepResults.Add(result);
                _results.Add(result.ToString());
                ResultList.ScrollIntoView(_results[^1]);

                int ok = 0, err = 0, flaui = 0, coord = 0;
                foreach (var r in _stepResults)
                {
                    if (r.Status == StepStatus.Success) ok++; else err++;
                    if (r.Mode   == PlaybackMode.FlaUI) flaui++; else coord++;
                }
                UpdateStats(ok, err, flaui, coord);
                Footer($"Krok {result.StepId} {result.StatusIcon}  " +
                       $"✓{ok} ✗{err}  ⚙{flaui} 🖱{coord}");
            });
        }

        private void OnPlaybackFinished(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                BtnPlay.IsEnabled     = _recorded.Count > 0;
                BtnRecord.IsEnabled   = true;
                BtnStopPlay.IsEnabled = false;
                BtnExport.IsEnabled   = _lastSession is not null;
                // Auto-save to DB if we have a linked sequence
                if (_lastSession is not null)
                    _db.SaveSession(_lastSession, _currentSequenceId, trigger: "Manual");
                SetStatus("⏸  Idle", "#6C7086");

                if (_lastSession is not null)
                {
                    string c = _lastSession.WasCancelled ? " (zrušeno)" : "";
                    TxtDur.Text = $"⏱ {_lastSession.TotalDuration.TotalSeconds:F1}s";
                    Footer($"Hotovo{c} – ✓{_lastSession.SuccessCount} ✗{_lastSession.FailureCount} " +
                           $"⚙FlaUI:{_lastSession.FlaUISteps} 🖱Coord:{_lastSession.CoordSteps} " +
                           $"za {_lastSession.TotalDuration.TotalSeconds:F1}s");
                }
            });
        }

        // ── Inspector panel ───────────────────────────────────────────────────

        private void ClickList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = ClickList.SelectedIndex;
            if (idx < 0 || idx >= _recorded.Count) { ClearInspector(); return; }
            ShowInspector(_recorded[idx]);
        }

        private void ShowInspector(ClickAction action)
        {
            TxtInspHint.Visibility    = Visibility.Collapsed;
            InspectorGrid.Visibility  = Visibility.Visible;
            InspectorGrid.Children.Clear();
            InspectorGrid.RowDefinitions.Clear();

            var fields = new List<(string label, string? value, string color)>();

            if (action.Element is { } el)
            {
                fields.Add(("Mode",     "⚙ FlaUI Element",           "#89B4FA"));
                fields.Add(("Selector", el.Selector,                  "#CDD6F4"));
                fields.Add(("Type",     el.ControlType,               "#CDD6F4"));
                fields.Add(("AutomId",  el.AutomationId ?? "—",       el.AutomationId != null ? "#A6E3A1" : "#6C7086"));
                fields.Add(("Name",     el.Name ?? "—",               el.Name         != null ? "#CDD6F4" : "#6C7086"));
                fields.Add(("Class",    el.ClassName ?? "—",          "#CDD6F4"));
                fields.Add(("Window",   el.WindowTitle ?? "—",        "#CDD6F4"));
                fields.Add(("Process",  el.ProcessName ?? "—",        "#FAB387"));
                if (el.AncestorPath.Count > 0)
                    fields.Add(("Path", string.Join(" › ", el.AncestorPath), "#6C7086"));
            }
            else
            {
                fields.Add(("Mode",   "🖱 Souřadnice",  "#F9E2AF"));
                fields.Add(("X",      action.X.ToString(), "#CDD6F4"));
                fields.Add(("Y",      action.Y.ToString(), "#CDD6F4"));
                fields.Add(("Button", action.Button.ToString(), "#CDD6F4"));
            }
            fields.Add(("Delay", $"+{action.DelayAfterPrevious.TotalMilliseconds:F0}ms", "#6C7086"));

            foreach (var (label, value, color) in fields)
            {
                InspectorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                int row = InspectorGrid.RowDefinitions.Count - 1;

                var lbl = new TextBlock
                {
                    Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86)),
                    FontSize = 10, Margin = new Thickness(0, 0, 6, 4),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
                InspectorGrid.Children.Add(lbl);

                var val = new TextBlock
                {
                    Text = value ?? "", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                    FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetRow(val, row); Grid.SetColumn(val, 1);
                InspectorGrid.Children.Add(val);
            }
        }

        private void ClearInspector()
        {
            TxtInspHint.Visibility   = Visibility.Visible;
            InspectorGrid.Visibility = Visibility.Collapsed;
            InspectorGrid.Children.Clear();
            InspectorGrid.RowDefinitions.Clear();
        }

        // ── Exception pane ────────────────────────────────────────────────────

        private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = ResultList.SelectedIndex;
            if (idx < 0 || idx >= _stepResults.Count) return;
            var step = _stepResults[idx];
            ShowEx(step.Exception);
            BtnTicket.IsEnabled = step.Status == StepStatus.Failed;
        }

        private void BtnTicket_Click(object sender, RoutedEventArgs e)
        {
            int idx = ResultList.SelectedIndex;
            if (idx < 0 || idx >= _stepResults.Count) return;
            var step = _stepResults[idx];
            if (step.Status != StepStatus.Failed) return;

            var dlg = new TicketDialog(step, new System.Collections.Generic.List<ClickAction>(_recorded), _lastSession ?? new TestSession())
            {
                Owner = this
            };
            dlg.Show();
        }

        private void GlobalExList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = GlobalExList.SelectedIndex;
            if (idx < 0 || idx >= _globalExData.Count) return;
            ShowEx(_globalExData[idx]);
        }

        private void ShowEx(ExceptionDetail? ex)
        {
            if (ex is null) { ClearExPane(); return; }

            ExBox.Visibility     = Visibility.Visible;
            TxtExType.Text       = ex.Type;
            TxtExMessage.Text    = ex.Message;
            TxtExSource.Text     = string.IsNullOrEmpty(ex.Source) ? "" : $"Source: {ex.Source}";
            TxtExTime.Text       = $"Zachyceno: {ex.CapturedAt:HH:mm:ss.fff}";

            if (ex.InnerException is not null)
            {
                TxtExInner.Visibility = Visibility.Visible;
                TxtExInner.Text = $"Inner: {ex.InnerException.ShortType}: {ex.InnerException.Message}";
            }
            else TxtExInner.Visibility = Visibility.Collapsed;

            TxtStack.Text = ex.FullDisplay();
        }

        private void ClearExPane()
        {
            ExBox.Visibility = Visibility.Collapsed;
            TxtStack.Text    = "← Vyber krok ve výsledcích pro zobrazení call stacku";
        }

        // Called from App.xaml.cs
        public void NotifyGlobalException(ExceptionDetail ex)
        {
            _globalExData.Add(ex);
            _globalExUI.Add($"🔥 {ex.CapturedAt:HH:mm:ss}  {ex.ShortType}: {ex.Message}");
            _lastSession?.UnhandledExceptions.Add(ex);
        }

        // ── Export ────────────────────────────────────────────────────────────

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_lastSession is null) return;
            try
            {
                string path = ReportExporter.ExportHtml(_lastSession);
                Footer($"Report uložen: {path}");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { Info($"Export selhal: {ex.Message}"); }
        }

        private void BtnClearResults_Click(object sender, RoutedEventArgs e)
        {
            _results.Clear();
            _stepResults.Clear();
            UpdateStats(0, 0, 0, 0);
            ClearExPane();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetStatus(string text, string hex)
        {
            TxtStatus.Text       = text;
            TxtStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private void UpdateStats(int ok, int err, int flaui, int coord)
        {
            TxtOk.Text    = $"✓  {ok}";
            TxtErr.Text   = $"✗  {err}";
            TxtFlaUI.Text = $"⚙  {flaui}";
            TxtCoord.Text = $"🖱  {coord}";
        }

        private void Footer(string msg) => TxtFooter.Text = msg;
        private static void Info(string msg) =>
            MessageBox.Show(msg, "ClickRecorder", MessageBoxButton.OK, MessageBoxImage.Information);

        // ── Database & Jobs ──────────────────────────────────────────────────────

        private void BtnSaveSequence_Click(object sender, RoutedEventArgs e)
        {
            if (_recorded.Count == 0) { Info("Žádná kliknutí k uložení."); return; }

            string name = TxtSequenceName.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = $"Sekvence {DateTime.Now:dd.MM HH:mm}";

            var seq = _db.SaveSequence(name, string.Empty, new System.Collections.Generic.List<ClickAction>(_recorded));
            _currentSequenceId = seq.Id;
            Footer($"💾 Sekvence '{name}' uložena (ID {seq.Id})");
        }

        private void BtnOpenTestCases_Click(object sender, RoutedEventArgs e)
        {
            var win = new TestCasesWindow(_tcSvc) { Owner = this };
            win.LoadToRecorderRequested += (_, steps) =>
            {
                _recorded.Clear();
                ClickList.Items.Clear();
                _clickId = 0; _lastClickTime = null;
                foreach (var a in steps)
                {
                    _clickId = Math.Max(_clickId, a.Id);
                    ClickList.Items.Add((a.UseElementPlayback ? "⚙ " : "🖱 ") + a.Summary);
                    _recorded.Add(a);
                }
                TxtRecordCount.Text       = _recorded.Count.ToString();
                BtnPlay.IsEnabled         = _recorded.Count > 0;
                BtnSaveSequence.IsEnabled = _recorded.Count > 0;
                Footer($"📂 Načteno {_recorded.Count} kroků z Test Case");
            };
            win.Show();
        }

        private void BtnSaveAsTestCase_Click(object sender, RoutedEventArgs e)
        {
            if (_recorded.Count == 0) { Info("Žádná kliknutí k uložení."); return; }
            string name = TxtSequenceName.Text.Trim();
            if (string.IsNullOrEmpty(name)) name = $"Test Case {DateTime.Now:dd.MM HH:mm}";

            // Create editor with first section already filled
            var editor = new Views.TestCaseEditorWindow(_tcSvc,
                new System.Collections.Generic.List<ClickAction>(_recorded), name)
            { Owner = this };
            editor.Saved   += (_, _) => Footer("🧪 Test Case uložen");
            editor.Closed  += (_, _) => { if (_openEditor == editor) _openEditor = null; UpdateAddToTcButton(); };
            editor.Show();

            // Track it so further recordings can be pushed in
            _openEditor = editor;
            UpdateAddToTcButton();
            Footer("🧪 Editor otevřen. Nahraj další kliknutí a klikni „➕ Přidat do Test Case“.");
        }

        private void BtnAddToTestCase_Click(object sender, RoutedEventArgs e)
        {
            if (_recorded.Count == 0) { Info("Žádná kliknutí k nahrání."); return; }
            if (_openEditor is null || !_openEditor.IsVisible)
            {
                Info("Nejprve otevři Test Case editor pomocí „🧪 Uložit jako Test Case“.");
                return;
            }

            string sectionName = TxtSequenceName.Text.Trim();
            if (string.IsNullOrEmpty(sectionName))
                sectionName = $"Sekce {DateTime.Now:HH:mm}";

            _openEditor.AddSection(sectionName,
                new System.Collections.Generic.List<ClickAction>(_recorded));
            _openEditor.Activate();   // bring to front
            Footer($"➕ {_recorded.Count} kliků přidáno jako sekce „{sectionName}“.");
        }

        private void UpdateAddToTcButton()
        {
            bool editorOpen = _openEditor is not null && _openEditor.IsVisible;
            BtnAddToTestCase.IsEnabled = editorOpen && _recorded.Count > 0;
            BtnAddToTestCase.Content   = editorOpen ? "➕ Přidat do Test Case" : "➕ (editor zavřen)";
        }

        private void BtnOpenJobs_Click(object sender, RoutedEventArgs e)
        {
            var win = new JobsWindow(_db, _scheduler!)
            {
                Owner = this
            };
            win.SequenceLoadRequested += (_, steps) =>
            {
                _recorded.Clear();
                ClickList.Items.Clear();
                _clickId = 0;
                _lastClickTime = null;
                foreach (var a in steps)
                {
                    _clickId = Math.Max(_clickId, a.Id);
                    ClickList.Items.Add((a.UseElementPlayback ? "⚙ " : "🖱 ") + a.Summary);
                    _recorded.Add(a);
                }
                TxtRecordCount.Text = _recorded.Count.ToString();
                BtnPlay.IsEnabled = _recorded.Count > 0;
                BtnSaveSequence.IsEnabled = _recorded.Count > 0;
                Footer($"📂 Načteno {_recorded.Count} kroků ze sekvence");
            };
            win.Show();
        }

        protected override void OnClosed(EventArgs e)
        {
            _scheduler?.Stop();
            _scheduler?.Dispose();
            _hook.Stop();
            _hook.Dispose();
            _playback.Stop();
            _playback.Dispose();
            _inspector.Dispose();
            base.OnClosed(e);
        }
    }
}
