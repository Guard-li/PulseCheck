using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PulseCheck.Agent;
using PulseCheck.Agent.Collectors;
using PulseCheck.Agent.Data;
using PulseCheck.Agent.Inventory;
using PulseCheck.Agent.Scheduling;
using PulseCheck.Agent.Security;
using PulseCheck.Agent.Services;
using PulseCheck.Contracts;

namespace PulseCheck.AgentService;

public static class ApiEndpoints
{
    private static readonly JsonSerializerOptions Json = JsonDefaults.Create();

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/health", (AgentIdentity identity, SqliteDatabase database) => Results.Ok(ApiResponse<object>.Ok(new
        {
            status = "ok",
            product = PulseCheckConstants.ProductName,
            version = identity.Version,
            deviceId = identity.DeviceId,
            dataVersion = PulseCheckConstants.DataVersion,
            utc = DateTimeOffset.UtcNow
        })));

        api.MapPost("/auth/pair", (PairRequest request, AgentSecurity security) =>
        {
            var response = security.Pair(request);
            return response is null
                ? Results.Json(ApiResponse<object>.Fail("PAIRING_CODE_INVALID", "配对码无效或已过期"), statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(ApiResponse<PairResponse>.Ok(response));
        });
        api.MapPost("/auth/token", (PairRequest request, AgentSecurity security) =>
        {
            var response = security.Pair(request);
            return response is null
                ? Results.Json(ApiResponse<object>.Fail("PAIRING_CODE_INVALID", "配对码无效或已过期"), statusCode: StatusCodes.Status401Unauthorized)
                : Results.Ok(ApiResponse<PairResponse>.Ok(response));
        });
        api.MapGet("/pairing/info", (HttpContext context, AgentOptions options, AgentIdentity identity, AgentSecurity security, AgentCertificate certificate) =>
        {
            if (!ApiAuthMiddleware.IsLoopback(context.Connection.RemoteIpAddress)) return Results.Forbid();
            var addresses = CertificateManager.GetLocalAddresses(includeLoopback: false);
            var host = addresses.FirstOrDefault() ?? Environment.MachineName;
            var uri = $"pulsecheck://pair?host={Uri.EscapeDataString(host)}&port={options.ApiPort}&deviceId={Uri.EscapeDataString(identity.DeviceId)}&fingerprint={certificate.Sha256Fingerprint}&code={security.CurrentPairingCode}";
            return Results.Ok(ApiResponse<PairingInfo>.Ok(new PairingInfo(
                identity.DeviceId,
                identity.DisplayName,
                $"https://{host}:{options.ApiPort}",
                options.ApiPort,
                certificate.Sha256Fingerprint,
                security.CurrentPairingCode,
                uri,
                null,
                security.PairingCodeExpiresAtUtc,
                addresses)));
        });
        api.MapDelete("/auth/devices/{controllerId}", (string controllerId, AgentSecurity security) =>
            Results.Ok(ApiResponse<bool>.Ok(security.Revoke(controllerId))));
        api.MapGet("/settings", (RuntimeSettingsStore settings) =>
            Results.Ok(ApiResponse<RuntimeSettingsView>.Ok(settings.Snapshot())));
        api.MapPut("/settings", (UpdateSettingsRequest request, RuntimeSettingsStore settings) =>
            Results.Ok(ApiResponse<RuntimeSettingsView>.Ok(settings.Update(request))));
        api.MapPost("/elevation/smart", async (SmartProbeService smart, CancellationToken cancellationToken) =>
            Results.Ok(ApiResponse<SmartProbeResponse>.Ok(await smart.RequestAsync(cancellationToken))));

        api.MapGet("/system/info", async (SqliteDatabase database, IMetricCollector collector, CancellationToken cancellationToken) =>
        {
            var snapshot = await database.GetLatestMetricAsync(cancellationToken) ?? await collector.CollectAsync(false, cancellationToken);
            return Results.Ok(ApiResponse<SystemMetrics>.Ok(snapshot.System));
        });
        api.MapGet("/metrics/current", async (SqliteDatabase database, IMetricCollector collector, CancellationToken cancellationToken) =>
        {
            var snapshot = await database.GetLatestMetricAsync(cancellationToken) ?? await collector.CollectAsync(false, cancellationToken);
            return Results.Ok(ApiResponse<MetricSnapshot>.Ok(snapshot));
        });
        api.MapGet("/metrics/history", async (DateTimeOffset? from, DateTimeOffset? to, int? limit, SqliteDatabase database, CancellationToken cancellationToken) =>
        {
            var end = to ?? DateTimeOffset.UtcNow;
            var start = from ?? end.AddHours(-1);
            if (start > end) return Results.BadRequest(ApiResponse<object>.Fail("INVALID_RANGE", "from 不能晚于 to"));
            var items = await database.GetMetricHistoryAsync(start, end, limit ?? 2000, cancellationToken);
            return Results.Ok(ApiResponse<IReadOnlyList<MetricSnapshot>>.Ok(items));
        });
        api.MapGet("/processes", async (SqliteDatabase database, CancellationToken cancellationToken) =>
        {
            var snapshot = await database.GetLatestMetricAsync(cancellationToken);
            return Results.Ok(ApiResponse<ProcessMetric[]>.Ok(snapshot?.TopProcesses ?? []));
        });
        api.MapGet("/storage", async (SqliteDatabase database, CancellationToken cancellationToken) =>
        {
            var snapshot = await database.GetLatestMetricAsync(cancellationToken);
            return snapshot is null
                ? Results.NotFound(ApiResponse<object>.Fail("NO_METRICS", "尚未采集指标"))
                : Results.Ok(ApiResponse<DiskMetrics>.Ok(snapshot.Disk));
        });
        api.MapGet("/network", async (SqliteDatabase database, CancellationToken cancellationToken) =>
        {
            var snapshot = await database.GetLatestMetricAsync(cancellationToken);
            return snapshot is null
                ? Results.NotFound(ApiResponse<object>.Fail("NO_METRICS", "尚未采集指标"))
                : Results.Ok(ApiResponse<NetworkMetrics>.Ok(snapshot.Network));
        });
        api.MapGet("/sensors", async (SqliteDatabase database, CancellationToken cancellationToken) =>
        {
            var snapshot = await database.GetLatestMetricAsync(cancellationToken);
            return snapshot is null
                ? Results.NotFound(ApiResponse<object>.Fail("NO_METRICS", "尚未采集指标"))
                : Results.Ok(ApiResponse<SensorMetrics>.Ok(snapshot.Sensors));
        });
        api.MapGet("/security", async (SqliteDatabase database, CancellationToken cancellationToken) =>
        {
            var snapshot = await database.GetLatestMetricAsync(cancellationToken);
            return snapshot is null
                ? Results.NotFound(ApiResponse<object>.Fail("NO_METRICS", "尚未采集指标"))
                : Results.Ok(ApiResponse<object>.Ok(new
                {
                    snapshot.System.IsAdministrator,
                    snapshot.System.OperatingMode,
                    snapshot.System.DefenderEnabled,
                    snapshot.System.FirewallEnabled,
                    snapshot.System.SecureBootEnabled,
                    snapshot.System.PendingReboot,
                    snapshot.System.TimeSynchronized,
                    snapshot.System.LastSecurityUpdateUtc
                }));
        });
        api.MapGet("/services", async (bool? refresh, SystemInventoryService inventory, CancellationToken cancellationToken) =>
            Results.Ok(ApiResponse<IReadOnlyList<ServiceInfo>>.Ok(await inventory.GetServicesAsync(refresh == true, cancellationToken))));
        api.MapGet("/startup", async (bool? refresh, SystemInventoryService inventory, CancellationToken cancellationToken) =>
            Results.Ok(ApiResponse<IReadOnlyList<StartupItemInfo>>.Ok(await inventory.GetStartupAsync(refresh == true, cancellationToken))));
        api.MapGet("/events", async (SystemInventoryService inventory, CancellationToken cancellationToken) =>
            Results.Ok(ApiResponse<IReadOnlyList<EventLogItem>>.Ok(await inventory.GetRecentEventsAsync(cancellationToken))));
        api.MapGet("/software", async (SystemInventoryService inventory, CancellationToken cancellationToken) =>
            Results.Ok(ApiResponse<IReadOnlyList<InstalledSoftwareInfo>>.Ok(await inventory.GetInstalledSoftwareAsync(cancellationToken))));

        api.MapPost("/scans", async (ScanRequest request, ScanManager scans, CancellationToken cancellationToken) =>
            Results.Accepted($"/api/v1/scans/{request}", ApiResponse<ScanRecord>.Ok(await scans.StartAsync(request, cancellationToken))));
        api.MapGet("/scans", async (int? limit, ScanManager scans, CancellationToken cancellationToken) =>
            Results.Ok(ApiResponse<IReadOnlyList<ScanRecord>>.Ok(await scans.ListAsync(limit ?? 100, cancellationToken))));
        api.MapGet("/scans/{scanId:guid}", async (Guid scanId, ScanManager scans, CancellationToken cancellationToken) =>
        {
            var scan = await scans.GetAsync(scanId, cancellationToken);
            return scan is null ? Results.NotFound(ApiResponse<object>.Fail("SCAN_NOT_FOUND", "扫描任务不存在")) : Results.Ok(ApiResponse<ScanRecord>.Ok(scan));
        });
        api.MapDelete("/scans/{scanId:guid}", (Guid scanId, ScanManager scans) =>
            scans.Cancel(scanId)
                ? Results.Ok(ApiResponse<bool>.Ok(true))
                : Results.NotFound(ApiResponse<object>.Fail("SCAN_NOT_FOUND", "扫描任务不存在或已结束")));

        api.MapGet("/reports", async (int? limit, SqliteDatabase database, CancellationToken cancellationToken) =>
            Results.Ok(ApiResponse<IReadOnlyList<ReportSummary>>.Ok(await database.ListReportsAsync(limit ?? 100, cancellationToken))));
        api.MapGet("/reports/{reportId:guid}", async (Guid reportId, SqliteDatabase database, CancellationToken cancellationToken) =>
        {
            var report = await database.GetReportAsync(reportId, cancellationToken);
            return report is null ? Results.NotFound(ApiResponse<object>.Fail("REPORT_NOT_FOUND", "报告不存在")) : Results.Ok(ApiResponse<HealthReport>.Ok(report));
        });
        api.MapGet("/reports/{reportId:guid}/artifacts/{format}", async (Guid reportId, string format, SqliteDatabase database, AgentOptions options, CancellationToken cancellationToken) =>
        {
            var report = await database.GetReportAsync(reportId, cancellationToken);
            var artifact = report?.Artifacts.FirstOrDefault(x => string.Equals(x.Format, format, StringComparison.OrdinalIgnoreCase));
            if (report is null || artifact is null) return Results.NotFound(ApiResponse<object>.Fail("ARTIFACT_NOT_FOUND", "报告文件不存在"));
            var root = System.IO.Path.GetFullPath(options.ReportDirectory);
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, report.ReportId.ToString("N"), artifact.FileName));
            if (!path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(ApiResponse<object>.Fail("INVALID_PATH", "报告路径无效"));
            if (!File.Exists(path)) return Results.NotFound(ApiResponse<object>.Fail("ARTIFACT_NOT_FOUND", "报告文件已被移动或删除"));
            return Results.File(path, ContentType(artifact.Format), artifact.FileName, enableRangeProcessing: true);
        });

        api.MapGet("/schedules", async (SqliteDatabase database, CancellationToken cancellationToken) =>
            Results.Ok(ApiResponse<IReadOnlyList<ScheduleDefinition>>.Ok(await database.ListSchedulesAsync(cancellationToken))));
        api.MapPost("/schedules", async (ScheduleRequest request, SqliteDatabase database) =>
        {
            try
            {
                var cron = new CronExpression(request.Cron);
                var schedule = new ScheduleDefinition
                {
                    Name = request.Name,
                    Cron = request.Cron,
                    ScanMode = request.ScanMode,
                    Intensity = request.Intensity,
                    Enabled = request.Enabled,
                    RunOnlyWhenIdle = request.RunOnlyWhenIdle,
                    RunOnlyOnAcPower = request.RunOnlyOnAcPower,
                    RunOnlyOnSpecifiedNetwork = request.RunOnlyOnSpecifiedNetwork,
                    NetworkSsid = request.NetworkSsid,
                    MaximumRuntimeSeconds = request.MaximumRuntimeSeconds,
                    RetryCount = request.RetryCount,
                    NextRunUtc = cron.NextOccurrence(DateTimeOffset.UtcNow)
                };
                await database.SaveScheduleAsync(schedule);
                return Results.Ok(ApiResponse<ScheduleDefinition>.Ok(schedule));
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(ApiResponse<object>.Fail("INVALID_CRON", ex.Message));
            }
        });
        api.MapPut("/schedules/{scheduleId:guid}", async (Guid scheduleId, ScheduleRequest request, SqliteDatabase database) =>
        {
            try
            {
                var existing = (await database.ListSchedulesAsync()).FirstOrDefault(x => x.ScheduleId == scheduleId);
                if (existing is null) return Results.NotFound(ApiResponse<object>.Fail("SCHEDULE_NOT_FOUND", "定时任务不存在"));
                var cron = new CronExpression(request.Cron);
                var updated = existing with
                {
                    Name = request.Name,
                    Cron = request.Cron,
                    ScanMode = request.ScanMode,
                    Intensity = request.Intensity,
                    Enabled = request.Enabled,
                    RunOnlyWhenIdle = request.RunOnlyWhenIdle,
                    RunOnlyOnAcPower = request.RunOnlyOnAcPower,
                    RunOnlyOnSpecifiedNetwork = request.RunOnlyOnSpecifiedNetwork,
                    NetworkSsid = request.NetworkSsid,
                    MaximumRuntimeSeconds = request.MaximumRuntimeSeconds,
                    RetryCount = request.RetryCount,
                    NextRunUtc = cron.NextOccurrence(DateTimeOffset.UtcNow)
                };
                await database.SaveScheduleAsync(updated);
                return Results.Ok(ApiResponse<ScheduleDefinition>.Ok(updated));
            }
            catch (FormatException ex)
            {
                return Results.BadRequest(ApiResponse<object>.Fail("INVALID_CRON", ex.Message));
            }
        });
        api.MapDelete("/schedules/{scheduleId:guid}", async (Guid scheduleId, SqliteDatabase database) =>
        {
            await database.DeleteScheduleAsync(scheduleId);
            return Results.Ok(ApiResponse<bool>.Ok(true));
        });

        api.MapGet("/alerts", async (bool? includeResolved, int? limit, SqliteDatabase database, CancellationToken cancellationToken) =>
            Results.Ok(ApiResponse<IReadOnlyList<AlertRecord>>.Ok(await database.ListAlertsAsync(includeResolved == true, limit ?? 200, cancellationToken))));

        api.Map("/realtime", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("WEBSOCKET_REQUIRED", "该端点仅接受 WebSocket 连接"));
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync("pulsecheck.v1");
            var hub = context.RequestServices.GetRequiredService<RealtimeHub>();
            var database = context.RequestServices.GetRequiredService<SqliteDatabase>();
            var identity = context.RequestServices.GetRequiredService<AgentIdentity>();
            var (id, reader) = hub.Subscribe();
            try
            {
                var latest = await database.GetLatestMetricAsync(context.RequestAborted);
                if (latest is not null)
                    await SendAsync(socket, new RealtimeEnvelope("metrics", identity.DeviceId, latest.TimestampUtc, latest.DataVersion, latest), context.RequestAborted);
                await foreach (var envelope in reader.ReadAllAsync(context.RequestAborted))
                    await SendAsync(socket, envelope, context.RequestAborted);
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            finally
            {
                hub.Unsubscribe(id);
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None); } catch { }
                }
            }
        });
    }

    private static string? TryQr(string value)
    {
        try { return QrCodeGenerator.GenerateSvg(value); }
        catch { return null; }
    }

    private static string ContentType(string format) => format.ToLowerInvariant() switch
    {
        "pdf" => "application/pdf",
        "html" => "text/html; charset=utf-8",
        "json" => "application/json; charset=utf-8",
        "csv" => "text/csv; charset=utf-8",
        _ => "application/octet-stream"
    };

    private static async Task SendAsync(WebSocket socket, RealtimeEnvelope envelope, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, Json);
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
    }
}