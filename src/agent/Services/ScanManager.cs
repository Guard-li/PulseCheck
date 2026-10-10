using System.Collections.Concurrent;
using PulseCheck.Agent.Collectors;
using PulseCheck.Agent.Data;
using PulseCheck.Agent.Diagnostics;
using PulseCheck.Agent.Inventory;
using PulseCheck.Agent.Reporting;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Services;

public sealed class ScanManager
{
    private readonly IMetricCollector _collector;
    private readonly DiagnosticEngine _diagnostics;
    private readonly SqliteDatabase _database;
    private readonly ReportGenerator _reports;
    private readonly AgentIdentity _identity;
    private readonly AgentOptions _options;
    private readonly SystemInventoryService _inventory;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellations = new();
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public ScanManager(
        IMetricCollector collector,
        DiagnosticEngine diagnostics,
        SqliteDatabase database,
        ReportGenerator reports,
        AgentIdentity identity,
        AgentOptions options,
        SystemInventoryService inventory)
    {
        _collector = collector;
        _diagnostics = diagnostics;
        _database = database;
        _reports = reports;
        _identity = identity;
        _options = options;
        _inventory = inventory;
    }

    public async Task<ScanRecord> StartAsync(ScanRequest request, CancellationToken cancellationToken = default)
    {
        var mode = string.Equals(request.Mode, "full", StringComparison.OrdinalIgnoreCase) ? "full" : "quick";
        var record = new ScanRecord
        {
            DeviceId = _identity.DeviceId,
            Mode = mode,
            Intensity = NormalizeIntensity(request.Intensity),
            Status = ScanStatus.Queued,
            Progress = new ScanProgress { Percent = 0, Stage = "queued", Message = "任务已创建" }
        };
        await _database.SaveScanAsync(record, cancellationToken);
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellations[record.ScanId] = source;
        _ = Task.Run(() => RunAsync(record, request, source.Token));
        return record;
    }

