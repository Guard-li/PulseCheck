using System.Reflection;
using System.Text.Json;
using PulseCheck.Contracts;

namespace PulseCheck.Agent;

public sealed record AgentIdentityFile(string DeviceId, string DisplayName, DateTimeOffset CreatedAtUtc);

public sealed class AgentIdentity
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = JsonDefaults.Create(true);

    public AgentIdentity(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "agent-identity.json");
        var loaded = Load();
        DeviceId = loaded?.DeviceId ?? Guid.NewGuid().ToString("N");
        DisplayName = loaded?.DisplayName ?? Environment.MachineName;
        if (loaded is null) Save();
    }

    public string DeviceId { get; private set; }
    public string DisplayName { get; private set; }
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    public void Rename(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return;
        DisplayName = displayName.Trim();
        Save();
    }

    private AgentIdentityFile? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            return JsonSerializer.Deserialize<AgentIdentityFile>(File.ReadAllText(_path), _json);
        }
        catch
        {
            return null;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new AgentIdentityFile(DeviceId, DisplayName, DateTimeOffset.UtcNow), _json));
    }
}