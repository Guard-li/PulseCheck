using Microsoft.AspNetCore.Http.Json;
using PulseCheck.Agent;
using PulseCheck.Agent.Collectors;
using PulseCheck.Agent.Data;
using PulseCheck.Agent.Diagnostics;
using PulseCheck.Agent.Inventory;
using PulseCheck.Agent.Reporting;
using PulseCheck.Agent.Scheduling;
using PulseCheck.Agent.Security;
using PulseCheck.Agent.Services;
using PulseCheck.AgentService.Logging;
using PulseCheck.Contracts;

namespace PulseCheck.AgentService;

public static class AgentApplication
{
    public static WebApplication Build(string[] args, bool serviceMode)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = baseDirectory,
            ApplicationName = typeof(AgentApplication).Assembly.FullName
        });
        builder.Configuration.AddJsonFile(Path.Combine(baseDirectory, "appsettings.json"), optional: true, reloadOnChange: true);

        var options = new AgentOptions();
        builder.Configuration.GetSection("Agent").Bind(options);
        ApplyCommandLineOverrides(options, args);
        options.ProductName = string.IsNullOrWhiteSpace(options.ProductName) ? PulseCheckConstants.ProductName : options.ProductName;
        options.DataDirectory = ResolvePath(baseDirectory, options.DataDirectory);
        options.ReportDirectory = ResolvePath(baseDirectory, options.ReportDirectory);
        Directory.CreateDirectory(options.DataDirectory);
        Directory.CreateDirectory(options.ReportDirectory);

        var identity = new AgentIdentity(options.DataDirectory);
        if (string.IsNullOrWhiteSpace(options.DisplayName)) options.DisplayName = identity.DisplayName;
        var certificate = CertificateManager.GetOrCreate(options.DataDirectory, identity.DeviceId, identity.DisplayName);
        var database = new SqliteDatabase(Path.Combine(options.DataDirectory, "pulsecheck.db"));
        database.Initialize();
        var rulesPath = Path.Combine(baseDirectory, "config", "health-rules.json");
        if (!File.Exists(rulesPath)) rulesPath = Path.Combine(builder.Environment.ContentRootPath, "config", "health-rules.json");
        var rules = HealthRuleSetLoader.Load(rulesPath);
        var diagnostics = new DiagnosticEngine(rules);
        var collector = new AgentMetricCollector(identity);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.ListenAnyIP(options.ApiPort, listen =>
            {
                if (options.RequireTls) listen.UseHttps(certificate.Certificate);
                else listen.UseConnectionLogging();
            });
        });
        builder.Logging.AddProvider(new RotatingJsonFileLoggerProvider(options.DataDirectory));
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(identity);
        builder.Services.AddSingleton(certificate);
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(diagnostics);
        builder.Services.AddSingleton<SystemInventoryService>();
        builder.Services.AddSingleton<ReportGenerator>();
        builder.Services.AddSingleton(collector);
        builder.Services.AddSingleton<IMetricCollector>(sp => sp.GetRequiredService<AgentMetricCollector>());
        builder.Services.AddSingleton<RealtimeHub>();
        builder.Services.AddSingleton<AlertEngine>();
        builder.Services.AddSingleton<ScanManager>();
        builder.Services.AddSingleton(new AgentSecurity(options.DataDirectory, options));
        builder.Services.AddHostedService<MetricSamplingService>();
        builder.Services.AddHostedService<UdpDiscoveryService>();
        builder.Services.AddHostedService<ScheduleService>();
        builder.Services.Configure<JsonOptions>(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            json.SerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            json.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            json.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
        });

        var app = builder.Build();
        app.UseMiddleware<ApiAuthMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        ApiEndpoints.Map(app);
        return app;
    }

    private static void ApplyCommandLineOverrides(AgentOptions options, string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string? value = null;
            var name = arg;
            var equals = arg.IndexOf('=');
            if (equals > 0)
            {
                name = arg[..equals];
                value = arg[(equals + 1)..];
            }
            if (name is "--Agent:ApiPort" or "--port")
            {
                value ??= i + 1 < args.Length ? args[++i] : null;
                if (int.TryParse(value, out var port) && port is > 0 and <= 65535) options.ApiPort = port;
            }
            else if (name is "--Agent:DataDirectory" or "--data")
            {
                value ??= i + 1 < args.Length ? args[++i] : null;
                if (!string.IsNullOrWhiteSpace(value)) options.DataDirectory = value;
            }
            else if (name is "--Agent:ReportDirectory" or "--reports")
            {
                value ??= i + 1 < args.Length ? args[++i] : null;
                if (!string.IsNullOrWhiteSpace(value)) options.ReportDirectory = value;
            }
        }
    }
    private static string ResolvePath(string baseDirectory, string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
}