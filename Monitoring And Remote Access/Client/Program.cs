namespace Client;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0)
            return Configure(args);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }

    private static int Configure(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--configure-server", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Usage: Client.exe --configure-server <https://host:port/remoteMonitoringHub>");
            return 2;
        }

        if (!Services.ClientSettingsStore.TryNormalizeServerUrl(args[1], out var serverUrl, out var error))
        {
            Console.Error.WriteLine($"Invalid server URL: {error}");
            return 3;
        }

        try
        {
            new Services.ClientSettingsStore().UpdateServerUrl(serverUrl);
            Console.WriteLine($"CAMS server URL configured: {serverUrl}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not update client settings: {ex.Message}");
            return 4;
        }
    }
}
