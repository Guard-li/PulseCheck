using System.Text.Json;
using Microsoft.Win32;
using PulseCheck.Agent.Collectors;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Inventory;

public sealed class SystemInventoryService
{
    private readonly object _gate = new();
    private DateTimeOffset _servicesCacheAt = DateTimeOffset.MinValue;
    private ServiceInfo[] _servicesCache = [];
    private DateTimeOffset _startupCacheAt = DateTimeOffset.MinValue;
    private StartupItemInfo[] _startupCache = [];

    public async Task<IReadOnlyList<ServiceInfo>> GetServicesAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!refresh && DateTimeOffset.UtcNow - _servicesCacheAt < TimeSpan.FromSeconds(30)) return _servicesCache;
        }
        var services = await Task.Run(() => ReadServices(), cancellationToken);
        lock (_gate)
        {
            _servicesCache = services;
            _servicesCacheAt = DateTimeOffset.UtcNow;
            return _servicesCache;
        }
    }

    public async Task<IReadOnlyList<StartupItemInfo>> GetStartupAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!refresh && DateTimeOffset.UtcNow - _startupCacheAt < TimeSpan.FromSeconds(30)) return _startupCache;
        }
        var startup = await Task.Run(() => ReadStartup(), cancellationToken);
        lock (_gate)
        {
            _startupCache = startup;
            _startupCacheAt = DateTimeOffset.UtcNow;
            return _startupCache;
        }
    }

    public Task<EventLogItem[]> GetRecentEventsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadEvents(), cancellationToken);

    public Task<InstalledSoftwareInfo[]> GetInstalledSoftwareAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadInstalledSoftware(), cancellationToken);

    private static ServiceInfo[] ReadServices()
    {
        using var document = CollectorUtilities.RunPowerShellJson(
            "Get-Service | Select-Object Name,DisplayName,Status,StartType | ConvertTo-Json -Compress",
            10000);
        if (document is null) return [];
        return ParseArray(document.RootElement).Select(item => new ServiceInfo(
            GetString(item, "Name") ?? string.Empty,
            GetString(item, "DisplayName") ?? string.Empty,
            GetString(item, "Status") ?? "Unknown",
            GetString(item, "StartType") ?? "Unknown",
            false)).Where(x => x.Name.Length > 0).OrderBy(x => x.Name).ToArray();
    }

    private static StartupItemInfo[] ReadStartup()
    {
        var result = new List<StartupItemInfo>();
        ReadRunKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "当前用户", result);
        ReadRunKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "所有用户", result);
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
                 }.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder).Take(200))
            {
                result.Add(new StartupItemInfo(Path.GetFileName(file), folder.Contains("Common", StringComparison.OrdinalIgnoreCase) ? "所有用户" : "当前用户", "***", "启动文件夹", true));
            }
        }
        return [.. result.OrderBy(x => x.Scope).ThenBy(x => x.Name)];
    }

    private static void ReadRunKey(RegistryKey root, string path, string scope, List<StartupItemInfo> destination)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(name)?.ToString() ?? string.Empty;
                destination.Add(new StartupItemInfo(name, scope, MaskCommand(value), "Run", true));
            }
        }
        catch { }
    }

    private static EventLogItem[] ReadEvents()
    {
        using var document = CollectorUtilities.RunPowerShellJson(
            "Get-WinEvent -FilterHashtable @{LogName='System';Level=1,2;StartTime=(Get-Date).AddDays(-7)} -MaxEvents 50 -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,LevelDisplayName,ProviderName,Message | ConvertTo-Json -Compress",
            15000);
        if (document is null) return [];
        return ParseArray(document.RootElement).Select(item => new EventLogItem(
            ParseDate(GetString(item, "TimeCreated")),
            (int)(GetLong(item, "Id") ?? 0),
            GetString(item, "LevelDisplayName") ?? "Unknown",
            GetString(item, "ProviderName") ?? "Unknown",
            GetString(item, "Message") ?? string.Empty,
            "System")).ToArray();
    }

    private static InstalledSoftwareInfo[] ReadInstalledSoftware()
    {
        using var document = CollectorUtilities.RunPowerShellJson(
            "$paths=@('HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*','HKLM:\\SOFTWARE\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*','HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*'); Get-ItemProperty $paths -ErrorAction SilentlyContinue | Where-Object {$_.DisplayName} | Select-Object DisplayName,DisplayVersion,Publisher,InstallDate | ConvertTo-Json -Compress",
            15000);
        if (document is null) return [];
        return ParseArray(document.RootElement).Select(item => new InstalledSoftwareInfo(
            GetString(item, "DisplayName") ?? string.Empty,
            GetString(item, "DisplayVersion"),
            GetString(item, "Publisher"),
            ParseDateOnly(GetString(item, "InstallDate")),
            "Windows")).Where(x => x.Name.Length > 0).OrderBy(x => x.Name).ToArray();
    }

    private static IEnumerable<JsonElement> ParseArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array) return root.EnumerateArray().ToArray();
        return root.ValueKind == JsonValueKind.Object ? [root] : [];
    }

    private static string? GetString(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? value.ToString() : null;

    private static long? GetLong(JsonElement element, string name)
    {
        if (!TryGet(element, name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.TryGetInt64(out var result) ? result : long.TryParse(value.ToString(), out result) ? result : null;
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value, out var result) ? result.ToUniversalTime() : null;
    private static DateOnly? ParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateOnly.TryParseExact(value, "yyyyMMdd", out var result) || DateOnly.TryParse(value, out result) ? result : null;
    }

    private static string MaskCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;
        var first = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? command;
        return $"{Path.GetFileName(first.Trim('"'))} [参数已隐藏]";
    }
}