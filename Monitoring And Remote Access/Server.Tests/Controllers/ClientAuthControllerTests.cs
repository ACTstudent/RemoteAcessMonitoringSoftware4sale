using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Server.Controllers;
using Server.Data;
using Server.Hubs;
using Server.Models;
using Server.Services;
using Shared.Contracts;
using System.Security.Claims;
using AspNetAuth = Microsoft.AspNetCore.Authentication.IAuthenticationService;
using ServerAuth = Server.Services.IAuthenticationService;

namespace Server.Tests.Controllers;

/// <summary>
/// The login endpoint the Windows agent posts to, which had no tests at all.
///
/// This is the outermost door into the server for an unauthenticated machine on
/// the lab network, so the checks that matter are the ones that decide who gets
/// a cookie: input limits, the per-IP attempt ceiling and - most importantly -
/// that a teacher or admin credential cannot be used to bring up a student
/// agent, which would hand a student workstation an account with a wider scope
/// than the workstation is ever meant to have.
///
/// ClientAuthController keeps its attempt counter in a private static
/// MemoryCache keyed by remote IP, shared by every instance. Each test below
/// therefore uses its own IP address so that one test's failures cannot spend
/// another test's attempt budget.
/// </summary>
public class ClientAuthControllerTests
{
    private static ApplicationDbContext GetDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>A distinct source address per test, so the shared attempt cache stays isolated.</summary>
    private static string UniqueIp()
    {
        var id = Guid.NewGuid().ToByteArray();
        return $"10.{id[0]}.{id[1]}.{id[2]}";
    }

