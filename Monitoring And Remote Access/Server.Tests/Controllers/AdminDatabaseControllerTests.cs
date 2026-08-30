using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Controllers;
using Server.Data;
using Server.Services;

namespace Server.Tests.Controllers;

public sealed class AdminDatabaseControllerTests
{
    [Fact]
    public void Controller_RequiresAdminRoleAndAutomaticAntiforgeryValidation()
    {
        var controllerType = typeof(AdminDatabaseController);

        var authorize = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("Admin", authorize.Roles);
        Assert.NotNull(controllerType.GetCustomAttribute<AutoValidateAntiforgeryTokenAttribute>());
    }

    [Fact]
    public async Task Index_WithoutAdminSession_RedirectsToLogin()
    {
        await using var db = CreateContext();
        var maintenance = new Mock<IDatabaseMaintenanceService>(MockBehavior.Strict);
        var controller = CreateController(maintenance.Object, db, isAdmin: false);

        var result = await controller.Index(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
        maintenance.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateBackup_AuditsAttemptAndSuccess()
    {
        await using var db = CreateContext();
        var backup = new DatabaseBackupInfo(
            "CAMS_20260830T120000000Z_test_12345678.db",
            "test",
            4096,
            DateTimeOffset.UtcNow);
        var maintenance = new Mock<IDatabaseMaintenanceService>();
        maintenance
            .Setup(service => service.CreateBackupAsync("test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(backup);
        var controller = CreateController(maintenance.Object, db);

        var result = await controller.CreateBackup("test", CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(
            new[] { "DatabaseBackupRequested", "DatabaseBackupCreated" },
            await db.AuditLogs.OrderBy(audit => audit.AuditLogId)
                .Select(audit => audit.Action)
                .ToArrayAsync());
        Assert.Equal(7, (await db.AuditLogs.FirstAsync()).UserId);
    }

    [Fact]
    public async Task StageRestore_WithoutExactConfirmation_DoesNotCallServiceOrAudit()
    {
        await using var db = CreateContext();
        var maintenance = new Mock<IDatabaseMaintenanceService>(MockBehavior.Strict);
        var controller = CreateController(maintenance.Object, db);

        var result = await controller.StageRestore(
            "CAMS_20260830T120000000Z_test_12345678.db",
            "restore",
            CancellationToken.None);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(await db.AuditLogs.ToListAsync());
        maintenance.VerifyNoOtherCalls();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AdminDatabaseController CreateController(
        IDatabaseMaintenanceService maintenance,
        ApplicationDbContext db,
        bool isAdmin = true)
    {
        var httpContext = new DefaultHttpContext
        {
            Session = new FakeSession()
        };
        if (isAdmin)
        {
            httpContext.Session.SetString("Role", "Admin");
            httpContext.Session.SetInt32("AdminId", 7);
        }

        var controller = new AdminDatabaseController(
            maintenance,
            db,
            NullLogger<AdminDatabaseController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        controller.TempData = new TempDataDictionary(
            httpContext,
            Mock.Of<ITempDataProvider>());
        return controller;
    }
}
