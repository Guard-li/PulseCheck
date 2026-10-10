using PulseCheck.Contracts;

namespace PulseCheck.Agent;

public sealed class AgentOptions
{
    public string ProductName { get; set; } = PulseCheckConstants.ProductName;
    public string DisplayName { get; set; } = Environment.MachineName;
    public int ApiPort { get; set; } = PulseCheckConstants.DefaultApiPort;
    public int DiscoveryPort { get; set; } = PulseCheckConstants.DefaultDiscoveryPort;
    public int SampleIntervalMs { get; set; } = 1000;
    public int RawRetentionDays { get; set; } = 7;
    public int AggregateRetentionDays { get; set; } = 90;
    public bool EnableInternetProbe { get; set; }
    public bool EnableUdpDiscovery { get; set; } = true;
    public bool RequireTls { get; set; } = true;
    public string DataDirectory { get; set; } = "data";
    public string ReportDirectory { get; set; } = "reports";
    public int PairingCodeLifetimeMinutes { get; set; } = 15;
    public int MaxReportHistory { get; set; } = 200;
    public string[] TrustedNetworks { get; set; } = [];
}