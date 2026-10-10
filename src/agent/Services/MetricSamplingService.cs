using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseCheck.Agent.Collectors;
using PulseCheck.Agent.Data;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Services;

public sealed class MetricSamplingService : BackgroundService
{
    private readonly IMetricCollector _collector;
    private readonly SqliteDatabase _database;
    private readonly RealtimeHub _hub;
    private readonly AlertEngine _alerts;
    private readonly AgentIdentity _identity;
    private readonly AgentOptions _options;
    private readonly RuntimeSettingsStore _runtimeSettings;
    private readonly ILogger<MetricSamplingService> _logger;
    private DateTimeOffset _lastCleanupUtc = DateTimeOffset.MinValue;

    public MetricSamplingService(
        IMetricCollector collector,
        SqliteDatabase database,
        RealtimeHub hub,
        AlertEngine alerts,
        AgentIdentity identity,
        AgentOptions options,
        RuntimeSettingsStore runtimeSettings,
        ILogger<MetricSamplingService> logger)
    {
        _collector = collector;
        _database = database;
        _hub = hub;
        _alerts = alerts;
        _identity = identity;
        _options = options;
        _runtimeSettings = runtimeSettings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var started = DateTimeOffset.UtcNow;
            try
            {
                var snapshot = await _collector.CollectAsync(includeSlowChecks: false, stoppingToken);
                _hub.Publish(new RealtimeEnvelope("metrics", _identity.DeviceId, snapshot.TimestampUtc, snapshot.DataVersion, snapshot));
                await _database.InsertMetricAsync(snapshot, stoppingToken);
                var changes = await _alerts.ProcessAsync(snapshot, stoppingToken);
                foreach (var alert in changes)
                    _hub.Publish(new RealtimeEnvelope("alert", _identity.DeviceId, DateTimeOffset.UtcNow, snapshot.DataVersion, alert));
                if (DateTimeOffset.UtcNow - _lastCleanupUtc > TimeSpan.FromHours(6))
                {
                    await _database.DeleteMetricsBeforeAsync(DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.RawRetentionDays)), stoppingToken);
                    _lastCleanupUtc = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "指标采集周期失败");
            }

            var interval = _runtimeSettings.SampleIntervalMs;
            var remaining = TimeSpan.FromMilliseconds(interval) - (DateTimeOffset.UtcNow - started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, stoppingToken);
        }
    }
}