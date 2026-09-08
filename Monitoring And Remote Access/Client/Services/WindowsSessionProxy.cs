using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace Client.Services;

/// <summary>Temporarily routes the signed-in Windows user's system-proxy browsers through CAMS.</summary>
public sealed class WindowsSessionProxy : IDisposable
{
    private static readonly string BackupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CAMS", "session-proxy-backup.json");
    private readonly ProxyBackup _backup;
    private readonly object _sync = new();
    private bool _disposed;

    public WindowsSessionProxy(int port, Uri server)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        RestorePreviousSession();
        var original = ReadSettings();
        // Keep the monitoring server reachable, even with a website allowlist.
        // Do not inherit broad bypass rules or a PAC that could bypass restrictions.
        var applied = new ProxySettings(3, $"127.0.0.1:{port}", $"<-loopback>;{server.IdnHost}", "");
        _backup = new ProxyBackup(original, applied);
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        var temporaryPath = BackupPath + ".tmp";
        using (var file = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, _backup);
            file.Flush(flushToDisk: true);
        }
        File.Move(temporaryPath, BackupPath, overwrite: true);
        try
        {
            WriteSettings(applied);
            if (!Matches(ReadSettings(), applied))
                throw new InvalidOperationException("Windows did not apply the CAMS website filter. Check this workstation's proxy policy.");
        }
        catch
        {
            // Preserve the recovery file if restoration itself fails.
            WriteSettings(original);
            File.Delete(BackupPath);
            throw;
        }

        // Two safety nets, because Dispose only runs when we unwind normally.
        // Logging off or shutting down restores before Windows tears us down;
        // the watchdog restores if we are killed or crash instead.
        SystemEvents.SessionEnding += OnSessionEnding;
        StartRestoreWatchdog();
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => Dispose();

    /// <summary>
    /// Launches a second copy of this executable that does nothing but wait for
    /// this process to end and then put the proxy settings back. It covers the
    /// deaths Dispose cannot: End task, a crash, a killed process tree.
    /// </summary>
    private static void StartRestoreWatchdog()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable)) return;
            Process.Start(new ProcessStartInfo(executable, $"--restore-proxy {Environment.ProcessId}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            });
        }
        catch (Exception)
        {
            // A watchdog is a safety net, not the mechanism. If it cannot start,
            // Dispose still restores on a normal exit and the client restores at
            // the next start before it connects.
        }
    }

    public void EnsureApplied()
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (Matches(ReadSettings(), _backup.Applied)) return;
            WriteSettings(_backup.Applied);
            if (!Matches(ReadSettings(), _backup.Applied))
                throw new InvalidOperationException("Windows proxy settings are preventing website enforcement.");
        }
    }

    public static void RestorePreviousSession()
    {
        if (!File.Exists(BackupPath)) return;
        var backup = JsonSerializer.Deserialize<ProxyBackup>(File.ReadAllText(BackupPath))
            ?? throw new InvalidDataException("The saved CAMS proxy settings could not be read.");
        // Only restore settings still pointing to our old listener. Preserve an
        // administrator's subsequent change instead of overwriting it at startup.
        if (ReadSettings().Server == backup.Applied.Server)
            WriteSettings(backup.Original);
        File.Delete(BackupPath);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            SystemEvents.SessionEnding -= OnSessionEnding;
            RestorePreviousSession();
        }
    }

    private static bool Matches(ProxySettings current, ProxySettings expected) =>
        current.Flags == expected.Flags && current.Server == expected.Server &&
        current.Bypass == expected.Bypass && current.AutoConfigUrl == expected.AutoConfigUrl;

    private sealed record ProxySettings(int Flags, string Server, string Bypass, string AutoConfigUrl);
    private sealed record ProxyBackup(ProxySettings Original, ProxySettings Applied);

    private static ProxySettings ReadSettings()
    {
        var size = Marshal.SizeOf<InternetOption>();
        var memory = Marshal.AllocHGlobal(size * 4);
        try
        {
            for (var i = 0; i < 4; i++)
                // FLAGS_UI preserves the user's configured auto-detect setting;
                // FLAGS can reflect the last resolved connection instead.
                Marshal.StructureToPtr(new InternetOption { Id = i == 0 ? 10 : i + 1 }, memory + i * size, false);
            var list = CreateList(memory);
            var length = list.Size;
            if (!InternetQueryOption(IntPtr.Zero, 75, ref list, ref length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read Windows proxy settings.");
            var flags = Marshal.PtrToStructure<InternetOption>(memory).Value.Number;
            string Value(int index) => Marshal.PtrToStringUni(
                Marshal.PtrToStructure<InternetOption>(memory + index * size).Value.Text) ?? "";
            return new ProxySettings(flags, Value(1), Value(2), Value(3));
        }
        finally
        {
            for (var i = 1; i < 4; i++)
            {
                var pointer = Marshal.PtrToStructure<InternetOption>(memory + i * size).Value.Text;
                if (pointer != IntPtr.Zero) GlobalFree(pointer);
            }
            Marshal.FreeHGlobal(memory);
        }
    }

    private static void WriteSettings(ProxySettings settings)
    {
        var values = new[] { settings.Server, settings.Bypass, settings.AutoConfigUrl };
        var size = Marshal.SizeOf<InternetOption>();
        var memory = Marshal.AllocHGlobal(size * 4);
        var strings = new List<IntPtr>();
        try
        {
            Marshal.StructureToPtr(new InternetOption { Id = 1, Value = new OptionValue { Number = settings.Flags } }, memory, false);
            for (var i = 0; i < values.Length; i++)
            {
                var pointer = Marshal.StringToHGlobalUni(values[i]);
                strings.Add(pointer);
                Marshal.StructureToPtr(new InternetOption { Id = i + 2, Value = new OptionValue { Text = pointer } }, memory + (i + 1) * size, false);
            }
            var list = CreateList(memory);
            if (!InternetSetOption(IntPtr.Zero, 75, ref list, list.Size))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not change Windows proxy settings.");
            if (!NotifyInternetOption(IntPtr.Zero, 39, IntPtr.Zero, 0) ||
                !NotifyInternetOption(IntPtr.Zero, 37, IntPtr.Zero, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not refresh Windows proxy settings.");
        }
        finally
        {
            foreach (var pointer in strings) Marshal.FreeHGlobal(pointer);
            Marshal.FreeHGlobal(memory);
        }
    }

    private static InternetOptionList CreateList(IntPtr options) => new()
    {
        Size = Marshal.SizeOf<InternetOptionList>(), Count = 4, Options = options
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct InternetOptionList
    {
        public int Size;
        public IntPtr Connection;
        public int Count;
        public int Error;
        public IntPtr Options;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InternetOption { public int Id; public OptionValue Value; }

    [StructLayout(LayoutKind.Explicit)]
    private struct OptionValue
    {
        [FieldOffset(0)] public int Number;
        [FieldOffset(0)] public IntPtr Text;
        [FieldOffset(0)] public System.Runtime.InteropServices.ComTypes.FILETIME FileTime;
    }

    [DllImport("wininet.dll", EntryPoint = "InternetQueryOptionW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetQueryOption(IntPtr handle, int option, ref InternetOptionList list, ref int length);

    [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InternetSetOption(IntPtr handle, int option, ref InternetOptionList list, int length);

    [DllImport("wininet.dll", EntryPoint = "InternetSetOptionW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NotifyInternetOption(IntPtr handle, int option, IntPtr buffer, int length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
