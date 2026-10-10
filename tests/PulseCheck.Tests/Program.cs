using System.Security.Cryptography;
using System.Text.Json;
using PulseCheck.Agent;
using PulseCheck.Agent.Data;
using PulseCheck.Agent.Diagnostics;
using PulseCheck.Agent.Reporting;
using PulseCheck.Agent.Scheduling;
using PulseCheck.Agent.Security;
using PulseCheck.Contracts;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Cron 解析与下一次执行", TestCronAsync),
    ("诊断引擎健康评分", TestDiagnosticsAsync),
    ("隐私脱敏", TestPrivacyAsync),
    ("SQLite 指标写入和读取", TestSqliteAsync),
    ("Agent 配对与令牌认证", TestSecurityAsync),
    ("QR 配对码生成", TestQrAsync),
    ("报告 JSON 哈希一致", TestReportAsync)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL  {test.Name}");
        Console.WriteLine(ex);
    }
}
Console.WriteLine($"\n{ tests.Length - failures.Count }/{tests.Length} tests passed");
if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}
return 0;

static Task TestCronAsync()
{
    var cron = new CronExpression("*/15 * * * *");
    var start = new DateTimeOffset(2026, 10, 10, 0, 7, 30, TimeSpan.Zero);
    var next = cron.NextOccurrence(start, TimeZoneInfo.Utc);
    Assert(next == new DateTimeOffset(2026, 10, 10, 0, 15, 0, TimeSpan.Zero), "下一次执行时间错误");
    AssertThrows<FormatException>(() => new CronExpression("bad"));
    return Task.CompletedTask;
}

static Task TestDiagnosticsAsync()
{
    var path = Path.Combine(AppContext.BaseDirectory, "config", "health-rules.json");
    var rules = HealthRuleSetLoader.Load(path);
    var engine = new DiagnosticEngine(rules);
    var snapshot = CreateSnapshot(95, 92, 95, false);
    var (score, findings) = engine.Evaluate(snapshot);
    Assert(findings.Any(x => x.RuleId == "CPU_USAGE_HIGH"), "应触发 CPU 规则");
    Assert(findings.Any(x => x.RuleId == "SYSTEM_DISK_LOW"), "应触发系统盘规则");
    Assert(findings.Any(x => x.RuleId == "DEFENDER_DISABLED"), "应触发 Defender 规则");
    Assert(score.OverallScore < 80, "总分应被扣减");
    Assert(score.Dimensions.Length == 7, "应有 7 个健康维度");
    return Task.CompletedTask;
}

static Task TestPrivacyAsync()
{
    var report = CreateReport(CreateSnapshot(20, 50, 80, true));
    var redacted = PrivacyRedactor.Redact(report);
    Assert(redacted.Device.DeviceId.StartsWith("***", StringComparison.Ordinal), "设备 ID 应脱敏");
    Assert(redacted.Snapshot.System.UserNameMasked == "***", "用户名应脱敏");
    Assert(redacted.Snapshot.Network.Adapters.All(x => x.IpAddresses.All(ip => ip.Contains("***"))), "IP 应脱敏");
    return Task.CompletedTask;
}

