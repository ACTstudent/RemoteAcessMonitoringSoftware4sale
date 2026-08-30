using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models
{
    public class Student
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(20)]
        public string StudentNumber { get; set; } = string.Empty;

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        private string _fullName = string.Empty;

        [Required]
        [StringLength(100)]
        public string FullName
        {
            get => !string.IsNullOrWhiteSpace(FirstName) || !string.IsNullOrWhiteSpace(LastName)
                ? $"{FirstName} {LastName}".Trim()
                : _fullName;
            set => _fullName = value;
        }

        [Required]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public string Status { get; set; } = "Active";
        public int FailedLoginAttempts { get; set; }
        public DateTime? LockoutEndUtc { get; set; }

        [Display(Name = "Grade / Section")]
        public string GradeSection { get; set; } = string.Empty;

        // Foreign Key -> Class
        public int? ClassId { get; set; }

        [ForeignKey("ClassId")]
        public Class? Class { get; set; }

        // Foreign Key -> Teacher (Adviser)
        public int? AdviserId { get; set; }

        [ForeignKey("AdviserId")]
        public Teacher? Adviser { get; set; }

        public ICollection<LabSession> LabSessions { get; set; } = new List<LabSession>();
    }
}
