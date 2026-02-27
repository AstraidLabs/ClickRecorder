using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ClickRecorder.Data.Entities
{
    // ── Saved click sequence (named, reusable) ────────────────────────────────

    public class DbSequence
    {
        public int      Id          { get; set; }
        [Required, MaxLength(200)]
        public string   Name        { get; set; } = string.Empty;
        public string   Description { get; set; } = string.Empty;
        public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt   { get; set; } = DateTime.UtcNow;

        // JSON-serialized List<ClickAction>
        public string   StepsJson   { get; set; } = "[]";

        public ICollection<DbSession>      Sessions { get; set; } = new List<DbSession>();
        public ICollection<DbScheduledJob> Jobs     { get; set; } = new List<DbScheduledJob>();
    }

    // ── Historical test session result ────────────────────────────────────────

    public class DbSession
    {
        public int      Id              { get; set; }
        public string   ExternalId      { get; set; } = string.Empty;  // TestSession.Id (8-char)
        public int?     SequenceId      { get; set; }
        public int?     JobId           { get; set; }
        public DateTime StartedAt       { get; set; }
        public DateTime? FinishedAt     { get; set; }
        public int      TotalSteps      { get; set; }
        public int      SuccessCount    { get; set; }
        public int      FailureCount    { get; set; }
        public int      RepeatCount     { get; set; }
        public double   SpeedMultiplier { get; set; }
        public bool     WasCancelled    { get; set; }
        public string   Trigger         { get; set; } = "Manual";  // "Manual" | "Job:<name>"

        // Full JSON dump of List<StepResult> for drill-down
        public string   ResultsJson     { get; set; } = "[]";

        public DbSequence?      Sequence { get; set; }
        public DbScheduledJob?  Job      { get; set; }
    }

    // ── Scheduled job ─────────────────────────────────────────────────────────

    public enum JobScheduleType { Once, Interval, Daily, Hourly }
    public enum JobStatus       { Active, Paused, Completed, Failed }

    public class DbScheduledJob
    {
        public int              Id           { get; set; }
        [Required, MaxLength(200)]
        public string           Name         { get; set; } = string.Empty;
        public int              SequenceId   { get; set; }
        public JobScheduleType  ScheduleType { get; set; }
        public JobStatus        Status       { get; set; } = JobStatus.Active;

        // When to run
        public DateTime?        RunAt        { get; set; }   // Once / Daily base time
        public int              IntervalMins { get; set; }   // Interval mode

        public int              RepeatCount  { get; set; } = 1;
        public double           SpeedMult    { get; set; } = 1.0;
        public bool             StopOnError  { get; set; }
        public bool             Screenshots  { get; set; }

        public DateTime         CreatedAt    { get; set; } = DateTime.UtcNow;
        public DateTime?        LastRunAt    { get; set; }
        public DateTime?        NextRunAt    { get; set; }
        public int              RunCount     { get; set; }
        public string           LastResult   { get; set; } = string.Empty;

        public DbSequence?             Sequence { get; set; }
        public ICollection<DbSession>  Sessions { get; set; } = new List<DbSession>();
    }
    // ── Test Case (rich JSON document) ───────────────────────────────────────

    public class DbTestCase
    {
        public int      Id          { get; set; }
        [Required, MaxLength(200)]
        public string   Title       { get; set; } = string.Empty;
        public string   Component   { get; set; } = string.Empty;
        public string   Tags        { get; set; } = string.Empty;   // comma-separated
        public int      Priority    { get; set; }                   // enum int
        public int      Status      { get; set; }                   // enum int
        public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt   { get; set; } = DateTime.UtcNow;
        public string   Author      { get; set; } = string.Empty;

        /// <summary>Full TestCase serialized as JSON (including Steps with ClickActions).</summary>
        public string   JsonData    { get; set; } = "{}";

        // Link to sequence if created from recorder
        public int?     SequenceId  { get; set; }
        public DbSequence? Sequence { get; set; }
    }
}
