using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseCheck.Agent.Collectors;
using PulseCheck.Agent.Data;
using PulseCheck.Agent.Services;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Scheduling;

public sealed class ScheduleService : BackgroundService
{
    private readonly SqliteDatabase _database;
    private readonly ScanManager _scans;
    private readonly ILogger<ScheduleService> _logger;

    public ScheduleService(SqliteDatabase database, ScanManager scans, ILogger<ScheduleService> logger)
    {
        _database = database;
        _scans = scans;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var schedules = await _database.ListSchedulesAsync(stoppingToken);
                var now = DateTimeOffset.UtcNow;
                foreach (var schedule in schedules.Where(x => x.Enabled))
                {
                    var next = schedule.NextRunUtc;
                    if (next is null)
                    {
                        next = SafeNext(schedule, now);
                        await _database.SaveScheduleAsync(schedule with { NextRunUtc = next }, stoppingToken);
                        continue;
                    }
                    if (next.Value > now) continue;

                    var grace = TimeSpan.FromSeconds(Math.Max(60, schedule.MissedRunGraceSeconds));
                    var effectiveNext = SafeNext(schedule, now);
                    if (now - next.Value > grace)
                    {
                        await _database.SaveScheduleAsync(schedule with { NextRunUtc = effectiveNext }, stoppingToken);
                        continue;
                    }
                    if (!await CanRunAsync(schedule, stoppingToken))
                    {
                        await _database.SaveScheduleAsync(schedule with { NextRunUtc = next.Value.AddMinutes(1) }, stoppingToken);
                        continue;
                    }

                    var scan = await _scans.StartAsync(new ScanRequest(schedule.ScanMode, schedule.Intensity, true, true), stoppingToken);
                    var updated = schedule with { LastRunUtc = now, NextRunUtc = effectiveNext };
                    await _database.SaveScheduleAsync(updated, stoppingToken);
                    _logger.LogInformation("定时任务 {ScheduleName} 已创建扫描 {ScanId}", schedule.Name, scan.ScanId);
                    if (schedule.MaximumRuntimeSeconds is int maximum)
                        _ = CancelAfterAsync(scan.ScanId, TimeSpan.FromSeconds(maximum), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "调度循环失败，将在 30 秒后重试");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task<bool> CanRunAsync(ScheduleDefinition schedule, CancellationToken cancellationToken)
    {
        if (schedule.RunOnlyOnAcPower)
        {
            var output = await Task.Run(() => PulseCheck.Agent.Native.NativeMethods.GetSystemPowerStatus(out var status) ? status.AcLineStatus : (byte)255, cancellationToken);
            if (output != 1) return false;
        }
        if (schedule.RunOnlyWhenIdle)
        {
            var input = new PulseCheck.Agent.Native.NativeMethods.LastInputInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PulseCheck.Agent.Native.NativeMethods.LastInputInfo>() };
            if (!PulseCheck.Agent.Native.NativeMethods.GetLastInputInfo(ref input)) return false;
            var idleMilliseconds = unchecked((uint)Environment.TickCount - input.Time);
            if (idleMilliseconds < 5 * 60 * 1000) return false;
        }
        if (schedule.RunOnlyOnSpecifiedNetwork && !string.IsNullOrWhiteSpace(schedule.NetworkSsid))
        {
            var output = CollectorUtilities.RunCommand("netsh", "wlan show interfaces", 3000);
            var currentSsid = output?.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !x.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))?.Split(':', 2).LastOrDefault()?.Trim();
            if (!string.Equals(currentSsid, schedule.NetworkSsid, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static DateTimeOffset SafeNext(ScheduleDefinition schedule, DateTimeOffset after)
    {
        try { return new CronExpression(schedule.Cron).NextOccurrence(after); }
        catch { return after.AddHours(1); }
    }

    private async Task CancelAfterAsync(Guid scanId, TimeSpan timeout, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(timeout, stoppingToken);
            _scans.Cancel(scanId);
        }
        catch (OperationCanceledException) { }
    }
}