    public async Task<ScanRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var persisted = await _database.GetScanAsync(id, cancellationToken);
        if (persisted is not null) return persisted;
        return null;
    }

    public Task<IReadOnlyList<ScanRecord>> ListAsync(int limit = 100, CancellationToken cancellationToken = default) =>
        _database.ListScansAsync(limit, cancellationToken);

    public bool Cancel(Guid id)
    {
        if (!_cancellations.TryGetValue(id, out var source)) return false;
        source.Cancel();
        return true;
    }

    private async Task RunAsync(ScanRecord queued, ScanRequest request, CancellationToken cancellationToken)
    {
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            var started = DateTimeOffset.UtcNow;
            var record = queued with
            {
                Status = ScanStatus.Running,
                StartedAtUtc = started,
                Progress = new ScanProgress { Percent = 5, Stage = "collecting", Message = "正在采集真实指标" }
            };
            await _database.SaveScanAsync(record, cancellationToken);
            var snapshot = await _collector.CollectAsync(record.Mode == "full", cancellationToken);
            record = record with { Progress = new ScanProgress { Percent = 60, Stage = "diagnosing", Message = "正在运行诊断规则" } };
            await _database.SaveScanAsync(record, cancellationToken);
            var services = await _inventory.GetServicesAsync(refresh: true, cancellationToken);
            var startup = await _inventory.GetStartupAsync(refresh: true, cancellationToken);
            var events = await _inventory.GetRecentEventsAsync(cancellationToken);
            var (score, baseFindings) = _diagnostics.Evaluate(snapshot);
            var inventoryFindings = BuildInventoryFindings(services, startup, events);
            var findings = baseFindings.Concat(inventoryFindings).OrderByDescending(x => x.Severity).ToArray();
            score = ApplyInventoryPenalty(score, inventoryFindings);
            var risk = RiskLevel(score, findings);
            var limitations = BuildLimitations(snapshot);
            var report = new HealthReport
            {
                ScanId = record.ScanId,
                Device = new DeviceSummary
                {
                    DeviceId = _identity.DeviceId,
                    DisplayName = _identity.DisplayName,
                    ComputerName = snapshot.System.ComputerName,
                    OsDescription = $"{snapshot.System.OsName} {snapshot.System.OsVersion}",
                    AgentVersion = _identity.Version
                },
                ScanStartedAtUtc = started,
                ScanFinishedAtUtc = DateTimeOffset.UtcNow,
                ScanMode = record.Mode,
                IsAdministrator = snapshot.System.IsAdministrator,
                OperatingMode = snapshot.System.OperatingMode,
                Health = score,
                RiskLevel = risk,
                Findings = findings,
                Snapshot = snapshot,
                Limitations = limitations,
                Evidence = BuildEvidence(snapshot, findings, services, startup, events)
            };
            record = record with { Progress = new ScanProgress { Percent = 80, Stage = "reporting", Message = "正在生成报告" } };
            await _database.SaveScanAsync(record, cancellationToken);
            report = await _reports.GenerateAsync(report, _options.ReportDirectory, request.GeneratePdf, request.RedactSensitiveData, cancellationToken);
            await _database.SaveReportAsync(report, cancellationToken);
            var finished = record with
            {
                Status = ScanStatus.Completed,
                FinishedAtUtc = DateTimeOffset.UtcNow,
                ReportId = report.ReportId,
                Progress = new ScanProgress { Percent = 100, Stage = "completed", Message = "检测完成" }
            };
            await _database.SaveScanAsync(finished, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await _database.SaveScanAsync(queued with
            {
                Status = ScanStatus.Cancelled,
                FinishedAtUtc = DateTimeOffset.UtcNow,
                Progress = new ScanProgress { Percent = 100, Stage = "cancelled", Message = "检测已取消" }
            });
        }
        catch (Exception ex)
        {
            await _database.SaveScanAsync(queued with
            {
                Status = ScanStatus.Failed,
                FinishedAtUtc = DateTimeOffset.UtcNow,
                ErrorCode = "SCAN_FAILED",
                ErrorMessage = ex.Message,
                Progress = new ScanProgress { Percent = 100, Stage = "failed", Message = "检测失败，请查看日志" }
            });
        }
        finally
        {
            _cancellations.TryRemove(queued.ScanId, out _);
            _runGate.Release();
        }
    }

    private static DiagnosticFinding[] BuildInventoryFindings(
        IReadOnlyList<ServiceInfo> services,
        IReadOnlyList<StartupItemInfo> startup,
        IReadOnlyList<EventLogItem> events)
    {
        var findings = new List<DiagnosticFinding>();
        var failedAutoServices = services.Where(x => string.Equals(x.StartType, "Automatic", StringComparison.OrdinalIgnoreCase) && !string.Equals(x.Status, "Running", StringComparison.OrdinalIgnoreCase)).Take(10).ToArray();
        if (failedAutoServices.Length > 0)
        {
            findings.Add(new DiagnosticFinding
            {
                RuleId = "AUTO_SERVICE_STOPPED",
                Severity = FindingSeverity.Notice,
                Title = "存在未运行的自动服务",
                Description = $"{failedAutoServices.Length} 个自动启动服务当前未运行。",
                PossibleCauses = ["服务启动失败", "依赖服务未就绪", "被安全软件或策略阻止"],
                Recommendations = ["在服务管理器查看失败原因", "确认是否为系统更新期间或按需启动的正常状态", "不要直接删除未知服务"],
                RequiresAdministrator = true,
                Evidence = [.. failedAutoServices.Select(x => new EvidenceItem(x.Name, $"{x.DisplayName}: {x.Status}", "Get-Service", DateTimeOffset.UtcNow))]
            });
        }
        if (startup.Count >= 15)
        {
            findings.Add(new DiagnosticFinding
            {
                RuleId = "STARTUP_ITEMS_HIGH",
                Severity = FindingSeverity.Notice,
                Title = "启动项数量较多",
                Description = $"检测到 {startup.Count} 个启动项，可能延长登录时间或占用后台资源。",
                PossibleCauses = ["同步、游戏、输入法或更新程序过多", "安装软件默认加入启动项"],
                Recommendations = ["在任务管理器启动页逐项确认用途", "禁用不需要的启动项前先保留安全与硬件驱动软件", "PulseCheck 不会自动修改启动项"],
                RequiresAdministrator = false,
                Evidence = [new EvidenceItem("startup.count", startup.Count.ToString(), "Startup registry/folders", DateTimeOffset.UtcNow)]
            });
        }
        var whea = events.Where(x => x.Provider.Contains("WHEA", StringComparison.OrdinalIgnoreCase) || x.Message.Contains("WHEA", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (whea.Length > 0)
        {
            findings.Add(new DiagnosticFinding
            {
                RuleId = "WHEA_EVENTS",
                Severity = FindingSeverity.Critical,
                Title = "检测到硬件错误事件",
                Description = $"最近 7 天发现 {whea.Length} 条 WHEA 硬件错误事件。",
                PossibleCauses = ["CPU、内存、PCIe 或存储链路硬件错误", "驱动/固件问题", "超频或散热不稳定"],
                Recommendations = ["备份重要数据", "更新 BIOS、芯片组和存储驱动", "在厂商支持渠道检查硬件诊断结果"],
                RequiresAdministrator = true,
                Evidence = [.. whea.Take(10).Select(x => new EvidenceItem(x.EventId.ToString(), Truncate(x.Message, 240), x.Provider, x.TimeCreatedUtc ?? DateTimeOffset.UtcNow))]
            });
        }
        else if (events.Count >= 20)
        {
            findings.Add(new DiagnosticFinding
            {
                RuleId = "SYSTEM_EVENT_VOLUME",
                Severity = FindingSeverity.Notice,
                Title = "系统错误事件数量较多",
                Description = $"最近 7 天读取到 {events.Count} 条严重或错误级系统事件。",
                PossibleCauses = ["驱动或服务异常", "硬件/网络错误", "更新或软件兼容问题"],
                Recommendations = ["打开事件查看器按时间和来源复核", "优先处理重复出现的来源", "保存报告后再清理日志"],
                RequiresAdministrator = true,
                Evidence = [new EvidenceItem("system.events.count", events.Count.ToString(), "Get-WinEvent", DateTimeOffset.UtcNow)]
            });
        }
        return [.. findings];
    }

    private static HealthScore ApplyInventoryPenalty(HealthScore score, DiagnosticFinding[] findings)
    {
        var penalty = findings.Sum(x => x.Severity switch
        {
            FindingSeverity.Critical => 25,
            FindingSeverity.Warning => 12,
            FindingSeverity.Notice => 5,
            _ => 0
        });
        var adjusted = Math.Round(Math.Clamp(score.OverallScore - penalty, 0, 100), 1);
        var status = adjusted switch
        {
            >= 90 => HealthStatus.Excellent,
            >= 80 => HealthStatus.Good,
            >= 65 => HealthStatus.Attention,
            >= 40 => HealthStatus.Warning,
            _ => HealthStatus.Critical
        };
        var dimensions = score.Dimensions.Select(x => x.Id == "stability" ? x with { Score = Math.Round(Math.Clamp(x.Score - penalty, 0, 100), 1), Status = status } : x).ToArray();
        return score with { OverallScore = adjusted, Status = status, Dimensions = dimensions };
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length] + "…";
    private static string NormalizeIntensity(string intensity) =>
        intensity?.ToLowerInvariant() switch
        {
            "high" => "high",
            "medium" => "medium",
            _ => "low"
        };

    private static string RiskLevel(HealthScore score, DiagnosticFinding[] findings)
    {
        if (findings.Any(x => x.Severity == FindingSeverity.Critical)) return "高";
        if (score.OverallScore < 65 || findings.Any(x => x.Severity == FindingSeverity.Warning)) return "中";
        if (score.OverallScore < 85) return "注意";
        return "低";
    }

    private static string[] BuildLimitations(MetricSnapshot snapshot)
    {
        var limitations = new List<string>();
        if (!snapshot.System.IsAdministrator) limitations.Add("受限模式：磁盘 SMART、安全状态和部分系统日志不可用。");
        if (snapshot.Sensors.Availability.Status != DataAvailability.Available) limitations.Add($"传感器限制：{snapshot.Sensors.Availability.Reason}");
        if (snapshot.Disk.SmartAvailability.Status != DataAvailability.Available) limitations.Add($"SMART 限制：{snapshot.Disk.SmartAvailability.Reason}");
        if (snapshot.Gpus.Length == 0) limitations.Add("GPU 限制：未检测到受支持 GPU，或厂商工具/驱动不可用。");
        return [.. limitations];
    }

    private static Dictionary<string, string> BuildEvidence(MetricSnapshot snapshot, DiagnosticFinding[] findings, IReadOnlyList<ServiceInfo> services, IReadOnlyList<StartupItemInfo> startup, IReadOnlyList<EventLogItem> events)
    {
        var evidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["snapshot.timestampUtc"] = snapshot.TimestampUtc.UtcDateTime.ToString("O"),
            ["cpu.totalUsagePercent"] = snapshot.Cpu.TotalUsagePercent.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            ["memory.usagePercent"] = snapshot.Memory.UsagePercent.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            ["memory.availableBytes"] = snapshot.Memory.AvailableBytes.ToString(),
            ["system.uptime"] = snapshot.System.Uptime.ToString(@"d\.hh\:mm\:ss"),
            ["sensor.availability"] = snapshot.Sensors.Availability.Status.ToString()
        };
        foreach (var disk in snapshot.Disk.PhysicalDisks)
        {
            evidence[$"disk.{disk.Id}.health"] = disk.HealthStatus ?? "unknown";
            if (disk.TemperatureCelsius is not null) evidence[$"disk.{disk.Id}.temperatureC"] = disk.TemperatureCelsius.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            if (disk.RemainingLifePercent is not null) evidence[$"disk.{disk.Id}.remainingLifePercent"] = disk.RemainingLifePercent.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }
        evidence["inventory.services.total"] = services.Count.ToString();
        evidence["inventory.services.notRunningAutomatic"] = services.Count(x => string.Equals(x.StartType, "Automatic", StringComparison.OrdinalIgnoreCase) && !string.Equals(x.Status, "Running", StringComparison.OrdinalIgnoreCase)).ToString();
        evidence["inventory.startup.count"] = startup.Count.ToString();
        evidence["inventory.events.severeOrError7d"] = events.Count.ToString();
        foreach (var finding in findings)
            foreach (var item in finding.Evidence) evidence[$"finding.{finding.RuleId}.{item.Key}"] = item.Value;
        return evidence;
    }
}