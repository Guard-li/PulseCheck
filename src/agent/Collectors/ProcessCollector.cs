using System.Diagnostics;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Collectors;

public sealed class ProcessCollector
{
    private readonly object _gate = new();
    private readonly Dictionary<int, (TimeSpan Cpu, DateTimeOffset At, DateTimeOffset? Start)> _last = new();

    public ProcessMetric[] Collect(int limit = 500)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new List<ProcessMeasure>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var totalCpu = SafeCpu(process);
                var start = TryStartTime(process);
                double? cpu = null;
                lock (_gate)
                {
                    if (_last.TryGetValue(process.Id, out var previous) && previous.Start == start && totalCpu is not null)
                    {
                        var elapsed = Math.Max(1, (now - previous.At).TotalMilliseconds);
                        var delta = Math.Max(0, (totalCpu.Value - previous.Cpu).TotalMilliseconds);
                        cpu = Math.Round(Math.Clamp(delta / (elapsed * Environment.ProcessorCount) * 100, 0, 100), 2);
                    }
                    if (totalCpu is not null) _last[process.Id] = (totalCpu.Value, now, start);
                }

                result.Add(new ProcessMeasure(
                    process.Id,
                    process.ProcessName,
                    cpu,
                    SafeWorkingSet(process),
                    SafePrivateBytes(process),
                    start));
            }
            catch
            {
                // Protected system processes can disappear or deny access between enumeration and inspection.
            }
            finally
            {
                process.Dispose();
            }
        }

        var ordered = result
            .OrderByDescending(x => x.CpuUsagePercent ?? -1)
            .ThenByDescending(x => x.WorkingSetBytes)
            .Take(Math.Clamp(limit, 10, 2000))
            .ToArray();

        // File version information is comparatively expensive. Only enrich the processes
        // that are actually returned instead of opening MainModule for every process each second.
        var details = new Dictionary<int, (string? Path, string? Company, string? Description)>();
        foreach (var item in ordered.Take(40))
        {
            try
            {
                using var process = Process.GetProcessById(item.ProcessId);
                details[item.ProcessId] = (
                    CollectorUtilities.MaskPath(TryExecutablePath(process)),
                    TryCompany(process),
                    TryDescription(process));
            }
            catch
            {
                details[item.ProcessId] = (null, null, null);
            }
        }

        var snapshot = ordered.Select(item =>
        {
            details.TryGetValue(item.ProcessId, out var detail);
            return new ProcessMetric(
                item.ProcessId,
                item.Name,
                item.CpuUsagePercent,
                item.WorkingSetBytes,
                item.PrivateBytes,
                item.StartTimeUtc,
                detail.Path,
                detail.Company,
                detail.Description,
                false);
        }).ToArray();

        lock (_gate)
        {
            var active = result.Select(x => x.ProcessId).ToHashSet();
            foreach (var stale in _last.Keys.Where(x => !active.Contains(x)).ToArray()) _last.Remove(stale);
        }
        return snapshot;
    }

    private static TimeSpan? SafeCpu(Process process)
    {
        try { return process.TotalProcessorTime; } catch { return null; }
    }

    private static DateTimeOffset? TryStartTime(Process process)
    {
        try { return process.StartTime.ToUniversalTime(); } catch { return null; }
    }

    private static ulong SafeWorkingSet(Process process)
    {
        try { return (ulong)Math.Max(0, process.WorkingSet64); } catch { return 0; }
    }

    private static ulong SafePrivateBytes(Process process)
    {
        try { return (ulong)Math.Max(0, process.PrivateMemorySize64); } catch { return 0; }
    }

    private static string? TryExecutablePath(Process process)
    {
        try { return process.MainModule?.FileName; } catch { return null; }
    }

    private static string? TryCompany(Process process)
    {
        try { return process.MainModule?.FileVersionInfo.CompanyName; } catch { return null; }
    }

    private static string? TryDescription(Process process)
    {
        try { return process.MainModule?.FileVersionInfo.FileDescription; } catch { return null; }
    }

    private sealed record ProcessMeasure(
        int ProcessId,
        string Name,
        double? CpuUsagePercent,
        ulong WorkingSetBytes,
        ulong PrivateBytes,
        DateTimeOffset? StartTimeUtc);
}