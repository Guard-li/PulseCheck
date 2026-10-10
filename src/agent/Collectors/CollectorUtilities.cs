using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Collectors;

internal static class CollectorUtilities
{
    public static string MaskMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return string.Empty;
        var normalized = mac.Replace("-", ":").Replace(".", ":");
        var parts = normalized.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"**:**:**:**:{parts[^2]}:{parts[^1]}" : "**";
    }

    public static string MaskPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var name = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(name) ? "***" : $"***\\{name}";
    }

    public static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return string.Empty;
        if (ip.Contains(':'))
        {
            var parts = ip.Split(':');
            return parts.Length > 1 ? $"{parts[0]}:***:{parts[^1]}" : "***";
        }
        var ipParts = ip.Split('.');
        return ipParts.Length == 4 ? $"{ipParts[0]}.{ipParts[1]}.***.***" : "***";
    }

    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static double Percent(double value, double total) =>
        total <= 0 ? 0 : Math.Round(Math.Clamp(value / total * 100, 0, 100), 2);

    public static string MaskUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return "***";
        return userName.Length <= 2 ? "***" : $"{userName[0]}***{userName[^1]}";
    }

    public static string? RunCommand(
        string fileName,
        string arguments,
        int timeoutMs = 4000)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            Task.WaitAll(outputTask, errorTask);
            return outputTask.Result + errorTask.Result;
        }
        catch
        {
            return null;
        }
    }

    public static JsonDocument? RunPowerShellJson(string script, int timeoutMs = 8000)
    {
        var escaped = script.Replace("'", "''", StringComparison.Ordinal);
        var output = RunCommand("powershell.exe", $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{escaped}\"", timeoutMs);
        if (string.IsNullOrWhiteSpace(output)) return null;
        try
        {
            var start = output.IndexOfAny(['{', '[']);
            if (start < 0) return null;
            return JsonDocument.Parse(output[start..].Trim());
        }
        catch
        {
            return null;
        }
    }

    public static string NormalizeProcessName(string value) =>
        Regex.Replace(value ?? string.Empty, @"[^a-zA-Z0-9._-]", string.Empty);

    public static string ToSeverityText(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => "严重",
        FindingSeverity.Warning => "警告",
        FindingSeverity.Notice => "注意",
        _ => "信息"
    };

    public static string ToHealthStatusText(HealthStatus status) => status switch
    {
        HealthStatus.Excellent => "优秀",
        HealthStatus.Good => "良好",
        HealthStatus.Attention => "注意",
        HealthStatus.Warning => "警告",
        _ => "严重"
    };
}