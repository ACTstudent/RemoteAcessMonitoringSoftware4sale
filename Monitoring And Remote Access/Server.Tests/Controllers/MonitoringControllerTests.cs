using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Server.Controllers;

namespace Server.Tests.Controllers;

/// <summary>
/// The shared /Monitoring entry point, which decides which dashboard a caller
/// lands on. It carries no [Authorize] attribute of its own, so the test that
/// matters is that an unauthenticated caller is handed to a portal that will
/// itself demand a login, rather than to a teacher or admin surface.
/// </summary>
public class MonitoringControllerTests
{
    private static MonitoringController CreateController(string? role, int? id)
    {
        var controller = new MonitoringController();
        var httpContext = new DefaultHttpContext { Session = new FakeSession() };
        if (role is not null) httpContext.Session.SetString("Role", role);
        if (role is not null && id.HasValue) httpContext.Session.SetInt32($"{role}Id", id.Value);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    [Theory]
    [InlineData("Teacher", 3, "Dashboard", "Teacher")]
    [InlineData("Admin", 1, "Index", "Admin")]
    [InlineData("Student", 9, "Index", "Student")]
    public void Index_SendsEachRoleToItsOwnDashboard(string role, int id, string action, string controllerName)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(CreateController(role, id).Index());
        Assert.Equal(action, redirect.ActionName);
        Assert.Equal(controllerName, redirect.ControllerName);
    }

    [Theory]
    [InlineData(null, null)]        // no session at all
    [InlineData("Teacher", null)]   // a role string with no matching id
    [InlineData("Admin", null)]
    [InlineData("Nonsense", 1)]     // a role the server does not issue
    public void Index_SendsAnUnidentifiedCallerToAPortalThatRequiresALogin(string? role, int? id)
    {
        // Falling through to the student portal is safe only because that portal
        // rejects anyone who is not a signed-in student. Reaching the teacher or
        // admin dashboard here would be the defect.
        var redirect = Assert.IsType<RedirectToActionResult>(CreateController(role, id).Index());
        Assert.Equal("Student", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
    }
}
