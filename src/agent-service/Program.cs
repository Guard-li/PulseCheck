using PulseCheck.AgentService;

if (ElevatedProbeRunner.IsProbe(args))
{
    Environment.ExitCode = ElevatedProbeRunner.Run(args);
    return;
}

if (WindowsServiceRuntime.IsService(args))
{
    await WindowsServiceRuntime.RunAsync(args);
}
else
{
    var app = AgentApplication.Build(args, serviceMode: false);
    await app.RunAsync();
}