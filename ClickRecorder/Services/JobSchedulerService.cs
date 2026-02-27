using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClickRecorder.Data.Entities;
using ClickRecorder.Models;

namespace ClickRecorder.Services
{
    /// <summary>
    /// Background timer that fires every 30 seconds, checks which jobs are due,
    /// and executes them using FlaUIPlaybackService.
    /// </summary>
    public class JobSchedulerService : IDisposable
    {
        private readonly DatabaseService       _db;
        private readonly FlaUIPlaybackService  _playback;
        private Timer?                         _timer;
        private bool                           _running;
        private readonly SemaphoreSlim         _lock = new(1, 1);

        public event EventHandler<JobRunEventArgs>? JobStarted;
        public event EventHandler<JobRunEventArgs>? JobFinished;
        public event EventHandler<string>?           Log;

        public bool IsRunning => _running;

        public JobSchedulerService(DatabaseService db, FlaUIPlaybackService playback)
        {
            _db       = db;
            _playback = playback;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            // Check every 30 seconds
            _timer = new Timer(_ => _ = CheckAndRunDueJobsAsync(),
                               null,
                               TimeSpan.FromSeconds(5),
                               TimeSpan.FromSeconds(30));
            Emit("⏰ Scheduler spuštěn (kontrola každých 30s)");
        }

        public void Stop()
        {
            _running = false;
            _timer?.Dispose();
            _timer = null;
            Emit("⏹ Scheduler zastaven");
        }

        private async Task CheckAndRunDueJobsAsync()
        {
            if (!await _lock.WaitAsync(0)) return;   // skip if already running a job
            try
            {
                var jobs = _db.GetActiveJobs();
                var now  = DateTime.UtcNow;
                var due  = jobs.Where(j => j.NextRunAt.HasValue && j.NextRunAt.Value <= now).ToList();

                foreach (var job in due)
                {
                    if (!_running) break;
                    await RunJobAsync(job);
                }
            }
            catch (Exception ex) { Emit($"🔥 Scheduler chyba: {ex.Message}"); }
            finally { _lock.Release(); }
        }

        private async Task RunJobAsync(DbScheduledJob job)
        {
            var steps = _db.LoadSequenceSteps(job.SequenceId);
            if (steps.Count == 0)
            {
                Emit($"⚠ Job '{job.Name}' – sekvence #{job.SequenceId} je prázdná, přeskakuji");
                _db.UpdateJobAfterRun(job.Id, false, "Prázdná sekvence");
                return;
            }

            Emit($"▶ Spouštím job '{job.Name}' (sekvence: {job.Sequence?.Name ?? "?"})");
            JobStarted?.Invoke(this, new JobRunEventArgs(job));

            TestSession session;
            try
            {
                session = await _playback.PlayAsync(
                    steps,
                    repeatCount:     job.RepeatCount,
                    speedMult:       job.SpeedMult,
                    stopOnError:     job.StopOnError,
                    takeScreenshots: job.Screenshots);
            }
            catch (Exception ex)
            {
                string msg = $"Výjimka při spuštění: {ex.Message}";
                Emit($"✗ Job '{job.Name}' SELHALO – {msg}");
                _db.UpdateJobAfterRun(job.Id, false, msg);
                JobFinished?.Invoke(this, new JobRunEventArgs(job, null, msg));
                return;
            }

            string summary = $"✓{session.SuccessCount} ✗{session.FailureCount} " +
                             $"za {session.TotalDuration.TotalSeconds:F1}s";

            // Persist result
            _db.SaveSession(session,
                sequenceId: job.SequenceId,
                jobId:      job.Id,
                trigger:    $"Job:{job.Id}");

            _db.UpdateJobAfterRun(job.Id, session.FailureCount == 0, summary);

            string icon = session.FailureCount == 0 ? "✓" : "✗";
            Emit($"{icon} Job '{job.Name}' dokončen – {summary}");
            JobFinished?.Invoke(this, new JobRunEventArgs(job, session, summary));
        }

        public async Task RunJobNowAsync(int jobId)
        {
            if (!await _lock.WaitAsync(0))
            {
                Emit("⚠ Jiný job právě běží, zkus to za chvíli");
                return;
            }
            try
            {
                var jobs = _db.GetAllJobs();
                var job  = jobs.FirstOrDefault(j => j.Id == jobId);
                if (job is not null) await RunJobAsync(job);
            }
            finally { _lock.Release(); }
        }

        private void Emit(string msg) =>
            Log?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {msg}");

        public void Dispose() { Stop(); _lock.Dispose(); }
    }

    public class JobRunEventArgs : EventArgs
    {
        public DbScheduledJob Job     { get; }
        public TestSession?   Session { get; }
        public string         Message { get; }

        public JobRunEventArgs(DbScheduledJob job, TestSession? session = null, string msg = "")
        {
            Job     = job;
            Session = session;
            Message = msg;
        }
    }
}
