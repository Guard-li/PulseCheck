using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PulseCheck.Contracts;
using PulseCheck.Agent.Native;

namespace PulseCheck.Agent.Collectors;

public sealed class SystemInfoCollector
{
    private DateTimeOffset _powerLastRead = DateTimeOffset.MinValue;
    private string? _powerPlanCache;

    public SystemMetrics Collect(bool includeSlowChecks = false)
    {
        var warnings = new List<string>();
        var powerOk = NativeMethods.GetSystemPowerStatus(out var powerStatus);
        var isAdmin = IsAdministrator();
        var biosPath = @"HARDWARE\DESCRIPTION\System\BIOS";
        var release = NativeMethods.ReadRegistryString(Registry.LocalMachine, biosPath, "BIOSReleaseDate");
        DateOnly? biosDate = DateOnly.TryParse(release, out var parsedDate) ? parsedDate : null;
        var bootTime = DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var powerPlan = ReadPowerPlan();
        if (!isAdmin) warnings.Add("Agent 当前以受限模式运行，部分系统与安全数据不可用。");
        return new SystemMetrics
        {
            ComputerName = Environment.MachineName,
            UserNameMasked = CollectorUtilities.MaskUserName(Environment.UserName),
            IsAdministrator = isAdmin,
            OperatingMode = isAdmin ? OperatingMode.Full : OperatingMode.Restricted,
            OsName = ReadOsName(),
            OsVersion = Environment.OSVersion.Version.ToString(),
            OsBuild = Environment.OSVersion.Version.Build.ToString(),
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            BootTimeUtc = bootTime,
            Uptime = DateTimeOffset.UtcNow - bootTime,
            PowerPlan = powerPlan,
            OnAcPower = powerOk && powerStatus.AcLineStatus != 255 ? powerStatus.AcLineStatus == 1 : null,
            BatteryChargePercent = powerOk && powerStatus.BatteryLifePercent != 255 ? powerStatus.BatteryLifePercent : null,
            BatteryHealthPercent = includeSlowChecks ? ReadBatteryHealth() : null,
            BiosVendor = NullIfEmpty(NativeMethods.ReadRegistryString(Registry.LocalMachine, biosPath, "BIOSVendor")),
            BiosVersion = NullIfEmpty(NativeMethods.ReadRegistryString(Registry.LocalMachine, biosPath, "BIOSVersion")),
            BiosReleaseDate = biosDate,
            BaseboardManufacturer = NullIfEmpty(NativeMethods.ReadRegistryString(Registry.LocalMachine, biosPath, "BaseBoardManufacturer")),
            BaseboardProduct = NullIfEmpty(NativeMethods.ReadRegistryString(Registry.LocalMachine, biosPath, "BaseBoardProduct")),
            PendingReboot = HasPendingReboot(),
            DefenderEnabled = includeSlowChecks ? ReadDefenderEnabled() : null,
            FirewallEnabled = includeSlowChecks ? ReadFirewallEnabled() : null,
            SecureBootEnabled = ReadSecureBoot(),
            TimeSynchronized = ReadTimeSynchronized(),
            LastSecurityUpdateUtc = includeSlowChecks ? ReadLastSecurityUpdate() : null,
            Warnings = [.. warnings]
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

    private static string ReadOsName()
    {
        var product = NativeMethods.ReadRegistryString(
            Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "ProductName",
            "Windows");
        var display = NativeMethods.ReadRegistryString(
            Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "DisplayVersion");
        return string.IsNullOrWhiteSpace(display) ? product : $"{product} {display}";
    }

    private string? ReadPowerPlan()
    {
        if (DateTimeOffset.UtcNow - _powerLastRead < TimeSpan.FromMinutes(5)) return _powerPlanCache;
        _powerLastRead = DateTimeOffset.UtcNow;
        var output = CollectorUtilities.RunCommand("powercfg", "/getactivescheme", 3000);
        if (string.IsNullOrWhiteSpace(output)) return _powerPlanCache;
        var match = Regex.Match(output, @"\((?<name>[^\)]+)\)|\s(?<guid>[0-9a-fA-F-]{36})");
        _powerPlanCache = match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : output.Trim().Split('\n').LastOrDefault()?.Trim();
        return _powerPlanCache;
    }

    private static bool HasPendingReboot()
    {
        try
        {
            if (Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null) return true;
            if (Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is not null) return true;
            using var session = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            return session?.GetValue("PendingFileRenameOperations") is string[] value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool? ReadSecureBoot()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return key?.GetValue("UEFISecureBootEnabled") is int value ? value != 0 : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool? ReadTimeSynchronized()
    {
        try
        {
            var value = NativeMethods.ReadRegistryValue(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\W32Time\Parameters", "Type");
            return value?.ToString() is not (null or "NoSync");
        }
        catch
        {
            return null;
        }
    }

    private static bool? ReadDefenderEnabled()
    {
        using var document = CollectorUtilities.RunPowerShellJson(
            "Get-MpComputerStatus | Select-Object AntivirusEnabled,RealTimeProtectionEnabled | ConvertTo-Json -Compress",
            5000);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (document.RootElement.TryGetProperty("AntivirusEnabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False) return enabled.GetBoolean();
        return null;
    }

    private static bool? ReadFirewallEnabled()
    {
        var output = CollectorUtilities.RunCommand("netsh", "advfirewall show allprofiles state", 4000);
        if (string.IsNullOrWhiteSpace(output)) return null;
        var enabled = Regex.IsMatch(output, @"\bON\b|启用", RegexOptions.IgnoreCase);
        var disabled = Regex.IsMatch(output, @"\bOFF\b|关闭", RegexOptions.IgnoreCase);
        return enabled ? true : disabled ? false : null;
    }

    private static DateTimeOffset? ReadLastSecurityUpdate()
    {
        using var document = CollectorUtilities.RunPowerShellJson(
            "$x=Get-HotFix | Where-Object {$_.InstalledOn} | Sort-Object InstalledOn -Descending | Select-Object -First 1 -ExpandProperty InstalledOn; if ($x) { @{ Date = $x } | ConvertTo-Json -Compress }",
            7000);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!document.RootElement.TryGetProperty("Date", out var value)) return null;
        return DateTimeOffset.TryParse(value.ToString(), out var parsed) ? parsed.ToUniversalTime() : null;
    }

    private static double? ReadBatteryHealth()
    {
        using var document = CollectorUtilities.RunPowerShellJson(
            "$d=Get-CimInstance -Namespace root/WMI -ClassName BatteryStaticData -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty DesignedCapacity; $f=Get-CimInstance -Namespace root/WMI -ClassName BatteryFullChargedCapacity -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullChargedCapacity; if ($d -and $f) { @{ Percent = [math]::Round(($f/$d)*100,1) } | ConvertTo-Json -Compress }",
            7000);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!document.RootElement.TryGetProperty("Percent", out var value)) return null;
        return value.TryGetDouble(out var result) ? result : null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}