using System.Text.Json;
using Client.Services;

namespace Client.Tests;

public sealed class ClientSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"cams-settings-{Guid.NewGuid():N}");

    [Fact]
    public void UpdateServerUrl_PreservesKnownNestedAndUnknownSettings()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "client-settings.json");
        File.WriteAllText(path, """
            {
              "ServerUrl": "https://localhost:5000/remoteMonitoringHub",
              "Enabled": false,
              "TelemetryQueue": { "MaxRecords": 321, "FutureQueueSetting": true },
              "FutureSetting": { "value": 42 }
            }
            """);

        var store = new ClientSettingsStore(path);
        store.UpdateServerUrl("https://cams.example:5443/remoteMonitoringHub");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("https://cams.example:5443/remoteMonitoringHub", document.RootElement.GetProperty("ServerUrl").GetString());
        Assert.False(document.RootElement.GetProperty("Enabled").GetBoolean());
        Assert.Equal(321, document.RootElement.GetProperty("TelemetryQueue").GetProperty("MaxRecords").GetInt32());
        Assert.True(document.RootElement.GetProperty("TelemetryQueue").GetProperty("FutureQueueSetting").GetBoolean());
        Assert.Equal(42, document.RootElement.GetProperty("FutureSetting").GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData("http://cams.example/remoteMonitoringHub")]
    [InlineData("https://cams.example/remoteMonitoringHub/")]
    [InlineData("https://cams.example/RemoteMonitoringHub")]
    [InlineData("https://cams.example/remoteMonitoringHub?x=1")]
    [InlineData("https://user@cams.example/remoteMonitoringHub")]
    [InlineData(" https://cams.example/remoteMonitoringHub")]
    public void TryNormalizeServerUrl_RejectsAnythingOutsideExactContract(string value)
    {
        Assert.False(ClientSettingsStore.TryNormalizeServerUrl(value, out _, out _));
    }

    [Fact]
    public void UpdateServerUrl_WhenAtomicReplaceFails_LeavesOriginalIntactAndCleansTemporaryFile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "client-settings.json");
        const string original = "{\"ServerUrl\":\"https://localhost:5000/remoteMonitoringHub\",\"Keep\":true}";
        File.WriteAllText(path, original);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var exception = Record.Exception(() => new ClientSettingsStore(path)
                .UpdateServerUrl("https://cams.example/remoteMonitoringHub"));
            Assert.True(exception is IOException or UnauthorizedAccessException, exception?.ToString());
        }

        Assert.Equal(original, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
