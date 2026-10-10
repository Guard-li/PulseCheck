using PulseCheck.Contracts;

namespace PulseCheck.Agent.Reporting;

public static class PrivacyRedactor
{
    public static HealthReport Redact(HealthReport report)
    {
        var snapshot = report.Snapshot;
        var redactedAdapters = snapshot.Network.Adapters.Select(adapter => adapter with
        {
            MacMasked = MaskMac(adapter.MacMasked),
            IpAddresses = adapter.IpAddresses.Select(MaskIp).ToArray(),
            DnsServers = adapter.DnsServers.Select(MaskIp).ToArray(),
            Gateways = adapter.Gateways.Select(MaskIp).ToArray(),
            Wifi = adapter.Wifi is null ? null : adapter.Wifi with { BssidMasked = MaskMac(adapter.Wifi.BssidMasked) }
        }).ToArray();
        var redactedProcesses = snapshot.TopProcesses.Select(process => process with
        {
            ExecutablePathMasked = string.IsNullOrWhiteSpace(process.ExecutablePathMasked) ? null : "***"
        }).ToArray();
        var redactedSnapshot = snapshot with
        {
            DeviceId = MaskDeviceId(snapshot.DeviceId),
            Network = snapshot.Network with { Adapters = redactedAdapters },
            TopProcesses = redactedProcesses,
            System = snapshot.System with { UserNameMasked = "***" }
        };
        return report with
        {
            Device = report.Device with { DeviceId = MaskDeviceId(report.Device.DeviceId) },
            Snapshot = redactedSnapshot,
            PrivacyMode = "redacted"
        };
    }

    private static string MaskDeviceId(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Length < 8 ? "***" : $"***{value[^8..]}";

    private static string MaskMac(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : "**:**:**:**:**:**";

    private static string MaskIp(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (value.Contains(':')) return "****:****:****:****::****";
        var parts = value.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.***.***" : "***";
    }
}