using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Contracts;

namespace Client.Services;

public sealed record DurableTelemetryRecord(Guid Id, TelemetryBatchItem Item);

public sealed class DurableTelemetryQueue
{
    private const string QueueFileName = "telemetry.queue";
    private const int MaxRecordBytes = 16 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _maxRecords;
    private readonly long _maxBytes;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private bool _initialized;
    private int _recordCount;

    public DurableTelemetryQueue(string? queueDirectory = null, int maxRecords = 1_000, long maxBytes = 2 * 1024 * 1024)
    {
        if (maxRecords < 1)
            throw new ArgumentOutOfRangeException(nameof(maxRecords));
        if (maxBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        queueDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CAMS",
            "Telemetry");
        if (string.IsNullOrWhiteSpace(queueDirectory))
            throw new InvalidOperationException("A LocalAppData telemetry queue path is unavailable.");

        QueueFilePath = Path.Combine(queueDirectory, QueueFileName);
        _maxRecords = maxRecords;
        _maxBytes = maxBytes;
    }

    public string QueueFilePath { get; }

    public async Task EnqueueAsync(TelemetryBatchItem item, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeItem(item, out var normalized))
            throw new ArgumentException("The telemetry item is invalid or is not privacy-safe.", nameof(item));

        var envelope = new QueueEnvelope(Guid.NewGuid(), normalized);
        var line = JsonSerializer.Serialize(envelope, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        if (bytes.LongLength > _maxBytes)
            throw new ArgumentException("The telemetry item exceeds the durable queue size limit.", nameof(item));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken);
            await AppendCoreAsync(bytes, cancellationToken);
            _recordCount++;

            if (_recordCount > _maxRecords || new FileInfo(QueueFilePath).Length > _maxBytes)
            {
                var result = await ReadRecordsCoreAsync(cancellationToken);
                ApplyBounds(result.Records);
                await RewriteCoreAsync(result.Records, cancellationToken);
                _recordCount = result.Records.Count;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DurableTelemetryRecord>> ReadBatchAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken);
            var result = await ReadRecordsCoreAsync(cancellationToken);
            var bounded = ApplyBounds(result.Records);
            if (result.Dirty || bounded)
                await RewriteCoreAsync(result.Records, cancellationToken);
            _recordCount = result.Records.Count;

            return result.Records
                .Take(batchSize)
                .Select(record => new DurableTelemetryRecord(record.Id, record.Item))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AcknowledgeAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var acknowledged = ids.Where(id => id != Guid.Empty).ToHashSet();
        if (acknowledged.Count == 0)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedCoreAsync(cancellationToken);
            var result = await ReadRecordsCoreAsync(cancellationToken);
            var removed = result.Records.RemoveAll(record => acknowledged.Contains(record.Id));
            var bounded = ApplyBounds(result.Records);
            if (removed > 0 || result.Dirty || bounded)
                await RewriteCoreAsync(result.Records, cancellationToken);
            _recordCount = result.Records.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedCoreAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        var directory = Path.GetDirectoryName(QueueFilePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = GetTemporaryPath();
        if (!File.Exists(QueueFilePath) && File.Exists(temporaryPath))
            File.Move(temporaryPath, QueueFilePath);
        else if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);

