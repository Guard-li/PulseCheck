using Microsoft.Win32;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Collectors;

public sealed class GpuCollector
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private readonly object _gate = new();
    private DateTimeOffset _lastRead = DateTimeOffset.MinValue;
    private GpuMetrics[] _cache = [];

    public GpuMetrics[] Collect()
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow - _lastRead < TimeSpan.FromSeconds(2)) return _cache;
        }
        var values = ReadNvidia();
        if (values.Length == 0) values = ReadRegistryAdapters();
        lock (_gate)
        {
            _cache = values;
            _lastRead = DateTimeOffset.UtcNow;
            return _cache;
        }
    }

    private static GpuMetrics[] ReadNvidia()
    {
        var output = CollectorUtilities.RunCommand(
            "nvidia-smi",
            "--query-gpu=name,utilization.gpu,memory.total,memory.used,temperature.gpu,power.draw,clocks.current.graphics,clocks.current.memory,fan.speed,utilization.encoder,utilization.decoder,driver_version --format=csv,noheader,nounits",
            4000);
        if (string.IsNullOrWhiteSpace(output)) return [];
        var result = new List<GpuMetrics>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length < 12) continue;
            result.Add(new GpuMetrics
            {
                Name = fields[0],
                Vendor = "NVIDIA",
                DriverVersion = fields[11],
                UsagePercent = Parse(fields[1]),
                MemoryTotalBytes = ToBytes(Parse(fields[2])),
                MemoryUsedBytes = ToBytes(Parse(fields[3])),
                TemperatureCelsius = Parse(fields[4]),
                PowerWatts = Parse(fields[5]),
                CoreClockMhz = Parse(fields[6]),
                MemoryClockMhz = Parse(fields[7]),
                FanPercent = Parse(fields[8]),
                EncoderUsagePercent = Parse(fields[9]),
                DecoderUsagePercent = Parse(fields[10]),
                Source = "nvidia-smi",
                RuntimeMetricAvailability = new MetricAvailability(DataAvailability.Available)
            });
        }
        return [.. result];
    }

    private static GpuMetrics[] ReadRegistryAdapters()
    {
        var result = new List<GpuMetrics>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DisplayClass);
            if (key is null) return [];
            foreach (var subName in key.GetSubKeyNames())
            {
                if (!int.TryParse(subName, out _)) continue;
                using var sub = key.OpenSubKey(subName);
                var name = sub?.GetValue("DriverDesc")?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                result.Add(new GpuMetrics
                {
                    Name = name,
                    Vendor = VendorFromName(name),
                    DriverVersion = sub?.GetValue("DriverVersion")?.ToString(),
                    Source = "Windows registry",
                    RuntimeMetricAvailability = new MetricAvailability(DataAvailability.NotSupported, "非 NVIDIA GPU 的实时利用率需要对应厂商 API 或性能计数器")
                });
            }
        }
        catch
        {
            return [];
        }
        return [.. result];
    }

    private static string VendorFromName(string name)
    {
        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return "NVIDIA";
        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return "Intel";
        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) return "AMD";
        return "Unknown";
    }

    private static double? Parse(string value) =>
        double.TryParse(value, out var result) ? result : null;

    private static ulong? ToBytes(double? mb) =>
        mb is null ? null : (ulong)Math.Max(0, mb.Value) * 1024UL * 1024UL;
}