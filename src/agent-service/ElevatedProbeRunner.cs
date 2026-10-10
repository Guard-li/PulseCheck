using System.Text.Json;
using PulseCheck.Agent.Collectors;
using PulseCheck.Agent.Security;
using PulseCheck.Contracts;

namespace PulseCheck.AgentService;

public static class ElevatedProbeRunner
{
    public static bool IsProbe(string[] args) =>
        args.Length >= 2 && string.Equals(args[0], "--elevated-probe", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        if (args.Length < 3 || !string.Equals(args[1], "smart", StringComparison.OrdinalIgnoreCase)) return 2;
        var outputPath = args[2];
        try
        {
            using var collector = new DiskCollector();
            var disks = collector.Collect(includeSmart: true).PhysicalDisks;
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new SmartProbeFile(true, null, disks), JsonDefaults.Create(true)));
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(outputPath, JsonSerializer.Serialize(new SmartProbeFile(false, ex.Message, []), JsonDefaults.Create(true))); } catch { }
            return 1;
        }
    }
}