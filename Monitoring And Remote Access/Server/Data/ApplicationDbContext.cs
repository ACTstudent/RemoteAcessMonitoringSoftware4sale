using Microsoft.EntityFrameworkCore;
using Server.Models;

namespace Server.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Admin> Admins { get; set; } = null!;
        public DbSet<Teacher> Teachers { get; set; } = null!;
        public DbSet<Student> Students { get; set; } = null!;
        public DbSet<Computer> Computers { get; set; } = null!;
        public DbSet<ComputerStatusHistory> ComputerStatusHistories { get; set; } = null!;
        public DbSet<LabSession> LabSessions { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<Permission> Permissions { get; set; } = null!;
        public DbSet<RestrictionRule> RestrictionRules { get; set; } = null!;
        public DbSet<BlacklistItem> BlacklistItems { get; set; } = null!;
        public DbSet<ApplicationCategory> ApplicationCategories { get; set; } = null!;
        public DbSet<WebsiteCategory> WebsiteCategories { get; set; } = null!;
        public DbSet<SessionRule> SessionRules { get; set; } = null!;
        public DbSet<LanConfiguration> LanConfigurations { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<SystemLog> SystemLogs { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<UsageLog> UsageLogs { get; set; } = null!;
        public DbSet<WebsiteUsageLog> WebsiteUsageLogs { get; set; } = null!;
        public DbSet<IdleInterval> IdleIntervals { get; set; } = null!;
        public DbSet<ActivityEvent> ActivityEvents { get; set; } = null!;
        public DbSet<MonitoringAlert> MonitoringAlerts { get; set; } = null!;
        public DbSet<BrowserMonitoringRecord> BrowserMonitoringRecords { get; set; } = null!;
        public DbSet<RemoteControlSession> RemoteControlSessions { get; set; } = null!;
        public DbSet<RemoteCommandLog> RemoteCommandLogs { get; set; } = null!;
        public DbSet<Class> Classes { get; set; } = null!;
        public DbSet<ClassStudent> ClassStudents { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Role>()
                .HasMany(r => r.Permissions)
                .WithMany(p => p.Roles)
                .UsingEntity(j => j.ToTable("RolePermissions"));

            modelBuilder.Entity<ClassStudent>()
                .HasOne(cs => cs.Class)
                .WithMany(c => c.ClassStudents)
                .HasForeignKey(cs => cs.ClassId);

            modelBuilder.Entity<ClassStudent>()
                .HasOne(cs => cs.Student)
                .WithMany()
                .HasForeignKey(cs => cs.StudentId);

            modelBuilder.Entity<ClassStudent>()
                .HasIndex(cs => new { cs.ClassId, cs.StudentId })
                .IsUnique();

            modelBuilder.Entity<Student>()
                .HasIndex(s => s.StudentNumber)
                .IsUnique();

            modelBuilder.Entity<Student>()
                .HasIndex(s => s.Username)
                .IsUnique();

            modelBuilder.Entity<UsageLog>()
                .HasIndex(log => new { log.PcName, log.Timestamp });

            modelBuilder.Entity<ComputerStatusHistory>()
                .HasOne(h => h.Computer)
                .WithMany()
                .HasForeignKey(h => h.ComputerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ComputerStatusHistory>()
                .HasIndex(h => new { h.ComputerId, h.ChangedAt });

            modelBuilder.Entity<IdleInterval>()
                .HasIndex(interval => new { interval.ConnectionId, interval.StartedAt });

            modelBuilder.Entity<IdleInterval>()
                .HasIndex(interval => new { interval.StudentId, interval.StartedAt });

            modelBuilder.Entity<ActivityEvent>()
                .HasIndex(activity => new { activity.PcName, activity.Timestamp });

            modelBuilder.Entity<ActivityEvent>()
                .HasIndex(activity => new { activity.StudentId, activity.Timestamp });

            modelBuilder.Entity<LabSession>()
                .HasIndex(session => new { session.StudentId, session.StartTime });

            modelBuilder.Entity<LabSession>()
                .HasIndex(session => session.StudentId)
                .IsUnique()
                .HasFilter("\"IsActive\" = 1");

            modelBuilder.Entity<LabSession>()
                .HasIndex(session => session.ComputerId)
                .IsUnique()
                .HasFilter("\"IsActive\" = 1 AND \"ComputerId\" IS NOT NULL");

            modelBuilder.Entity<Computer>()
                .Property(computer => computer.LaboratoryStation)
                .UseCollation("NOCASE");

            modelBuilder.Entity<Computer>()
                .HasIndex(computer => computer.LaboratoryStation)
                .IsUnique();

            modelBuilder.Entity<Computer>()
                .HasIndex(computer => computer.AssignedTo)
                .IsUnique()
                .HasFilter("\"AssignedTo\" IS NOT NULL");

            modelBuilder.Entity<RemoteCommandLog>()
                .HasIndex(log => new { log.TeacherId, log.StudentId, log.Timestamp });

            modelBuilder.Entity<MonitoringAlert>()
                .HasIndex(alert => new { alert.StudentId, alert.DedupeKey, alert.CreatedAt });

            modelBuilder.Entity<MonitoringAlert>()
                .HasIndex(alert => new { alert.StudentId, alert.GroupKey, alert.LastSeenAt });

            modelBuilder.Entity<BrowserMonitoringRecord>()
                .HasIndex(record => new { record.StudentId, record.Timestamp });

            modelBuilder.Entity<BrowserMonitoringRecord>()
                .HasIndex(record => new { record.PcName, record.Timestamp });

            // Relationships matching pro CRUD model
            modelBuilder.Entity<Class>()
                .HasOne(c => c.Teacher)
                .WithMany(t => t.Classes)
                .HasForeignKey(c => c.TeacherId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Class>()
                .HasIndex(c => new { c.ClassName, c.AcademicYear });

            modelBuilder.Entity<Student>()
                .HasOne(s => s.Class)
                .WithMany(c => c.Students)
                .HasForeignKey(s => s.ClassId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Student>()
                .HasOne(s => s.Adviser)
                .WithMany(t => t.Students)
                .HasForeignKey(s => s.AdviserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Seed roles
            modelBuilder.Entity<Role>().HasData(
                new Role { RoleId = 1, Name = "Administrator", Description = "Full system control" },
                new Role { RoleId = 2, Name = "Teacher", Description = "Laboratory session control" },
                new Role { RoleId = 3, Name = "Student", Description = "Restricted usage" });

            // Seed default session rule
            modelBuilder.Entity<SessionRule>().HasData(new SessionRule
            {
                SessionRuleId = 1,
                Name = "Default 60-minute session",
                MaxDurationMinutes = 60,
                AllowPause = true,
                AllowRemoteControl = true,
                IsDefault = true,
                IsActive = true,
                CreatedAt = new DateTime(2026, 8, 29, 0, 0, 0, DateTimeKind.Utc)
            });
        }
    }
}
