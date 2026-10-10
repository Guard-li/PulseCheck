using PulseCheck.AgentService;

if (WindowsServiceRuntime.IsService(args))
{
    await WindowsServiceRuntime.RunAsync(args);
}
else
{
    var app = AgentApplication.Build(args, serviceMode: false);
    await app.RunAsync();
}