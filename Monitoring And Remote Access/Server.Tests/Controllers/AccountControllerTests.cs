using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Server.Controllers;
using Server.Services;
using Server.Models;
using Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Controllers;

public class AccountControllerTests
{
    private readonly Mock<IAuthenticationService> _authMock;
    private readonly AccountController _controller;

    public AccountControllerTests()
    {
        _authMock = new Mock<IAuthenticationService>();
        _controller = new AccountController(_authMock.Object);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        httpContext.Session = new FakeSession();
        var services = new ServiceCollection();
        services.AddMvc();
        services.AddLogging();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        httpContext.RequestServices = services.BuildServiceProvider();

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };
    }

    [Fact]
    public void Login_Get_ReturnsView()
    {
        var result = _controller.Login();
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task LoginPost_InvalidCredentials_ReturnsViewWithError()
    {
        _authMock.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Invalid, null, null));

        var result = await _controller.Login("baduser", "badpass");
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task LoginPost_AdminRole_RedirectsToAdminIndex()
    {
        _authMock.Setup(a => a.LoginAsync("admin", "pass", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Admin, 1, "Boss"));

        var result = await _controller.Login("admin", "pass");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Admin", redirect.ControllerName);
        Assert.Equal("Index", redirect.ActionName);
    }

    [Fact]
    public async Task LoginPost_TeacherRole_RedirectsToTeacherDashboard()
    {
        _authMock.Setup(a => a.LoginAsync("teacher", "pass", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Teacher, 1, "Ms. Jane"));

        var result = await _controller.Login("teacher", "pass");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Teacher", redirect.ControllerName);
        Assert.Equal("Dashboard", redirect.ActionName);
    }

    /// <summary>
    /// This used to assert that a student signing in on the web was redirected
    /// to the monitoring index. The web portal is now for teachers and
    /// administrators only, so the expectation is inverted: no redirect at all,
    /// because there is nowhere on the web for a student to go.
    /// </summary>
    [Fact]
    public async Task LoginPost_StudentRole_DoesNotEnterThePortal()
    {
        _authMock.Setup(a => a.LoginAsync("student", "pass", It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, 1, "John"));

        var result = await _controller.Login("student", "pass");

        Assert.IsNotType<RedirectToActionResult>(result);
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Logout_ClearsSessionAndRedirects()
    {
        var result = await _controller.Logout();
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
    }

    [Fact]
    public async Task ChangeStudentPassword_UsesCurrentPasswordAndHashesNewPassword()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options);
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
        var student = new Student { Username = "student", StudentNumber = "S1", PasswordHash = hasher.HashPassword(new object(), "oldpass") };
        db.Students.Add(student);
        await db.SaveChangesAsync();

        var service = new AuthenticationService(db);
        Assert.True(await service.ChangeStudentPasswordAsync(student.Id, "oldpass", "newpassword"));
        Assert.NotEqual(student.PasswordHash, hasher.HashPassword(new object(), "newpassword"));
        Assert.Equal(Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(new object(), student.PasswordHash, "newpassword"));
        Assert.False(await service.ChangeStudentPasswordAsync(student.Id, "wrongpass", "anotherpass"));
    }

    /// <summary>
    /// The web portal is for teachers and administrators. A student uses the
    /// CAMS Student Client, which signs in through ClientAuthController; the
    /// browser form must turn them away and say where to go instead.
    /// </summary>
    [Fact]
    public async Task LoginPost_StudentCredentials_AreRefusedAndPointedAtTheClient()
    {
        _authMock.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, 7, "Sam Student", "student1", "S-7"));

        var result = await _controller.Login("student1", "correct-password");

        var view = Assert.IsType<ViewResult>(result);
        var message = view.ViewData["Error"] as string ?? "";
        Assert.Contains("teachers and administrators", message);
        Assert.Contains("CAMS Student Client", message);
    }

    [Fact]
    public async Task LoginPost_StudentCredentials_EstablishNoSession()
    {
        _authMock.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, 7, "Sam Student", "student1", "S-7"));

        await _controller.Login("student1", "correct-password");

        var session = _controller.HttpContext.Session;
        Assert.Null(session.GetString("Role"));
        Assert.Null(session.GetInt32("StudentId"));
        Assert.Null(session.GetString("FullName"));
    }

    [Fact]
    public async Task LoginPost_StudentCredentials_DoNotSpendTheAttemptBudget()
    {
        // Their password was right; they are simply at the wrong door. Counting
        // it would lock a shared workstation out of a portal it cannot use anyway.
        _authMock.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, 7, "Sam Student", "student1", "S-7"));

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var view = Assert.IsType<ViewResult>(await _controller.Login("student1", "correct-password"));
            Assert.DoesNotContain("Too many", view.ViewData["Error"] as string ?? "");
        }
    }

}

internal sealed class FakeSession : ISession
{
    private readonly Dictionary<string, byte[]> _store = new();
    public bool IsAvailable => true;
    public string Id => Guid.NewGuid().ToString();
    public IEnumerable<string> Keys => _store.Keys;
    public void Clear() => _store.Clear();
    public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken ct) => Task.CompletedTask;
    public void Remove(string key) => _store.Remove(key);
    public void Set(string key, byte[] value) => _store[key] = value;
    public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
}
