using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using D3Energy.UI.Automation.Data;
using D3Energy.UI.Automation.Data.Entities;
using D3Energy.UI.Automation.Models;

namespace D3Energy.UI.Automation.Services
{
    public class TestCaseService
    {
        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters             = { new JsonStringEnumConverter() }
        };

        // ── CRUD ──────────────────────────────────────────────────────────────

        public DbTestCase Save(TestCase tc, int? sequenceId = null)
        {
            tc.UpdatedAt = DateTime.UtcNow;
            using var db = AppDbContext.Create();

            // Try find by TC.Id embedded in JsonData
            var existing = db.TestCases
                .FirstOrDefault(x => x.JsonData.Contains($"\"Id\": \"{tc.Id}\"") ||
                                     x.JsonData.Contains($"\"Id\":\"{tc.Id}\""));

            if (existing is not null)
            {
                Map(existing, tc, sequenceId);
                db.SaveChanges();
                return existing;
            }

            var entity = new DbTestCase();
            Map(entity, tc, sequenceId);
            db.TestCases.Add(entity);
            db.SaveChanges();
            return entity;
        }

        public DbTestCase SaveById(int dbId, TestCase tc)
        {
            tc.UpdatedAt = DateTime.UtcNow;
            using var db = AppDbContext.Create();
            var entity   = db.TestCases.Find(dbId)
                ?? throw new InvalidOperationException($"TestCase #{dbId} not found");
            Map(entity, tc, entity.SequenceId);
            db.SaveChanges();
            return entity;
        }

        private static void Map(DbTestCase e, TestCase tc, int? seqId)
        {
            e.Title       = tc.Title;
            e.Component   = tc.Component;
            e.Tags        = string.Join(",", tc.Tags);
            e.Priority    = (int)tc.Priority;
            e.Status      = (int)tc.Status;
            e.UpdatedAt   = DateTime.UtcNow;
            e.Author      = tc.Author;
            e.SequenceId  = seqId;
            e.JsonData    = JsonSerializer.Serialize(tc, _json);
            if (e.CreatedAt == default) e.CreatedAt = tc.CreatedAt;
        }

        public TestCase? Load(int dbId)
        {
            using var db = AppDbContext.Create();
            var entity   = db.TestCases.Find(dbId);
            if (entity is null) return null;
            var tc = JsonSerializer.Deserialize<TestCase>(entity.JsonData, _json);

            // Backward compat: if old format had flat Steps, migrate
            if (tc is not null && tc.Sections.Count == 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(entity.JsonData);
                    if (doc.RootElement.TryGetProperty("Steps", out var stepsEl))
                    {
                        var steps = stepsEl.Deserialize<List<TestCaseStep>>(_json) ?? new();
                        tc.MigrateFromFlatSteps(steps);
                    }
                }
                catch { /* ignore */ }
            }
            return tc;
        }

        public List<DbTestCase> GetAll(string? filterTag = null, int? filterPriority = null)
        {
            using var db = AppDbContext.Create();
            var q = db.TestCases.AsQueryable();
            if (!string.IsNullOrEmpty(filterTag))
                q = q.Where(x => x.Tags.Contains(filterTag));
            if (filterPriority.HasValue)
                q = q.Where(x => x.Priority == filterPriority.Value);
            return q.OrderByDescending(x => x.UpdatedAt).ToList();
        }

        public void Delete(int dbId)
        {
            using var db = AppDbContext.Create();
            var e = db.TestCases.Find(dbId);
            if (e is not null) { db.TestCases.Remove(e); db.SaveChanges(); }
        }

        // ── JSON file import / export ─────────────────────────────────────────

        public string ExportToFile(int dbId)
        {
            var tc   = Load(dbId) ?? throw new InvalidOperationException($"TestCase #{dbId} not found");
            string dir  = GetExportDir();
            string safe = string.Concat(tc.Title.Split(Path.GetInvalidFileNameChars()));
            string path = Path.Combine(dir, $"{safe}_{tc.Id}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(tc, _json));
            return path;
        }

        public string ExportAllToFile()
        {
            using var db = AppDbContext.Create();
            var list = db.TestCases.ToList()
                .Select(e => JsonSerializer.Deserialize<TestCase>(e.JsonData, _json))
                .Where(x => x is not null).ToList();
            string dir  = GetExportDir();
            string path = Path.Combine(dir, $"all_testcases_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(list, _json));
            return path;
        }

        public (int imported, int skipped) ImportFromFile(string path)
        {
            string raw = File.ReadAllText(path);
            int ok = 0, skip = 0;
            if (raw.TrimStart().StartsWith("["))
            {
                var list = JsonSerializer.Deserialize<List<TestCase>>(raw, _json) ?? new();
                foreach (var tc in list) { try { Save(tc); ok++; } catch { skip++; } }
            }
            else
            {
                var tc = JsonSerializer.Deserialize<TestCase>(raw, _json);
                if (tc is not null) { Save(tc); ok++; } else skip++;
            }
            return (ok, skip);
        }

        private static string GetExportDir()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "D3Energy.UI.Automation_TestCases");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