    private static (ClientAuthController Controller, Mock<ServerAuth> Auth, Mock<AspNetAuth> SignIn)
        CreateController(string ipAddress, ApplicationDbContext? context = null)
    {
        var auth = new Mock<ServerAuth>();
        var signIn = new Mock<AspNetAuth>();

        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var lifecycle = new LabSessionLifecycleService(context ?? GetDbContext(), hub.Object);
        var controller = new ClientAuthController(auth.Object, lifecycle);

        var services = new ServiceCollection();
        services.AddSingleton(signIn.Object);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Session = new FakeSession()
        };
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ipAddress);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, auth, signIn);
    }

    private static StudentClientLoginRequest Request(
        string username = "student1", string password = "Passw0rd!", string pcName = "LAB-01") =>
        new(username, password, pcName);

    // ---------- Input limits ----------

    [Theory]
    [InlineData("", "Passw0rd!", "LAB-01")]
    [InlineData("   ", "Passw0rd!", "LAB-01")]
    [InlineData("student1", "", "LAB-01")]
    [InlineData("student1", "   ", "LAB-01")]
    [InlineData("student1", "Passw0rd!", "")]
    [InlineData("student1", "Passw0rd!", "   ")]
    public async Task Login_RejectsMissingFields(string username, string password, string pcName)
    {
        var (controller, auth, _) = CreateController(UniqueIp());

        var result = await controller.Login(new StudentClientLoginRequest(username, password, pcName));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        auth.Verify(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Theory]
    [InlineData(51, 8, 8)]    // username over 50
    [InlineData(8, 257, 8)]   // password over 256
    [InlineData(8, 8, 101)]   // workstation name over 100
    public async Task Login_RejectsOversizedFields(int usernameLength, int passwordLength, int pcNameLength)
    {
        var (controller, auth, _) = CreateController(UniqueIp());

        var result = await controller.Login(new StudentClientLoginRequest(
            new string('u', usernameLength), new string('p', passwordLength), new string('w', pcNameLength)));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        auth.Verify(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Theory]
    [InlineData(50, 256, 100)]
    public async Task Login_AcceptsFieldsExactlyAtTheLimit(int usernameLength, int passwordLength, int pcNameLength)
    {
        var (controller, auth, _) = CreateController(UniqueIp());
        auth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, 7, "Exactly At Limit", "atlimit", "S-100"));

        var result = await controller.Login(new StudentClientLoginRequest(
            new string('u', usernameLength), new string('p', passwordLength), new string('w', pcNameLength)));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ---------- Role boundary ----------

    [Theory]
    [InlineData(AccountRole.Teacher)]
    [InlineData(AccountRole.Admin)]
    public async Task Login_RefusesNonStudentAccountsOnTheClientAgentEndpoint(AccountRole role)
    {
        // A teacher signing in through the student agent would give a lab
        // workstation a teacher cookie. The endpoint must refuse it even though
        // the credentials themselves are perfectly valid.
        var (controller, auth, signIn) = CreateController(UniqueIp());
        auth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(role, 42, "Valid Non-Student", "teacher1"));

        var result = await controller.Login(Request());

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        signIn.Verify(s => s.SignInAsync(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()),
            Times.Never);
    }

    [Fact]
    public async Task Login_RefusesAStudentRoleWithNoAccountId()
    {
        var (controller, auth, signIn) = CreateController(UniqueIp());
        auth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, null, "No Account Id"));

        var result = await controller.Login(Request());

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        signIn.Verify(s => s.SignInAsync(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()),
            Times.Never);
    }

    [Fact]
    public async Task Login_RejectsInvalidCredentialsWithoutRevealingWhichPartWasWrong()
    {
        var (controller, auth, _) = CreateController(UniqueIp());
        auth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Invalid, null, null));

        var result = await controller.Login(Request());

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
        var message = Assert.IsType<string>(unauthorized.Value);
        Assert.DoesNotContain("password", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Successful sign-in ----------

    [Fact]
    public async Task Login_SignsInAsAStudentAgentAndReturnsTheStudentNumber()
    {
        var (controller, auth, signIn) = CreateController(UniqueIp());
        auth.Setup(a => a.LoginAsync("student1", "Passw0rd!", "LAB-01", It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, 11, "Ana Cruz", "student1", "S-2026-011"));

        ClaimsPrincipal? signedIn = null;
        signIn.Setup(s => s.SignInAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<AuthenticationProperties>()))
            .Callback<HttpContext, string, ClaimsPrincipal, AuthenticationProperties>((_, _, p, _) => signedIn = p)
            .Returns(Task.CompletedTask);

        var result = await controller.Login(Request());

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<StudentClientLoginResponse>(ok.Value);
        Assert.Equal("S-2026-011", body.StudentId);
        Assert.Equal("Ana Cruz", body.DisplayName);

        Assert.NotNull(signedIn);
        Assert.Equal("Student", signedIn!.FindFirstValue(ClaimTypes.Role));
        Assert.Equal("11", signedIn.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("LAB-01", signedIn.FindFirstValue(AuthPrincipalFactory.PcNameClaim));
        Assert.Equal(bool.TrueString, signedIn.FindFirstValue(AuthPrincipalFactory.ClientAgentClaim));
    }

    [Fact]
    public async Task Login_TrimsTheUsernameAndWorkstationBeforeAuthenticating()
    {
        var (controller, auth, _) = CreateController(UniqueIp());
        auth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, 5, "Trimmed", "student1", "S-5"));

        await controller.Login(Request(username: "  student1  ", pcName: "  LAB-01  "));

        auth.Verify(a => a.LoginAsync("student1", "Passw0rd!", "LAB-01", It.IsAny<string>()), Times.Once);
    }

    // ---------- Attempt ceiling ----------

    [Fact]
    public async Task Login_StopsCallingTheAuthServiceAfterFiveFailuresFromOneAddress()
    {
        var ip = UniqueIp();
        var (controller, auth, _) = CreateController(ip);
        auth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Invalid, null, null));

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.IsType<UnauthorizedObjectResult>((await controller.Login(Request())).Result);
        }

        var blocked = await controller.Login(Request());

        var status = Assert.IsType<ObjectResult>(blocked.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, status.StatusCode);
        auth.Verify(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Exactly(5));
    }

    [Fact]
    public async Task Login_ClearsTheFailureCountAfterASuccessfulSignIn()
    {
        var ip = UniqueIp();
        var (controller, auth, _) = CreateController(ip);
        auth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Invalid, null, null));

        for (var attempt = 1; attempt <= 4; attempt++) await controller.Login(Request());

        auth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, 3, "Recovered", "student1", "S-3"));
        Assert.IsType<OkObjectResult>((await controller.Login(Request())).Result);

        // The budget is spent per address, so after a success the next mistake
        // must be treated as the first one again rather than the fifth.
        auth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Invalid, null, null));
        Assert.IsType<UnauthorizedObjectResult>((await controller.Login(Request())).Result);
    }

    [Fact]
    public async Task Login_CountsFailuresPerAddressRatherThanGlobally()
    {
        var busyIp = UniqueIp();
        var quietIp = UniqueIp();
        var (busy, busyAuth, _) = CreateController(busyIp);
        busyAuth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Invalid, null, null));
        for (var attempt = 1; attempt <= 6; attempt++) await busy.Login(Request());

        var (quiet, quietAuth, _) = CreateController(quietIp);
        quietAuth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, 9, "Unaffected", "student2", "S-9"));

        Assert.IsType<OkObjectResult>((await quiet.Login(Request(username: "student2"))).Result);
    }

    /// <summary>
    /// Documents the blast radius of the per-IP ceiling rather than asserting it
    /// is correct. Every workstation behind one classroom NAT shares a source
    /// address, so five mistyped passwords - from any mix of students - close
    /// the endpoint for everyone else behind that address. Recorded as
    /// DEF-2026-0906-002; this test pins the current behaviour so a fix has to
    /// come back and change it deliberately.
    /// </summary>
    [Fact]
    public async Task Login_SharedAddressCeilingLocksOutStudentsWhoDidNotFail()
    {
        var sharedIp = UniqueIp();
        var (mistyping, mistypingAuth, _) = CreateController(sharedIp);
        mistypingAuth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Invalid, null, null));
        for (var attempt = 1; attempt <= 5; attempt++) await mistyping.Login(Request(username: "clumsy"));

        var (innocent, innocentAuth, _) = CreateController(sharedIp);
        innocentAuth.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new LoginResult(AccountRole.Student, 21, "Correct Credentials", "careful", "S-21"));

        var result = await innocent.Login(Request(username: "careful"));

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, status.StatusCode);
        innocentAuth.Verify(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    // ---------- Logout ----------

    [Fact]
    public async Task Logout_EndsOnlyTheSignedInStudentsSessionsAndClearsTheCookie()
    {
        using var db = GetDbContext();
        var student = new Student { StudentNumber = "S-30", FullName = "Signed In", Username = "signedin", PasswordHash = "h" };
        var other = new Student { StudentNumber = "S-31", FullName = "Someone Else", Username = "someoneelse", PasswordHash = "h" };
        db.Students.AddRange(student, other);
        await db.SaveChangesAsync();

        db.LabSessions.AddRange(
            new LabSession { StudentId = student.Id, IsActive = true, StartTime = DateTime.UtcNow.AddMinutes(-10), Status = "Running" },
            new LabSession { StudentId = other.Id, IsActive = true, StartTime = DateTime.UtcNow.AddMinutes(-10), Status = "Running" });
        await db.SaveChangesAsync();

        var (controller, _, signIn) = CreateController(UniqueIp(), db);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, student.Id.ToString()),
            new Claim(ClaimTypes.Role, "Student")
        }, "TestScheme"));

        var result = await controller.Logout();

        Assert.IsType<NoContentResult>(result);
        Assert.False(await db.LabSessions.AnyAsync(s => s.StudentId == student.Id && s.IsActive));
        Assert.True(await db.LabSessions.AnyAsync(s => s.StudentId == other.Id && s.IsActive));
        signIn.Verify(s => s.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }

    [Fact]
    public async Task Logout_StillClearsTheCookieWhenTheIdentifierIsUnusable()
    {
        var (controller, _, signIn) = CreateController(UniqueIp());
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "not-a-number"),
            new Claim(ClaimTypes.Role, "Student")
        }, "TestScheme"));

        var result = await controller.Logout();

        Assert.IsType<NoContentResult>(result);
        signIn.Verify(s => s.SignOutAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<AuthenticationProperties>()),
            Times.Once);
    }
}
