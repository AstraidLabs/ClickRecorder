using System;
using System.Collections.Generic;
using System.Text;
using ClickRecorder.Models;

namespace ClickRecorder.Services
{
    public static class TicketGenerator
    {
        /// <summary>
        /// Generates a plain-text bug ticket ready to paste into Redmine, Jira,
        /// GitHub Issues, Notion, or any other tracker.
        /// </summary>
        public static string Generate(
            StepResult       failedStep,
            List<ClickAction> allSteps,
            TestSession       session,
            string            appName    = "",
            string            appVersion = "")
        {
            var sb = new StringBuilder();
            var ex = failedStep.Exception;

            // ── Title line ────────────────────────────────────────────────────
            string exShort = ex is not null ? $"{ex.ShortType}: {ex.Message}" : "neočekávané selhání";
            string element = failedStep.Element?.Selector ?? $"({failedStep.X},{failedStep.Y})";
            sb.AppendLine($"[BUG] {exShort} při kliknutí na {element}");
            sb.AppendLine();

            // ── Environment ───────────────────────────────────────────────────
            sb.AppendLine("## Prostředí");
            sb.AppendLine($"- OS:        {Environment.OSVersion}");
            sb.AppendLine($"- Runtime:   {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"- Stroj:     {Environment.MachineName} / {Environment.UserName}");
            if (!string.IsNullOrEmpty(appName))
                sb.AppendLine($"- Aplikace:  {appName} {appVersion}".TrimEnd());
            sb.AppendLine($"- Session:   {session.Id}  ({session.StartedAt:dd.MM.yyyy HH:mm:ss})");
            sb.AppendLine();

            // ── Reproduction steps ────────────────────────────────────────────
            sb.AppendLine("## Kroky k reprodukci");
            int num = 1;
            foreach (var a in allSteps)
            {
                string el = a.Element is not null
                    ? $"{a.Element.ControlType} {a.Element.Selector}" +
                      (a.Element.WindowTitle is { } w ? $" [{w}]" : "")
                    : $"souřadnice ({a.X},{a.Y})";

                string marker = a.Id == failedStep.StepId ? "  <-- SELHÁNÍ" : "";
                sb.AppendLine($"{num}. [{a.Button}] klik na {el} (+{a.DelayAfterPrevious.TotalMilliseconds:F0}ms){marker}");
                num++;
            }
            sb.AppendLine();

            // ── Failed step ───────────────────────────────────────────────────
            sb.AppendLine("## Selhání – detail");
            sb.AppendLine($"- Krok:      #{failedStep.StepId:D3}  (opakování {failedStep.RepeatIndex})");
            sb.AppendLine($"- Čas:       {failedStep.ExecutedAt:HH:mm:ss.fff}");
            sb.AppendLine($"- Trvání:    {failedStep.Duration.TotalMilliseconds:F0} ms");
            sb.AppendLine($"- Mód:       {(failedStep.Mode == PlaybackMode.FlaUI ? "FlaUI element" : "Win32 souřadnice")}");

            if (failedStep.Element is { } fe)
            {
                sb.AppendLine($"- Selector:  {fe.Selector}");
                sb.AppendLine($"- Type:      {fe.ControlType}");
                if (fe.AutomationId != null) sb.AppendLine($"- AutomationId: {fe.AutomationId}");
                if (fe.Name        != null) sb.AppendLine($"- Name:      {fe.Name}");
                if (fe.WindowTitle != null) sb.AppendLine($"- Okno:      {fe.WindowTitle}");
                if (fe.ProcessName != null) sb.AppendLine($"- Proces:    {fe.ProcessName}");
                if (fe.AncestorPath.Count > 0)
                    sb.AppendLine($"- Cesta:     {string.Join(" > ", fe.AncestorPath)}");
            }
            sb.AppendLine();

            // ── Exception ─────────────────────────────────────────────────────
            if (ex is not null)
            {
                sb.AppendLine("## Výjimka");
                AppendException(sb, ex);
            }

            // ── Screenshot ────────────────────────────────────────────────────
            if (!string.IsNullOrEmpty(failedStep.ScreenshotPath))
            {
                sb.AppendLine("## Screenshot");
                sb.AppendLine(failedStep.ScreenshotPath);
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine($"Generováno: ClickRecorder · {DateTime.Now:dd.MM.yyyy HH:mm:ss}");

            return sb.ToString();
        }

        private static void AppendException(StringBuilder sb, ExceptionDetail ex, int depth = 0)
        {
            string prefix = depth == 0 ? "" : $"  [Inner {depth}] ";
            sb.AppendLine($"{prefix}Typ:     {ex.Type}");
            sb.AppendLine($"{prefix}Zpráva:  {ex.Message}");
            if (!string.IsNullOrEmpty(ex.Source))
                sb.AppendLine($"{prefix}Source:  {ex.Source}");
            sb.AppendLine($"{prefix}Call Stack:");
            foreach (var line in ex.StackTrace.Split('\n'))
                sb.AppendLine($"  {line.TrimEnd()}");
            sb.AppendLine();

            if (ex.InnerException is not null)
                AppendException(sb, ex.InnerException, depth + 1);
        }
    }
}
