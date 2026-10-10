using System.Globalization;
using System.Reflection;
using System.Text.Json;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Diagnostics;

public sealed class HealthRuleSet
{
    public string Version { get; init; } = "1.0";
    public DimensionDefinition[] Dimensions { get; init; } = [];
    public HealthRuleDefinition[] Rules { get; init; } = [];
}

public sealed class DimensionDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public double Weight { get; init; }
    public int Order { get; init; }
}

public sealed class HealthRuleDefinition
{
    public string Id { get; init; } = string.Empty;
    public string Dimension { get; init; } = string.Empty;
    public FindingSeverity Severity { get; init; }
    public string MetricPath { get; init; } = string.Empty;
    public string Comparison { get; init; } = "greaterThan";
    public double? Threshold { get; init; }
    public int ConsecutiveSamples { get; init; } = 1;
    public double Penalty { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string[] PossibleCauses { get; init; } = [];
    public string[] Recommendations { get; init; } = [];
    public bool RequiresAdministrator { get; init; }
    public string? ReferenceUrl { get; init; }
}

public static class HealthRuleSetLoader
{
    public static HealthRuleSet Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("健康规则配置不存在", path);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<HealthRuleSet>(json, JsonDefaults.Create())
            ?? throw new InvalidDataException("健康规则配置为空或格式无效");
    }
}