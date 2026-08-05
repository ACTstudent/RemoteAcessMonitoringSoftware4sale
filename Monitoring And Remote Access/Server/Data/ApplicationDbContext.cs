using Microsoft.AspNetCore.Identity;
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var hasher = new PasswordHasher<object>();

            modelBuilder.Entity<Role>()
                .HasMany(r => r.Permissions)
                .WithMany(p => p.Roles)
                .UsingEntity(j => j.ToTable("RolePermissions"));

            // Seed initial Admin user
            modelBuilder.Entity<Admin>().HasData(new Admin
            {
                Id = 1,
                Username = "admin",
                PasswordHash = hasher.HashPassword(null, "admin123"),
                FullName = "System Administrator"
            });

            // Seed sample Student
            modelBuilder.Entity<Student>().HasData(new Student
            {
                Id = 1,
                StudentNumber = "STU-2026-001",
                FullName = "John Doe",
                Username = "student1",
                PasswordHash = hasher.HashPassword(null, "student123")
            });

            // Seed sample Teacher
            modelBuilder.Entity<Teacher>().HasData(new Teacher
            {
                TeacherId = 1,
                FirstName = "Maria",
                LastName = "Santos",
                Email = "maria.santos@pardo.edu.ph",
                Username = "teacher1",
                PasswordHash = hasher.HashPassword(null, "teacher123"),
                ContactNumber = "09171234567",
                Status = "Active"
            });

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
