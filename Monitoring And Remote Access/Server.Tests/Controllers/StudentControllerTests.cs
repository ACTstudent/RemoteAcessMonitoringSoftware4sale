using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Data;
using Server.Models;

namespace Server.Tests.Controllers;

/// <summary>
/// The portal a student sees on their own workstation, which had no tests.
///
/// Two things matter here. The first is that every action refuses a caller who
/// is not a signed-in student, because these actions read the student id
/// straight out of the session and would otherwise act on whoever happens to be
/// there. The second is scope: a student's alert list and the "mark read"
/// action must reach that student's own notifications and the lab-wide
/// broadcasts, and nothing belonging to a classmate.
/// </summary>
public class StudentControllerTests
{
    private const int StudentId = 41;
    private const int ClassmateId = 42;

    private static ApplicationDbContext GetDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static StudentController CreateController(ApplicationDbContext context, string? role = "Student", int? studentId = StudentId)
    {
        var controller = new StudentController(context);
        var httpContext = new DefaultHttpContext { Session = new FakeSession() };
        if (role is not null) httpContext.Session.SetString("Role", role);
        if (studentId.HasValue) httpContext.Session.SetInt32($"{role}Id", studentId.Value);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static Notification Note(int? owner, string title, bool read = false, DateTime? createdAt = null) => new()
    {
        StudentId = owner,
        Type = "Alert",
        Title = title,
        Message = title,
        IsRead = read,
        CreatedAt = createdAt ?? DateTime.UtcNow
    };

    private static void AssertRedirectedToLogin(IActionResult result)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);
        Assert.Equal("Account", redirect.ControllerName);
    }

    // ---------- Every action refuses a caller who is not a signed-in student ----------

    public static TheoryData<string?, int?> NonStudentCallers => new()
    {
        { null, null },              // anonymous
        { "Teacher", 7 },            // a teacher hitting the student portal directly
        { "Admin", 1 },              // an administrator doing the same
        { "Student", null }          // the role string without the matching id
    };

    [Theory]
    [MemberData(nameof(NonStudentCallers))]
    public async Task Index_DeniesCallersWhoAreNotASignedInStudent(string? role, int? id)
    {
        using var db = GetDbContext();
        AssertRedirectedToLogin(await CreateController(db, role, id).Index());
    }

    [Theory]
    [MemberData(nameof(NonStudentCallers))]
    public async Task Alerts_DeniesCallersWhoAreNotASignedInStudent(string? role, int? id)
    {
        using var db = GetDbContext();
        AssertRedirectedToLogin(await CreateController(db, role, id).Alerts());
    }

    [Theory]
    [MemberData(nameof(NonStudentCallers))]
    public void Settings_DeniesCallersWhoAreNotASignedInStudent(string? role, int? id)
    {
        using var db = GetDbContext();
        AssertRedirectedToLogin(CreateController(db, role, id).Settings());
    }

    [Theory]
    [MemberData(nameof(NonStudentCallers))]
    public async Task MarkRead_DeniesCallersWhoAreNotASignedInStudentAndChangesNothing(string? role, int? id)
    {
        using var db = GetDbContext();
        var note = Note(StudentId, "Untouched");
        db.Notifications.Add(note);
        await db.SaveChangesAsync();

        AssertRedirectedToLogin(await CreateController(db, role, id).MarkRead(note.NotificationId));

        Assert.False((await db.Notifications.FindAsync(note.NotificationId))!.IsRead);
    }

    [Theory]
    [MemberData(nameof(NonStudentCallers))]
    public async Task ResetPassword_DeniesCallersWhoAreNotASignedInStudent(string? role, int? id)
    {
        using var db = GetDbContext();
        var input = new PasswordChangeInput { CurrentPassword = "old", NewPassword = "newpassword1", ConfirmPassword = "newpassword1" };
        AssertRedirectedToLogin(await CreateController(db, role, id).ResetPassword(input));
    }

    [Theory]
    [MemberData(nameof(NonStudentCallers))]
    public async Task SessionStatusJson_ReturnsNothingUsefulToCallersWhoAreNotASignedInStudent(string? role, int? id)
    {
        using var db = GetDbContext();
        var result = await CreateController(db, role, id)._SessionStatusJson();

        // The status endpoint answers with an empty object rather than a redirect,
        // so the check is that it leaks no session state at all.
        var json = Assert.IsType<JsonResult>(result);
        var payload = System.Text.Json.JsonSerializer.Serialize(json.Value);
        Assert.Equal("{}", payload);
    }

    // ---------- Alert scope ----------

    [Fact]
    public async Task Alerts_ShowsOwnAndBroadcastNotificationsButNotAClassmates()
    {
        using var db = GetDbContext();
        db.Notifications.AddRange(
            Note(StudentId, "Mine"),
            Note(null, "Broadcast to the whole lab"),
            Note(ClassmateId, "Belongs to a classmate"));
        await db.SaveChangesAsync();

        var result = await CreateController(db).Alerts();

        var view = Assert.IsType<ViewResult>(result);
        var shown = Assert.IsAssignableFrom<IEnumerable<Notification>>(view.Model).ToList();
        Assert.Equal(new[] { "Mine", "Broadcast to the whole lab" }.OrderBy(t => t),
            shown.Select(n => n.Title).OrderBy(t => t));
    }

    [Fact]
    public async Task Alerts_ShowsTheNewestHundredNotificationsFirst()
    {
        using var db = GetDbContext();
        var start = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 120; i++) db.Notifications.Add(Note(StudentId, $"Alert {i:D3}", createdAt: start.AddMinutes(i)));
        await db.SaveChangesAsync();

        var result = await CreateController(db).Alerts();

        var shown = Assert.IsAssignableFrom<IEnumerable<Notification>>(Assert.IsType<ViewResult>(result).Model).ToList();
        Assert.Equal(100, shown.Count);
        Assert.Equal("Alert 119", shown.First().Title);
        Assert.Equal("Alert 020", shown.Last().Title);
    }

    [Fact]
    public async Task MarkRead_MarksTheStudentsOwnNotification()
    {
        using var db = GetDbContext();
        var note = Note(StudentId, "Mine");
        db.Notifications.Add(note);
        await db.SaveChangesAsync();

        var result = await CreateController(db).MarkRead(note.NotificationId);

        Assert.Equal("Alerts", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.True((await db.Notifications.FindAsync(note.NotificationId))!.IsRead);
    }

    [Fact]
    public async Task MarkRead_MarksALabWideBroadcast()
    {
        using var db = GetDbContext();
        var note = Note(null, "Broadcast");
        db.Notifications.Add(note);
        await db.SaveChangesAsync();

        await CreateController(db).MarkRead(note.NotificationId);

        Assert.True((await db.Notifications.FindAsync(note.NotificationId))!.IsRead);
    }

    [Fact]
    public async Task MarkRead_LeavesAClassmatesNotificationAlone()
    {
        using var db = GetDbContext();
        var theirs = Note(ClassmateId, "Not mine to dismiss");
        db.Notifications.Add(theirs);
        await db.SaveChangesAsync();

        var result = await CreateController(db).MarkRead(theirs.NotificationId);

        // The redirect is identical either way, so the assertion that carries the
        // weight is the stored row: guessing an id must not clear someone else's alert.
        Assert.Equal("Alerts", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.False((await db.Notifications.FindAsync(theirs.NotificationId))!.IsRead);
    }

    [Fact]
    public async Task MarkRead_IsHarmlessWhenTheNotificationDoesNotExist()
    {
        using var db = GetDbContext();
        var result = await CreateController(db).MarkRead(987654);
        Assert.Equal("Alerts", Assert.IsType<RedirectToActionResult>(result).ActionName);
    }

    // ---------- Password change ----------

    [Fact]
    public async Task ResetPassword_SaysTheConfirmationDoesNotMatchRatherThanBlamingTheCurrentPassword()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        controller.ModelState.AddModelError(nameof(PasswordChangeInput.ConfirmPassword), "mismatch");

        var result = await controller.ResetPassword(new PasswordChangeInput
        {
            CurrentPassword = "correct-horse",
            NewPassword = "newpassword1",
            ConfirmPassword = "different-one"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Settings", view.ViewName);
        Assert.Equal("The new password and confirmation do not match.", view.ViewData["Error"]);
    }

    [Fact]
    public async Task ResetPassword_ExplainsTheLengthRuleWhenTheNewPasswordIsTooShort()
    {
        using var db = GetDbContext();
        var controller = CreateController(db);
        controller.ModelState.AddModelError(nameof(PasswordChangeInput.NewPassword), "too short");

        var result = await controller.ResetPassword(new PasswordChangeInput
        {
            CurrentPassword = "correct-horse",
            NewPassword = "short",
            ConfirmPassword = "short"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Your new password must be at least 8 characters long.", view.ViewData["Error"]);
    }

    [Fact]
    public async Task ResetPassword_RejectsAWrongCurrentPasswordAndLeavesTheStoredHashAlone()
    {
        using var db = GetDbContext();
        var hasher = new PasswordHasher<object>();
        var student = new Student
        {
            Id = StudentId,
            StudentNumber = "S-41",
            FullName = "Password Owner",
            Username = "pwowner",
            PasswordHash = hasher.HashPassword(new object(), "the-real-password")
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var before = student.PasswordHash;

        var result = await CreateController(db).ResetPassword(new PasswordChangeInput
        {
            CurrentPassword = "not-the-real-password",
            NewPassword = "newpassword1",
            ConfirmPassword = "newpassword1"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Current password is incorrect.", view.ViewData["Error"]);
        Assert.Equal(before, (await db.Students.FindAsync(StudentId))!.PasswordHash);
    }

    [Fact]
    public async Task ResetPassword_ChangesTheStoredHashWhenTheCurrentPasswordIsRight()
    {
        using var db = GetDbContext();
        var hasher = new PasswordHasher<object>();
        var student = new Student
        {
            Id = StudentId,
            StudentNumber = "S-41",
            FullName = "Password Owner",
            Username = "pwowner",
            PasswordHash = hasher.HashPassword(new object(), "the-real-password")
        };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var before = student.PasswordHash;

        var result = await CreateController(db).ResetPassword(new PasswordChangeInput
        {
            CurrentPassword = "the-real-password",
            NewPassword = "a-brand-new-password",
            ConfirmPassword = "a-brand-new-password"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Password updated successfully.", view.ViewData["Success"]);

        var stored = (await db.Students.FindAsync(StudentId))!.PasswordHash;
        Assert.NotEqual(before, stored);
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(new object(), stored, "a-brand-new-password"));
    }

    // ---------- Live session status ----------

    [Fact]
    public async Task SessionStatusJson_ReportsNoActiveSession()
    {
        using var db = GetDbContext();
        var json = Assert.IsType<JsonResult>(await CreateController(db)._SessionStatusJson());
        Assert.Equal("{\"active\":false}", System.Text.Json.JsonSerializer.Serialize(json.Value));
    }

    [Fact]
    public async Task SessionStatusJson_ReportsTheRemainingMinutesAndStationOfTheActiveSession()
    {
        using var db = GetDbContext();
        var computer = new Computer { LaboratoryStation = "LAB-07" };
        db.Computers.Add(computer);
        await db.SaveChangesAsync();

        db.LabSessions.Add(new LabSession
        {
            StudentId = StudentId,
            ComputerId = computer.ComputerId,
            IsActive = true,
            Status = "Running",
            StartTime = DateTime.UtcNow.AddMinutes(-20),
            MaxDurationMinutes = 60
        });
        await db.SaveChangesAsync();

        var json = Assert.IsType<JsonResult>(await CreateController(db)._SessionStatusJson());
        var payload = System.Text.Json.JsonSerializer.Serialize(json.Value);

        Assert.Contains("\"active\":true", payload);
        Assert.Contains("\"status\":\"Running\"", payload);
        Assert.Contains("\"station\":\"LAB-07\"", payload);
        // Twenty minutes in, roughly forty remain; allow a minute of clock drift.
        var remaining = System.Text.Json.JsonDocument.Parse(payload).RootElement.GetProperty("remaining").GetInt32();
        Assert.InRange(remaining, 39, 40);
    }

    [Fact]
    public async Task SessionStatusJson_ReportsNoLimitWhenTheSessionHasNoMaximumDuration()
    {
        using var db = GetDbContext();
        db.LabSessions.Add(new LabSession
        {
            StudentId = StudentId,
            IsActive = true,
            Status = "Running",
            StartTime = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var json = Assert.IsType<JsonResult>(await CreateController(db)._SessionStatusJson());
        Assert.Contains("\"remaining\":null", System.Text.Json.JsonSerializer.Serialize(json.Value));
    }

    [Fact]
    public async Task SessionStatusJson_NeverReportsNegativeTimeOnAnOverrunSession()
    {
        using var db = GetDbContext();
        db.LabSessions.Add(new LabSession
        {
            StudentId = StudentId,
            IsActive = true,
            Status = "Running",
            StartTime = DateTime.UtcNow.AddMinutes(-120),
            MaxDurationMinutes = 30
        });
        await db.SaveChangesAsync();

        var json = Assert.IsType<JsonResult>(await CreateController(db)._SessionStatusJson());
        Assert.Contains("\"remaining\":0", System.Text.Json.JsonSerializer.Serialize(json.Value));
    }

    [Fact]
    public async Task SessionStatusJson_IgnoresAClassmatesActiveSession()
    {
        using var db = GetDbContext();
        db.LabSessions.Add(new LabSession
        {
            StudentId = ClassmateId,
            IsActive = true,
            Status = "Running",
            StartTime = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var json = Assert.IsType<JsonResult>(await CreateController(db)._SessionStatusJson());
        Assert.Equal("{\"active\":false}", System.Text.Json.JsonSerializer.Serialize(json.Value));
    }

    // ---------- Dashboard ----------

    [Fact]
    public async Task Index_ShowsOnlyGlobalRulesAndThoseOfTheTeacherRunningTheSession()
    {
        using var db = GetDbContext();
        const int sessionTeacher = 5, otherTeacher = 6;
        db.LabSessions.Add(new LabSession
        {
            StudentId = StudentId,
            TeacherId = sessionTeacher,
            IsActive = true,
            Status = "Running",
            StartTime = DateTime.UtcNow.AddMinutes(-5)
        });
        // RestrictionRule.IsGlobal defaults to true, so a teacher-scoped rule has
        // to clear it as well as set TeacherId. Setting TeacherId alone leaves the
        // rule visible to the whole lab.
        db.RestrictionRules.AddRange(
            new RestrictionRule { RuleType = "Website", Mode = "Block", Target = "global.example", IsActive = true, IsGlobal = true },
            new RestrictionRule { RuleType = "Website", Mode = "Block", Target = "mine.example", IsActive = true, IsGlobal = false, TeacherId = sessionTeacher },
            new RestrictionRule { RuleType = "Website", Mode = "Block", Target = "other-teacher.example", IsActive = true, IsGlobal = false, TeacherId = otherTeacher },
            new RestrictionRule { RuleType = "Website", Mode = "Block", Target = "retired.example", IsActive = false, IsGlobal = true });
        await db.SaveChangesAsync();

        var result = await CreateController(db).Index();

        var view = Assert.IsType<ViewResult>(result);
        var rules = Assert.IsAssignableFrom<IEnumerable<RestrictionRule>>(view.ViewData["Rules"]).ToList();
        Assert.Equal(new[] { "global.example", "mine.example" }.OrderBy(t => t),
            rules.Select(r => r.Target).OrderBy(t => t));
    }

    [Fact]
    public async Task Index_RemembersTheAssignedStationForTheRestOfTheSession()
    {
        using var db = GetDbContext();
        var computer = new Computer { LaboratoryStation = "LAB-12" };
        db.Computers.Add(computer);
        await db.SaveChangesAsync();
        db.LabSessions.Add(new LabSession
        {
            StudentId = StudentId,
            ComputerId = computer.ComputerId,
            IsActive = true,
            Status = "Running",
            StartTime = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        await controller.Index();

        Assert.Equal("LAB-12", controller.HttpContext.Session.GetString("AssignedUnit"));
    }
}
