using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Reporting;

public sealed class ReportGenerator
{
    private readonly JsonSerializerOptions _json = JsonDefaults.Create(true);

    public async Task<HealthReport> GenerateAsync(
        HealthReport source,
        string reportDirectory,
        bool generatePdf,
        bool redact,
        CancellationToken cancellationToken = default)
    {
        var report = redact ? PrivacyRedactor.Redact(source) : source;
        var folder = Path.Combine(reportDirectory, report.ReportId.ToString("N"));
        Directory.CreateDirectory(folder);
        var baseName = $"PulseCheck-{report.GeneratedAtUtc.ToLocalTime():yyyyMMdd-HHmmss}-{report.ReportId.ToString("N")[..8]}";
        var canonical = JsonSerializer.SerializeToUtf8Bytes(report with { ReportHash = string.Empty, Artifacts = [] }, _json);
        var reportHash = Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
        report = report with { ReportHash = reportHash };

        var artifacts = new List<ReportArtifact>();
        var htmlPath = Path.Combine(folder, $"{baseName}.html");
        var html = BuildHtml(report, LoadTemplateCss());
        await File.WriteAllTextAsync(htmlPath, html, new UTF8Encoding(false), cancellationToken);
        artifacts.Add(CreateArtifact("html", htmlPath, folder));

        var csvPath = Path.Combine(folder, $"{baseName}.csv");
        var csv = BuildCsv(report);
        await File.WriteAllTextAsync(csvPath, csv, new UTF8Encoding(true), cancellationToken);
        artifacts.Add(CreateArtifact("csv", csvPath, folder));

        if (generatePdf)
        {
            var pdfPath = Path.Combine(folder, $"{baseName}.pdf");
            if (!EdgePdfRenderer.TryRender(htmlPath, pdfPath, out var error))
            {
                BasicPdfWriter.Write(pdfPath, report);
                error = "Edge 无头渲染不可用，已生成基础 PDF；中文正式排版请查看 HTML 或安装 Microsoft Edge。";
            }
            artifacts.Add(CreateArtifact("pdf", pdfPath, folder));
            if (!string.IsNullOrWhiteSpace(error))
            {
                var errorPath = Path.Combine(folder, "pdf-render-warning.txt");
                await File.WriteAllTextAsync(errorPath, error, new UTF8Encoding(false), cancellationToken);
            }
        }

        var jsonPath = Path.Combine(folder, $"{baseName}.json");
        var jsonReport = report with { Artifacts = [] };
        var json = JsonSerializer.SerializeToUtf8Bytes(jsonReport, _json);
        await File.WriteAllBytesAsync(jsonPath, json, cancellationToken);
        var jsonArtifact = CreateArtifact("json", jsonPath, folder);
        report = report with { Artifacts = [jsonArtifact, .. artifacts] };
        return report;
    }
    private static ReportArtifact CreateArtifact(string format, string path, string folder)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ReportArtifact(format, Path.GetFileName(path), Path.GetRelativePath(folder, path).Replace('\\', '/'), bytes.LongLength, hash);
    }

    private static string LoadTemplateCss()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "report-templates", "default.css");
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch { return string.Empty; }
    }

    private static string BuildHtml(HealthReport report, string customCss)
    {
        var encoder = HtmlEncoder.Default;
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine("<title>PulseCheck 健康检测报告</title><style>");
        sb.AppendLine(":root{color-scheme:light dark;--accent:#1677ff;--ok:#16a34a;--warn:#d97706;--bad:#dc2626;--bg:#f6f8fb;--card:#fff;--text:#172033;--muted:#60708a}@media(prefers-color-scheme:dark){:root{--bg:#101724;--card:#172033;--text:#eef4ff;--muted:#a9b7ce}}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font-family:'Microsoft YaHei',system-ui,sans-serif;line-height:1.6}.page{max-width:1080px;margin:auto;padding:32px 20px 72px}.hero,.card{background:var(--card);border:1px solid #dbe3ef;border-radius:16px;padding:22px;margin:14px 0;box-shadow:0 8px 28px #1b2a4a0c}.hero{display:grid;grid-template-columns:1fr auto;gap:24px;align-items:center}.score{font-size:56px;font-weight:800;color:var(--accent)}.muted{color:var(--muted)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px}.metric{background:var(--bg);border-radius:12px;padding:14px}.metric b{display:block;font-size:22px}.finding{border-left:5px solid var(--warn);padding:12px 16px;background:var(--bg);border-radius:10px;margin:10px 0}.finding.critical{border-color:var(--bad)}.finding.information{border-color:var(--accent)}table{width:100%;border-collapse:collapse}th,td{padding:10px;border-bottom:1px solid #dbe3ef;text-align:left;vertical-align:top}h1,h2,h3{margin-top:0}.tag{display:inline-block;padding:3px 9px;border-radius:999px;background:#1677ff1a;color:var(--accent);font-size:12px}.footer{font-size:12px;color:var(--muted);word-break:break-all}@media(max-width:600px){.hero{grid-template-columns:1fr}.page{padding:18px 12px}.score{font-size:44px}}@media print{body{background:#fff}.page{max-width:none;padding:0}.hero,.card{box-shadow:none;break-inside:avoid}}</style></head><body><main class=\"page\">");
        sb.Append($"<section class=\"hero\"><div><span class=\"tag\">{encoder.Encode(report.ScanMode == "full" ? "完整检测" : "快速检测")}</span><h1>{encoder.Encode(report.Device.DisplayName)}</h1><p class=\"muted\">{encoder.Encode(report.Device.ComputerName)} · {encoder.Encode(report.Device.OsDescription)}</p><p>报告 ID：<code>{report.ReportId}</code><br>开始：{report.ScanStartedAtUtc.ToLocalTime():F}<br>结束：{report.ScanFinishedAtUtc.ToLocalTime():F}<br>权限：{(report.IsAdministrator ? "完整模式" : "受限模式")}</p></div><div><div class=\"score\">{report.Health.OverallScore:F1}</div><strong>{CollectorText(report.Health.Status)}</strong><div class=\"muted\">总体健康分数</div></div></section>");
        sb.Append("<section class=\"card\"><h2>体检结论</h2>");
        sb.Append($"<p>风险等级：<strong>{encoder.Encode(report.RiskLevel)}</strong>；发现 {report.Findings.Length} 项需要关注的事项。</p>");
        if (report.Findings.Length == 0) sb.Append("<p>当前规则未发现异常。</p>");
        foreach (var finding in report.Findings)
        {
            sb.Append($"<article class=\"finding {finding.Severity.ToString().ToLowerInvariant()}\"><strong>{encoder.Encode(CollectorText(finding.Severity))} · {encoder.Encode(finding.Title)}</strong><p>{encoder.Encode(finding.Description)}</p>");
            if (finding.Evidence.Length > 0) sb.Append($"<p class=\"muted\">证据：{encoder.Encode(string.Join("；", finding.Evidence.Select(x => $"{x.Key}={x.Value}")))}</p>");
            if (finding.Recommendations.Length > 0) sb.Append($"<p>建议：{encoder.Encode(string.Join("；", finding.Recommendations))}</p>");
            sb.Append("</article>");
        }
        sb.Append("</section><section class=\"card\"><h2>评分维度</h2><table><thead><tr><th>维度</th><th>分数</th><th>状态</th><th>权重</th><th>建议</th></tr></thead><tbody>");
        foreach (var dimension in report.Health.Dimensions)
            sb.Append($"<tr><td>{encoder.Encode(dimension.Name)}</td><td>{dimension.Score:F1}</td><td>{CollectorText(dimension.Status)}</td><td>{dimension.Weight:F0}%</td><td>{encoder.Encode(string.Join("；", dimension.Recommendations))}</td></tr>");
        sb.Append("</tbody></table></section>");
        sb.Append("<section class=\"card\"><h2>实时指标快照</h2><div class=\"grid\">");
        AppendMetric(sb, "CPU 使用率", $"{report.Snapshot.Cpu.TotalUsagePercent:F1}%");
        AppendMetric(sb, "CPU 频率", report.Snapshot.Cpu.CurrentFrequencyMhz is null ? "不可用" : $"{report.Snapshot.Cpu.CurrentFrequencyMhz:F0} MHz");
        AppendMetric(sb, "内存使用率", $"{report.Snapshot.Memory.UsagePercent:F1}%");
        AppendMetric(sb, "可用内存", FormatBytes(report.Snapshot.Memory.AvailableBytes));
        var systemVolume = report.Snapshot.Disk.Volumes.FirstOrDefault(x => x.IsSystemVolume);
        AppendMetric(sb, "系统盘剩余", systemVolume is null ? "不可用" : FormatBytes(systemVolume.FreeBytes));
        AppendMetric(sb, "网络下行", report.Snapshot.Network.DownloadBytesPerSecond is null ? "不可用" : $"{FormatBytes((ulong)Math.Max(0, report.Snapshot.Network.DownloadBytesPerSecond.Value))}/s");
        AppendMetric(sb, "GPU", report.Snapshot.Gpus.FirstOrDefault()?.Name ?? "未检测到");
        AppendMetric(sb, "温度", report.Snapshot.Sensors.CpuTemperatureCelsius is null ? "不可用" : $"{report.Snapshot.Sensors.CpuTemperatureCelsius:F1} °C");
        sb.Append("</div></section>");
        sb.Append("<section class=\"card\"><h2>系统与硬件概览</h2><table><tbody>");
        AppendRow(sb, "操作系统", report.Snapshot.System.OsName);
        AppendRow(sb, "系统版本", report.Snapshot.System.OsVersion);
        AppendRow(sb, "系统构建", report.Snapshot.System.OsBuild);
        AppendRow(sb, "启动时间", report.Snapshot.System.BootTimeUtc.ToLocalTime().ToString("F"));
        AppendRow(sb, "运行时长", report.Snapshot.System.Uptime.ToString(@"d\.hh\:mm\:ss"));
        AppendRow(sb, "CPU", report.Snapshot.Cpu.Name);
        AppendRow(sb, "BIOS", $"{report.Snapshot.System.BiosVendor} {report.Snapshot.System.BiosVersion}".Trim());
        AppendRow(sb, "主板", $"{report.Snapshot.System.BaseboardManufacturer} {report.Snapshot.System.BaseboardProduct}".Trim());
        foreach (var disk in report.Snapshot.Disk.PhysicalDisks)
            AppendRow(sb, "物理磁盘", $"{disk.FriendlyName} / {disk.MediaType} / {disk.HealthStatus ?? "状态未知"}");
        sb.Append("</tbody></table></section>");
        if (report.Evidence.Count > 0)
        {
            sb.Append("<section class=\"card\"><h2>原始证据</h2><table><thead><tr><th>字段</th><th>值</th></tr></thead><tbody>");
            foreach (var item in report.Evidence.OrderBy(x => x.Key).Take(80))
                sb.Append($"<tr><td><code>{encoder.Encode(item.Key)}</code></td><td>{encoder.Encode(item.Value)}</td></tr>");
            sb.Append("</tbody></table></section>");
        }        if (report.Limitations.Length > 0)
            sb.Append($"<section class=\"card\"><h2>检测限制</h2><ul>{string.Join(string.Empty, report.Limitations.Select(x => $"<li>{encoder.Encode(x)}</li>"))}</ul></section>");
        sb.Append($"<footer class=\"footer\">隐私模式：{encoder.Encode(report.PrivacyMode)} · 规则版本：{encoder.Encode(report.Health.ScoringRuleVersion)} · 报告哈希（SHA256）：{encoder.Encode(report.ReportHash)} · 生成时间：{report.GeneratedAtUtc.ToLocalTime():F}</footer>");
        sb.Append("</main></body></html>");
        var html = sb.ToString();
        return string.IsNullOrWhiteSpace(customCss) ? html : html.Replace("</style>", $"{customCss}`n</style>", StringComparison.Ordinal);
    }

    private static string BuildCsv(HealthReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("section,metric,value,unit,timestamp_utc,source,rule_id");
        Row("system", "computer_name", report.Device.ComputerName, string.Empty, report.GeneratedAtUtc, "system");
        Row("health", "overall_score", report.Health.OverallScore.ToString("F1", CultureInfo.InvariantCulture), "score", report.GeneratedAtUtc, "diagnostic-engine");
        Row("cpu", "total_usage", report.Snapshot.Cpu.TotalUsagePercent.ToString("F2", CultureInfo.InvariantCulture), "percent", report.Snapshot.TimestampUtc, "GetSystemTimes/NtQuerySystemInformation");
        Row("memory", "usage", report.Snapshot.Memory.UsagePercent.ToString("F2", CultureInfo.InvariantCulture), "percent", report.Snapshot.TimestampUtc, "GlobalMemoryStatusEx");
        var systemVolume = report.Snapshot.Disk.Volumes.FirstOrDefault(x => x.IsSystemVolume);
        if (systemVolume is not null)
        {
            Row("storage", "system_free", systemVolume.FreeBytes.ToString(CultureInfo.InvariantCulture), "bytes", report.Snapshot.TimestampUtc, "DriveInfo");
            Row("storage", "system_used", systemVolume.UsedPercent.ToString("F2", CultureInfo.InvariantCulture), "percent", report.Snapshot.TimestampUtc, "DriveInfo");
        }
        if (report.Snapshot.Disk.AverageResponseTimeMilliseconds is double response)
            Row("storage", "average_response", response.ToString("F2", CultureInfo.InvariantCulture), "milliseconds", report.Snapshot.TimestampUtc, "PDH");
        foreach (var adapter in report.Snapshot.Network.Adapters.Where(x => x.Status == "Up"))
        {
            if (adapter.DownloadBytesPerSecond is double down) Row("network", $"download_{adapter.Name}", down.ToString("F2", CultureInfo.InvariantCulture), "bytes_per_second", report.Snapshot.TimestampUtc, "NetworkInterface");
            if (adapter.UploadBytesPerSecond is double up) Row("network", $"upload_{adapter.Name}", up.ToString("F2", CultureInfo.InvariantCulture), "bytes_per_second", report.Snapshot.TimestampUtc, "NetworkInterface");
        }
        foreach (var finding in report.Findings)
            foreach (var evidence in finding.Evidence)
                Row("finding", evidence.Key, evidence.Value, string.Empty, evidence.ObservedAtUtc, evidence.Source, finding.RuleId);
        return sb.ToString();

        void Row(string section, string metric, string value, string unit, DateTimeOffset timestamp, string source, string ruleId = "")
        {
            sb.Append(Escape(section)).Append(',')
              .Append(Escape(metric)).Append(',')
              .Append(Escape(value)).Append(',')
              .Append(Escape(unit)).Append(',')
              .Append(timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)).Append(',')
              .Append(Escape(source)).Append(',')
              .Append(Escape(ruleId)).AppendLine();
        }
    }

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static void AppendMetric(StringBuilder sb, string label, string value) =>
        sb.Append($"<div class=\"metric\"><span class=\"muted\">{HtmlEncoder.Default.Encode(label)}</span><b>{HtmlEncoder.Default.Encode(value)}</b></div>");

    private static void AppendRow(StringBuilder sb, string label, string value) =>
        sb.Append($"<tr><th>{HtmlEncoder.Default.Encode(label)}</th><td>{HtmlEncoder.Default.Encode(value)}</td></tr>");

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private static string CollectorText(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => "严重",
        FindingSeverity.Warning => "警告",
        FindingSeverity.Notice => "注意",
        _ => "信息"
    };

    private static string CollectorText(HealthStatus status) => status switch
    {
        HealthStatus.Excellent => "优秀",
        HealthStatus.Good => "良好",
        HealthStatus.Attention => "注意",
        HealthStatus.Warning => "警告",
        _ => "严重"
    };
}