using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Server.Data;
using Server.Models;

namespace Server.Services;

public static class AccountSeeder
{
    public static void SeedConfiguredAccounts(ApplicationDbContext db, IConfiguration configuration)
    {
        var hasher = new PasswordHasher<object>();
        var configuredAccounts = new List<string>();
        var reservedIdentifiers = db.Admins.Select(account => account.Username)
            .Concat(db.Teachers.Select(account => account.Username))
            .Concat(db.Students.Select(account => account.Username))
            .Concat(db.Students.Select(account => account.StudentNumber))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var adminPassword = configuration["Cams:InitialAdminPassword"];
        if (!string.IsNullOrWhiteSpace(adminPassword))
        {
            if (adminPassword.Length < 12)
                throw new InvalidOperationException("Cams:InitialAdminPassword must contain at least 12 characters.");

            var adminUsername = GetValue(configuration, "Cams:InitialAdminUsername", "admin");
            var existingAdmin = db.Admins.FirstOrDefault(account => account.Username.ToLower() == adminUsername.ToLower());
            if (existingAdmin is null)
            {
                ReserveIdentifier(reservedIdentifiers, adminUsername, "administrator username");
                db.Admins.Add(new Admin
                {
                    Username = adminUsername,
                    FullName = "System Administrator",
                    PasswordHash = hasher.HashPassword(new object(), adminPassword)
                });
                configuredAccounts.Add($"administrator '{adminUsername}' created");
            }
            else
            {
                existingAdmin.PasswordHash = hasher.HashPassword(new object(), adminPassword);
                existingAdmin.IsActive = true;
                existingAdmin.FailedLoginAttempts = 0;
                existingAdmin.LockoutEndUtc = null;
                configuredAccounts.Add($"administrator '{existingAdmin.Username}' recovered");
            }
        }

        if (configuredAccounts.Count == 0)
        {
            return;
        }

        db.SaveChanges();
        foreach (var account in configuredAccounts)
        {
            Console.WriteLine($"[CAMS] Configured {account}.");
        }
    }

    private static string GetValue(IConfiguration configuration, string key, string fallback)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static void ReserveIdentifier(HashSet<string> identifiers, string value, string label)
    {
        if (!identifiers.Add(value))
            throw new InvalidOperationException($"The configured {label} '{value}' is already used by another CAMS account.");
    }

}
