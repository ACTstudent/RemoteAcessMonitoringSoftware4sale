using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Server.Services;

/// <summary>
/// One way to hand a CSV back to the browser.
///
/// Thirteen export actions across two controllers each built the same
/// FileContentResult by hand, and they had drifted: three stamped the file name
/// with <c>DateTime.Now</c> while the rest used <c>UtcNow</c>. Two exports
/// downloaded seconds apart could therefore carry names hours apart, and sorting
/// a folder of them put them in the wrong order. Naming is not a detail when the
/// file name is the only thing distinguishing one download from the next.
///
/// The escape function was also copied verbatim into both controllers.
/// </summary>
public static class CsvExport
{
    public const string ContentType = "text/csv; charset=utf-8";

    /// <summary>
    /// Quotes a value for a CSV cell.
    ///
    /// Note what this does not do: it does not neutralise a leading =, +, - or
    /// @, which a spreadsheet may execute as a formula. That is tracked as
    /// SEC-01 and is deliberately not fixed here - changing it would alter the
    /// contents of every export at the same time as this change alters their
    /// names, and the two should not be tangled. This is now the one place it
    /// would need to change.
    /// </summary>
    public static string Escape(string? value) =>
        "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// The download name for an export. Always UTC: a file name is compared
    /// against other file names, and a local stamp makes two exports taken
    /// together look hours apart.
    /// </summary>
    public static string FileName(string subject, DateTime utcNow) =>
        $"CAMS-{subject}-{utcNow:yyyyMMdd-HHmm}.csv";

    /// <summary>Builds the response. <paramref name="subject"/> names the file.</summary>
    public static FileContentResult Result(string subject, string csv, DateTime? utcNow = null) =>
        Result(subject, Encoding.UTF8.GetBytes(csv), utcNow);

    /// <summary>
    /// For content already encoded, such as the bulk-import error report.
    /// </summary>
    public static FileContentResult Result(string subject, byte[] content, DateTime? utcNow = null) =>
        new(content, ContentType)
        {
            FileDownloadName = FileName(subject, utcNow ?? DateTime.UtcNow)
        };
}
