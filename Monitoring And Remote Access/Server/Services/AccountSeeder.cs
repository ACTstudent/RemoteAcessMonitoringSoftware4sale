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
        var seededAccounts = new List<string>();
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
            if (!HasAdmin(db, adminUsername))
            {
                ReserveIdentifier(reservedIdentifiers, adminUsername, "administrator username");
                db.Admins.Add(new Admin
                {
                    Username = adminUsername,
                    FullName = "System Administrator",
                    PasswordHash = hasher.HashPassword(new object(), adminPassword)
                });
                seededAccounts.Add($"administrator '{adminUsername}'");
            }
        }

        var teacherPassword = configuration["Cams:SeededTeacherPassword"];
        if (!string.IsNullOrWhiteSpace(teacherPassword))
        {
            if (teacherPassword.Length < 8)
                throw new InvalidOperationException("Cams:SeededTeacherPassword must contain at least 8 characters.");

            var teacherUsername = GetValue(configuration, "Cams:SeededTeacherUsername", "teacher");
            if (!HasTeacher(db, teacherUsername))
            {
                ReserveIdentifier(reservedIdentifiers, teacherUsername, "teacher username");
                var firstName = GetValue(configuration, "Cams:SeededTeacherFirstName", "Seeded");
                var lastName = GetValue(configuration, "Cams:SeededTeacherLastName", "Teacher");
                db.Teachers.Add(new Teacher
                {
                    FirstName = firstName,
                    LastName = lastName,
                    Email = configuration["Cams:SeededTeacherEmail"]?.Trim() ?? string.Empty,
                    Username = teacherUsername,
                    PasswordHash = hasher.HashPassword(new object(), teacherPassword),
                    Status = "Active"
                });
                seededAccounts.Add($"teacher '{teacherUsername}'");
            }
        }

        var studentPassword = configuration["Cams:SeededStudentPassword"];
        if (!string.IsNullOrWhiteSpace(studentPassword))
        {
            if (studentPassword.Length < 8)
                throw new InvalidOperationException("Cams:SeededStudentPassword must contain at least 8 characters.");

            var studentUsername = GetValue(configuration, "Cams:SeededStudentUsername", "student");
            var studentNumber = GetValue(configuration, "Cams:SeededStudentNumber", "STUDENT-001");
            if (!HasStudent(db, studentUsername, studentNumber))
            {
                ReserveIdentifier(reservedIdentifiers, studentUsername, "student username");
                ReserveIdentifier(reservedIdentifiers, studentNumber, "student number");
                var firstName = GetValue(configuration, "Cams:SeededStudentFirstName", "Seeded");
                var lastName = GetValue(configuration, "Cams:SeededStudentLastName", "Student");
                db.Students.Add(new Student
                {
                    StudentNumber = studentNumber,
                    FirstName = firstName,
                    LastName = lastName,
                    FullName = $"{firstName} {lastName}".Trim(),
                    Username = studentUsername,
                    PasswordHash = hasher.HashPassword(new object(), studentPassword),
                    Status = "Active",
                    GradeSection = configuration["Cams:SeededStudentGradeSection"]?.Trim() ?? string.Empty
                });
                seededAccounts.Add($"student '{studentUsername}'");
            }
        }

        if (seededAccounts.Count == 0)
        {
            return;
        }

        db.SaveChanges();
        foreach (var account in seededAccounts)
        {
            Console.WriteLine($"[CAMS] Configured {account} account created.");
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

    private static bool HasAdmin(ApplicationDbContext db, string username) =>
        db.Admins.Any(account => account.Username.ToLower() == username.ToLower());

    private static bool HasTeacher(ApplicationDbContext db, string username) =>
        db.Teachers.Any(account => account.Username.ToLower() == username.ToLower());

    private static bool HasStudent(ApplicationDbContext db, string username, string studentNumber) =>
        db.Students.Any(account =>
            account.Username.ToLower() == username.ToLower() ||
            account.StudentNumber.ToLower() == studentNumber.ToLower());
}
