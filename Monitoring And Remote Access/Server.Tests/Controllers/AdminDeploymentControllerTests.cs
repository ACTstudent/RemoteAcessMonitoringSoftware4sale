using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using Server.Controllers;
using Server.Models;
using Server.Services;

namespace Server.Tests.Controllers;

public sealed class AdminDeploymentControllerTests
{
    [Fact]
    public void Controller_RequiresAdminAndAntiforgery_AndBundleIsPostOnly()
    {
        var type = typeof(AdminDeploymentController);
        Assert.Equal("Admin", type.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
        Assert.NotNull(type.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>());
        var bundle = type.GetMethod(nameof(AdminDeploymentController.Bundle))!;
        Assert.NotNull(bundle.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(bundle.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public async Task Index_ReturnsStatusWithNoStoreHeaders()
    {
        var service = new Mock<IDeploymentService>();
        service.Setup(item => item.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeploymentViewModel { ServerVersion = "1.0.0" });
        var controller = CreateController(service.Object);

        var result = await controller.Index(CancellationToken.None);

        Assert.IsType<ViewResult>(result);
        Assert.Contains("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task InstallerIntegrityFailure_ReturnsConflictWithoutFileDetails()
    {
        var service = new Mock<IDeploymentService>();
        service.Setup(item => item.OpenInstallerAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidDataException("secret path"));
        var controller = CreateController(service.Object);

        var result = await controller.Installer(CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.DoesNotContain("secret", conflict.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static AdminDeploymentController CreateController(IDeploymentService service)
    {
        var context = new DefaultHttpContext();
        var controller = new AdminDeploymentController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>())
        };
        return controller;
    }
}
