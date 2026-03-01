using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using D3Energy.UI.Automation.Data.Entities;
using D3Energy.UI.Automation.Models;
using D3Energy.UI.Automation.Services;

namespace D3Energy.UI.Automation.Views
{
    public partial class JobsWindow : Window
    {
        private readonly DatabaseService      _db;
        private readonly JobSchedulerService  _scheduler;
        private readonly ObservableCollection<string> _logItems = new();

        // Fired when user wants to load a sequence into the main recorder
        public event EventHandler<List<ClickAction>>? SequenceLoadRequested;

        private List<DbScheduledJob> _jobs     = new();
        private List<DbSequence>     _seqs     = new();
        private List<DbSession>      _history  = new();

        public JobsWindow(DatabaseService db, JobSchedulerService scheduler)
        {
            InitializeComponent();
            _db        = db;
            _scheduler = scheduler;

            SchedulerLog.ItemsSource = _logItems;
            _scheduler.Log         += (_, msg) => Dispatcher.Invoke(() =>
            {
                _logItems.Add(msg);
                SchedulerLog.ScrollIntoView(_logItems[^1]);
            });
            _scheduler.JobFinished += (_, args) => Dispatcher.Invoke(() =>
            {
                RefreshJobs();
                RefreshHistory();
            });

            UpdateSchedulerBadge();
            Refresh();
        }

        // ── Refresh all lists ─────────────────────────────────────────────────

        private void Refresh()
        {
            RefreshSequences();
            RefreshJobs();
            RefreshHistory();
        }

        private void RefreshSequences()
        {
            _seqs = _db.GetAllSequences();
            SequenceList.Items.Clear();
            foreach (var s in _seqs)
                SequenceList.Items.Add($"📂  {s.Name,-30}  {s.UpdatedAt.ToLocalTime():dd.MM.yy HH:mm}  " +
                                       $"[{CountSteps(s.StepsJson)} kroků]");

            // Refresh combo in job form
            CboJobSequence.ItemsSource = _seqs;
            if (_seqs.Count > 0) CboJobSequence.SelectedIndex = 0;
        }

        private void RefreshJobs()
        {
            _jobs = _db.GetAllJobs();
            JobList.Items.Clear();
            foreach (var j in _jobs)
            {
                string status = j.Status switch
                {
                    JobStatus.Active    => "▶",
                    JobStatus.Paused    => "⏸",
                    JobStatus.Completed => "✓",
                    JobStatus.Failed    => "✗",
                    _                  => "?"
                };
                string next = j.NextRunAt.HasValue
                    ? j.NextRunAt.Value.ToLocalTime().ToString("dd.MM HH:mm")
                    : "—";
                string seq = j.Sequence?.Name ?? $"#{j.SequenceId}";
                JobList.Items.Add($"{status}  {j.Name,-25}  {seq,-20}  " +
                                  $"Next:{next}  Runs:{j.RunCount}  {j.LastResult}");
            }
        }

        private void RefreshHistory()
        {
            _history = _db.GetSessionHistory(limit: 200);
            HistoryList.Items.Clear();
            foreach (var s in _history)
            {
                string icon = s.FailureCount == 0 ? "✓" : "✗";
                string dur  = s.FinishedAt.HasValue
                    ? $"{(s.FinishedAt.Value - s.StartedAt).TotalSeconds:F0}s"
                    : "?";
                HistoryList.Items.Add(
                    $"{icon}  {s.StartedAt.ToLocalTime():dd.MM.yy HH:mm:ss}  " +
                    $"✓{s.SuccessCount} ✗{s.FailureCount}  {dur}  [{s.Trigger}]  #{s.ExternalId}");
            }
        }

