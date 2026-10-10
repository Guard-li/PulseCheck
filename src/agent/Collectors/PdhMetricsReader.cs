using System.Runtime.InteropServices;

namespace PulseCheck.Agent.Collectors;

internal sealed class PdhMetricsReader : IDisposable
{
    private readonly IntPtr _query;
    private readonly IntPtr _readBytes;
    private readonly IntPtr _writeBytes;
    private readonly IntPtr _responseTime;
    private readonly IntPtr _busyTime;
    private bool _disposed;

    private PdhMetricsReader(IntPtr query, IntPtr read, IntPtr write, IntPtr response, IntPtr busy)
    {
        _query = query;
        _readBytes = read;
        _writeBytes = write;
        _responseTime = response;
        _busyTime = busy;
    }

    public static PdhMetricsReader? TryCreate()
    {
        if (PdhMethods.PdhOpenQuery(null, IntPtr.Zero, out var query) != 0) return null;
        try
        {
            if (PdhMethods.PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Disk Read Bytes/sec", IntPtr.Zero, out var read) != 0 ||
                PdhMethods.PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Disk Write Bytes/sec", IntPtr.Zero, out var write) != 0 ||
                PdhMethods.PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Avg. Disk sec/Transfer", IntPtr.Zero, out var response) != 0 ||
                PdhMethods.PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\% Disk Time", IntPtr.Zero, out var busy) != 0)
            {
                PdhMethods.PdhCloseQuery(query);
                return null;
            }
            PdhMethods.PdhCollectQueryData(query);
            return new PdhMetricsReader(query, read, write, response, busy);
        }
        catch
        {
            PdhMethods.PdhCloseQuery(query);
            return null;
        }
    }

    public (double? Read, double? Write, double? ResponseMs, double? Busy) Read()
    {
        try
        {
            if (_disposed || PdhMethods.PdhCollectQueryData(_query) != 0) return (null, null, null, null);
            var read = ReadValue(_readBytes, 0);
            var write = ReadValue(_writeBytes, 0);
            var response = ReadValue(_responseTime, 1000);
            var busy = ReadValue(_busyTime, 1);
            return (read, write, response, busy);
        }
        catch
        {
            return (null, null, null, null);
        }
    }

    private static double? ReadValue(IntPtr counter, double scale)
    {
        if (counter == IntPtr.Zero) return null;
        if (PdhMethods.PdhGetFormattedCounterValue(counter, 0x00000200, out _, out var value) != 0) return null;
        if (value.Status != 0) return null;
        return value.DoubleValue * scale;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PdhMethods.PdhCloseQuery(_query);
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFmtCounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double DoubleValue;
    }

    private static class PdhMethods
    {
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        internal static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        internal static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        internal static extern uint PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll")]
        internal static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

        [DllImport("pdh.dll")]
        internal static extern uint PdhCloseQuery(IntPtr query);
    }
}