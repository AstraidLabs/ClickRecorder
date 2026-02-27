using System;
using System.IO;
using System.Text;
using ClickRecorder.Models;

namespace ClickRecorder.Services
{
    public static class ReportExporter
    {
        public static string ExportHtml(TestSession session)
        {
            string dir  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "ClickRecorder_Reports");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir,
                $"report_{session.Id}_{session.StartedAt:yyyyMMdd_HHmmss}.html");
            File.WriteAllText(path, BuildHtml(session), Encoding.UTF8);
            return path;
        }

        // ── HTML ──────────────────────────────────────────────────────────────

        private static string BuildHtml(TestSession s)
        {
            string verdict     = s.FailureCount == 0 ? "PASS" : "FAIL";
            string verdictColor = s.FailureCount == 0 ? "#a6e3a1" : "#f38ba8";
            var sb = new StringBuilder();

            sb.Append("""
<!DOCTYPE html>
<html lang="cs">
<head>
<meta charset="UTF-8"/>
<title>ClickRecorder</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:'Segoe UI',sans-serif;background:#1e1e2e;color:#cdd6f4;padding:24px;font-size:13px}
h1{font-size:20px;margin-bottom:4px}
.sub{color:#6c7086;font-size:12px;margin-bottom:20px}
.cards{display:flex;gap:12px;flex-wrap:wrap;margin-bottom:20px}
.card{background:#313244;border-radius:8px;padding:14px 18px;min-width:120px}
.card .v{font-size:26px;font-weight:700;margin-bottom:3px}
.card .l{font-size:11px;color:#6c7086}
.ok{color:#a6e3a1}.err{color:#f38ba8}.warn{color:#fab387}.info{color:#89b4fa}
h2{font-size:14px;margin:20px 0 10px;color:#89b4fa;font-weight:600}
table{width:100%;border-collapse:collapse;background:#181825;border-radius:8px;overflow:hidden;font-size:12px}
th{background:#313244;padding:9px 12px;text-align:left;color:#89b4fa;font-weight:600;white-space:nowrap}
td{padding:8px 12px;border-top:1px solid #252535;vertical-align:top}
tr:hover td{background:#1f1f2e}
.badge{display:inline-block;padding:2px 7px;border-radius:4px;font-weight:700;font-size:11px}
.bok{background:#1a3328;color:#a6e3a1}.berr{background:#351a1a;color:#f38ba8}
.bflaui{background:#1a2a44;color:#89b4fa}.bcoord{background:#2a2a1a;color:#f9e2af}
details{margin-top:5px}summary{cursor:pointer;color:#89b4fa;font-size:11px;user-select:none}
pre{margin-top:6px;background:#0d0d18;padding:10px;border-radius:6px;white-space:pre-wrap;
     word-break:break-all;color:#cdd6f4;line-height:1.55;font-size:11px}
.inner{margin-top:6px;border-left:3px solid #f38ba8;padding-left:10px}
code{font-family:Consolas,monospace;background:#252535;padding:1px 5px;border-radius:3px}
</style>
</head>
<body>
""");

            sb.AppendLine("<h1>🖱️ ClickRecorder – Test Report</h1>");
            sb.AppendLine($"<div class='sub'>Session: <code>{s.Id}</code> &nbsp;|&nbsp; {s.StartedAt:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine($"&nbsp;|&nbsp; Duration: <code>{s.TotalDuration.TotalSeconds:F1}s</code></div>");
            sb.AppendLine("<div class='cards'>");
            sb.AppendLine($"  <div class='card'><div class='v' style='color:{verdictColor}'>{verdict}</div><div class='l'>Result</div></div>");
            sb.AppendLine($"  <div class='card'><div class='v'>{s.Steps.Count}</div><div class='l'>Total steps</div></div>");
            sb.AppendLine($"  <div class='card'><div class='v ok'>{s.SuccessCount}</div><div class='l'>Passed</div></div>");
            sb.AppendLine($"  <div class='card'><div class='v err'>{s.FailureCount}</div><div class='l'>Failed</div></div>");
            sb.AppendLine($"  <div class='card'><div class='v info'>{s.FlaUISteps}</div><div class='l'>FlaUI steps</div></div>");
            sb.AppendLine($"  <div class='card'><div class='v warn'>{s.CoordSteps}</div><div class='l'>Coord steps</div></div>");
            sb.AppendLine($"  <div class='card'><div class='v warn'>{s.RepeatCount}×</div><div class='l'>Repeats</div></div>");
            sb.AppendLine($"  <div class='card'><div class='v'>{s.SpeedMultiplier}×</div><div class='l'>Speed</div></div>");
            sb.AppendLine("</div>");


            // Steps table
            sb.AppendLine("<h2>Steps</h2>");
            sb.AppendLine("<table><thead><tr>" +
                "<th>#</th><th>R</th><th>Status</th><th>Mode</th>" +
                "<th>Element / Coordinates</th><th>Window</th><th>ms</th><th>Time</th>" +
                "<th>Exception &amp; Call Stack</th></tr></thead><tbody>");

            foreach (var step in s.Steps)
            {
                string sBadge = step.Status == StepStatus.Success
                    ? "<span class='badge bok'>✓ OK</span>"
                    : "<span class='badge berr'>✗ FAIL</span>";
                string mBadge = step.Mode == PlaybackMode.FlaUI
                    ? "<span class='badge bflaui'>⚙ FlaUI</span>"
                    : "<span class='badge bcoord'>🖱 Coord</span>";

                string elem = step.Element is not null
                    ? H(step.Element.Selector)
                    : $"({step.X},{step.Y})";
                string win = step.Element?.WindowTitle is { } t ? H(t)
                           : step.Element?.ProcessName is { } p ? H(p) : "";

                string exCol = step.Exception is not null ? ExHtml(step.Exception) : "";
                if (step.ScreenshotPath is not null)
                    exCol += $"<div style='margin-top:5px;color:#fab387;font-size:11px'>📸 {H(step.ScreenshotPath)}</div>";

                sb.AppendLine($"<tr><td>{step.StepId:D3}</td><td>{step.RepeatIndex}</td>" +
                              $"<td>{sBadge}</td><td>{mBadge}</td>" +
                              $"<td><code>{elem}</code></td><td>{win}</td>" +
                              $"<td>{step.Duration.TotalMilliseconds:F0}</td>" +
                              $"<td>{step.ExecutedAt:HH:mm:ss.fff}</td>" +
                              $"<td>{exCol}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");

            // Global exceptions
            if (s.UnhandledExceptions.Count > 0)
            {
                sb.AppendLine("<h2>Global / Unhandled Exceptions</h2>");
                sb.AppendLine("<table><thead><tr><th>Time</th><th>Source</th><th>Detail</th></tr></thead><tbody>");
                foreach (var ex in s.UnhandledExceptions)
                    sb.AppendLine($"<tr><td>{ex.CapturedAt:HH:mm:ss.fff}</td>" +
                                  $"<td><code>{H(ex.Source)}</code></td>" +
                                  $"<td>{ExHtml(ex)}</td></tr>");
                sb.AppendLine("</tbody></table>");
            }

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static string ExHtml(ExceptionDetail ex)
        {
            var sb   = new StringBuilder();
            var cur  = ex;
            int depth = 0;
            while (cur is not null)
            {
                if (depth > 0) sb.AppendLine("<div class='inner'><strong>Inner Exception:</strong>");
                sb.AppendLine($"<strong class='err'>{H(cur.ShortType)}</strong>: {H(cur.Message)}");
                sb.AppendLine($"<details><summary>📋 View call stack</summary><pre>{H(cur.StackTrace)}</pre></details>");
                if (depth > 0) sb.AppendLine("</div>");
                cur = cur.InnerException;
                depth++;
            }
            return sb.ToString();
        }

        private static string H(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("&","&amp;").Replace("<","&lt;")
                    .Replace(">","&gt;").Replace("\"","&quot;").Replace("'","&#39;");
        }
    }
}
