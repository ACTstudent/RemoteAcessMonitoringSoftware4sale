using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Text.Json;

namespace Server.Filters;

/// <summary>
/// Hands a rejected form back with what the user typed still in it.
///
/// Every create and update in the admin and teacher portals reports a validation
/// failure by setting <c>TempData["ErrorMessage"]</c> and redirecting to the
/// list. That is a sound pattern for the message, but the redirect discards the
/// submission: 53 failure paths did this and not one returned the view with the
/// model, so a mistyped username emptied the whole dialog and the roster import
/// form threw away four fields for every row.
///
/// Rewriting 53 actions to return their view would touch every page in the
/// portal. This does it centrally instead: when a form POST ends in a redirect
/// carrying an error, the submitted values ride along in TempData and the shell
/// puts them back.
///
/// Passwords are never carried. Neither is the antiforgery token, which is
/// single-use and would be stale by the time the form is shown again.
/// </summary>
public sealed class PreserveSubmissionFilter : IActionFilter
{
    public const string TempDataKey = "PreservedSubmission";

    /// <summary>
    /// Anything whose name contains one of these is never round-tripped.
    ///
    /// A substring match rather than a suffix one, because the field names are
    /// not consistent: the bulk roster form posts <c>bulkPasswords</c>, plural,
    /// which a suffix match on "password" lets straight through.
    /// </summary>
    private static readonly string[] NeverPreserve =
    {
        "password", "secret", "token", "apikey"
    };

    private const int MaxFields = 60;
    private const int MaxValueLength = 2000;

    public void OnActionExecuting(ActionExecutingContext context)
    {
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (context.Result is not RedirectToActionResult and not RedirectResult)
        {
            return;
        }

        var request = context.HttpContext.Request;
        if (!HttpMethods.IsPost(request.Method) || !request.HasFormContentType)
        {
            return;
        }

        if (context.Controller is not Controller controller)
        {
            return;
        }

        // Only a rejected submission is worth carrying back. A success redirects
        // to a list the user is finished with.
        if (controller.TempData is not ITempDataDictionary tempData ||
            tempData.Peek("ErrorMessage") is null)
        {
            return;
        }

        var preserved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in request.Form)
        {
            if (preserved.Count >= MaxFields)
            {
                break;
            }
            if (IsSensitive(field.Key))
            {
                continue;
            }

            var value = field.Value.ToString();
            if (value.Length == 0 || value.Length > MaxValueLength)
            {
                continue;
            }

            preserved[field.Key] = value;
        }

        if (preserved.Count > 0)
        {
            tempData[TempDataKey] = JsonSerializer.Serialize(preserved);
        }
    }

    private static bool IsSensitive(string name)
    {
        foreach (var sensitive in NeverPreserve)
        {
            if (name.Contains(sensitive, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
