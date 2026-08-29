using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Server.Data;
using Server.Services;

namespace Server.Tests.Services;

public class AccountSeederTests
{
    [Fact]
    public void SeedConfiguredAccounts_CreatesConfiguredAccountsWithHashedPasswords()
    {
        using var context = CreateContext();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Cams:InitialAdminPassword"] = "admin-secret",
            ["Cams:SeededTeacherPassword"] = "teacher-secret",
            ["Cams:SeededStudentPassword"] = "student-secret",
            ["Cams:SeededStudentNumber"] = "S-001"
        });

        AccountSeeder.SeedConfiguredAccounts(context, configuration);

        var hasher = new PasswordHasher<object>();
        var admin = Assert.Single(context.Admins);
        var teacher = Assert.Single(context.Teachers);
        var student = Assert.Single(context.Students);

        Assert.Equal("admin", admin.Username);
        Assert.Equal("teacher", teacher.Username);
        Assert.Equal("student", student.Username);
        Assert.Equal("S-001", student.StudentNumber);
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(new object(), admin.PasswordHash, "admin-secret"));
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(new object(), teacher.PasswordHash, "teacher-secret"));
        Assert.Equal(PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(new object(), student.PasswordHash, "student-secret"));

        AccountSeeder.SeedConfiguredAccounts(context, configuration);

        Assert.Single(context.Admins);
        Assert.Single(context.Teachers);
        Assert.Single(context.Students);
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