        var result = await ReadRecordsCoreAsync(cancellationToken);
        var bounded = ApplyBounds(result.Records);
        if (result.Dirty || bounded)
            await RewriteCoreAsync(result.Records, cancellationToken);
        _recordCount = result.Records.Count;
        _initialized = true;
    }

    private async Task AppendCoreAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            QueueFilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private async Task<QueueReadResult> ReadRecordsCoreAsync(CancellationToken cancellationToken)
    {
        var records = new List<QueueEnvelope>();
        if (!File.Exists(QueueFilePath))
            return new QueueReadResult(records, false);

        var dirty = false;
        using var stream = new FileStream(
            QueueFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, false));
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Contains('\uFFFD') ||
                Encoding.UTF8.GetByteCount(line) > MaxRecordBytes)
            {
                dirty = true;
                continue;
            }

            try
            {
                var envelope = JsonSerializer.Deserialize<QueueEnvelope>(line, _jsonOptions);
                if (envelope is null || envelope.Id == Guid.Empty ||
                    !TryNormalizeItem(envelope.Item, out var normalized))
                {
                    dirty = true;
                    continue;
                }

                var canonical = new QueueEnvelope(envelope.Id, normalized);
                records.Add(canonical);
                dirty |= !string.Equals(line, JsonSerializer.Serialize(canonical, _jsonOptions), StringComparison.Ordinal);
            }
            catch (JsonException)
            {
                dirty = true;
            }
        }

        return new QueueReadResult(records, dirty);
    }

    private bool ApplyBounds(List<QueueEnvelope> records)
    {
        var changed = false;
        while (records.Count > _maxRecords)
        {
            records.RemoveAt(0);
            changed = true;
        }

        var sizes = records
            .Select(record => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(record, _jsonOptions)) + 1L)
            .ToList();
        var totalBytes = sizes.Sum();
        while (records.Count > 0 && totalBytes > _maxBytes)
        {
            totalBytes -= sizes[0];
            sizes.RemoveAt(0);
            records.RemoveAt(0);
            changed = true;
        }

        return changed;
    }

    private async Task RewriteCoreAsync(IReadOnlyList<QueueEnvelope> records, CancellationToken cancellationToken)
    {
        var temporaryPath = GetTemporaryPath();
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            foreach (var record in records)
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, _jsonOptions) + "\n");
                await stream.WriteAsync(bytes, cancellationToken);
            }
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, QueueFilePath, overwrite: true);
    }

    private string GetTemporaryPath() => QueueFilePath + ".tmp";

    public static bool TryNormalizeItem(TelemetryBatchItem? item, out TelemetryBatchItem normalized)
    {
        normalized = new TelemetryBatchItem();
        if (item is null || item.PayloadCount != 1)
            return false;

        if (item.IdleStatus is { } idle)
        {
            var timestamp = NormalizeTimestamp(idle.Timestamp);
            if (!IsRecent(timestamp)) return false;
            normalized = TelemetryBatchItem.From(new IdleStatusMessage(
                string.Empty,
                string.Empty,
                string.Empty,
                idle.IsIdle,
                timestamp));
            return true;
        }

        if (item.ActiveApp is { } app)
        {
            if (!TelemetryValueNormalizer.TryNormalizeApplicationName(app.ApplicationName, out var applicationName))
                return false;
            var timestamp = NormalizeTimestamp(app.Timestamp);
            if (!IsRecent(timestamp)) return false;
            normalized = TelemetryBatchItem.From(new ActiveAppMessage(
                string.Empty,
                string.Empty,
                string.Empty,
                applicationName,
                timestamp));
            return true;
        }

        if (item.WebsiteActivity is { } website)
        {
            if (!WebsiteDomainNormalizer.TryNormalize(website.Domain, out var domain) || domain.Length > 300 ||
                string.IsNullOrWhiteSpace(website.Browser) || website.Browser.Length > 50 ||
                website.Browser.Any(char.IsControl))
                return false;
            var timestamp = NormalizeTimestamp(website.Timestamp);
            if (!IsRecent(timestamp)) return false;
            normalized = TelemetryBatchItem.From(new WebsiteActivityMessage(
                string.Empty,
                string.Empty,
                string.Empty,
                domain,
                website.Browser.Trim().ToLowerInvariant(),
                timestamp));
            return true;
        }

        if (item.BrowserMonitoringStatus is { } status)
        {
            if (string.IsNullOrWhiteSpace(status.Browser) || status.Browser.Length > 50 ||
                status.Browser.Any(char.IsControl) || !Enum.IsDefined(status.Mode) ||
                (status.Detail?.Length ?? 0) > 300 || (status.Detail?.Any(char.IsControl) ?? false))
                return false;
            var timestamp = NormalizeTimestamp(status.Timestamp);
            if (!IsRecent(timestamp)) return false;
            normalized = TelemetryBatchItem.From(new BrowserMonitoringStatusMessage(
                string.Empty,
                string.Empty,
                string.Empty,
                status.Browser.Trim().ToLowerInvariant(),
                status.Mode,
                timestamp,
                NormalizeBrowserDetail(status.Detail)));
            return true;
        }

        if (item.Infraction is { } infraction)
        {
            if (string.IsNullOrWhiteSpace(infraction.TargetType) || infraction.TargetType.Length > 50 ||
                string.IsNullOrWhiteSpace(infraction.Target) || infraction.Target.Length > 500 ||
                infraction.TargetType.Any(char.IsControl) || infraction.Target.Any(char.IsControl))
                return false;
            var timestamp = NormalizeTimestamp(infraction.Timestamp);
            if (!IsRecent(timestamp)) return false;
            normalized = TelemetryBatchItem.From(new InfractionMessage(
                string.Empty,
                string.Empty,
                string.Empty,
                infraction.Target.Trim(),
                infraction.TargetType.Trim(),
                timestamp));
            return true;
        }

        return false;
    }

    private static DateTime NormalizeTimestamp(DateTime timestamp)
    {
        if (timestamp == default)
            return DateTime.UtcNow;
        if (timestamp.Kind == DateTimeKind.Local)
            return timestamp.ToUniversalTime();
        return timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
            : timestamp;
    }

    private static bool IsRecent(DateTime timestamp)
    {
        var now = DateTime.UtcNow;
        return timestamp >= now.AddDays(-6) && timestamp <= now.AddMinutes(5);
    }

    private static string? NormalizeBrowserDetail(string? detail) =>
        BrowserMonitoringStatusMessage.NormalizeDetail(detail);

    private sealed record QueueEnvelope(Guid Id, TelemetryBatchItem Item);
    private sealed record QueueReadResult(List<QueueEnvelope> Records, bool Dirty);
}
