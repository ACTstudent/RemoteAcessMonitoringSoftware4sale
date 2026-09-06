using System.Text;
using Microsoft.AspNetCore.Mvc;
using Server.Services;
using Xunit;

namespace Server.Tests.Services;

/// <summary>
/// CODE-01, the shared-export slice. Thirteen actions built their own
/// FileContentResult and three of them stamped the name with local time, so two
/// exports taken seconds apart could be named hours apart. These test the one
/// place that now decides.
/// </summary>
public class CsvExportTests
{
    [Fact]
    public void FileName_IsBuiltFromTheSubjectAndTheInstantGiven()
    {
        var name = CsvExport.FileName("AuditLog", new DateTime(2026, 9, 7, 14, 5, 0, DateTimeKind.Utc));

        Assert.Equal("CAMS-AuditLog-20260907-1405.csv", name);
    }

    [Fact]
    public void TwoExportsAtTheSameInstant_AgreeOnTheirTimestamp()
    {
        var instant = new DateTime(2026, 9, 7, 14, 5, 0, DateTimeKind.Utc);

        // This is the defect that prompted the consolidation: the audit export
        // used DateTime.Now while the one beside it used UtcNow, so a folder of
        // downloads sorted wrongly and two files taken together looked hours
        // apart.
        var audit = CsvExport.FileName("AuditLog", instant);
        var logs = CsvExport.FileName("SystemLogs", instant);

        Assert.Equal(audit[^18..], logs[^18..]);
    }

    [Fact]
    public void Result_CarriesTheCsvContentTypeAndADownloadName()
    {
        var result = CsvExport.Result("Alerts", "a,b\n1,2\n",
            new DateTime(2026, 9, 7, 14, 5, 0, DateTimeKind.Utc));

        Assert.Equal("text/csv; charset=utf-8", result.ContentType);
        Assert.Equal("CAMS-Alerts-20260907-1405.csv", result.FileDownloadName);
        Assert.Equal("a,b\n1,2\n", Encoding.UTF8.GetString(result.FileContents));
    }

    [Fact]
    public void Result_AcceptsContentThatIsAlreadyEncoded()
    {
        var bytes = Encoding.UTF8.GetBytes("row\n");

        var result = CsvExport.Result("Student-Import-Errors", bytes,
            new DateTime(2026, 9, 7, 14, 5, 0, DateTimeKind.Utc));

        Assert.Equal(bytes, result.FileContents);
        Assert.Equal("CAMS-Student-Import-Errors-20260907-1405.csv", result.FileDownloadName);
    }

    [Theory]
    [InlineData(null, "\"\"")]
    [InlineData("", "\"\"")]
    [InlineData("plain", "\"plain\"")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has \"quotes\"", "\"has \"\"quotes\"\"\"")]
    [InlineData("has\nnewline", "\"has\nnewline\"")]
    public void Escape_QuotesTheValueAndDoublesInnerQuotes(string? input, string expected)
    {
        Assert.Equal(expected, CsvExport.Escape(input));
    }

    [Fact]
    public void Escape_DoesNotYetNeutraliseFormulas_AndThatIsDeliberate()
    {
        // Recorded, not asserted as desirable. SEC-01 is open: a spreadsheet may
        // execute a leading '='. Changing it would alter the contents of every
        // export, which is a separate change from the one that fixed their
        // names. This test exists so the next person finds the decision rather
        // than assuming the escape already handles it.
        Assert.Equal("\"=HYPERLINK(\"\"x\"\")\"", CsvExport.Escape("=HYPERLINK(\"x\")"));
    }

    [Fact]
    public void Result_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow.AddMinutes(-1);

        var result = (FileContentResult)CsvExport.Result("Attendance", "x\n");

        var stamp = result.FileDownloadName!["CAMS-Attendance-".Length..^".csv".Length];
        var parsed = DateTime.ParseExact(stamp, "yyyyMMdd-HHmm",
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.InRange(parsed, before, DateTime.UtcNow.AddMinutes(1));
    }
}
