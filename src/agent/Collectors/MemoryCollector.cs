using System.Runtime.InteropServices;
using PulseCheck.Contracts;
using PulseCheck.Agent.Native;

namespace PulseCheck.Agent.Collectors;

public sealed class MemoryCollector
{
    public MemoryMetrics Collect()
    {
        var (status, memoryOk) = NativeMethods.ReadMemoryStatus();
        var (performance, performanceOk) = NativeMethods.ReadPerformanceInformation();
        if (!memoryOk) throw new InvalidOperationException("GlobalMemoryStatusEx failed");
        var used = status.TotalPhysical - Math.Min(status.TotalPhysical, status.AvailablePhysical);
        var pageSize = performanceOk && performance.PageSize > 0 ? (ulong)performance.PageSize : 4096UL;
        var modules = status.TotalPhysical > 0 && performanceOk && performance.PhysicalTotal > 0
            ? (ulong)performance.PhysicalTotal * pageSize
            : status.TotalPhysical;
        var hardwareReserved = status.TotalPhysical > modules ? status.TotalPhysical - modules : 0;
        return new MemoryMetrics
        {
            TotalBytes = status.TotalPhysical,
            AvailableBytes = status.AvailablePhysical,
            UsedBytes = used,
            UsagePercent = CollectorUtilities.Percent(used, status.TotalPhysical),
            CommittedBytes = performanceOk ? (ulong)performance.CommitTotal * pageSize : null,
            CommitLimitBytes = performanceOk ? (ulong)performance.CommitLimit * pageSize : null,
            CachedBytes = performanceOk ? (ulong)performance.SystemCache * pageSize : null,
            PageFileTotalBytes = status.TotalPageFile == ulong.MaxValue ? null : status.TotalPageFile,
            PageFileAvailableBytes = status.AvailablePageFile == ulong.MaxValue ? null : status.AvailablePageFile,
            HardwareReservedBytes = hardwareReserved > 0 ? hardwareReserved : null,
            Modules = ReadModules()
        };
    }

    private static MemoryModuleInfo[] ReadModules()
    {
        try
        {
            var table = NativeMethods.ReadSmbiosTable();
            if (table.Length < 8) return [];
            var cursor = 8;
            var result = new List<MemoryModuleInfo>();
            while (cursor + 4 <= table.Length)
            {
                var type = table[cursor];
                var length = table[cursor + 1];
                if (length < 4) break;
                if (type == 17 && cursor + length <= table.Length)
                {
                    var strings = NativeMethods.ReadSmbiosStrings(table, cursor, length);
                    var size = ReadUInt16(table, cursor + 12);
                    var extended = ReadUInt32(table, cursor + 28);
                    ulong? capacity = size == 0xFFFF ? extended * 1024UL * 1024UL : size == 0x7FFF ? null : size * 1024UL * 1024UL;
                    var serial = NativeMethods.GetSmbiosString(strings, table[cursor + 24]);
                    result.Add(new MemoryModuleInfo(
                        NativeMethods.GetSmbiosString(strings, table[cursor + 16]),
                        NativeMethods.GetSmbiosString(strings, table[cursor + 23]),
                        NativeMethods.GetSmbiosString(strings, table[cursor + 26]),
                        string.IsNullOrWhiteSpace(serial) ? null : MaskSerial(serial),
                        capacity,
                        ReadUInt16(table, cursor + 21),
                        FormFactorName(table[cursor + 14])));
                }

                var next = cursor + length;
                while (next < table.Length && !(table[next] == 0 && next + 1 < table.Length && table[next + 1] == 0)) next++;
                cursor = Math.Min(table.Length, next + 2);
            }
            return [.. result];
        }
        catch
        {
            return [];
        }
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        offset + 1 < data.Length ? BitConverter.ToUInt16(data, offset) : (ushort)0;

    private static uint ReadUInt32(byte[] data, int offset) =>
        offset + 3 < data.Length ? BitConverter.ToUInt32(data, offset) : 0;

    private static string? MaskSerial(string serial)
    {
        var value = serial.Trim();
        return value.Length <= 4 ? "***" : $"***{value[^4..]}";
    }

    private static string FormFactorName(byte value) => value switch
    {
        9 => "DIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        _ => "Unknown"
    };
}