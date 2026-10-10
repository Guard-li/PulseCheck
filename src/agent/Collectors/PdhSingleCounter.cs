using System.Runtime.InteropServices;

namespace PulseCheck.Agent.Collectors;

internal sealed class PdhSingleCounter : IDisposable
{
    private readonly IntPtr _query;
    private readonly IntPtr _counter;
    private readonly object _gate = new();
    private bool _disposed;

    private PdhSingleCounter(IntPtr query, IntPtr counter)
    {
        _query = query;
        _counter = counter;
    }

    public static PdhSingleCounter? TryCreate(params string[] counterPaths)
    {
        foreach (var path in counterPaths)
        {
            var result = TryCreateCore(path);
            if (result is not null) return result;
        }
        return null;
    }

    private static PdhSingleCounter? TryCreateCore(string path)
    {
        if (PdhMethods.PdhOpenQuery(null, IntPtr.Zero, out var query) != 0) return null;
        try
        {
            if (PdhMethods.PdhAddEnglishCounter(query, path, IntPtr.Zero, out var counter) != 0)
            {
                PdhMethods.PdhCloseQuery(query);
                return null;
            }
            PdhMethods.PdhCollectQueryData(query);
            return new PdhSingleCounter(query, counter);
        }
        catch
        {
            PdhMethods.PdhCloseQuery(query);
            return null;
        }
    }

    public double? Read()
    {
        lock (_gate)
        {
            if (_disposed || PdhMethods.PdhCollectQueryData(_query) != 0) return null;
            if (PdhMethods.PdhGetFormattedCounterValue(_counter, 0x00000200, out _, out var value) != 0 || value.Status != 0) return null;
            return double.IsFinite(value.DoubleValue) ? value.DoubleValue : null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            PdhMethods.PdhCloseQuery(_query);
        }
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