namespace PulseCheck.Contracts;

public enum ScanStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record ScanRequest(
    string Mode = "quick",
    string Intensity = "low",
    bool GeneratePdf = true,
    bool RedactSensitiveData = true,
    bool AllowDiskWriteTest = false);

public sealed record ScanProgress
{
    public int Percent { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ScanRecord
{
    public Guid ScanId { get; init; } = Guid.NewGuid();
    public string DeviceId { get; init; } = string.Empty;
    public string Mode { get; init; } = "quick";
    public string Intensity { get; init; } = "low";
    public ScanStatus Status { get; init; } = ScanStatus.Queued;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
    public ScanProgress? Progress { get; init; }
    public Guid? ReportId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record ReportSummary(
    Guid ReportId,
    Guid ScanId,
    string DeviceName,
    DateTimeOffset GeneratedAtUtc,
    int Score,
    string RiskLevel,
    int FindingCount,
    int CriticalCount,
    ReportArtifact[] Artifacts);

public sealed record ScheduleDefinition
{
    public Guid ScheduleId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public string Cron { get; init; } = "0 0 * * *";
    public string ScanMode { get; init; } = "quick";
    public string Intensity { get; init; } = "low";
    public bool RunOnlyWhenIdle { get; init; }
    public bool RunOnlyOnAcPower { get; init; }
    public bool RunOnlyOnSpecifiedNetwork { get; init; }
    public string? NetworkSsid { get; init; }
    public int? MaximumRuntimeSeconds { get; init; } = 300;
    public int RetryCount { get; init; }
    public int MissedRunGraceSeconds { get; init; } = 3600;
    public DateTimeOffset? LastRunUtc { get; init; }
    public DateTimeOffset? NextRunUtc { get; init; }
}

public sealed record AlertRecord
{
    public Guid AlertId { get; init; } = Guid.NewGuid();
    public string RuleId { get; init; } = string.Empty;
    public FindingSeverity Severity { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; init; } = "active";
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public DateTimeOffset? ResolvedAtUtc { get; init; }
    public bool IsMuted { get; init; }
    public string DeduplicationKey { get; init; } = string.Empty;
    public EvidenceItem[] Evidence { get; init; } = [];
}

public sealed record PairRequest(
    string PairingCode,
    string ControllerName,
    string Role = "readonly");

public sealed record PairResponse(
    string DeviceId,
    string DeviceName,
    string Token,
    string Role,
    DateTimeOffset ExpiresAtUtc)
{
    public string ControllerId { get; init; } = string.Empty;
}

public sealed record PairingInfo(
    string DeviceId,
    string DeviceName,
    string ApiBaseUrl,
    int ApiPort,
    string CertificateSha256,
    string PairingCode,
    string PairingUri,
    string? QrCodeSvg,
    DateTimeOffset CodeExpiresAtUtc,
    string[] LocalAddresses);

public sealed record ScheduleRequest(
    string Name,
    string Cron,
    string ScanMode = "quick",
    string Intensity = "low",
    bool Enabled = true,
    bool RunOnlyWhenIdle = false,
    bool RunOnlyOnAcPower = false,
    bool RunOnlyOnSpecifiedNetwork = false,
    string? NetworkSsid = null,
    int? MaximumRuntimeSeconds = 300,
    int RetryCount = 0);

public sealed record RealtimeEnvelope(
    string Type,
    string DeviceId,
    DateTimeOffset TimestampUtc,
    string DataVersion,
    object? Payload);
public sealed record ServiceInfo(
    string Name,
    string DisplayName,
    string Status,
    string StartType,
    bool CanStop);

public sealed record StartupItemInfo(
    string Name,
    string Scope,
    string CommandMasked,
    string Source,
    bool Enabled);

public sealed record EventLogItem(
    DateTimeOffset? TimeCreatedUtc,
    int EventId,
    string Level,
    string Provider,
    string Message,
    string LogName);

public sealed record InstalledSoftwareInfo(
    string Name,
    string? Version,
    string? Publisher,
    DateOnly? InstallDate,
    string Scope);
public sealed record RuntimeSettingsView(
    int SampleIntervalMs,
    int MinimumSampleIntervalMs,
    int MaximumSampleIntervalMs,
    string Description);

public sealed record UpdateSettingsRequest(int SampleIntervalMs);

public sealed record SmartProbeResponse(
    bool Authorized,
    string Message,
    PhysicalDiskInfo[] Disks);