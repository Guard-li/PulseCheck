using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PulseCheck.AgentService;

public static class WindowsServiceRuntime
{
    private const string ServiceName = "PulseCheckAgent";
    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceAcceptStop = 0x00000001;
    private const uint ServiceRunning = 0x00000004;
    private const uint ServiceStartPending = 0x00000002;
    private const uint ServiceStopPending = 0x00000003;
    private const uint ServiceStopped = 0x00000001;
    private const uint ServiceControlStop = 0x00000001;
    private const uint NoError = 0;

    private static readonly ManualResetEventSlim StopEvent = new(false);
    private static ServiceMainDelegate? _serviceMain;
    private static HandlerExDelegate? _handler;
    private static ServiceStatus _status;
    private static IntPtr _statusHandle;

    public static bool IsService(string[] args) =>
        args.Contains("--service", StringComparer.OrdinalIgnoreCase) || (!Environment.UserInteractive && OperatingSystem.IsWindows());

    public static Task RunAsync(string[] args)
    {
        _serviceMain = ServiceMain;
        _handler = ServiceControlHandler;
        var table = new ServiceTableEntry[2];
        table[0].ServiceName = Marshal.StringToHGlobalUni(ServiceName);
        table[0].ServiceMain = Marshal.GetFunctionPointerForDelegate(_serviceMain);
        table[1].ServiceName = IntPtr.Zero;
        table[1].ServiceMain = IntPtr.Zero;
        var tableSize = Marshal.SizeOf<ServiceTableEntry>() * table.Length;
        var pointer = Marshal.AllocHGlobal(tableSize);
        try
        {
            for (var i = 0; i < table.Length; i++) Marshal.StructureToPtr(table[i], pointer + i * Marshal.SizeOf<ServiceTableEntry>(), false);
            if (!StartServiceCtrlDispatcher(pointer))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "StartServiceCtrlDispatcher failed");
            return Task.CompletedTask;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
            if (table[0].ServiceName != IntPtr.Zero) Marshal.FreeHGlobal(table[0].ServiceName);
        }
    }

    private static void ServiceMain(int argc, IntPtr argv)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(ServiceName, _handler!, IntPtr.Zero);
        if (_statusHandle == IntPtr.Zero) return;
        SetStatus(ServiceStartPending);
        try
        {
            var app = AgentApplication.Build([], serviceMode: true);
            app.StartAsync().GetAwaiter().GetResult();
            SetStatus(ServiceRunning, ServiceAcceptStop);
            StopEvent.Wait();
            SetStatus(ServiceStopPending);
            app.StopAsync().GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SetStatus(ServiceStopped);
        }
        catch
        {
            _status.Win32ExitCode = 1064;
            SetStatus(ServiceStopped);
            throw;
        }
    }

    private static uint ServiceControlHandler(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        if (control == ServiceControlStop) StopEvent.Set();
        return NoError;
    }

    private static void SetStatus(uint state, uint acceptedControls = 0)
    {
        _status.ServiceType = ServiceWin32OwnProcess;
        _status.CurrentState = state;
        _status.ControlsAccepted = acceptedControls;
        _status.Win32ExitCode = NoError;
        _status.ServiceSpecificExitCode = 0;
        _status.CheckPoint = 0;
        _status.WaitHint = 0;
        SetServiceStatus(_statusHandle, ref _status);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceTableEntry
    {
        public IntPtr ServiceName;
        public IntPtr ServiceMain;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ServiceMainDelegate(int argc, IntPtr argv);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint HandlerExDelegate(uint control, uint eventType, IntPtr eventData, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartServiceCtrlDispatcher(IntPtr serviceTable);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerExDelegate handler, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr statusHandle, ref ServiceStatus status);
}