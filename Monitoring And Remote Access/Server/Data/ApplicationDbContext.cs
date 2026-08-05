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
        public DbSet<Student> Students { get; set; } = null!;
        public DbSet<LabSession> LabSessions { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Seed initial Admin user
            modelBuilder.Entity<Admin>().HasData(new Admin
            {
                Id = 1,
                Username = "admin",
                PasswordHash = "admin123", // For production, use BCrypt or ASP.NET Identity password hashing
                FullName = "System Administrator"
            });

            // Seed sample Student
            modelBuilder.Entity<Student>().HasData(new Student
            {
                Id = 1,
                StudentNumber = "STU-2026-001",
                FullName = "John Doe",
                Username = "student1",
                PasswordHash = "student123"
            });
        }
    }
}
