using System.Text.Json;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Collectors;

public sealed class DiskCollector : IDisposable
{
    private readonly object _gate = new();
    private PdhMetricsReader? _performance;
    private bool _performanceInitialized;

    public DiskMetrics Collect(bool includeSmart)
    {
        var volumes = CollectVolumes();
        var physical = includeSmart ? CollectPhysicalDisks() : [];
        (double? read, double? write, double? response, double? busy) performance = (null, null, null, null);
        lock (_gate)
        {
            if (!_performanceInitialized)
            {
                _performance = PdhMetricsReader.TryCreate();
                _performanceInitialized = true;
            }
            if (_performance is not null) performance = _performance.Read();
        }

        return new DiskMetrics
        {
            Volumes = volumes,
            PhysicalDisks = physical,
            TotalReadBytesPerSecond = performance.read,
            TotalWriteBytesPerSecond = performance.write,
            AverageResponseTimeMilliseconds = performance.response,
            BusyTimePercent = performance.busy,
            SmartAvailability = physical.Length > 0
                ? new MetricAvailability(DataAvailability.Available)
                : new MetricAvailability(DataAvailability.RequiresAdministrator, "需要管理员权限和 Windows Storage 管理组件读取 SMART")
        };
    }

    private static VolumeInfo[] CollectVolumes()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') + "\\";
        var result = new List<VolumeInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                var name = drive.Name;
                var ready = drive.IsReady;
                var total = ready ? (ulong)drive.TotalSize : 0;
                var free = ready ? (ulong)drive.AvailableFreeSpace : 0;
                result.Add(new VolumeInfo(
                    name,
                    ready ? drive.VolumeLabel : string.Empty,
                    drive.DriveType.ToString(),
                    ready ? drive.DriveFormat : string.Empty,
                    total,
                    free,
                    ready ? CollectorUtilities.Percent(total - free, total) : 0,
                    string.Equals(name, systemRoot, StringComparison.OrdinalIgnoreCase),
                    ready,
                    null));
            }
            catch
            {
                result.Add(new VolumeInfo(drive.Name, string.Empty, drive.DriveType.ToString(), string.Empty, 0, 0, 0, false, false, "权限不足或卷不可读"));
            }
        }
        return [.. result.OrderByDescending(x => x.IsSystemVolume).ThenBy(x => x.Name)];
    }

    private static PhysicalDiskInfo[] CollectPhysicalDisks()
    {
        using var disksDocument = CollectorUtilities.RunPowerShellJson(
            "Get-PhysicalDisk | Select-Object DeviceId,FriendlyName,MediaType,BusType,Size,HealthStatus,OperationalStatus,SerialNumber,FirmwareVersion | ConvertTo-Json -Compress",
            10000);
        if (disksDocument is null) return [];
        var reliability = new Dictionary<string, (double? Temperature, double? Wear, ulong? Hours)>(StringComparer.OrdinalIgnoreCase);
        using (var reliabilityDocument = CollectorUtilities.RunPowerShellJson(
                   "Get-PhysicalDisk | Get-StorageReliabilityCounter | Select-Object DeviceId,Temperature,Wear,PowerOnHours | ConvertTo-Json -Compress",
                   10000))
        {
            if (reliabilityDocument is not null)
            {
                foreach (var item in Enumerate(reliabilityDocument.RootElement))
                {
                    var id = GetString(item, "DeviceId");
                    reliability[id ?? string.Empty] = (
                        GetDouble(item, "Temperature"),
                        GetDouble(item, "Wear"),
                        GetUlong(item, "PowerOnHours"));
                }
            }
        }

        var results = new List<PhysicalDiskInfo>();
        foreach (var item in Enumerate(disksDocument.RootElement))
        {
            var id = GetString(item, "DeviceId") ?? Guid.NewGuid().ToString("N");
            reliability.TryGetValue(id, out var health);
            var serial = GetString(item, "SerialNumber");
            var wear = health.Wear;
            results.Add(new PhysicalDiskInfo(
                id,
                GetString(item, "FriendlyName") ?? "Unknown disk",
                GetString(item, "MediaType") ?? "Unspecified",
                GetString(item, "BusType") ?? "Unknown",
                GetUlong(item, "Size") ?? 0,
                GetString(item, "HealthStatus"),
                GetString(item, "OperationalStatus"),
                health.Temperature,
                wear is null ? null : Math.Round(Math.Clamp(100 - wear.Value, 0, 100), 1),
                health.Hours,
                string.IsNullOrWhiteSpace(serial) ? null : (serial.Length <= 4 ? "***" : $"***{serial[^4..]}"),
                GetString(item, "FirmwareVersion")));
        }
        return [.. results];
    }

    private static IEnumerable<JsonElement> Enumerate(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) yield return item;
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : null;

    private static double? GetDouble(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.TryGetDouble(out var result) ? result : null;
    }

    private static ulong? GetUlong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.TryGetUInt64(out var result) ? result : null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _performance?.Dispose();
            _performance = null;
        }
    }
}