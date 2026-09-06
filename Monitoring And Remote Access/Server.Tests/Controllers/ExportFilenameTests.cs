using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Controllers;
using Server.Data;
using Server.Hubs;
using Server.Services;
using System.Security.Claims;

namespace Server.Tests.Controllers;

/// <summary>
/// Every export names its file from the same clock.
///
/// Three of the four used <c>DateTime.UtcNow</c> and one used
/// <c>DateTime.Now</c>. On a server in Asia/Manila that put eight hours between
/// files downloaded seconds apart, so a set of exports saved together no longer
/// sorted together and looked like it came from two different sessions.
///
/// These assertions can only tell the two clocks apart when the machine running
/// them is not itself on UTC; on a UTC build agent they still pin the format and
/// that a timestamp is present.
/// </summary>
public class ExportFilenameTests
{
    private static ApplicationDbContext GetDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (AdminController Admin, TeacherController Teacher) CreateControllers(ApplicationDbContext context)
    {
        var hub = new Mock<IHubContext<RemoteMonitoringHub>>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>())).Returns(Mock.Of<IClientProxy>());
        clients.Setup(c => c.Client(It.IsAny<string>())).Returns(Mock.Of<ISingleClientProxy>());
        hub.SetupGet(h => h.Clients).Returns(clients.Object);

        var admin = new AdminController(context, new LabSessionLifecycleService(context, hub.Object));
        var adminContext = new DefaultHttpContext { Session = new FakeSession() };
        adminContext.Session.SetString("Role", "Admin");
        adminContext.Session.SetInt32("AdminId", 1);
        adminContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Admin")
        }, "Test"));
        admin.ControllerContext = new ControllerContext { HttpContext = adminContext };
        admin.TempData = new TempDataDictionary(adminContext, Mock.Of<ITempDataProvider>());

        var teacher = new TeacherController(
            context, new SessionManagerService(hub.Object), new LabSessionLifecycleService(context, hub.Object));
        var teacherContext = new DefaultHttpContext { Session = new FakeSession() };
        teacherContext.Session.SetString("Role", "Teacher");
        teacherContext.Session.SetInt32("TeacherId", 1);
        teacher.ControllerContext = new ControllerContext { HttpContext = teacherContext };
        teacher.TempData = new TempDataDictionary(teacherContext, Mock.Of<ITempDataProvider>());
        teacher.Url = Mock.Of<IUrlHelper>();

        return (admin, teacher);
    }

    /// <summary>The stamp a filename should carry, allowing for the minute ticking over mid-test.</summary>
    private static IEnumerable<string> AcceptableUtcStamps()
    {
        var now = DateTime.UtcNow;
        yield return now.AddMinutes(-1).ToString("yyyyMMdd-HHmm");
        yield return now.ToString("yyyyMMdd-HHmm");
        yield return now.AddMinutes(1).ToString("yyyyMMdd-HHmm");
    }

    private static string StampOf(string fileName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d{8}-\d{4})");
        Assert.True(match.Success, $"no timestamp found in \"{fileName}\"");
        return match.Groups[1].Value;
    }

    public static TheoryData<string> ExportNames => new() { "usage", "attendance", "remote commands", "alerts" };

    private static async Task<string> FileNameOf(string which)
    {
        using var db = GetDbContext();
        var (admin, teacher) = CreateControllers(db);
        IActionResult result = which switch
        {
            "usage" => await admin.ExportUsageCsv(null, null),
            "attendance" => await admin.ExportAttendanceCsv(),
            "remote commands" => await admin.ExportRemoteCommandsCsv(),
            "alerts" => await teacher.ExportAlertsCsv(new Server.Models.AlertListFilter()),
            _ => throw new ArgumentOutOfRangeException(nameof(which))
        };
        var file = Assert.IsType<FileContentResult>(result);
        return file.FileDownloadName;
    }

    [Theory]
    [MemberData(nameof(ExportNames))]
    public async Task EveryExportStampsItsFilenameInUtc(string which)
    {
        var stamp = StampOf(await FileNameOf(which));

        Assert.Contains(stamp, AcceptableUtcStamps());
    }

    [Fact]
    public async Task ExportsRequestedTogetherCarryTheSameTimestamp()
    {
        var stamps = new List<string>();
        foreach (var which in new[] { "usage", "attendance", "remote commands", "alerts" })
        {
            stamps.Add(StampOf(await FileNameOf(which)));
        }

        // The minute may tick over while the four run, so compare the parsed
        // instants rather than the strings and allow a single minute of spread.
        var parsed = stamps
            .Select(s => DateTime.ParseExact(s, "yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var spread = parsed.Max() - parsed.Min();

        Assert.True(spread <= TimeSpan.FromMinutes(1),
            $"exports disagreed by {spread}: {string.Join(", ", stamps)}");
    }
}
