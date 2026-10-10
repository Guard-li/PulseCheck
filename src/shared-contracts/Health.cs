namespace PulseCheck.Contracts;

public enum FindingSeverity
{
    Information,
    Notice,
    Warning,
    Critical
}

public enum HealthStatus
{
    Excellent,
    Good,
    Attention,
    Warning,
    Critical
}

public sealed record EvidenceItem(
    string Key,
    string Value,
    string Source,
    DateTimeOffset ObservedAtUtc,
    string? MetricPath = null);

public sealed record DiagnosticFinding
{
    public string RuleId { get; init; } = string.Empty;
    public FindingSeverity Severity { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string[] PossibleCauses { get; init; } = [];
    public string[] Recommendations { get; init; } = [];
    public EvidenceItem[] Evidence { get; init; } = [];
    public bool RequiresAdministrator { get; init; }
    public string? ReferenceUrl { get; init; }
}

public sealed record HealthDimension
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double Score { get; init; }
    public double Weight { get; init; }
    public HealthStatus Status { get; init; }
    public string[] Evidence { get; init; } = [];
    public string[] Recommendations { get; init; } = [];
    public bool RequiresAdministrator { get; init; }
}

public sealed record HealthScore
{
    public double OverallScore { get; init; }
    public HealthStatus Status { get; init; }
    public HealthDimension[] Dimensions { get; init; } = [];
    public string ScoringRuleVersion { get; init; } = "1.0";
}

public sealed record DeviceSummary
{
    public string DeviceId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ComputerName { get; init; } = string.Empty;
    public string OsDescription { get; init; } = string.Empty;
    public string AgentVersion { get; init; } = string.Empty;
}

public sealed record ReportArtifact(
    string Format,
    string FileName,
    string RelativePath,
    long SizeBytes,
    string Sha256);

public sealed record HealthReport
{
    public Guid ReportId { get; init; } = Guid.NewGuid();
    public Guid ScanId { get; init; }
    public DeviceSummary Device { get; init; } = new();
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ScanStartedAtUtc { get; init; }
    public DateTimeOffset ScanFinishedAtUtc { get; init; }
    public string ScanMode { get; init; } = "quick";
    public bool IsAdministrator { get; init; }
    public OperatingMode OperatingMode { get; init; }
    public HealthScore Health { get; init; } = new();
    public string RiskLevel { get; init; } = "低";
    public DiagnosticFinding[] Findings { get; init; } = [];
    public MetricSnapshot Snapshot { get; init; } = null!;
    public Dictionary<string, string> Evidence { get; init; } = [];
    public string[] Limitations { get; init; } = [];
    public ReportArtifact[] Artifacts { get; init; } = [];
    public string ReportHash { get; init; } = string.Empty;
    public string PrivacyMode { get; init; } = "default-masked";
}