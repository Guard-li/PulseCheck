using System.Text.Json;
using PulseCheck.Contracts;

namespace PulseCheck.Agent;

public sealed class RuntimeSettingsStore
{
    private readonly AgentOptions _options;
    private readonly string _path;
    private readonly JsonSerializerOptions _json = JsonDefaults.Create(true);
    private readonly object _gate = new();

    public RuntimeSettingsStore(AgentOptions options)
    {
        _options = options;
        _path = Path.Combine(options.DataDirectory, "runtime-settings.json");
        Load();
    }

    public int SampleIntervalMs
    {
        get
        {
            lock (_gate) return Math.Clamp(_options.SampleIntervalMs, 500, 10000);
        }
    }

    public RuntimeSettingsView Snapshot()
    {
        lock (_gate)
        {
            return new RuntimeSettingsView(SampleIntervalMs, 500, 10000, "实时采样周期越短，CPU、磁盘和网络曲线越灵敏，但资源占用更高。");
        }
    }

    public RuntimeSettingsView Update(UpdateSettingsRequest request)
    {
        lock (_gate)
        {
            _options.SampleIntervalMs = Math.Clamp(request.SampleIntervalMs, 500, 10000);
            Save();
            return Snapshot();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var persisted = JsonSerializer.Deserialize<RuntimeSettingsFile>(File.ReadAllText(_path), _json);
            if (persisted is not null) _options.SampleIntervalMs = Math.Clamp(persisted.SampleIntervalMs, 500, 10000);
        }
        catch
        {
            _options.SampleIntervalMs = Math.Clamp(_options.SampleIntervalMs, 500, 10000);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new RuntimeSettingsFile(_options.SampleIntervalMs), _json));
    }

    private sealed record RuntimeSettingsFile(int SampleIntervalMs);
}