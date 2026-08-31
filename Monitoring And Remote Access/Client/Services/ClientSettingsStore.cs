using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace Client.Services;

public sealed class ClientSettings
{
    public string ServerUrl { get; set; } = ClientSettingsStore.DefaultServerUrl;
    public bool Enabled { get; set; } = true;
    public bool ManageChrome { get; set; } = true;
    public bool ManageBrave { get; set; } = true;
    public int ChromePort { get; set; } = 9222;
    public int BravePort { get; set; } = 9223;
    public int RestartDelayMilliseconds { get; set; } = 1000;
    public int PolicyRefreshIntervalSeconds { get; set; } = 30;
    public TelemetryQueueOptions TelemetryQueue { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }

    public ManagedBrowserOptions ToManagedBrowserOptions() => new(
        Enabled, ManageChrome, ManageBrave, ChromePort, BravePort, RestartDelayMilliseconds);
}

public sealed class ClientSettingsStore
{
    public const string DefaultServerUrl = "https://localhost:5000/remoteMonitoringHub";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;

    public ClientSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client-settings.json");
    }

    public ClientSettings Load()
    {
        if (!File.Exists(_path))
            return new ClientSettings();

        var settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(_path), SerializerOptions)
            ?? throw new InvalidDataException("Client settings must contain a JSON object.");
        if (!TryNormalizeServerUrl(settings.ServerUrl, out var normalized, out var error))
            throw new InvalidDataException($"Invalid ServerUrl: {error}");

        settings.ServerUrl = normalized;
        settings.TelemetryQueue ??= new TelemetryQueueOptions();
        return settings;
    }

    public ClientSettings LoadOrDefault()
    {
        try
        {
            return Load();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        catch (InvalidDataException) { }

        return new ClientSettings();
    }

    public void UpdateServerUrl(string serverUrl)
    {
        if (!TryNormalizeServerUrl(serverUrl, out var normalized, out var error))
            throw new ArgumentException(error, nameof(serverUrl));

        JsonObject root;
        if (File.Exists(_path))
        {
            root = JsonNode.Parse(File.ReadAllText(_path)) as JsonObject
                ?? throw new InvalidDataException("Client settings must contain a JSON object.");
        }
        else
        {
            root = JsonSerializer.SerializeToNode(new ClientSettings(), SerializerOptions)!.AsObject();
        }

        root[nameof(ClientSettings.ServerUrl)] = normalized;
        WriteAtomically(root.ToJsonString(SerializerOptions));
    }

    public static bool TryNormalizeServerUrl(string? value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = "The URL must be an absolute HTTPS URL ending exactly in /remoteMonitoringHub.";
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.Equals(uri.AbsolutePath, "/remoteMonitoringHub", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        normalized = uri.AbsoluteUri;
        error = string.Empty;
        return true;
    }

    private void WriteAtomically(string content)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
