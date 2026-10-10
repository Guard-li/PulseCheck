using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using PulseCheck.Agent.Collectors;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Security;

public sealed class SmartProbeService
{
    private readonly AgentOptions _options;
    private readonly string _resultPath;
    private readonly JsonSerializerOptions _json = JsonDefaults.Create(true);
    private readonly object _gate = new();
    private PhysicalDiskInfo[] _cache = [];
    private DateTimeOffset _cacheAtUtc = DateTimeOffset.MinValue;

    public SmartProbeService(AgentOptions options)
    {
        _options = options;
        _resultPath = Path.Combine(options.DataDirectory, "smart-probe-result.json");
    }

    public async Task<SmartProbeResponse> RequestAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow - _cacheAtUtc < TimeSpan.FromMinutes(10)) return new SmartProbeResponse(true, "使用最近一次授权的 SMART 结果", _cache);
        }
        if (IsAdministrator())
        {
            using var collector = new DiskCollector();
            var disks = collector.Collect(includeSmart: true).PhysicalDisks;
            SetCache(disks);
            return new SmartProbeResponse(true, "当前 Agent 已具备管理员权限", disks);
        }

        try
        {
            if (File.Exists(_resultPath)) File.Delete(_resultPath);
            var process = Process.Start(BuildElevatedStartInfo());
            if (process is null) return new SmartProbeResponse(false, "无法启动管理员授权进程", []);
            var exited = await Task.Run(() => process.WaitForExit(60000), cancellationToken);
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new SmartProbeResponse(false, "等待管理员授权超时，请重新点击授权按钮", []);
            }
            if (!File.Exists(_resultPath)) return new SmartProbeResponse(false, "管理员进程未返回 SMART 结果", []);
            var result = JsonSerializer.Deserialize<SmartProbeFile>(await File.ReadAllTextAsync(_resultPath, cancellationToken), _json);
            if (result is null || !result.Success)
                return new SmartProbeResponse(false, result?.Error ?? "SMART 读取失败", []);
            SetCache(result.Disks);
            return new SmartProbeResponse(true, "管理员授权成功，已读取物理磁盘与 SMART 信息", result.Disks);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new SmartProbeResponse(false, "你取消了管理员授权，SMART 仍不可用", []);
        }
        catch (Exception ex)
        {
            return new SmartProbeResponse(false, $"授权或 SMART 读取失败：{ex.Message}", []);
        }
    }

    private ProcessStartInfo BuildElevatedStartInfo()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 Agent 可执行文件路径");
        var entry = Assembly.GetEntryAssembly()?.Location;
        var isDotnetHost = string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase);
        var arguments = isDotnetHost && !string.IsNullOrWhiteSpace(entry)
            ? $"\"{entry}\" --elevated-probe smart \"{_resultPath}\""
            : $"--elevated-probe smart \"{_resultPath}\"";
        return new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory
        };
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private void SetCache(PhysicalDiskInfo[] disks)
    {
        lock (_gate)
        {
            _cache = disks;
            _cacheAtUtc = DateTimeOffset.UtcNow;
        }
    }
}

public sealed record SmartProbeFile(bool Success, string? Error, PhysicalDiskInfo[] Disks);