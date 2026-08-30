using System.Diagnostics;
using System.Windows.Automation;
using Shared.Contracts;

namespace Client.Services;

public enum BrowserMonitoringStatus
{
    Captured,
    Fallback
}

public sealed record BrowserWebsiteObservation(
    string? Domain,
    string Browser,
    BrowserMonitoringStatus Status,
    BrowserMonitoringMode Mode = BrowserMonitoringMode.WindowTitleFallback);

public static class BrowserUrlCollector
{
    private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "brave"
    };

    public static BrowserWebsiteObservation? TryGetForegroundWebsite()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        string processName;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch
        {
            return null;
        }

        if (!BrowserProcessNames.Contains(processName))
            return null;

        var browser = processName.ToLowerInvariant();

        try
        {
            var window = AutomationElement.FromHandle(hwnd);
            var edits = window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            foreach (AutomationElement edit in edits)
            {
                var automationId = edit.Current.AutomationId;
                var name = edit.Current.Name;
                if (!IsAddressBar(processName, automationId, name))
                    continue;

                if (!edit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                    continue;

                var value = ((ValuePattern)pattern).Current.Value;
                if (TryGetHttpHost(value, out var host))
                    return new BrowserWebsiteObservation(host, browser, BrowserMonitoringStatus.Captured);
            }
        }
        catch
        {
            // UI Automation can fail for elevated, protected, or changing browser windows.
        }

        // The browser is foreground, but URL capture is unavailable. Active-app
        // reporting remains the privacy-preserving fallback signal.
        return new BrowserWebsiteObservation(null, browser, BrowserMonitoringStatus.Fallback);
    }

    private static bool IsAddressBar(string processName, string automationId, string name)
    {
        if (automationId.Equals("addressBar", StringComparison.OrdinalIgnoreCase) ||
            automationId.Equals("urlbar-input", StringComparison.OrdinalIgnoreCase))
            return true;

        if (processName.Equals("firefox", StringComparison.OrdinalIgnoreCase))
            return name.Contains("Address and Search Bar", StringComparison.OrdinalIgnoreCase);

        return name.Contains("address and search", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Address bar", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetHttpHost(string? value, out string host) =>
        WebsiteDomainNormalizer.TryNormalize(value, out host);
}
