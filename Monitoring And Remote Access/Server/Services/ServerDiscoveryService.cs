using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace Server.Services;

public class ServerDiscoveryService : BackgroundService
{
    private const int BroadcastPort = 5001;
    private const int BroadcastIntervalMs = 3000;

    private readonly ILogger<ServerDiscoveryService> _logger;
    private readonly IConfiguration _configuration;

    public ServerDiscoveryService(ILogger<ServerDiscoveryService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var httpsPort = _configuration.GetValue("Cams:HttpsPort", 5000);
        string? lastAdvertisedUrls = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var endpoints = GetLanEndpoints();
            var advertisedUrls = string.Join(", ", endpoints.Select(endpoint =>
                $"https://{endpoint.Address}:{httpsPort}/remoteMonitoringHub"));

            if (endpoints.Count == 0)
            {
                if (lastAdvertisedUrls != string.Empty)
                    _logger.LogWarning("[Discovery] No usable LAN IPv4 address found. Broadcast disabled.");
                lastAdvertisedUrls = string.Empty;
            }
            else
            {
                if (!string.Equals(lastAdvertisedUrls, advertisedUrls, StringComparison.Ordinal))
                {
                    _logger.LogInformation(
                        $"[Discovery] Broadcasting on UDP:{BroadcastPort} — {advertisedUrls}");
                    lastAdvertisedUrls = advertisedUrls;
                }

                foreach (var endpoint in endpoints)
                    await BroadcastAsync(endpoint, httpsPort, stoppingToken);
            }

            await Task.Delay(BroadcastIntervalMs, stoppingToken);
        }
    }

    private async Task BroadcastAsync(
        LanEndpoint endpoint,
        int httpsPort,
        CancellationToken stoppingToken)
    {
        var payload = new DiscoveryPayload(
            $"https://{endpoint.Address}:{httpsPort}/remoteMonitoringHub",
            "CAMS");
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var data = System.Text.Encoding.UTF8.GetBytes(json);

        try
        {
            using var udpClient = new UdpClient(new IPEndPoint(endpoint.Address, 0))
            {
                EnableBroadcast = true
            };

            var targets = endpoint.BroadcastAddress.Equals(IPAddress.Broadcast)
                ? new[] { new IPEndPoint(IPAddress.Broadcast, BroadcastPort) }
                : new[]
                {
                    new IPEndPoint(endpoint.BroadcastAddress, BroadcastPort),
                    new IPEndPoint(IPAddress.Broadcast, BroadcastPort)
                };

            foreach (var target in targets)
            {
                if (stoppingToken.IsCancellationRequested)
                    return;

                try
                {
                    await udpClient.SendAsync(data, data.Length, target);
                }
                catch (SocketException ex)
                {
                    _logger.LogWarning(
                        $"[Discovery] Broadcast to {target.Address} failed from {endpoint.Address}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException)
        {
            _logger.LogWarning(
                $"[Discovery] Broadcast failed from {endpoint.Address}: {ex.Message}");
        }
    }

    private static IReadOnlyList<LanEndpoint> GetLanEndpoints()
    {
        var candidates = new List<(LanEndpoint Endpoint, int Priority)>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var isLan = nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            var hasGateway = nic.GetIPProperties().GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(gateway.Address) &&
                !IsLinkLocal(gateway.Address));

            foreach (var addressInfo in nic.GetIPProperties().UnicastAddresses)
            {
                var address = addressInfo.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork ||
                    IPAddress.IsLoopback(address) ||
                    IsLinkLocal(address) ||
                    (OperatingSystem.IsWindows() &&
                     addressInfo.DuplicateAddressDetectionState != DuplicateAddressDetectionState.Preferred))
                {
                    continue;
                }

                var priority = isLan ? (hasGateway ? 0 : 1) : 2;
                var endpoint = new LanEndpoint(address, GetBroadcastAddress(address, addressInfo.IPv4Mask));
                if (candidates.Any(candidate => candidate.Endpoint.Address.Equals(endpoint.Address)))
                    continue;

                candidates.Add((endpoint, priority));
            }
        }

        var orderedCandidates = candidates
            .OrderBy(candidate => candidate.Priority)
            .ToArray();
        if (orderedCandidates.Length == 0)
            return Array.Empty<LanEndpoint>();

        var bestPriority = orderedCandidates[0].Priority;
        return orderedCandidates
            .Where(candidate => candidate.Priority == bestPriority)
            .Select(candidate => candidate.Endpoint)
            .ToArray();
    }

    private static IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
    {
        if (subnetMask.AddressFamily != AddressFamily.InterNetwork)
            return IPAddress.Broadcast;

        var addressBytes = address.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();
        var broadcastBytes = new byte[addressBytes.Length];
        for (var index = 0; index < broadcastBytes.Length; index++)
            broadcastBytes[index] = (byte)(addressBytes[index] | ~maskBytes[index]);

        return new IPAddress(broadcastBytes);
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    private sealed record LanEndpoint(IPAddress Address, IPAddress BroadcastAddress);
    private record DiscoveryPayload(string ServerUrl, string AppName);
}
