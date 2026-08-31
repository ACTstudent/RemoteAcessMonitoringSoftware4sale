using Client.Services;
using Shared.Contracts;

namespace Client.Tests;

public sealed class DurableTelemetryQueueTests
{
    [Fact]
    public void NormalizeInfraction_RemovesClientIdentityAndPreservesPolicyTarget()
    {
        var item = TelemetryBatchItem.From(new InfractionMessage(
            "spoofed-connection", "spoofed-student", "spoofed-pc", "game.exe", "Application", DateTime.UtcNow));

        Assert.True(DurableTelemetryQueue.TryNormalizeItem(item, out var normalized));
        Assert.Equal(string.Empty, normalized.Infraction!.ConnectionId);
        Assert.Equal(string.Empty, normalized.Infraction.StudentId);
        Assert.Equal(string.Empty, normalized.Infraction.PcName);
        Assert.Equal("game.exe", normalized.Infraction.Target);
    }

    [Fact]
    public async Task Queue_SurvivesRestartAndStoresOnlyNormalizedDtoValues()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var queue = new DurableTelemetryQueue(directory, maxRecords: 10, maxBytes: 64 * 1024);
            await queue.EnqueueAsync(TelemetryBatchItem.From(new ActiveAppMessage(
                "connection", "student", "PC-01", "chrome - Private page title", DateTime.UtcNow)));
            await queue.EnqueueAsync(TelemetryBatchItem.From(new WebsiteActivityMessage(
                "connection", "student", "PC-01", "https://user:secret@Example.com/private?q=secret",
                "Chrome", DateTime.UtcNow)));
            await queue.EnqueueAsync(TelemetryBatchItem.From(new BrowserMonitoringStatusMessage(
                "connection", "student", "PC-01", "Chrome", BrowserMonitoringMode.ManagedProtocol,
                DateTime.UtcNow, "https://user:secret@example.com/private")));

            var restarted = new DurableTelemetryQueue(directory, maxRecords: 10, maxBytes: 64 * 1024);
            var batch = await restarted.ReadBatchAsync(10);

            Assert.Equal(3, batch.Count);
            Assert.Equal("chrome", batch[0].Item.ActiveApp!.ApplicationName);
            Assert.Equal(string.Empty, batch[0].Item.ActiveApp!.ConnectionId);
            Assert.Equal(string.Empty, batch[0].Item.ActiveApp!.StudentId);
            Assert.Equal("example.com", batch[1].Item.WebsiteActivity!.Domain);
            Assert.Null(batch[2].Item.BrowserMonitoringStatus!.Detail);
            Assert.DoesNotContain("secret", await File.ReadAllTextAsync(restarted.QueueFilePath), StringComparison.OrdinalIgnoreCase);

            await restarted.AcknowledgeAsync(new[] { batch[0].Id });
            var remaining = await new DurableTelemetryQueue(directory, 10, 64 * 1024).ReadBatchAsync(10);
            Assert.Equal(2, remaining.Count);
            Assert.NotNull(remaining[0].Item.WebsiteActivity);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Queue_DropsOldestAndSkipsMalformedRecords()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var queue = new DurableTelemetryQueue(directory, maxRecords: 2, maxBytes: 64 * 1024);
            await queue.EnqueueAsync(TelemetryBatchItem.From(new IdleStatusMessage("", "", "", true, DateTime.UtcNow)));
            await queue.EnqueueAsync(TelemetryBatchItem.From(new ActiveAppMessage("", "", "", "code", DateTime.UtcNow)));
            await queue.EnqueueAsync(TelemetryBatchItem.From(new WebsiteActivityMessage(
                "", "", "", "example.com", "browser", DateTime.UtcNow)));
            await File.AppendAllTextAsync(queue.QueueFilePath, "{malformed\n");

            var restarted = new DurableTelemetryQueue(directory, maxRecords: 2, maxBytes: 64 * 1024);
            var batch = await restarted.ReadBatchAsync(10);

            Assert.Equal(2, batch.Count);
            Assert.NotNull(batch[0].Item.ActiveApp);
            Assert.NotNull(batch[1].Item.WebsiteActivity);
            Assert.DoesNotContain("malformed", await File.ReadAllTextAsync(restarted.QueueFilePath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "cams-telemetry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
