using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Moq;
using Server.Filters;
using System.Text.Json;

namespace Server.Tests.Filters;

/// <summary>
/// The filter that hands a rejected form back with what the user typed still in
/// it. The part that matters most here is what it refuses to carry: the values
/// cross the redirect in TempData and are written into the page, so a password
/// riding along would be a real leak rather than an inconvenience.
/// </summary>
public class PreserveSubmissionFilterTests
{
    private static ActionExecutedContext BuildContext(
        Dictionary<string, string> form,
        IActionResult result,
        string? errorMessage = "Something was wrong.",
        string method = "POST",
        bool asForm = true)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        if (asForm)
        {
            httpContext.Request.ContentType = "application/x-www-form-urlencoded";
            httpContext.Request.Form = new FormCollection(
                form.ToDictionary(pair => pair.Key, pair => new Microsoft.Extensions.Primitives.StringValues(pair.Value)));
        }

        var controller = new TestController
        {
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
        if (errorMessage is not null)
        {
            controller.TempData["ErrorMessage"] = errorMessage;
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller)
        {
            Result = result
        };
    }

    private sealed class TestController : Controller { }

    private static Dictionary<string, string>? Preserved(ActionExecutedContext context)
    {
        var controller = (Controller)context.Controller!;
        var raw = controller.TempData[PreserveSubmissionFilter.TempDataKey] as string;
        return raw is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
    }

    private static readonly IActionResult ARedirect = new RedirectToActionResult("Teachers", "Admin", null);

    [Fact]
    public void ARejectedSubmissionIsCarriedBack()
    {
        var context = BuildContext(new()
        {
            ["FirstName"] = "Preserved",
            ["LastName"] = "Probe",
            ["Username"] = "taken"
        }, ARedirect);

        new PreserveSubmissionFilter().OnActionExecuted(context);

        var preserved = Preserved(context);
        Assert.NotNull(preserved);
        Assert.Equal("Preserved", preserved!["FirstName"]);
        Assert.Equal("taken", preserved["Username"]);
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("PasswordHash")]
    [InlineData("CurrentPassword")]
    [InlineData("NewPassword")]
    [InlineData("ConfirmPassword")]
    [InlineData("bulkPasswords")]
    [InlineData("__RequestVerificationToken")]
    [InlineData("apiKey")]
    [InlineData("clientSecret")]
    public void SensitiveFieldsAreNeverCarried(string fieldName)
    {
        var context = BuildContext(new()
        {
            ["FirstName"] = "Preserved",
            [fieldName] = "must-not-survive"
        }, ARedirect);

        new PreserveSubmissionFilter().OnActionExecuted(context);

        var preserved = Preserved(context);
        Assert.NotNull(preserved);
        Assert.False(preserved!.ContainsKey(fieldName), $"{fieldName} was carried back");
        Assert.DoesNotContain("must-not-survive", JsonSerializer.Serialize(preserved));
    }

    [Fact]
    public void ASuccessfulSubmissionCarriesNothing()
    {
        // No error message means the action succeeded; the user is finished with
        // that form and should not find it refilled.
        var context = BuildContext(new() { ["FirstName"] = "Accepted" }, ARedirect, errorMessage: null);

        new PreserveSubmissionFilter().OnActionExecuted(context);

        Assert.Null(Preserved(context));
    }

    [Fact]
    public void AResultThatIsNotARedirectCarriesNothing()
    {
        // An action that returns its own view already has the values in the model.
        var context = BuildContext(new() { ["FirstName"] = "Rendered" }, new ViewResult());

        new PreserveSubmissionFilter().OnActionExecuted(context);

        Assert.Null(Preserved(context));
    }

    [Fact]
    public void AGetRequestCarriesNothing()
    {
        var context = BuildContext(new() { ["search"] = "term" }, ARedirect, method: "GET");

        new PreserveSubmissionFilter().OnActionExecuted(context);

        Assert.Null(Preserved(context));
    }

    [Fact]
    public void ARequestWithNoFormCarriesNothing()
    {
        var context = BuildContext(new(), ARedirect, asForm: false);

        new PreserveSubmissionFilter().OnActionExecuted(context);

        Assert.Null(Preserved(context));
    }

    [Fact]
    public void EmptyFieldsAreNotCarried()
    {
        var context = BuildContext(new()
        {
            ["FirstName"] = "Preserved",
            ["MiddleName"] = ""
        }, ARedirect);

        new PreserveSubmissionFilter().OnActionExecuted(context);

        var preserved = Preserved(context);
        Assert.True(preserved!.ContainsKey("FirstName"));
        Assert.False(preserved.ContainsKey("MiddleName"));
    }

    [Fact]
    public void AnOversizedValueIsDroppedRatherThanStoredInASessionCookie()
    {
        var context = BuildContext(new()
        {
            ["FirstName"] = "Preserved",
            ["Notes"] = new string('x', 5000)
        }, ARedirect);

        new PreserveSubmissionFilter().OnActionExecuted(context);

        var preserved = Preserved(context);
        Assert.True(preserved!.ContainsKey("FirstName"));
        Assert.False(preserved.ContainsKey("Notes"));
    }

    [Fact]
    public void AVeryWideFormIsCapped()
    {
        var form = new Dictionary<string, string>();
        for (var i = 0; i < 200; i++)
        {
            form[$"field{i}"] = "value";
        }

        var context = BuildContext(form, ARedirect);

        new PreserveSubmissionFilter().OnActionExecuted(context);

        Assert.InRange(Preserved(context)!.Count, 1, 60);
    }
}
