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
        public DbSet<LabSession> LabSessions { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<Permission> Permissions { get; set; } = null!;
        public DbSet<RestrictionRule> RestrictionRules { get; set; } = null!;
        public DbSet<BlacklistItem> BlacklistItems { get; set; } = null!;
        public DbSet<SessionRule> SessionRules { get; set; } = null!;
        public DbSet<LanConfiguration> LanConfigurations { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<SystemLog> SystemLogs { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<UsageLog> UsageLogs { get; set; } = null!;
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

            // Relationships matching pro CRUD model
            modelBuilder.Entity<Class>()
                .HasOne(c => c.Teacher)
                .WithMany(t => t.Classes)
                .HasForeignKey(c => c.TeacherId)
                .OnDelete(DeleteBehavior.SetNull);

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
                IsActive = true
            });
        }
    }
}
