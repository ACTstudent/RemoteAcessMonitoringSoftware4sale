using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace Server.Services;

public class ServerDiscoveryService : BackgroundService
{
    private const int BroadcastPort = 5001;
    private const int BroadcastIntervalMs = 3000;
    private static readonly IPEndPoint BroadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, BroadcastPort);

    private readonly ILogger<ServerDiscoveryService> _logger;

    public ServerDiscoveryService(ILogger<ServerDiscoveryService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var localIp = GetLocalIpAddress();
        if (string.IsNullOrEmpty(localIp))
        {
            _logger.LogWarning("[Discovery] No LAN IP found. Broadcast disabled.");
            return;
        }

        var payload = new DiscoveryPayload($"http://{localIp}:5000/remoteMonitoringHub", "CAMS");
        var json = JsonSerializer.Serialize(payload);
        var data = System.Text.Encoding.UTF8.GetBytes(json);

        _logger.LogInformation($"[Discovery] Broadcasting on UDP:{BroadcastPort} — {payload.ServerUrl}");

        using var udpClient = new UdpClient { EnableBroadcast = true };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await udpClient.SendAsync(data, data.Length, BroadcastEndpoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Discovery] Broadcast failed: {ex.Message}");
            }

            await Task.Delay(BroadcastIntervalMs, stoppingToken);
        }
    }

    private static string? GetLocalIpAddress()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    return addr.Address.ToString();
            }
        }
        return null;
    }

    private record DiscoveryPayload(string ServerUrl, string AppName);
}