using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Server.Data;
using Server.Services;

namespace Server.Tests.Services;

public class AccountSeederTests
{
    [Fact]
    public void SeedConfiguredAccounts_CreatesConfiguredAdminWithHashedPassword()
    {
        using var context = CreateContext();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Cams:InitialAdminPassword"] = "admin-secret"
        });

        AccountSeeder.SeedConfiguredAccounts(context, configuration);

        var hasher = new PasswordHasher<object>();
        var admin = Assert.Single(context.Admins);

        Assert.Equal("admin", admin.Username);
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(new object(), admin.PasswordHash, "admin-secret"));

        AccountSeeder.SeedConfiguredAccounts(context, configuration);

        Assert.Single(context.Admins);
        Assert.Empty(context.Teachers);
        Assert.Empty(context.Students);
    }

    [Fact]
    public void SeedConfiguredAccounts_WithoutPasswords_DoesNotCreateAccounts()
    {
        using var context = CreateContext();

        AccountSeeder.SeedConfiguredAccounts(context, CreateConfiguration(new Dictionary<string, string?>()));

        Assert.Empty(context.Admins);
        Assert.Empty(context.Teachers);
        Assert.Empty(context.Students);
    }

    [Fact]
    public void SeedConfiguredAccounts_DoesNotUseProductionDefaults()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Cams:InitialAdminPassword"] = "",
            ["Cams:SeededStudentPassword"] = ""
        });
        using var db = CreateContext();

        AccountSeeder.SeedConfiguredAccounts(db, configuration);

        Assert.Empty(db.Admins);
        Assert.Empty(db.Students);
    }

    [Fact]
    public void SeedConfiguredAccounts_RejectsWeakInitialAdminPassword()
    {
        using var db = CreateContext();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Cams:InitialAdminPassword"] = "too-short"
        });

        var error = Assert.Throws<InvalidOperationException>(() =>
            AccountSeeder.SeedConfiguredAccounts(db, configuration));

        Assert.Contains("at least 12 characters", error.Message);
        Assert.Empty(db.Admins);
    }

    [Fact]
    public void SeedConfiguredAccounts_RejectsAdminIdentifierUsedByTeacher()
    {
        using var db = CreateContext();
        db.Teachers.Add(new Server.Models.Teacher
        {
            Username = "admin",
            PasswordHash = "hash",
            Status = "Active"
        });
        db.SaveChanges();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Cams:InitialAdminPassword"] = "admin-secret"
        });

        var error = Assert.Throws<InvalidOperationException>(() => AccountSeeder.SeedConfiguredAccounts(db, configuration));

        Assert.Contains("already used", error.Message);
        Assert.Empty(db.Admins);
        Assert.Single(db.Teachers);
    }

    [Fact]
    public void SeedConfiguredAccounts_RecoversExistingAdministrator()
    {
        using var db = CreateContext();
        var hasher = new PasswordHasher<object>();
        var admin = new Server.Models.Admin
        {
            Username = "Admin",
            FullName = "Existing Administrator",
            PasswordHash = hasher.HashPassword(new object(), "old-admin-password"),
            IsActive = false,
            FailedLoginAttempts = 4,
            LockoutEndUtc = DateTime.UtcNow.AddHours(1)
        };
        db.Admins.Add(admin);
        db.SaveChanges();

        AccountSeeder.SeedConfiguredAccounts(db, CreateConfiguration(new Dictionary<string, string?>
        {
            ["Cams:InitialAdminUsername"] = "admin",
            ["Cams:InitialAdminPassword"] = "new-admin-password"
        }));

        Assert.True(admin.IsActive);
        Assert.Equal(0, admin.FailedLoginAttempts);
        Assert.Null(admin.LockoutEndUtc);
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(new object(), admin.PasswordHash, "new-admin-password"));
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
