using PulseCheck.Contracts;

namespace PulseCheck.Agent.Collectors;

public interface IMetricCollector
{
    Task<MetricSnapshot> CollectAsync(bool includeSlowChecks, CancellationToken cancellationToken = default);
}

public sealed class AgentMetricCollector : IMetricCollector, IDisposable
{
    private readonly AgentIdentity _identity;
    private readonly CpuCollector _cpu = new();
    private readonly MemoryCollector _memory = new();
    private readonly DiskCollector _disk = new();
    private readonly NetworkCollector _network = new();
    private readonly GpuCollector _gpu = new();
    private readonly ProcessCollector _processes = new();
    private readonly SystemInfoCollector _system = new();
    private readonly SensorCollector _sensors = new();
    private readonly SemaphoreSlim _collectGate = new(1, 1);

    public AgentMetricCollector(AgentIdentity identity)
    {
        _identity = identity;
    }

    public async Task<MetricSnapshot> CollectAsync(bool includeSlowChecks, CancellationToken cancellationToken = default)
    {
        await _collectGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
            cancellationToken.ThrowIfCancellationRequested();
            var cpu = _cpu.Collect();
            cancellationToken.ThrowIfCancellationRequested();
            var memory = _memory.Collect();
            var disk = _disk.Collect(includeSlowChecks);
            var network = _network.Collect();
            var gpus = _gpu.Collect();
            var system = _system.Collect(includeSlowChecks);
            var sensors = _sensors.Collect(includeSlowChecks);
            var processes = _processes.CollectTop();
            if (sensors.CpuTemperatureCelsius is double temperature)
            {
                cpu = cpu with
                {
                    TemperatureCelsius = temperature,
                    TemperatureAvailability = new MetricAvailability(DataAvailability.Available)
                };
            }
            if (gpus.Length > 0)
            {
                sensors = sensors with
                {
                    GpuTemperatureCelsius = gpus.Select(x => x.TemperatureCelsius).FirstOrDefault(x => x is not null),
                    GpuPowerWatts = gpus.Select(x => x.PowerWatts).FirstOrDefault(x => x is not null)
                };
            }
            cpu = Sanitize(cpu);
            memory = Sanitize(memory);
            disk = Sanitize(disk);
            network = Sanitize(network);
            gpus = gpus.Select(Sanitize).ToArray();
            sensors = Sanitize(sensors);
            processes = processes.Select(Sanitize).ToArray();
            var snapshot = new MetricSnapshot

            {
                DeviceId = _identity.DeviceId,
                TimestampUtc = DateTimeOffset.UtcNow,
                Cpu = cpu,
                Memory = memory,
                Gpus = gpus,
                Disk = disk,
                Network = network,
                System = system,
                Sensors = sensors,
                TopProcesses = processes
            };
                return snapshot;
            }, cancellationToken);
        }
        finally
        {
            _collectGate.Release();
        }
    }

    private static CpuMetrics Sanitize(CpuMetrics value) => value with
    {
        TotalUsagePercent = Finite(value.TotalUsagePercent) ?? 0,
        PerCoreUsagePercent = value.PerCoreUsagePercent.Select(x => Finite(x) ?? 0).ToArray(),
        CurrentFrequencyMhz = Finite(value.CurrentFrequencyMhz),
        BaseFrequencyMhz = Finite(value.BaseFrequencyMhz),
        MaxFrequencyMhz = Finite(value.MaxFrequencyMhz),
        TemperatureCelsius = Finite(value.TemperatureCelsius),
        PackagePowerWatts = Finite(value.PackagePowerWatts),
        InterruptTimePercent = Finite(value.InterruptTimePercent),
        DpcTimePercent = Finite(value.DpcTimePercent)
    };

    private static MemoryMetrics Sanitize(MemoryMetrics value) => value with
    {
        UsagePercent = Finite(value.UsagePercent) ?? 0
    };

    private static GpuMetrics Sanitize(GpuMetrics value) => value with
    {
        UsagePercent = Finite(value.UsagePercent),
        TemperatureCelsius = Finite(value.TemperatureCelsius),
        PowerWatts = Finite(value.PowerWatts),
        CoreClockMhz = Finite(value.CoreClockMhz),
        MemoryClockMhz = Finite(value.MemoryClockMhz),
        FanPercent = Finite(value.FanPercent),
        EncoderUsagePercent = Finite(value.EncoderUsagePercent),
        DecoderUsagePercent = Finite(value.DecoderUsagePercent)
    };

    private static DiskMetrics Sanitize(DiskMetrics value) => value with
    {
        Volumes = value.Volumes.Select(x => x with { UsedPercent = Finite(x.UsedPercent) ?? 0 }).ToArray(),
        PhysicalDisks = value.PhysicalDisks.Select(x => x with
        {
            TemperatureCelsius = Finite(x.TemperatureCelsius),
            RemainingLifePercent = Finite(x.RemainingLifePercent)
        }).ToArray(),
        TotalReadBytesPerSecond = Finite(value.TotalReadBytesPerSecond),
        TotalWriteBytesPerSecond = Finite(value.TotalWriteBytesPerSecond),
        AverageResponseTimeMilliseconds = Finite(value.AverageResponseTimeMilliseconds),
        BusyTimePercent = Finite(value.BusyTimePercent)
    };

    private static NetworkMetrics Sanitize(NetworkMetrics value) => value with
    {
        Adapters = value.Adapters.Select(x => x with
        {
            DownloadBytesPerSecond = Finite(x.DownloadBytesPerSecond),
            UploadBytesPerSecond = Finite(x.UploadBytesPerSecond),
            Wifi = x.Wifi is null ? null : x.Wifi with
            {
                SignalPercent = Finite(x.Wifi.SignalPercent),
                ReceiveRateMbps = Finite(x.Wifi.ReceiveRateMbps),
                TransmitRateMbps = Finite(x.Wifi.TransmitRateMbps)
            }
        }).ToArray(),
        DownloadBytesPerSecond = Finite(value.DownloadBytesPerSecond),
        UploadBytesPerSecond = Finite(value.UploadBytesPerSecond),
        LanLatencyMs = Finite(value.LanLatencyMs),
        InternetLatencyMs = Finite(value.InternetLatencyMs),
        DnsResolutionMs = Finite(value.DnsResolutionMs),
        JitterMs = Finite(value.JitterMs),
        PacketLossPercent = Finite(value.PacketLossPercent)
    };

    private static SystemMetrics Sanitize(SystemMetrics value) => value with
    {
        BatteryChargePercent = Finite(value.BatteryChargePercent),
        BatteryHealthPercent = Finite(value.BatteryHealthPercent)
    };

    private static SensorMetrics Sanitize(SensorMetrics value) => value with
    {
        CpuTemperatureCelsius = Finite(value.CpuTemperatureCelsius),
        CpuPowerWatts = Finite(value.CpuPowerWatts),
        GpuTemperatureCelsius = Finite(value.GpuTemperatureCelsius),
        GpuPowerWatts = Finite(value.GpuPowerWatts),
        FanRpm = Finite(value.FanRpm)
    };

    private static ProcessMetric Sanitize(ProcessMetric value) => value with
    {
        CpuUsagePercent = Finite(value.CpuUsagePercent)
    };

    private static double? Finite(double? value) => value is not null && double.IsFinite(value.Value) ? value : null;
    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
    public void Dispose() => _disk.Dispose();
}