        private static int CountSteps(string json)
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<object>>(json);
                return list?.Count ?? 0;
            }
            catch { return 0; }
        }

        // ── Scheduler controls ────────────────────────────────────────────────

        private void BtnStartScheduler_Click(object sender, RoutedEventArgs e)
        {
            _scheduler.Start();
            BtnStartScheduler.IsEnabled = false;
            BtnStopScheduler.IsEnabled  = true;
            UpdateSchedulerBadge();
        }

        private void BtnStopScheduler_Click(object sender, RoutedEventArgs e)
        {
            _scheduler.Stop();
            BtnStartScheduler.IsEnabled = true;
            BtnStopScheduler.IsEnabled  = false;
            UpdateSchedulerBadge();
        }

        private void UpdateSchedulerBadge()
        {
            if (_scheduler.IsRunning)
            {
                TxtSchedulerStatus.Text = "⏰ Scheduler: Běží";
                SchedulerBadge.Background = System.Windows.Media.Brushes.Transparent;
            }
            else
            {
                TxtSchedulerStatus.Text = "⏸ Scheduler: Zastaven";
            }
        }

        // ── Job form ──────────────────────────────────────────────────────────

        private void CboScheduleType_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (PanelRunAt == null) return;
            bool isInterval = CboScheduleType.SelectedIndex == 1;
            bool isHourly   = CboScheduleType.SelectedIndex == 2;
            PanelRunAt.Visibility      = isInterval || isHourly ? Visibility.Collapsed : Visibility.Visible;
            PanelInterval.Visibility   = isInterval             ? Visibility.Visible   : Visibility.Collapsed;
        }

        private void BtnAddJob_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtJobName.Text))
            { Footer("❌ Zadej název jobu"); return; }

            if (CboJobSequence.SelectedItem is not DbSequence seq)
            { Footer("❌ Vyber sekvenci"); return; }

            if (!int.TryParse(TxtJobRepeat.Text, out int rep) || rep < 1) rep = 1;
            if (!double.TryParse(TxtJobSpeed.Text.Replace(",","."),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double spd) || spd <= 0) spd = 1.0;

            var schedIdx = CboScheduleType.SelectedIndex;
            var schedType = schedIdx switch
            {
                1 => JobScheduleType.Interval,
                2 => JobScheduleType.Hourly,
                3 => JobScheduleType.Daily,
                _ => JobScheduleType.Once
            };

            DateTime? runAt = null;
            if (schedType == JobScheduleType.Once || schedType == JobScheduleType.Daily)
            {
                if (!DateTime.TryParse(TxtRunAt.Text, out var dt))
                { Footer("❌ Neplatný datum/čas (formát: 2026-03-01 08:00)"); return; }
                runAt = dt.ToUniversalTime();
            }

            int intervalMins = 60;
            if (schedType == JobScheduleType.Interval)
            {
                if (!int.TryParse(TxtIntervalMins.Text, out intervalMins) || intervalMins < 1)
                { Footer("❌ Neplatný interval"); return; }
            }

            var job = new DbScheduledJob
            {
                Name          = TxtJobName.Text.Trim(),
                SequenceId    = seq.Id,
                ScheduleType  = schedType,
                Status        = JobStatus.Active,
                RunAt         = runAt,
                IntervalMins  = intervalMins,
                RepeatCount   = rep,
                SpeedMult     = spd,
                StopOnError   = ChkJobStopOnError.IsChecked == true,
                Screenshots   = ChkJobScreenshots.IsChecked == true
            };

            _db.SaveJob(job);
            TxtJobName.Clear();
            RefreshJobs();
            Footer($"✓ Job '{job.Name}' přidán. Next run: {job.NextRunAt?.ToLocalTime():dd.MM HH:mm}");
        }

        // ── Job list actions ──────────────────────────────────────────────────

        private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool sel = JobList.SelectedIndex >= 0;
            BtnRunNow.IsEnabled    = sel;
            BtnPauseJob.IsEnabled  = sel;
            BtnDeleteJob.IsEnabled = sel;
        }

        private async void BtnRunNow_Click(object sender, RoutedEventArgs e)
        {
            int idx = JobList.SelectedIndex;
            if (idx < 0 || idx >= _jobs.Count) return;
            Footer($"▶ Spouštím job '{_jobs[idx].Name}'…");
            await _scheduler.RunJobNowAsync(_jobs[idx].Id);
        }

        private void BtnPauseJob_Click(object sender, RoutedEventArgs e)
        {
            int idx = JobList.SelectedIndex;
            if (idx < 0 || idx >= _jobs.Count) return;
            var job = _jobs[idx];
            var newStatus = job.Status == JobStatus.Active ? JobStatus.Paused : JobStatus.Active;
            _db.SetJobStatus(job.Id, newStatus);
            RefreshJobs();
            Footer($"Job '{job.Name}' → {newStatus}");
        }

        private void BtnDeleteJob_Click(object sender, RoutedEventArgs e)
        {
            int idx = JobList.SelectedIndex;
            if (idx < 0 || idx >= _jobs.Count) return;
            var job = _jobs[idx];
            if (MessageBox.Show($"Smazat job '{job.Name}'?", "Potvrdit",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _db.DeleteJob(job.Id);
            RefreshJobs();
        }

        // ── Sequence tab ──────────────────────────────────────────────────────

        private void SequenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool sel = SequenceList.SelectedIndex >= 0;
            BtnLoadSequence.IsEnabled    = sel;
            BtnDeleteSequence.IsEnabled  = sel;
        }

        private void BtnLoadSequence_Click(object sender, RoutedEventArgs e)
        {
            int idx = SequenceList.SelectedIndex;
            if (idx < 0 || idx >= _seqs.Count) return;
            var steps = _db.LoadSequenceSteps(_seqs[idx].Id);
            SequenceLoadRequested?.Invoke(this, steps);
            Footer($"✓ Sekvence '{_seqs[idx].Name}' načtena do recorderu");
        }

        private void BtnDeleteSequence_Click(object sender, RoutedEventArgs e)
        {
            int idx = SequenceList.SelectedIndex;
            if (idx < 0 || idx >= _seqs.Count) return;
            var seq = _seqs[idx];
            if (MessageBox.Show($"Smazat sekvenci '{seq.Name}'?", "Potvrdit",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _db.DeleteSequence(seq.Id);
            RefreshSequences();
        }

        // ── History tab ───────────────────────────────────────────────────────

        private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = HistoryList.SelectedIndex;
            if (idx < 0 || idx >= _history.Count) { TxtSessionDetail.Text = "← Vyber session"; return; }
            var s = _history[idx];

            var sb = new StringBuilder();
            sb.AppendLine($"Session ID:  {s.ExternalId}");
            sb.AppendLine($"Spuštěno:    {s.StartedAt.ToLocalTime():dd.MM.yyyy HH:mm:ss}");
            if (s.FinishedAt.HasValue)
                sb.AppendLine($"Dokončeno:   {s.FinishedAt.Value.ToLocalTime():HH:mm:ss}  " +
                              $"({(s.FinishedAt.Value - s.StartedAt).TotalSeconds:F1}s)");
            sb.AppendLine($"Trigger:     {s.Trigger}");
            sb.AppendLine($"Kroků:       {s.TotalSteps}  (×{s.RepeatCount})");
            sb.AppendLine($"Výsledek:    ✓{s.SuccessCount}  ✗{s.FailureCount}");
            sb.AppendLine($"Rychlost:    {s.SpeedMultiplier}×");
            if (s.WasCancelled) sb.AppendLine("⚠ Zrušeno uživatelem");

            // Load step details
            var steps = _db.LoadSessionResults(s.Id);
            if (steps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("─── Kroky ───────────────────────────────");
                foreach (var r in steps)
                    sb.AppendLine(r.ToString());
            }

            TxtSessionDetail.Text = sb.ToString();
        }

        private void BtnRefreshHistory_Click(object sender, RoutedEventArgs e) => RefreshHistory();

        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => _logItems.Clear();

        private void Footer(string msg) => TxtFooter.Text = msg;
    }
}
