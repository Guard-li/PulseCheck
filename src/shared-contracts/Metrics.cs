namespace PulseCheck.Contracts;

public sealed record MetricSnapshot
{
    public Guid SnapshotId { get; init; } = Guid.NewGuid();
    public string DeviceId { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string DataVersion { get; init; } = PulseCheckConstants.DataVersion;
    public required CpuMetrics Cpu { get; init; }
    public required MemoryMetrics Memory { get; init; }
    public GpuMetrics[] Gpus { get; init; } = [];
    public DiskMetrics Disk { get; init; } = new();
    public NetworkMetrics Network { get; init; } = new();
    public SystemMetrics System { get; init; } = new();
    public SensorMetrics Sensors { get; init; } = new();
    public ProcessMetric[] TopProcesses { get; init; } = [];
}

public sealed record CpuMetrics
{
    public string Name { get; init; } = "Unknown CPU";
    public string? Manufacturer { get; init; }
    public string? ProcessorId { get; init; }
    public int PhysicalCoreCount { get; init; }
    public int LogicalProcessorCount { get; init; }
    public double TotalUsagePercent { get; init; }
    public double[] PerCoreUsagePercent { get; init; } = [];
    public double? CurrentFrequencyMhz { get; init; }
    public double? BaseFrequencyMhz { get; init; }
    public double? MaxFrequencyMhz { get; init; }
    public double? TemperatureCelsius { get; init; }
    public double? PackagePowerWatts { get; init; }
    public bool? ThermalThrottling { get; init; }
    public long? ContextSwitchCount { get; init; }
    public double? InterruptTimePercent { get; init; }
    public double? DpcTimePercent { get; init; }
    public MetricAvailability TemperatureAvailability { get; init; } = new(DataAvailability.Unavailable, "需要传感器驱动或 BIOS 支持");
}

public sealed record MemoryMetrics
{
    public ulong TotalBytes { get; init; }
    public ulong AvailableBytes { get; init; }
    public ulong UsedBytes { get; init; }
    public double UsagePercent { get; init; }
    public ulong? CommittedBytes { get; init; }
    public ulong? CommitLimitBytes { get; init; }
    public ulong? CachedBytes { get; init; }
    public ulong? PageFileTotalBytes { get; init; }
    public ulong? PageFileAvailableBytes { get; init; }
    public ulong? HardwareReservedBytes { get; init; }
    public MemoryModuleInfo[] Modules { get; init; } = [];
}

public sealed record MemoryModuleInfo(
    string? BankLabel,
    string? Manufacturer,
    string? PartNumber,
    string? SerialMasked,
    ulong? CapacityBytes,
    uint? SpeedMhz,
    string? FormFactor);

public sealed record GpuMetrics
{
    public string Name { get; init; } = "Unknown GPU";
    public string? Vendor { get; init; }
    public string? DriverVersion { get; init; }
    public double? UsagePercent { get; init; }
    public ulong? MemoryTotalBytes { get; init; }
    public ulong? MemoryUsedBytes { get; init; }
    public double? TemperatureCelsius { get; init; }
    public double? PowerWatts { get; init; }
    public double? CoreClockMhz { get; init; }
    public double? MemoryClockMhz { get; init; }
    public double? FanPercent { get; init; }
    public double? EncoderUsagePercent { get; init; }
    public double? DecoderUsagePercent { get; init; }
    public string Source { get; init; } = "registry";
    public MetricAvailability RuntimeMetricAvailability { get; init; } = new(DataAvailability.Available);
}

public sealed record DiskMetrics
{
    public VolumeInfo[] Volumes { get; init; } = [];
    public PhysicalDiskInfo[] PhysicalDisks { get; init; } = [];
    public double? TotalReadBytesPerSecond { get; init; }
    public double? TotalWriteBytesPerSecond { get; init; }
    public double? AverageResponseTimeMilliseconds { get; init; }
    public double? BusyTimePercent { get; init; }
    public MetricAvailability SmartAvailability { get; init; } = new(DataAvailability.RequiresAdministrator, "SMART 需要存储驱动和管理员权限");
}

public sealed record VolumeInfo(
    string Name,
    string Label,
    string DriveType,
    string FileSystem,
    ulong TotalBytes,
    ulong FreeBytes,
    double UsedPercent,
    bool IsSystemVolume,
    bool IsReady,
    string? HealthStatus);

public sealed record PhysicalDiskInfo(
    string Id,
    string FriendlyName,
    string MediaType,
    string BusType,
    ulong SizeBytes,
    string? HealthStatus,
    string? OperationalStatus,
    double? TemperatureCelsius,
    double? RemainingLifePercent,
    ulong? PowerOnHours,
    string? SerialMasked,
    string? FirmwareVersion);

public sealed record NetworkMetrics
{
    public NetworkAdapterInfo[] Adapters { get; init; } = [];
    public double? DownloadBytesPerSecond { get; init; }
    public double? UploadBytesPerSecond { get; init; }
    public int? EstablishedTcpConnections { get; init; }
    public double? LanLatencyMs { get; init; }
    public double? InternetLatencyMs { get; init; }
    public double? DnsResolutionMs { get; init; }
    public double? JitterMs { get; init; }
    public double? PacketLossPercent { get; init; }
    public string? TracerouteSummary { get; init; }
}

public sealed record NetworkAdapterInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? MacMasked { get; init; }
    public string[] IpAddresses { get; init; } = [];
    public string[] DnsServers { get; init; } = [];
    public string[] Gateways { get; init; } = [];
    public ulong ReceiveBytes { get; init; }
    public ulong SentBytes { get; init; }
    public double? DownloadBytesPerSecond { get; init; }
    public double? UploadBytesPerSecond { get; init; }
    public long? ReceiveErrors { get; init; }
    public long? SendErrors { get; init; }
    public long? DiscardedPackets { get; init; }
    public string? LinkSpeed { get; init; }
    public string? DriverVersion { get; init; }
    public WifiInfo? Wifi { get; init; }
}

