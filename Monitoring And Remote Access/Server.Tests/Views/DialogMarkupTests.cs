using System.Text.RegularExpressions;

namespace Server.Tests.Views;

// Source checks over the Razor views, in the manner of HubHeartbeatTests.
public class DialogMarkupTests
{
    // "Deactivate this teacher account across CAMS?" left a teacher unsure which
    // row they had clicked. Every confirmation now shows the record's name above
    // the question, so a new form that asks without naming its record fails here.
    [Fact]
    public void EveryConfirmation_NamesTheRecordItActsOn()
    {
        var unnamed = ViewFiles()
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "<form\\b[^>]*data-confirm=[^>]*>")
                .Select(form => (File: Path.GetFileName(file), Tag: form.Value)))
            .Where(form => !form.Tag.Contains("data-confirm-subject="))
            .Select(form => $"{form.File}: {form.Tag[..Math.Min(form.Tag.Length, 140)]}")
            .ToList();

        Assert.Empty(unnamed);
    }

    // Dialog headers are the pale sage band. The solid green one survives only
    // on the student alert, whose script recolours it by severity.
    [Fact]
    public void DialogHeaders_UseThePaleBand_ExceptTheStudentAlert()
    {
        var solid = ViewFiles()
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "<div class=\"modal-header[^\"]*surface-brand[^>]*>")
                .Select(header => (File: Path.GetFileName(file), Tag: header.Value)))
            .Where(header => !header.Tag.Contains("id=\"alertModalHeader\""))
            .Select(header => $"{header.File}: {header.Tag}")
            .ToList();

        Assert.Empty(solid);
    }

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
