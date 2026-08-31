using System.Net;
using System.Net.Sockets;
using System.Text;
using Client.Services;

namespace Client.Tests;

public class ServerDiscoveryClientTests
{
    [Fact]
    public async Task DiscoverAsync_ReceivesLoopbackDatagramOnDiscoveryPort()
    {
        const string expectedUrl = "https://discovery.test:5443/remoteMonitoringHub";
        var payload = Encoding.UTF8.GetBytes(
            $$"""{"serverUrl":"{{expectedUrl}}","appName":"CAMS"}""");

        ServerDiscoveryClient.ResetCache();
        try
        {
            var discoveryTask = ServerDiscoveryClient.DiscoverAsync(timeoutMs: 2000, retries: 1);
            using var sender = new UdpClient(AddressFamily.InterNetwork);

            for (var attempt = 0; attempt < 10 && !discoveryTask.IsCompleted; attempt++)
            {
                await Task.Delay(50);
                await sender.SendAsync(payload, new IPEndPoint(IPAddress.Loopback, 5001));
            }

            Assert.Equal(expectedUrl, await discoveryTask);
        }
        finally
        {
            ServerDiscoveryClient.ResetCache();
        }
    }
}
