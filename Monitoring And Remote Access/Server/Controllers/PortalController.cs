using Microsoft.AspNetCore.Mvc;
using Server.Data;
using Server.Models;
using Server.Services;
using Shared.Contracts;

namespace Server.Controllers;

/// <summary>
/// What the Admin and Teacher portals do identically.
///
/// The two controllers had their own copies of the refusal redirect, the audit
/// write, the CSV escape, and - twenty-one times over - the same block that
/// turns a <see cref="ClassOperationResult"/> into a message and a redirect.
/// The business logic was already in ClassManagementService; this is the HTTP
/// shell around it, which is where the remaining duplication actually lived.
///
/// Only the actor differs: the admin portal can be driven by a teacher, so it
/// works out who is acting; the teacher portal always knows.
/// </summary>
public abstract class PortalController : Controller
{
    /// <summary>The audit trail is written through this.</summary>
    protected abstract ApplicationDbContext Db { get; }

    /// <summary>
    /// Who is acting, for the audit trail. UserType uses the same role
    /// vocabulary as the authentication cookie.
    /// </summary>
    protected abstract (string UserType, int? UserId) Actor { get; }

    /// <summary>
    /// Where a caller goes when the portal is not theirs. Identical in both
    /// portals, and deliberately a redirect rather than a 403: this fires for
    /// callers whose session has gone, and the sign-in form is where they need
    /// to be. A signed-in caller with the wrong role is handled earlier, by the
    /// authorization filters, which send them to AccessDenied.
    /// </summary>
    protected IActionResult Denied() => RedirectToAction("Login", "Account");

    /// <summary>Records an action against the acting account.</summary>
    protected async Task AuditAsync(string action, string details)
    {
        var (userType, userId) = Actor;
        Db.AuditLogs.Add(new AuditLog
        {
            UserType = userType,
            UserId = userId,
            Action = action,
            Details = details,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Timestamp = DateTime.UtcNow
        });
        await Db.SaveChangesAsync();
    }

    /// <summary>
    /// Applies the outcome of a class or roster operation: on failure the error
    /// goes to the page and nothing is audited, on success the action is
    /// recorded and the confirmation shown.
    ///
    /// Returns whether it succeeded, so the caller still chooses where to go -
    /// the redirect target varies per action and is genuinely not shared.
    /// </summary>
    protected async Task<bool> RecordAsync(
        ClassOperationResult result,
        string auditAction,
        string auditDetails,
        string successMessage)
    {
        if (!result.Success)
        {
            // Nothing happened, so nothing is audited. An audit trail that
            // records attempts as if they were changes is worse than none.
            TempData["ErrorMessage"] = result.Error;
            return false;
        }

        await AuditAsync(auditAction, auditDetails);
        TempData["Message"] = successMessage;
        return true;
    }

    /// <summary>
    /// Quotes a value for a CSV cell. One implementation, in
    /// <see cref="CsvExport"/>; the short name is kept because the export
    /// actions read better with it.
    /// </summary>
    protected static string Csv(string? value) => CsvExport.Escape(value);
}
