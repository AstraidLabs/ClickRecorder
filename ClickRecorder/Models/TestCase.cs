using System;
using System.Collections.Generic;
using System.Linq;

namespace ClickRecorder.Models
{
    public enum TestCasePriority { Critical, High, Medium, Low }
    public enum TestCaseStatus   { Draft, Active, Deprecated }

    // ── Single step inside a section ─────────────────────────────────────────

    public class TestCaseStep
    {
        public int          Order          { get; set; }
        public string       Description    { get; set; } = string.Empty;
        public string       ExpectedResult { get; set; } = string.Empty;
        public ClickAction? Click          { get; set; }
    }

    // ── Named section (group of steps from one recording pass) ───────────────

    public class TestCaseSection
    {
        public int                  Order       { get; set; }
        public string               Name        { get; set; } = string.Empty;
        public string               Description { get; set; } = string.Empty;
        public List<TestCaseStep>   Steps       { get; set; } = new();

        public int TotalClicks => Steps.Count(s => s.Click is not null);

        /// <summary>Build a section from a recorded ClickAction list.</summary>
        public static TestCaseSection FromClickActions(
            string name, List<ClickAction> actions, int sectionOrder = 1)
        {
            var sec = new TestCaseSection { Name = name, Order = sectionOrder };
            for (int i = 0; i < actions.Count; i++)
            {
                var a    = actions[i];
                string desc = a.Element is not null
                    ? $"[{a.Button}] {a.Element.ControlType} {a.Element.Selector}" +
                      (a.Element.WindowTitle is { } w ? $" v '{w}'" : "")
                    : $"[{a.Button}] ({a.X},{a.Y})";

                sec.Steps.Add(new TestCaseStep
                {
                    Order          = i + 1,
                    Description    = desc,
                    ExpectedResult = "",
                    Click          = a
                });
            }
            return sec;
        }
    }

    // ── Test Case – root document ─────────────────────────────────────────────

    public class TestCase
    {
        // Identity
        public string           Id              { get; set; } = Guid.NewGuid().ToString("N")[..12].ToUpper();
        public string           Title           { get; set; } = string.Empty;
        public string           Description     { get; set; } = string.Empty;
        public string           Component       { get; set; } = string.Empty;
        public TestCasePriority Priority        { get; set; } = TestCasePriority.Medium;
        public TestCaseStatus   Status          { get; set; } = TestCaseStatus.Active;
        public List<string>     Tags            { get; set; } = new();
        public string           Version         { get; set; } = "1.0";
        public string           Author          { get; set; } = Environment.UserName;
        public DateTime         CreatedAt       { get; set; } = DateTime.UtcNow;
        public DateTime         UpdatedAt       { get; set; } = DateTime.UtcNow;

        // Content
        public string                Preconditions   { get; set; } = string.Empty;
        public List<TestCaseSection> Sections        { get; set; } = new();
        public string                ExpectedOutcome { get; set; } = string.Empty;
        public string                Notes           { get; set; } = string.Empty;

        // Derived helpers
        public int TotalSteps  => Sections.Sum(s => s.Steps.Count);
        public int TotalClicks => Sections.Sum(s => s.TotalClicks);

        public string PriorityIcon => Priority switch
        {
            TestCasePriority.Critical => "🔴",
            TestCasePriority.High     => "🟠",
            TestCasePriority.Medium   => "🟡",
            _                         => "🟢"
        };
        public string StatusIcon => Status switch
        {
            TestCaseStatus.Active     => "✅",
            TestCaseStatus.Draft      => "📝",
            TestCaseStatus.Deprecated => "🚫",
            _                         => "?"
        };

        // Flatten all clicks (for playback)
        public List<ClickAction> AllClicks() =>
            Sections.SelectMany(s => s.Steps)
                    .Where(s => s.Click is not null)
                    .OrderBy(s => s.Click!.RecordedAt)
                    .Select(s => s.Click!)
                    .ToList();

        // Build from single recording – creates one default section
        public static TestCase FromClickActions(string title, List<ClickAction> actions)
        {
            var tc = new TestCase { Title = title };
            tc.Sections.Add(TestCaseSection.FromClickActions("Kroky", actions, 1));
            return tc;
        }

        // Migrate old flat Steps into first section (backward compat)
        public void MigrateFromFlatSteps(List<TestCaseStep> steps)
        {
            if (Sections.Count == 0 && steps.Count > 0)
                Sections.Add(new TestCaseSection { Order = 1, Name = "Kroky", Steps = steps });
        }
    }
}
