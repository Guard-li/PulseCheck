using PulseCheck.Agent.Data;
using PulseCheck.Agent.Diagnostics;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Services;

public sealed class AlertEngine
{
    private readonly SqliteDatabase _database;
    private readonly DiagnosticEngine _diagnostics;
    private readonly Dictionary<string, AlertRecord> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;

    public AlertEngine(SqliteDatabase database, DiagnosticEngine diagnostics)
    {
        _database = database;
        _diagnostics = diagnostics;
    }

    public async Task<AlertRecord[]> ProcessAsync(MetricSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_loaded)
            {
                var existing = await _database.ListAlertsAsync(includeResolved: false, cancellationToken: cancellationToken);
                foreach (var alert in existing)
                    if (!string.IsNullOrWhiteSpace(alert.DeduplicationKey)) _active[alert.DeduplicationKey] = alert;
                _loaded = true;
            }

            var (_, findings) = _diagnostics.Evaluate(snapshot);
            var current = findings
                .Where(x => x.Severity is FindingSeverity.Warning or FindingSeverity.Critical)
                .ToDictionary(x => $"{x.RuleId}|{snapshot.DeviceId}", StringComparer.OrdinalIgnoreCase);
            var changed = new List<AlertRecord>();
            foreach (var finding in current.Values)
            {
                var key = $"{finding.RuleId}|{snapshot.DeviceId}";
                if (_active.ContainsKey(key)) continue;
                var alert = new AlertRecord
                {
                    RuleId = finding.RuleId,
                    Severity = finding.Severity,
                    Title = finding.Title,
                    Description = finding.Description,
                    DeduplicationKey = key,
                    Evidence = finding.Evidence
                };
                _active[key] = alert;
                changed.Add(alert);
                await _database.SaveAlertAsync(alert, cancellationToken);
            }

            foreach (var key in _active.Keys.Where(x => !current.ContainsKey(x)).ToArray())
            {
                var alert = _active[key];
                var resolved = alert with
                {
                    Status = "resolved",
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    ResolvedAtUtc = DateTimeOffset.UtcNow
                };
                _active.Remove(key);
                changed.Add(resolved);
                await _database.SaveAlertAsync(resolved, cancellationToken);
            }
            return [.. changed];
        }
        finally
        {
            _gate.Release();
        }
    }
}