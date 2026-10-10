using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PulseCheck.Agent.Native;

internal static class NativeMethods
{
    internal const int SystemProcessorPerformanceInformationClass = 8;
    internal const int ProcessorInformation = 11;
    internal const uint FirmwareTableProviderSignature = 0x52534D42;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemProcessorPerformanceInformation
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPerformanceInfo(out PerformanceInformation info, uint size);

    [DllImport("ntdll.dll")]
    internal static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    [DllImport("powrprof.dll")]
    internal static extern uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        uint outputBufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetSystemFirmwareTable(
        uint firmwareTableProviderSignature,
        uint firmwareTableId,
        IntPtr firmwareTableBuffer,
        uint bufferSize);

    [DllImport("kernel32.dll")]
    internal static extern ulong GetTickCount64();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LastInputInfo info);

    internal static (MemoryStatusEx Status, bool Success) ReadMemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        var success = GlobalMemoryStatusEx(ref status);
        return (status, success);
    }

    internal static (PerformanceInformation Info, bool Success) ReadPerformanceInformation()
    {
        var success = GetPerformanceInfo(out var info, (uint)Marshal.SizeOf<PerformanceInformation>());
        return (info, success);
    }

    internal static string ReadRegistryString(RegistryKey root, string path, string name, string fallback = "")
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name)?.ToString() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    internal static object? ReadRegistryValue(RegistryKey root, string path, string name, object? fallback = null)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    internal static byte[] ReadSmbiosTable()
    {
        var size = GetSystemFirmwareTable(FirmwareTableProviderSignature, 0, IntPtr.Zero, 0);
        if (size == 0 || size > 4 * 1024 * 1024) return [];
        var pointer = Marshal.AllocHGlobal((int)size);
        try
        {
            var written = GetSystemFirmwareTable(FirmwareTableProviderSignature, 0, pointer, size);
            if (written == 0) return [];
            var buffer = new byte[written];
            Marshal.Copy(pointer, buffer, 0, buffer.Length);
            return buffer;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    internal static string[] ReadSmbiosStrings(byte[] table, int offset, byte formattedLength)
    {
        var strings = new List<string>();
        var cursor = offset + formattedLength;
        if (cursor >= table.Length) return [];
        while (cursor < table.Length)
        {
            var end = Array.IndexOf(table, (byte)0, cursor);
            if (end < 0) break;
            if (end == cursor)
            {
                if (strings.Count > 0) break;
                cursor++;
                continue;
            }
            strings.Add(Encoding.ASCII.GetString(table, cursor, end - cursor));
            cursor = end + 1;
        }
        return [.. strings];
    }

    internal static string GetSmbiosString(string[] strings, byte index) =>
        index > 0 && index <= strings.Length ? strings[index - 1] : string.Empty;
}