using System.Runtime.InteropServices;
using Microsoft.Win32;
using PulseCheck.Agent.Native;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Collectors;

public sealed class CpuCollector : IDisposable
{
    private readonly object _gate = new();
    private NativeMethods.SystemProcessorPerformanceInformation[] _last = [];
    private DateTimeOffset _lastSample = DateTimeOffset.MinValue;
    private PdhSingleCounter? _performanceCounter;
    private bool _performanceInitialized;

    public CpuMetrics Collect()
    {
        lock (_gate)
        {
            var cores = ReadPerCore();
            var now = DateTimeOffset.UtcNow;
            var usage = CalculateUsage(cores, now);
            if (!_performanceInitialized)
            {
                _performanceCounter = PdhSingleCounter.TryCreate(
                    @"\Processor Information(_Total)\% Processor Utility",
                    @"\Processor Information(_Total)\% Processor Time");
                _performanceInitialized = true;
            }
            var totalUsage = _performanceCounter?.Read();
            if (totalUsage is not null)
                usage.Total = Math.Round(Math.Clamp(totalUsage.Value, 0, 100), 2);

            var power = ReadProcessorPower();
            var name = NativeMethods.ReadRegistryString(
                Registry.LocalMachine,
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                "ProcessorNameString",
                Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown CPU");
            var vendor = NativeMethods.ReadRegistryString(
                Registry.LocalMachine,
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                "VendorIdentifier");
            var identifier = NativeMethods.ReadRegistryString(
                Registry.LocalMachine,
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                "Identifier");
            var baseMhz = TryReadUInt(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "~MHz");
            var physical = TryReadInt(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "NumberOfCores") ?? 0;
            if (physical <= 0) physical = Math.Max(1, Environment.ProcessorCount / 2);

            return new CpuMetrics
            {
                Name = name,
                Manufacturer = vendor,
                ProcessorId = identifier,
                PhysicalCoreCount = physical,
                LogicalProcessorCount = cores.Length == 0 ? Environment.ProcessorCount : cores.Length,
                TotalUsagePercent = usage.Total,
                PerCoreUsagePercent = usage.PerCore,
                CurrentFrequencyMhz = power.CurrentMhz,
                BaseFrequencyMhz = baseMhz,
                MaxFrequencyMhz = power.MaxMhz,
                TemperatureCelsius = null,
                PackagePowerWatts = null,
                ThermalThrottling = power.Throttling,
                TemperatureAvailability = new MetricAvailability(DataAvailability.RequiresSensorDriver, "Windows 标准 API 不提供稳定的 CPU 温度；需要用户自行安装兼容传感器驱动")
            };
        }
    }

    private NativeMethods.SystemProcessorPerformanceInformation[] ReadPerCore()
    {
        var count = Environment.ProcessorCount;
        var size = Marshal.SizeOf<NativeMethods.SystemProcessorPerformanceInformation>();
        var pointer = Marshal.AllocHGlobal(size * count);
        try
        {
            var result = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemProcessorPerformanceInformationClass,
                pointer,
                size * count,
                out _);
            if (result < 0) return [];
            var values = new NativeMethods.SystemProcessorPerformanceInformation[count];
            for (var i = 0; i < count; i++)
                values[i] = Marshal.PtrToStructure<NativeMethods.SystemProcessorPerformanceInformation>(pointer + i * size);
            return values;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private (double Total, double[] PerCore) CalculateUsage(
        NativeMethods.SystemProcessorPerformanceInformation[] current,
        DateTimeOffset now)
    {
        if (current.Length == 0 || _last.Length != current.Length || _lastSample == DateTimeOffset.MinValue)
        {
            _last = current;
            _lastSample = now;
            return (0, new double[current.Length]);
        }

        var perCore = new double[current.Length];
        long totalIdle = 0;
        long totalKernel = 0;
        long totalUser = 0;
        for (var i = 0; i < current.Length; i++)
        {
            var kernel = current[i].KernelTime - _last[i].KernelTime;
            var user = current[i].UserTime - _last[i].UserTime;
            var idle = current[i].IdleTime - _last[i].IdleTime;
            var total = kernel + user;
            perCore[i] = total <= 0 ? 0 : Math.Round(Math.Clamp((total - idle) * 100d / total, 0, 100), 2);
            totalIdle += Math.Max(0, idle);
            totalKernel += Math.Max(0, kernel);
            totalUser += Math.Max(0, user);
        }
        _last = current;
        _lastSample = now;
        var all = totalKernel + totalUser;
        var totalUsage = all <= 0 ? 0 : Math.Round(Math.Clamp((all - totalIdle) * 100d / all, 0, 100), 2);
        return (totalUsage, perCore);
    }

    private static (double? CurrentMhz, double? MaxMhz, bool? Throttling) ReadProcessorPower()
    {
        try
        {
            var count = Environment.ProcessorCount;
            var size = Marshal.SizeOf<NativeMethods.ProcessorPowerInformation>();
            var pointer = Marshal.AllocHGlobal(size * count);
            try
            {
                var result = NativeMethods.CallNtPowerInformation(
                    NativeMethods.ProcessorInformation,
                    IntPtr.Zero,
                    0,
                    pointer,
                    (uint)(size * count));
                if (result != 0) return (null, null, null);
                var values = new NativeMethods.ProcessorPowerInformation[count];
                for (var i = 0; i < count; i++)
                    values[i] = Marshal.PtrToStructure<NativeMethods.ProcessorPowerInformation>(pointer + i * size);
                var current = values.Average(x => (double)x.CurrentMhz);
                var maximum = values.Max(x => (double)x.MaxMhz);
                var throttling = values.Any(x => x.CurrentMhz > 0 && x.MhzLimit > 0 && x.CurrentMhz < x.MhzLimit * 0.7);
                return (Math.Round(current, 0), Math.Round(maximum, 0), throttling);
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static double? TryReadUInt(RegistryKey root, string path, string name)
    {
        var value = NativeMethods.ReadRegistryValue(root, path, name);
        return value is int i ? i : null;
    }

    private static int? TryReadInt(RegistryKey root, string path, string name)
    {
        var value = NativeMethods.ReadRegistryValue(root, path, name);
        return value is int i ? i : null;
    }

    public void Dispose() => _performanceCounter?.Dispose();
}