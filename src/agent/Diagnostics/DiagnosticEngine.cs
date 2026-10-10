using System.Globalization;
using System.Reflection;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Diagnostics;

public sealed class DiagnosticEngine
{
    private readonly HealthRuleSet _rules;

    public DiagnosticEngine(HealthRuleSet rules)
    {
        _rules = rules;
        ValidateRules(_rules);
    }

    public (HealthScore Score, DiagnosticFinding[] Findings) Evaluate(MetricSnapshot snapshot)
    {
        var findings = new List<DiagnosticFinding>();
        foreach (var rule in _rules.Rules)
        {
            var value = MetricPathResolver.Resolve(snapshot, rule.MetricPath);
            if (value is null || !Matches(value, rule.Comparison, rule.Threshold)) continue;
            findings.Add(new DiagnosticFinding
            {
                RuleId = rule.Id,
                Severity = rule.Severity,
                Title = rule.Title,
                Description = FormatDescription(rule.Description, value, rule.Threshold),
                PossibleCauses = rule.PossibleCauses,
                Recommendations = rule.Recommendations,
                RequiresAdministrator = rule.RequiresAdministrator,
                ReferenceUrl = rule.ReferenceUrl,
                Evidence = [new EvidenceItem(rule.Id, FormatValue(value), "metric", snapshot.TimestampUtc, rule.MetricPath)]
            });
        }

        var dimensions = new List<HealthDimension>();
        foreach (var definition in _rules.Dimensions.OrderBy(x => x.Order))
        {
            var dimensionFindings = findings.Where(x => x.RuleId is not null && RuleDimension(x.RuleId) == definition.Id).ToArray();
            var penalty = dimensionFindings.Sum(x => _rules.Rules.First(r => r.Id == x.RuleId).Penalty);
            var score = Math.Round(Math.Clamp(100 - penalty, 0, 100), 1);
            dimensions.Add(new HealthDimension
            {
                Id = definition.Id,
                Name = definition.Name,
                Weight = definition.Weight,
                Score = score,
                Status = ToStatus(score),
                Evidence = [.. dimensionFindings.Select(x => x.Description)],
                Recommendations = [.. dimensionFindings.SelectMany(x => x.Recommendations).Distinct(StringComparer.Ordinal)],
                RequiresAdministrator = dimensionFindings.Any(x => x.RequiresAdministrator)
            });
        }

        var totalWeight = dimensions.Sum(x => x.Weight);
        var overall = totalWeight <= 0 ? 100 : Math.Round(dimensions.Sum(x => x.Score * x.Weight) / totalWeight, 1);
        return (new HealthScore
        {
            OverallScore = overall,
            Status = ToStatus(overall),
            Dimensions = [.. dimensions],
            ScoringRuleVersion = _rules.Version
        }, [.. findings.OrderByDescending(x => x.Severity).ThenBy(x => x.RuleId)]);
    }

    private string RuleDimension(string ruleId) =>
        _rules.Rules.FirstOrDefault(x => x.Id == ruleId)?.Dimension ?? "stability";

    private static bool Matches(object value, string comparison, double? threshold)
    {
        var numeric = ToDouble(value);
        if (numeric is not null)
        {
            return comparison.ToLowerInvariant() switch
            {
                "greaterthan" => numeric > threshold,
                "greaterorequal" => numeric >= threshold,
                "lessthan" => numeric < threshold,
                "lessorequal" => numeric <= threshold,
                "equals" => Math.Abs(numeric.Value - (threshold ?? 0)) < 0.0001,
                "notequals" => Math.Abs(numeric.Value - (threshold ?? 0)) >= 0.0001,
                _ => false
            };
        }

        var boolean = value as bool?;
        return comparison.ToLowerInvariant() switch
        {
            "istrue" => boolean == true,
            "isfalse" => boolean == false,
            "equals" => string.Equals(value.ToString(), threshold?.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string FormatDescription(string template, object value, double? threshold)
    {
        var formattedValue = FormatValue(value);
        var numeric = ToDouble(value) ?? threshold ?? 0;
        return template
            .Replace("{value:F1}", numeric.ToString("F1", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{threshold:F1}", (threshold ?? 0).ToString("F1", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{value}", formattedValue, StringComparison.Ordinal);
    }

    private static string FormatValue(object value) => value switch
    {
        bool boolean => boolean ? "是" : "否",
        IFormattable formattable => formattable.ToString("0.##", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static double? ToDouble(object value)
    {
        if (value is bool) return null;
        try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static HealthStatus ToStatus(double score) => score switch
    {
        >= 90 => HealthStatus.Excellent,
        >= 80 => HealthStatus.Good,
        >= 65 => HealthStatus.Attention,
        >= 40 => HealthStatus.Warning,
        _ => HealthStatus.Critical
    };

    private static void ValidateRules(HealthRuleSet rules)
    {
        if (rules.Dimensions.Length == 0) throw new InvalidDataException("健康评分至少需要一个维度");
        if (rules.Rules.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != rules.Rules.Length)
            throw new InvalidDataException("健康规则 ID 必须唯一");
        var dimensions = rules.Dimensions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var rule in rules.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.MetricPath))
                throw new InvalidDataException("健康规则必须包含 ID 和 metricPath");
            if (!dimensions.Contains(rule.Dimension))
                throw new InvalidDataException($"规则 {rule.Id} 引用了不存在的维度 {rule.Dimension}");
        }
    }
}