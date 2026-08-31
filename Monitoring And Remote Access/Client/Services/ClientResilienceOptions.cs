namespace Client.Services;

public sealed class ClientResilienceOptions
{
    public int PolicyRefreshIntervalSeconds { get; set; } = 30;
    public TelemetryQueueOptions TelemetryQueue { get; set; } = new();

    public static ClientResilienceOptions Load()
    {
        var settings = new ClientSettingsStore().LoadOrDefault();
        return Normalize(new ClientResilienceOptions
        {
            PolicyRefreshIntervalSeconds = settings.PolicyRefreshIntervalSeconds,
            TelemetryQueue = settings.TelemetryQueue
        });
    }

    private static ClientResilienceOptions Normalize(ClientResilienceOptions options)
    {
        options.PolicyRefreshIntervalSeconds = Math.Clamp(options.PolicyRefreshIntervalSeconds, 5, 3600);
        options.TelemetryQueue ??= new TelemetryQueueOptions();
        options.TelemetryQueue.MaxRecords = Math.Clamp(options.TelemetryQueue.MaxRecords, 10, 10_000);
        options.TelemetryQueue.MaxBytes = Math.Clamp(options.TelemetryQueue.MaxBytes, 64 * 1024, 16 * 1024 * 1024);
        options.TelemetryQueue.BatchSize = Math.Clamp(options.TelemetryQueue.BatchSize, 1, 50);
        return options;
    }
}

public sealed class TelemetryQueueOptions
{
    public int MaxRecords { get; set; } = 1_000;
    public long MaxBytes { get; set; } = 2 * 1024 * 1024;
    public int BatchSize { get; set; } = 25;
}
