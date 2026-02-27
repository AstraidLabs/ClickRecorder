using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace ClickRecorder.Services;

public sealed class ApplicationLauncherService
{
    public LaunchResult Launch(string appNameOrId)
    {
        if (string.IsNullOrWhiteSpace(appNameOrId))
        {
            return LaunchResult.Fail("Zadej název aplikace, alias nebo AUMID.");
        }

        string query = appNameOrId.Trim();

        string directError = string.Empty;
        string appIdError = string.Empty;
        string startAppsError = string.Empty;

        // 1) Přímé spuštění (exe cesta, app execution alias, URL protocol)
        if (TryStartShell(query, out directError))
        {
            return LaunchResult.Ok($"Aplikace byla spuštěna: {query}");
        }

        // 2) Přímý AUMID (MSIX/UWP): např. Microsoft.WindowsCalculator_8wekyb3d8bbwe!App
        if (query.Contains('!') && TryStartAppsFolderByAppId(query, out appIdError))
        {
            return LaunchResult.Ok($"Spuštěna MSIX/UWP aplikace ({query}).");
        }

        // 3) Hledání podle Start Menu názvu -> AppID, potom shell:AppsFolder\<AppID>
        string? appId = ResolveAppIdFromStartApps(query);
        if (!string.IsNullOrWhiteSpace(appId) && TryStartAppsFolderByAppId(appId, out startAppsError))
        {
            return LaunchResult.Ok($"Spuštěna aplikace '{query}' přes Start Apps ({appId}).");
        }

        string combinedError = string.Join(" | ", new[] { directError, appIdError, startAppsError }
            .Where(e => !string.IsNullOrWhiteSpace(e)));

        return LaunchResult.Fail(
            $"Nepodařilo se spustit '{query}'. Zkus přesnější název, AUMID nebo cestu k .exe. {combinedError}".Trim());
    }

    private static bool TryStartShell(string fileNameOrUri, out string error)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileNameOrUri,
                UseShellExecute = true,
                WorkingDirectory = ResolveWorkingDirectory(fileNameOrUri)
            };

            using var process = Process.Start(psi);
            error = string.Empty;
            return process is not null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryStartAppsFolderByAppId(string appId, out string error)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"shell:AppsFolder\\{appId}",
                UseShellExecute = true
            };

            using var process = Process.Start(psi);
            error = string.Empty;
            return process is not null;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? ResolveAppIdFromStartApps(string query)
    {
        try
        {
            string escaped = query.Replace("'", "''");
            string script =
                "$q = '" + escaped + "'; " +
                "Get-StartApps | " +
                "Where-Object { $_.Name -like \"*$q*\" } | " +
                "Sort-Object Name | Select-Object -First 1 -ExpandProperty AppID";

            string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedScript}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveWorkingDirectory(string fileNameOrUri)
    {
        try
        {
            if (File.Exists(fileNameOrUri))
            {
                return Path.GetDirectoryName(fileNameOrUri) ?? Environment.CurrentDirectory;
            }
        }
        catch
        {
            // best effort only
        }

        return Environment.CurrentDirectory;
    }
}

public readonly record struct LaunchResult(bool Success, string Message)
{
    public static LaunchResult Ok(string message) => new(true, message);
    public static LaunchResult Fail(string message) => new(false, message);
}
