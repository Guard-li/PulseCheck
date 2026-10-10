using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Collectors;

public sealed class NetworkCollector
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Received, long Sent, DateTimeOffset At)> _last = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _wifiLastRead = DateTimeOffset.MinValue;
    private WifiInfo? _wifiCache;

    public NetworkMetrics Collect()
    {
        var now = DateTimeOffset.UtcNow;
        var adapters = new List<NetworkAdapterInfo>();
        double? totalDown = null;
        double? totalUp = null;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var stats = adapter.GetIPStatistics();
                var current = (stats.BytesReceived, stats.BytesSent, now);
                double? down = null;
                double? up = null;
                lock (_gate)
                {
                    if (_last.TryGetValue(adapter.Id, out var previous))
                    {
                        var seconds = Math.Max(0.1, (now - previous.At).TotalSeconds);
                        down = Math.Max(0, current.BytesReceived - previous.Received) / seconds;
                        up = Math.Max(0, current.BytesSent - previous.Sent) / seconds;
                        totalDown = (totalDown ?? 0) + down;
                        totalUp = (totalUp ?? 0) + up;
                    }
                    _last[adapter.Id] = current;
                }

                var properties = adapter.GetIPProperties();
                var wifi = adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? ReadWifi() : null;
                adapters.Add(new NetworkAdapterInfo
                {
                    Id = adapter.Id,
                    Name = adapter.Name,
                    Description = adapter.Description,
                    Type = adapter.NetworkInterfaceType.ToString(),
                    Status = adapter.OperationalStatus.ToString(),
                    MacMasked = adapter.GetPhysicalAddress().ToString(),
                    IpAddresses = properties.UnicastAddresses.Select(x => x.Address.ToString()).ToArray(),
                    DnsServers = properties.DnsAddresses.Select(x => x.ToString()).ToArray(),
                    Gateways = properties.GatewayAddresses.Select(x => x.Address.ToString()).ToArray(),
                    ReceiveBytes = (ulong)Math.Max(0, current.BytesReceived),
                    SentBytes = (ulong)Math.Max(0, current.BytesSent),
                    DownloadBytesPerSecond = down,
                    UploadBytesPerSecond = up,
                    ReceiveErrors = SafeLong(stats.IncomingPacketsWithErrors),
                    SendErrors = SafeLong(stats.OutgoingPacketsWithErrors),
                    DiscardedPackets = SafeLong(stats.IncomingPacketsDiscarded + stats.OutgoingPacketsDiscarded),
                    LinkSpeed = adapter.Speed > 0 ? $"{adapter.Speed / 1_000_000d:0.##} Mbps" : null,
                    Wifi = wifi
                });
            }
            catch
            {
                // A disabled or virtual adapter may reject statistics; omit it rather than fabricate data.
            }
        }

        return new NetworkMetrics
        {
            Adapters = [.. adapters.OrderByDescending(x => x.Status == "Up").ThenBy(x => x.Name)],
            DownloadBytesPerSecond = totalDown,
            UploadBytesPerSecond = totalUp,
            EstablishedTcpConnections = ReadTcpConnections()
        };
    }

    private static long? SafeLong(long value) => value;

    private static int? ReadTcpConnections()
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length; }
        catch { return null; }
    }

    private WifiInfo? ReadWifi()
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow - _wifiLastRead < TimeSpan.FromSeconds(20)) return _wifiCache;
            _wifiLastRead = DateTimeOffset.UtcNow;
        }
        var output = CollectorUtilities.RunCommand("netsh", "wlan show interfaces", 3000);
        if (string.IsNullOrWhiteSpace(output)) return null;
        string? ssid = null, bssid = null, band = null;
        int? channel = null, rssi = null;
        double? signal = null, receive = null, transmit = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Contains("SSID", StringComparison.OrdinalIgnoreCase) && !key.Contains("BSSID", StringComparison.OrdinalIgnoreCase)) ssid = value;
            else if (key.Contains("BSSID", StringComparison.OrdinalIgnoreCase)) bssid = value;
            else if (key.Contains("Signal", StringComparison.OrdinalIgnoreCase) || key.Contains("信号")) signal = ParseDouble(value);
            else if (key.Contains("RSSI", StringComparison.OrdinalIgnoreCase)) rssi = (int?)ParseDouble(value);
            else if (key.Contains("Channel", StringComparison.OrdinalIgnoreCase) || key.Contains("信道")) channel = (int?)ParseDouble(value);
            else if (key.Contains("Receive rate", StringComparison.OrdinalIgnoreCase) || key.Contains("接收速率")) receive = ParseDouble(value);
            else if (key.Contains("Transmit rate", StringComparison.OrdinalIgnoreCase) || key.Contains("传输速率")) transmit = ParseDouble(value);
            else if (key.Contains("Radio type", StringComparison.OrdinalIgnoreCase) || key.Contains("无线电类型")) band = value;
        }
        _wifiCache = ssid is null && signal is null ? null : new WifiInfo(
            ssid,
            string.IsNullOrWhiteSpace(bssid) ? null : MaskBssid(bssid),
            band,
            channel,
            signal,
            rssi,
            receive,
            transmit);
        return _wifiCache;
    }

    private static double? ParseDouble(string value)
    {
        var match = Regex.Match(value, @"-?\d+(?:\.\d+)?");
        return match.Success && double.TryParse(match.Value, out var result) ? result : null;
    }

    private static string MaskBssid(string value)
    {
        var parts = value.Replace('-', ':').Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"**:**:**:**:{parts[^2]}:{parts[^1]}" : "**";
    }
}