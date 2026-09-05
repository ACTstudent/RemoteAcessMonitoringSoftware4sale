using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Controllers;
using Server.Data;
using Server.Hubs;
using Server.Models;
using Server.Services;

namespace Server.Tests.Controllers;

/// <summary>
/// The alert list keeps the teacher's filter across every action.
///
/// Alert actions used to redirect to a bare Alerts URL with
/// includeAcknowledged pinned to true, so a teacher working through, say,
/// critical open alerts for one station was dropped back into an unfiltered
/// list after each acknowledge and had to rebuild the filter every time. The
/// CSV export had the matching defect in the other direction: it ignored the
/// filter's status entirely and exported every status regardless of what was
/// on screen.
/// </summary>
public class TeacherAlertFilterTests
{
    private const int TeacherId = 1;

    private static ApplicationDbContext GetDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static TeacherController CreateController(ApplicationDbContext context)
    {
        var hubMock = new Mock<IHubContext<RemoteMonitoringHub>>();
        var clientsMock = new Mock<IHubClients>();
        var proxyMock = new Mock<IClientProxy>();
        clientsMock.Setup(c => c.All).Returns(proxyMock.Object);
        clientsMock.Setup(c => c.User(It.IsAny<string>())).Returns(proxyMock.Object);
        clientsMock.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>())).Returns(proxyMock.Object);
        hubMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var controller = new TeacherController(
            context,
            new SessionManagerService(hubMock.Object),
            new LabSessionLifecycleService(context, hubMock.Object));
        controller.Url = Mock.Of<IUrlHelper>();

        var httpContext = new DefaultHttpContext { Session = new FakeSession() };
        httpContext.Session.SetString("Role", "Teacher");
        httpContext.Session.SetInt32("TeacherId", TeacherId);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    /// <summary>A student the alert scope will accept, plus one open alert group for them.</summary>
    private static async Task<MonitoringAlert> SeedAlertAsync(
        ApplicationDbContext db, string station = "LAB-07", string severity = "Critical", bool acknowledged = false)
    {
        const string studentNumber = "S-ALERT-1";
        if (!await db.Students.AnyAsync(student => student.StudentNumber == studentNumber))
        {
            db.Students.Add(new Student
            {
                StudentNumber = studentNumber,
                FullName = "Alert Subject",
                Username = "alertsubject",
                PasswordHash = "hash"
            });
        }

        var alert = new MonitoringAlert
        {
            StudentId = studentNumber,
            PcName = station,
            Severity = severity,
            Title = $"Probe {Guid.NewGuid():N}",
            Message = "Seeded for the alert filter tests",
            DedupeKey = Guid.NewGuid().ToString("N"),
            IsAcknowledged = acknowledged,
            AcknowledgedAt = acknowledged ? DateTime.UtcNow : null,
            AcknowledgedByTeacherId = acknowledged ? TeacherId : null,
            CreatedAt = DateTime.UtcNow
        };
        db.MonitoringAlerts.Add(alert);
        await db.SaveChangesAsync();
        return alert;
    }

    private static AlertListFilter CriticalStationFilter() => new()
    {
        Severity = "Critical",
        Station = "LAB-07",
        Page = 2
    };

    private static IDictionary<string, object?> RouteValues(IActionResult result)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(TeacherController.Alerts), redirect.ActionName);
        Assert.NotNull(redirect.RouteValues);
        return redirect.RouteValues!;
    }

    private static void AssertFilterSurvived(IDictionary<string, object?> routeValues)
    {
        Assert.Equal("Critical", routeValues["severity"]);
        Assert.Equal("LAB-07", routeValues["station"]);
        Assert.Equal(2, routeValues["page"]);
    }

    [Fact]
    public async Task AcknowledgeAlert_ReturnsToTheSameFilteredPage()
    {
        using var db = GetDbContext();
        var alert = await SeedAlertAsync(db);
        var controller = CreateController(db);

        var result = await controller.AcknowledgeAlert(alert.MonitoringAlertId, true, CriticalStationFilter());

        AssertFilterSurvived(RouteValues(result));
    }

    [Fact]
    public async Task AcknowledgeAlert_DoesNotForceAcknowledgedAlertsBackIntoTheList()
    {
        using var db = GetDbContext();
        var alert = await SeedAlertAsync(db);
        var controller = CreateController(db);

        var result = await controller.AcknowledgeAlert(alert.MonitoringAlertId, true, new AlertListFilter());

        // The old redirect pinned includeAcknowledged=true, which widened a list the
        // teacher had deliberately narrowed to open alerts.
        Assert.Null(RouteValues(result)["includeAcknowledged"]);
    }

    [Fact]
    public async Task AcknowledgeAlert_ReportsTheOutcome()
    {
        using var db = GetDbContext();
        var alert = await SeedAlertAsync(db);
        var controller = CreateController(db);

        await controller.AcknowledgeAlert(alert.MonitoringAlertId, true, new AlertListFilter());

        // The group leaves an open-only list once acknowledged, so silence would
        // read as the action having failed.
        Assert.Equal("Alert group acknowledged.", controller.TempData["Message"]);
    }

    [Fact]
    public async Task BulkAcknowledgeAlerts_ReturnsToTheSameFilteredPage()
    {
        using var db = GetDbContext();
        var alert = await SeedAlertAsync(db);
        var controller = CreateController(db);

        var result = await controller.BulkAcknowledgeAlerts(
            new List<int> { alert.MonitoringAlertId }, CriticalStationFilter());

        AssertFilterSurvived(RouteValues(result));
    }

    [Fact]
    public async Task BulkDismissAlerts_ReturnsToTheSameFilteredPage()
    {
        using var db = GetDbContext();
        var alert = await SeedAlertAsync(db);
        var controller = CreateController(db);

        var result = await controller.BulkDismissAlerts(
            new List<int> { alert.MonitoringAlertId }, "Handled in class", CriticalStationFilter());

        AssertFilterSurvived(RouteValues(result));
    }

    [Fact]
    public async Task BulkReopenAlerts_ReturnsToTheSameFilteredPage()
    {
        using var db = GetDbContext();
        var alert = await SeedAlertAsync(db, acknowledged: true);
        var controller = CreateController(db);

        var result = await controller.BulkReopenAlerts(
            new List<int> { alert.MonitoringAlertId }, CriticalStationFilter());

        AssertFilterSurvived(RouteValues(result));
    }

    [Fact]
    public async Task BulkAction_WithNothingSelected_KeepsTheFilterAndExplains()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        var result = await controller.BulkAcknowledgeAlerts(new List<int>(), CriticalStationFilter());

        AssertFilterSurvived(RouteValues(result));
        Assert.Equal("Select at least one alert group.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task Alerts_BackwardsDateRange_KeepsTheEnteredDatesAndExplains()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        var filter = new AlertListFilter
        {
            From = new DateTime(2026, 9, 5),
            To = new DateTime(2026, 9, 1)
        };

        var result = await controller.Alerts(filter);

        // A teacher can produce this from the filter form, so it must not become a
        // bare 400 that throws the entered dates away.
        var model = Assert.IsType<AlertListViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.NotNull(model.FilterWarning);
        Assert.Equal(new DateTime(2026, 9, 5), model.Filter.From);
        Assert.Equal(new DateTime(2026, 9, 1), model.Filter.To);
    }

    [Fact]
    public async Task Alerts_OutOfRangePaging_IsClampedRatherThanRejected()
    {
        using var db = GetDbContext();
        await SeedAlertAsync(db);
        var controller = CreateController(db);

        var result = await controller.Alerts(new AlertListFilter { Page = 0, PageSize = 99_999 });

        // A hand-edited or stale link should still show the list.
        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Alerts_WithNoAlertsAtAll_RendersTheEmptyListRatherThanRedirecting()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        var result = await controller.Alerts(new AlertListFilter());

        // The page-correcting redirect must not fire on an empty list, or the
        // first page would bounce against itself.
        var model = Assert.IsType<AlertListViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(model.Alerts.Items);
        Assert.Equal(1, model.Alerts.Page);
    }

    [Fact]
    public async Task Alerts_UnknownStatus_IsRejected()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);

        var result = await controller.Alerts(new AlertListFilter { Status = "NotAStatus" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Alerts_PageBeyondTheLast_RedirectsSoTheUrlMatchesTheListShown()
    {
        using var db = GetDbContext();
        await SeedAlertAsync(db);
        var controller = CreateController(db);

        var result = await controller.Alerts(new AlertListFilter { Page = 5 });

        // The query falls back to the last real page; the address bar has to follow,
        // or a page=5 URL showing page 1 gets shared or bookmarked.
        var routeValues = RouteValues(result);
        Assert.Null(routeValues["page"]);
    }

    [Fact]
    public async Task ExportAlertsCsv_DefaultsToTheOpenAlertsTheListShows()
    {
        using var db = GetDbContext();
        await SeedAlertAsync(db);
        var acknowledged = await SeedAlertAsync(db, acknowledged: true);
        var controller = CreateController(db);

        // No status and no includeAcknowledged is the default list: open alerts only.
        var result = await controller.ExportAlertsCsv(new AlertListFilter());

        var csv = System.Text.Encoding.UTF8.GetString(Assert.IsType<FileContentResult>(result).FileContents);
        Assert.DoesNotContain(acknowledged.Title, csv);
    }

    [Fact]
    public async Task ExportAlertsCsv_IncludesEveryStatusWhenTheListDoes()
    {
        using var db = GetDbContext();
        var open = await SeedAlertAsync(db);
        var acknowledged = await SeedAlertAsync(db, acknowledged: true);
        var controller = CreateController(db);

        var result = await controller.ExportAlertsCsv(new AlertListFilter { IncludeAcknowledged = true });

        var csv = System.Text.Encoding.UTF8.GetString(Assert.IsType<FileContentResult>(result).FileContents);
        Assert.Contains(open.Title, csv);
        Assert.Contains(acknowledged.Title, csv);
    }
}

/// <summary>
/// <see cref="AlertListFilter"/> is the single place that decides which alerts a
/// filter selects, so the list, the paging links and the export cannot disagree.
/// </summary>
public class AlertListFilterTests
{
    [Fact]
    public void ResolvesToOpenAlertsByDefault()
    {
        Assert.True(new AlertListFilter().TryResolveStatus(out var status));
        Assert.Equal(MonitoringAlertStatus.Open, status);
    }

    [Fact]
    public void IncludeAcknowledgedMeansEveryStatus()
    {
        Assert.True(new AlertListFilter { IncludeAcknowledged = true }.TryResolveStatus(out var status));
        Assert.Null(status);
    }

    [Fact]
    public void AnExplicitStatusWinsOverIncludeAcknowledged()
    {
        var filter = new AlertListFilter { IncludeAcknowledged = true, Status = "Dismissed" };

        Assert.True(filter.TryResolveStatus(out var status));
        Assert.Equal(MonitoringAlertStatus.Dismissed, status);
    }

    [Fact]
    public void AnUnknownStatusIsReportedRatherThanIgnored()
    {
        Assert.False(new AlertListFilter { Status = "Snoozed" }.TryResolveStatus(out var status));
        Assert.Null(status);
    }

    [Theory]
    [InlineData("open", MonitoringAlertStatus.Open)]
    [InlineData("ACKNOWLEDGED", MonitoringAlertStatus.Acknowledged)]
    public void StatusNamesAreMatchedWithoutRegardToCase(string name, MonitoringAlertStatus expected)
    {
        Assert.True(new AlertListFilter { Status = name }.TryResolveStatus(out var status));
        Assert.Equal(expected, status);
    }

    [Fact]
    public void DefaultsAreLeftOutOfTheRouteValuesSoAPlainListKeepsAPlainUrl()
    {
        var values = new AlertListFilter().ToRouteValues().GetType()
            .GetProperties().ToDictionary(p => p.Name, p => p.GetValue(new AlertListFilter().ToRouteValues()));

        Assert.All(values.Values, value => Assert.Null(value));
    }

    [Fact]
    public void SetValuesSurviveTheRoundTripToRouteValues()
    {
        var filter = new AlertListFilter
        {
            IncludeAcknowledged = true,
            From = new DateTime(2026, 9, 1),
            To = new DateTime(2026, 9, 5),
            Severity = "  Critical  ",
            StudentId = "S-1",
            Station = "LAB-07",
            Status = "Open",
            Page = 3,
            PageSize = 25
        };

        var values = filter.ToRouteValues();
        var read = values.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(values));

        Assert.Equal("true", read["includeAcknowledged"]);
        Assert.Equal("2026-09-01", read["from"]);
        Assert.Equal("2026-09-05", read["to"]);
        Assert.Equal("Critical", read["severity"]);
        Assert.Equal("S-1", read["studentId"]);
        Assert.Equal("LAB-07", read["station"]);
        Assert.Equal("Open", read["status"]);
        Assert.Equal(3, read["page"]);
        Assert.Equal(25, read["pageSize"]);
    }

    [Fact]
    public void AnExplicitPageOverridesTheFiltersOwnPage()
    {
        var values = new AlertListFilter { Page = 4 }.ToRouteValues(2);

        Assert.Equal(2, values.GetType().GetProperty("page")!.GetValue(values));
    }

    [Fact]
    public void ABackwardsDateRangeIsRecognised()
    {
        Assert.False(new AlertListFilter
        {
            From = new DateTime(2026, 9, 5),
            To = new DateTime(2026, 9, 1)
        }.HasUsableDateRange);

        Assert.True(new AlertListFilter
        {
            From = new DateTime(2026, 9, 1),
            To = new DateTime(2026, 9, 5)
        }.HasUsableDateRange);
    }

    [Fact]
    public void PagingIsPulledBackIntoRange()
    {
        var filter = new AlertListFilter { Page = 0, PageSize = 99_999 };

        filter.ClampPaging();

        Assert.Equal(1, filter.Page);
        Assert.Equal(AlertListFilter.DefaultPageSize, filter.PageSize);
    }
}
