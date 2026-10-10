using System.Text.Json;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Collectors;

public sealed class SensorCollector
{
    public SensorMetrics Collect(bool includeSlowProbe)
    {
        double? cpuTemperature = null;
        if (includeSlowProbe)
        {
            using var document = CollectorUtilities.RunPowerShellJson(
                "Get-CimInstance -Namespace root/WMI -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty CurrentTemperature | ConvertTo-Json -Compress",
                5000);
            if (document is not null && document.RootElement.ValueKind == JsonValueKind.Number && document.RootElement.TryGetDouble(out var tenthsKelvin))
                cpuTemperature = Math.Round(tenthsKelvin / 10d - 273.15, 1);
        }
        return new SensorMetrics
        {
            CpuTemperatureCelsius = cpuTemperature,
            CpuPowerWatts = null,
            GpuTemperatureCelsius = null,
            GpuPowerWatts = null,
            FanRpm = null,
            Availability = cpuTemperature is null
                ? new MetricAvailability(DataAvailability.RequiresSensorDriver, "未检测到标准 ACPI 温度传感器；不会用估算值伪造温度")
                : new MetricAvailability(DataAvailability.Available)
        };
    }
}