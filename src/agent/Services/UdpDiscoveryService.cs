using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PulseCheck.Contracts;

namespace PulseCheck.Agent.Services;

public sealed class UdpDiscoveryService : BackgroundService
{
    private readonly AgentOptions _options;
    private readonly AgentIdentity _identity;
    private readonly Security.AgentCertificate _certificate;
    private readonly ILogger<UdpDiscoveryService> _logger;
    private readonly JsonSerializerOptions _json = JsonDefaults.Create();

    public UdpDiscoveryService(
        AgentOptions options,
        AgentIdentity identity,
        Security.AgentCertificate certificate,
        ILogger<UdpDiscoveryService> logger)
    {
        _options = options;
        _identity = identity;
        _certificate = certificate;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableUdpDiscovery) return;
        using var client = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, _options.DiscoveryPort));
            client.EnableBroadcast = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UDP 设备发现端口 {Port} 无法绑定，已跳过发现服务", _options.DiscoveryPort);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var packet = await client.ReceiveAsync(stoppingToken);
                var request = Encoding.UTF8.GetString(packet.Buffer).Trim();
                if (!request.Equals("PULSECHECK_DISCOVER_V1", StringComparison.Ordinal)) continue;
                var response = new
                {
                    service = "PulseCheck",
                    version = 1,
                    deviceId = _identity.DeviceId,
                    displayName = _identity.DisplayName,
                    apiPort = _options.ApiPort,
                    tls = _options.RequireTls,
                    certificateSha256 = _certificate.Sha256Fingerprint,
                    agentVersion = _identity.Version
                };
                var bytes = JsonSerializer.SerializeToUtf8Bytes(response, _json);
                await client.SendAsync(bytes, packet.RemoteEndPoint, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "处理 UDP 发现请求失败");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}