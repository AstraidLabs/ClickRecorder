using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ClickRecorder.Data;
using ClickRecorder.Data.Entities;
using ClickRecorder.Models;

namespace ClickRecorder.Services
{
    /// <summary>
    /// All DB operations – called from UI thread or background threads.
    /// Creates a fresh DbContext per operation to avoid threading issues.
    /// </summary>
    public class DatabaseService
    {
        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented   = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // ── Sequences ─────────────────────────────────────────────────────────

        public DbSequence SaveSequence(string name, string description,
                                       List<ClickAction> steps)
        {
            using var db = AppDbContext.Create();
            var existing = db.Sequences.FirstOrDefault(s => s.Name == name);
            if (existing is not null)
            {
                existing.StepsJson   = JsonSerializer.Serialize(steps, _json);
                existing.Description = description;
                existing.UpdatedAt   = DateTime.UtcNow;
                db.SaveChanges();
                return existing;
            }

            var seq = new DbSequence
            {
                Name        = name,
                Description = description,
                StepsJson   = JsonSerializer.Serialize(steps, _json),
                CreatedAt   = DateTime.UtcNow,
                UpdatedAt   = DateTime.UtcNow
            };
            db.Sequences.Add(seq);
            db.SaveChanges();
            return seq;
        }

        public List<DbSequence> GetAllSequences()
        {
            using var db = AppDbContext.Create();
            return db.Sequences.OrderByDescending(s => s.UpdatedAt).ToList();
        }

        public List<ClickAction> LoadSequenceSteps(int sequenceId)
        {
            using var db = AppDbContext.Create();
            var seq = db.Sequences.Find(sequenceId);
            if (seq is null) return new();
            return JsonSerializer.Deserialize<List<ClickAction>>(seq.StepsJson, _json) ?? new();
        }

        public void DeleteSequence(int id)
        {
            using var db = AppDbContext.Create();
            var s = db.Sequences.Find(id);
            if (s is not null) { db.Sequences.Remove(s); db.SaveChanges(); }
        }

        // ── Sessions ──────────────────────────────────────────────────────────

        public DbSession SaveSession(TestSession session,
                                     int? sequenceId = null,
                                     int? jobId      = null,
                                     string trigger  = "Manual")
        {
            using var db = AppDbContext.Create();
            var entity = new DbSession
            {
                ExternalId      = session.Id,
                SequenceId      = sequenceId,
                JobId           = jobId,
                StartedAt       = session.StartedAt,
                FinishedAt      = session.FinishedAt,
                TotalSteps      = session.TotalActions,
                SuccessCount    = session.SuccessCount,
                FailureCount    = session.FailureCount,
                RepeatCount     = session.RepeatCount,
                SpeedMultiplier = session.SpeedMultiplier,
                WasCancelled    = session.WasCancelled,
                Trigger         = trigger,
                ResultsJson     = JsonSerializer.Serialize(session.Steps, _json)
            };
            db.Sessions.Add(entity);
            db.SaveChanges();
            return entity;
        }

        public List<DbSession> GetSessionHistory(int? sequenceId = null, int limit = 100)
        {
            using var db = AppDbContext.Create();
            var q = db.Sessions.AsQueryable();
            if (sequenceId.HasValue) q = q.Where(s => s.SequenceId == sequenceId);
            return q.OrderByDescending(s => s.StartedAt).Take(limit).ToList();
        }

        public List<StepResult> LoadSessionResults(int sessionId)
        {
            using var db = AppDbContext.Create();
            var s = db.Sessions.Find(sessionId);
            if (s is null) return new();
            return JsonSerializer.Deserialize<List<StepResult>>(s.ResultsJson, _json) ?? new();
        }

        // ── Scheduled Jobs ────────────────────────────────────────────────────

        public DbScheduledJob SaveJob(DbScheduledJob job)
        {
            using var db = AppDbContext.Create();
            if (job.Id == 0)
            {
                job.CreatedAt = DateTime.UtcNow;
                job.NextRunAt = ComputeNextRun(job, DateTime.UtcNow);
                db.Jobs.Add(job);
            }
            else
            {
                db.Jobs.Update(job);
            }
            db.SaveChanges();
            return job;
        }

        public List<DbScheduledJob> GetActiveJobs()
        {
            using var db = AppDbContext.Create();
            return db.Jobs
                     .Include(j => j.Sequence)
                     .Where(j => j.Status == JobStatus.Active)
                     .OrderBy(j => j.NextRunAt)
                     .ToList();
        }

        public List<DbScheduledJob> GetAllJobs()
        {
            using var db = AppDbContext.Create();
            return db.Jobs
                     .Include(j => j.Sequence)
                     .OrderByDescending(j => j.CreatedAt)
                     .ToList();
        }

        public void UpdateJobAfterRun(int jobId, bool success,
                                       string resultSummary)
        {
            using var db = AppDbContext.Create();
            var job = db.Jobs.Find(jobId);
            if (job is null) return;

            job.LastRunAt  = DateTime.UtcNow;
            job.RunCount++;
            job.LastResult = resultSummary;

            if (job.ScheduleType == JobScheduleType.Once)
                job.Status = JobStatus.Completed;
            else
                job.NextRunAt = ComputeNextRun(job, DateTime.UtcNow);

            db.SaveChanges();
        }

        public void SetJobStatus(int jobId, JobStatus status)
        {
            using var db = AppDbContext.Create();
            var job = db.Jobs.Find(jobId);
            if (job is null) return;
            job.Status = status;
            if (status == JobStatus.Active)
                job.NextRunAt = ComputeNextRun(job, DateTime.UtcNow);
            db.SaveChanges();
        }

        public void DeleteJob(int id)
        {
            using var db = AppDbContext.Create();
            var j = db.Jobs.Find(id);
            if (j is not null) { db.Jobs.Remove(j); db.SaveChanges(); }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public static DateTime? ComputeNextRun(DbScheduledJob job, DateTime from)
        {
            return job.ScheduleType switch
            {
                JobScheduleType.Once     => job.RunAt,
                JobScheduleType.Interval => from.AddMinutes(job.IntervalMins),
                JobScheduleType.Hourly   => from.AddHours(1),
                JobScheduleType.Daily    => job.RunAt.HasValue
                    ? from.Date.AddDays(1).Add(job.RunAt.Value.TimeOfDay)
                    : from.AddDays(1),
                _ => null
            };
        }
    }
}