static async Task TestSqliteAsync()
{
    var directory = TemporaryDirectory();
    try
    {
        var database = new SqliteDatabase(Path.Combine(directory, "test.db"));
        database.Initialize();
        var snapshot = CreateSnapshot(25, 45, 70, true);
        await database.InsertMetricAsync(snapshot);
        var latest = await database.GetLatestMetricAsync();
        Assert(latest?.SnapshotId == snapshot.SnapshotId, "SQLite 应返回同一快照");
        var history = await database.GetMetricHistoryAsync(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert(history.Count == 1, "历史查询应返回一条记录");
        var scan = new ScanRecord { DeviceId = snapshot.DeviceId, Status = ScanStatus.Completed, FinishedAtUtc = DateTimeOffset.UtcNow };
        await database.SaveScanAsync(scan);
        Assert((await database.GetScanAsync(scan.ScanId))?.Status == ScanStatus.Completed, "扫描记录应持久化");
    }
    finally { Directory.Delete(directory, true); }
}

static Task TestSecurityAsync()
{
    var directory = TemporaryDirectory();
    try
    {
        var options = new AgentOptions { DataDirectory = directory, PairingCodeLifetimeMinutes = 15 };
        var security = new AgentSecurity(directory, options);
        var pairingCode = security.CurrentPairingCode;
        var response = security.Pair(new PairRequest(pairingCode, "Unit Test", "owner"));
        Assert(response is not null, "正确配对码应成功");
        Assert(security.Authenticate(response!.Token)?.Role == "owner", "令牌应通过认证");
        Assert(security.Pair(new PairRequest(pairingCode, "Again", "owner")) is null, "配对码应一次性失效");
        Assert(security.Revoke(response.ControllerId), "设备应可撤销");
        Assert(security.Authenticate(response.Token) is null, "撤销后的令牌应失效");
    }
    finally { Directory.Delete(directory, true); }
    return Task.CompletedTask;
}

static Task TestQrAsync()
{
    var svg = QrCodeGenerator.GenerateSvg("pulsecheck://pair?host=192.168.1.2&port=17821&code=123456");
    Assert(svg.StartsWith("<svg", StringComparison.Ordinal), "应输出 SVG");
    Assert(svg.Contains("path fill=\"#000\"", StringComparison.Ordinal), "应包含二维码模块");
    Assert(svg.Length > 3000, "二维码模块数量异常");
    return Task.CompletedTask;
}

static async Task TestReportAsync()
{
    var directory = TemporaryDirectory();
    try
    {
        var generator = new ReportGenerator();
        var report = CreateReport(CreateSnapshot(20, 40, 80, true));
        var generated = await generator.GenerateAsync(report, directory, generatePdf: false, redact: false);
        var jsonArtifact = generated.Artifacts.Single(x => x.Format == "json");
        var path = Path.Combine(directory, generated.ReportId.ToString("N"), jsonArtifact.FileName);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant();
        Assert(hash == jsonArtifact.Sha256, "JSON 文件哈希应匹配");
        Assert(File.Exists(Path.Combine(directory, generated.ReportId.ToString("N"), generated.Artifacts.Single(x => x.Format == "html").FileName)), "HTML 报告应存在");
    }
    finally { Directory.Delete(directory, true); }
}

static MetricSnapshot CreateSnapshot(double cpu, double memory, double diskUsed, bool defender)
{
    return new MetricSnapshot
    {
        DeviceId = "test-device-12345678",
        TimestampUtc = DateTimeOffset.UtcNow,
        Cpu = new CpuMetrics { Name = "Test CPU", PhysicalCoreCount = 4, LogicalProcessorCount = 8, TotalUsagePercent = cpu },
        Memory = new MemoryMetrics { TotalBytes = 16UL * 1024 * 1024 * 1024, AvailableBytes = 4UL * 1024 * 1024 * 1024, UsedBytes = 12UL * 1024 * 1024 * 1024, UsagePercent = memory },
        Disk = new DiskMetrics
        {
            Volumes = [new VolumeInfo("C:\\", "System", "Fixed", "NTFS", 1000, 1000UL * (100 - (ulong)diskUsed) / 100, diskUsed, true, true, "Healthy")],
            AverageResponseTimeMilliseconds = 10,
            SmartAvailability = new MetricAvailability(DataAvailability.Available)
        },
        Network = new NetworkMetrics { Adapters = [new NetworkAdapterInfo { Name = "Ethernet", Status = "Up", IpAddresses = ["192.168.1.10"], MacMasked = "AA:BB:CC:DD:EE:FF" }] },
        System = new SystemMetrics { ComputerName = "TEST-PC", UserNameMasked = "t***t", IsAdministrator = true, DefenderEnabled = defender, OsName = "Windows 11", OsVersion = "10.0.26200", BootTimeUtc = DateTimeOffset.UtcNow.AddHours(-2), Uptime = TimeSpan.FromHours(2) },
        Sensors = new SensorMetrics { CpuTemperatureCelsius = 82, Availability = new MetricAvailability(DataAvailability.Available) },
        TopProcesses = [new ProcessMetric(42, "test", 12, 1024, 2048, DateTimeOffset.UtcNow, "***\\test.exe", "Test", "Test process", false)]
    };
}

static HealthReport CreateReport(MetricSnapshot snapshot)
{
    var rules = HealthRuleSetLoader.Load(Path.Combine(AppContext.BaseDirectory, "config", "health-rules.json"));
    var engine = new DiagnosticEngine(rules);
    var (score, findings) = engine.Evaluate(snapshot);
    return new HealthReport
    {
        ScanId = Guid.NewGuid(),
        Device = new DeviceSummary { DeviceId = snapshot.DeviceId, DisplayName = "TEST-PC", ComputerName = "TEST-PC", OsDescription = "Windows 11", AgentVersion = "0.1.0" },
        ScanStartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
        ScanFinishedAtUtc = DateTimeOffset.UtcNow,
        ScanMode = "quick",
        IsAdministrator = true,
        OperatingMode = OperatingMode.Full,
        Health = score,
        RiskLevel = "低",
        Findings = findings,
        Snapshot = snapshot,
        Evidence = new Dictionary<string, string> { ["test"] = "true" }
    };
}

static string TemporaryDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), "PulseCheckTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void AssertThrows<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"预期抛出 {typeof(T).Name}");
}