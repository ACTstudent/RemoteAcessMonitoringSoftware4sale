using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Server.Controllers;

namespace Server.Tests.Controllers;

public sealed class DeploymentPingControllerTests
{
    [Fact]
    public void Ping_IsAnonymousNoStoreAndContainsOnlyMinimalFields()
    {
        Assert.NotNull(typeof(DeploymentPingController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        var controller = new DeploymentPingController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = Assert.IsType<OkObjectResult>(controller.Get());
        var names = result.Value!.GetType().GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray();

        Assert.Equal(new[] { "product", "status", "utc", "version" }, names);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }
}
