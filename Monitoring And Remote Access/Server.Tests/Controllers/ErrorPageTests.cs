using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Server.Controllers;

namespace Server.Tests.Controllers;

// The exception handler re-runs a failed request against /Account/Error with
// the request's own method. With Error limited to GET, any form post that threw
// - removing a blacklist entry, for one - showed a bare "HTTP ERROR 405"
// instead of the error page and its correlation id.
public class ErrorPageTests
{
    [Fact]
    public void ErrorPage_AcceptsTheFailedRequestsOwnMethod()
    {
        var error = typeof(AccountController).GetMethod(nameof(AccountController.Error))!;

        Assert.Empty(error.GetCustomAttributes<HttpMethodAttribute>());
        Assert.NotNull(error.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>());
    }
}
