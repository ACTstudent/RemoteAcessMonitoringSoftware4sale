using System.Text.RegularExpressions;

namespace Server.Tests.Views;

// Source checks over the Razor views and their scripts, in the manner of
// DialogMarkupTests: things a page must not go back to doing.
public class PortalMarkupTests
{
    // Classroom Records' only export was the browser's Print, which gave a
    // picture of the page. An export is a CSV file from the server.
    [Fact]
    public void NoPage_ExportsByPrintingItself()
    {
        var printing = ViewFiles()
            .Where(file => File.ReadAllText(file).Contains("window.print", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(printing);
    }

    [Fact]
    public void ClassroomRecords_DownloadsItsTablesAsCsv()
    {
        var records = ReadView("Teacher", "Records.cshtml");

        Assert.Contains("Url.Action(\"ExportRecordsCsv\", \"Teacher\")", records);
        Assert.Contains("Url.Action(\"ExportWebsiteActivityCsv\", \"Teacher\")", records);
        // Usage is websites only.
        Assert.DoesNotContain("Application Activity History", records);
    }

    // An alert folded inside its student's row was disabled until the row was
    // opened. The header box then looked ticked while nothing was selected, and
    // Acknowledge and Dismiss answered "Select at least one alert group".
    [Fact]
    public void AlertBoxes_AreSelectableWhileTheirStudentIsFolded()
    {
        var alerts = ReadView("Teacher", "Alerts.cshtml");
        var boxes = Regex.Matches(alerts, "<input\\b[^>]*data-alert-select\\b(?!-)[^>]*>").Select(match => match.Value).ToList();

        Assert.NotEmpty(boxes);
        Assert.DoesNotContain(boxes, box => Regex.IsMatch(box, "\\bdisabled\\b"));
        // Each student's row carries a box of its own, and the row can dismiss one alert.
        Assert.Contains("data-alert-select-student", alerts);
        Assert.Contains("asp-action=\"BulkDismissAlerts\"", alerts);

        var groups = File.ReadAllText(Path.Combine(FindServerProject(), "wwwroot", "js", "log-groups.js"));
        Assert.DoesNotContain("disabled", groups);
    }

    // Whether a teacher can log in is changed by the row's Deactivate /
    // Activate button, not by a field on the Add or Edit form.
    [Fact]
    public void TheTeacherForms_HaveNoStatusField()
    {
        var teachers = ReadView("Admin", "Teachers.cshtml");

        Assert.DoesNotContain("name=\"Status\"", teachers);
        Assert.Contains("action=\"/Admin/SetAccountActive\"", teachers);
    }

    [Fact]
    public void TheNameInThePageHeader_IsALinkToAccountSettings()
    {
        var layout = ReadView("Shared", "_AppLayout.cshtml");

        Assert.Matches("<a href=\"@Url\\.Action\\(nav\\.SettingsAction, nav\\.SettingsController\\)\"[^>]*user-profile-badge", layout);
    }

    private static string ReadView(string folder, string name) =>
        File.ReadAllText(Path.Combine(FindServerProject(), "Views", folder, name));

    private static IEnumerable<string> ViewFiles() =>
        Directory.EnumerateFiles(Path.Combine(FindServerProject(), "Views"), "*.cshtml", SearchOption.AllDirectories);

    private static string FindServerProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var server = Path.Combine(directory.FullName, "Server");
            if (Directory.Exists(Path.Combine(server, "Views"))) return server;
        }
        throw new DirectoryNotFoundException("The Server project was not found above the test output directory.");
    }
}
