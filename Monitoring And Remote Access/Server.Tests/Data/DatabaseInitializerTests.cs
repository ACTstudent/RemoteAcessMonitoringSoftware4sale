using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Services;

namespace Server.Tests.Data;

public class DatabaseInitializerTests
{
    [Fact]
    public void Initialize_AppliesMigrationToFreshSqliteDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateContext(connection);

        DatabaseInitializer.Initialize(db);

        Assert.True(db.Database.GetAppliedMigrations().Any());
        Assert.True(db.Database.CanConnect());
        Assert.Equal(3, db.Roles.Count());
    }

    [Fact]
    public void Initialize_BaselinesExistingEnsureCreatedSqliteDatabase()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = CreateContext(connection);
        db.Database.EnsureCreated();

        DatabaseInitializer.Initialize(db);

        Assert.Single(db.Database.GetAppliedMigrations());
        Assert.Equal(3, db.Roles.Count());
    }

    private static ApplicationDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        return new ApplicationDbContext(options);
    }
}
