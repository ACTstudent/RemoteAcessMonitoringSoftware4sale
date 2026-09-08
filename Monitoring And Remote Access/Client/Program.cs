namespace Client;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            if (string.Equals(args[0], "--restore-proxy", StringComparison.OrdinalIgnoreCase))
                return RestoreProxyWatchdog(args);
            return Configure(args);
        }

        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(true,
            @"Local\CAMS.StudentClient." + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value,
            out var firstInstance);
        if (!firstInstance)
        {
            MainForm.ShowStartupMessage("CAMS is already running. Open it from the notification area.");
            return 1;
        }
        try
        {
            // Recover the previous configuration after a crash before connecting
            // to the server, which might otherwise inherit a dead local proxy.
            Services.WindowsSessionProxy.RestorePreviousSession();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MainForm.ShowStartupMessage($"CAMS could not start or restore browser settings.\n\n{ex.Message}", error: true);
            return 1;
        }
        finally
        {
            try { Services.WindowsSessionProxy.RestorePreviousSession(); }
            catch (Exception ex)
            {
                MainForm.ShowStartupMessage($"Restart CAMS to restore the previous Windows proxy settings.\n\n{ex.Message}", error: true);
            }
        }
        return 0;
    }

    /// <summary>
    /// Waits for the client to exit by any means - a clean shutdown, a crash, or
    /// End task from Task Manager - and then puts the Windows proxy settings back.
    ///
    /// Without this, a client that dies without unwinding leaves the machine's
    /// browsers pointed at a listener that no longer exists, which to a student
    /// looks exactly like the internet being broken, and the only fix is turning
    /// the proxy off by hand in Windows Settings. That is not a state a lab
    /// machine should ever be left in.
    ///
    /// Restoring is idempotent: a client that exited cleanly has already restored
    /// and deleted the backup, so this finds nothing to do and stops.
    /// </summary>
    private static int RestoreProxyWatchdog(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out var clientProcessId))
        {
            Console.Error.WriteLine("Usage: Client.exe --restore-proxy <process id>");
            return 2;
        }

        try
        {
            using var client = System.Diagnostics.Process.GetProcessById(clientProcessId);
            client.WaitForExit();
        }
        catch (ArgumentException)
        {
            // Already gone. Restore anyway - that is the case this exists for.
        }

        try
        {
            Services.WindowsSessionProxy.RestorePreviousSession();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not restore the Windows proxy settings: {ex.Message}");
            return 4;
        }
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