public sealed record WifiInfo(
    string? Ssid,
    string? BssidMasked,
    string? Band,
    int? Channel,
    double? SignalPercent,
    int? RssiDbm,
    double? ReceiveRateMbps,
    double? TransmitRateMbps);

public sealed record SystemMetrics
{
    public string ComputerName { get; init; } = Environment.MachineName;
    public string UserNameMasked { get; init; } = "***";
    public bool IsAdministrator { get; init; }
    public OperatingMode OperatingMode { get; init; }
    public string OsName { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string OsBuild { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public DateTimeOffset BootTimeUtc { get; init; }
    public TimeSpan Uptime { get; init; }
    public string? PowerPlan { get; init; }
    public bool? OnAcPower { get; init; }
    public double? BatteryChargePercent { get; init; }
    public double? BatteryHealthPercent { get; init; }
    public string? BiosVendor { get; init; }
    public string? BiosVersion { get; init; }
    public DateOnly? BiosReleaseDate { get; init; }
    public string? BaseboardManufacturer { get; init; }
    public string? BaseboardProduct { get; init; }
    public bool? PendingReboot { get; init; }
    public bool? DefenderEnabled { get; init; }
    public bool? FirewallEnabled { get; init; }
    public bool? SecureBootEnabled { get; init; }
    public bool? TimeSynchronized { get; init; }
    public DateTimeOffset? LastSecurityUpdateUtc { get; init; }
    public string[] Warnings { get; init; } = [];
}

public sealed record SensorMetrics
{
    public double? CpuTemperatureCelsius { get; init; }
    public double? CpuPowerWatts { get; init; }
    public double? GpuTemperatureCelsius { get; init; }
    public double? GpuPowerWatts { get; init; }
    public double? FanRpm { get; init; }
    public MetricAvailability Availability { get; init; } = new(DataAvailability.RequiresSensorDriver, "完整传感器读取需要 LibreHardwareMonitor 驱动；不会自动安装未签名驱动");
}

public sealed record ProcessMetric(
    int ProcessId,
    string Name,
    double? CpuUsagePercent,
    ulong WorkingSetBytes,
    ulong PrivateBytes,
    DateTimeOffset? StartTimeUtc,
    string? ExecutablePathMasked,
    string? Company,
    string? Description,
    bool IsElevated);