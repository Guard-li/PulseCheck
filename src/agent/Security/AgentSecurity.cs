using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Security;

public sealed record AuthSession(string ControllerId, string ControllerName, string Role, DateTimeOffset ExpiresAtUtc);

public sealed class AgentSecurity
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = JsonDefaults.Create(true);
    private readonly object _gate = new();
    private SecurityFile _file;
    private string? _currentPairingCode;
    private DateTimeOffset _pairingCodeExpiresAt;

    public AgentSecurity(string dataDirectory, AgentOptions options)
    {
        _path = Path.Combine(dataDirectory, "security.json");
        _file = Load();
        RotatePairingCode(options.PairingCodeLifetimeMinutes);
    }

    public string CurrentPairingCode
    {
        get { lock (_gate) return IsPairingCodeValid ? _currentPairingCode! : RotatePairingCode(15); }
    }

    public DateTimeOffset PairingCodeExpiresAtUtc
    {
        get { lock (_gate) return _pairingCodeExpiresAt; }
    }

    public bool IsPairingCodeValid => _currentPairingCode is not null && DateTimeOffset.UtcNow <= _pairingCodeExpiresAt;

    public string RotatePairingCode(int lifetimeMinutes)
    {
        lock (_gate)
        {
            _currentPairingCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            _pairingCodeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(Math.Clamp(lifetimeMinutes, 5, 60));
            _file = _file with { PairingCodeHash = Hash(_currentPairingCode), PairingCodeExpiresAtUtc = _pairingCodeExpiresAt };
            Save();
            return _currentPairingCode;
        }
    }

    public PairResponse? Pair(PairRequest request)
    {
        lock (_gate)
        {
            if (!IsPairingCodeValid || !FixedEquals(Hash(request.PairingCode.Trim()), _file.PairingCodeHash)) return null;
            var role = string.Equals(request.Role, "owner", StringComparison.OrdinalIgnoreCase) ? "owner" : "readonly";
            var controllerId = Guid.NewGuid().ToString("N");
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var expires = DateTimeOffset.UtcNow.AddYears(2);
            var tokenRecord = new TokenRecord(controllerId, request.ControllerName.Trim(), role, Hash(token), expires);
            _file = _file with
            {
                Tokens = [.. _file.Tokens, tokenRecord],
                PairingCodeHash = string.Empty,
                PairingCodeExpiresAtUtc = null
            };
            _currentPairingCode = null;
            _pairingCodeExpiresAt = DateTimeOffset.MinValue;
            Save();
            return new PairResponse(_file.DeviceId, Environment.MachineName, token, role, expires) { ControllerId = controllerId };
        }
    }

    public AuthSession? Authenticate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        lock (_gate)
        {
            var tokenHash = Hash(token);
            var match = _file.Tokens.FirstOrDefault(x => FixedEquals(x.TokenHash, tokenHash) && x.ExpiresAtUtc > DateTimeOffset.UtcNow);
            return match is null ? null : new AuthSession(match.ControllerId, match.ControllerName, match.Role, match.ExpiresAtUtc);
        }
    }

    public bool Revoke(string controllerId)
    {
        lock (_gate)
        {
            var remaining = _file.Tokens.Where(x => !string.Equals(x.ControllerId, controllerId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (remaining.Length == _file.Tokens.Length) return false;
            _file = _file with { Tokens = remaining };
            Save();
            return true;
        }
    }

    public SecurityFile Snapshot()
    {
        lock (_gate) return _file with { Tokens = _file.Tokens.Select(x => x with { TokenHash = string.Empty }).ToArray() };
    }

    private SecurityFile Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var loaded = JsonSerializer.Deserialize<SecurityFile>(File.ReadAllText(_path), _json);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // A damaged security file must not silently authorize; start with an empty token list.
        }
        var created = new SecurityFile(Guid.NewGuid().ToString("N"), string.Empty, null, []);
        _file = created;
        Save();
        return created;
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        try
        {
            if (File.Exists(_path))
                File.SetAttributes(_path, File.GetAttributes(_path) & ~FileAttributes.Hidden);
        }
        catch { }
        File.WriteAllText(_path, JsonSerializer.Serialize(_file, _json));
        try
        {
            File.SetAttributes(_path, File.GetAttributes(_path) | FileAttributes.Hidden);
        }
        catch { }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null) return false;
        var a = Encoding.UTF8.GetBytes(left);
        var b = Encoding.UTF8.GetBytes(right);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}

public sealed record SecurityFile(
    string DeviceId,
    string PairingCodeHash,
    DateTimeOffset? PairingCodeExpiresAtUtc,
    TokenRecord[] Tokens);

public sealed record TokenRecord(
    string ControllerId,
    string ControllerName,
    string Role,
    string TokenHash,
    DateTimeOffset ExpiresAtUtc);