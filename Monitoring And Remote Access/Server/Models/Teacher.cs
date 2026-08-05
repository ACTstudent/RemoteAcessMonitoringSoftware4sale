using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class Teacher
    {
        [Key]
        public int TeacherId { get; set; }

        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        [StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [StringLength(50)]
        public string ContactNumber { get; set; } = string.Empty;

        public string Status { get; set; } = "Active";

        public ICollection<LabSession> LabSessions { get; set; } = new List<LabSession>();
    }
}
