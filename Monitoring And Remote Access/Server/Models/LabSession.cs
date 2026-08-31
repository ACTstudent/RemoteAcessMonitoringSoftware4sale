using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class LabSession
    {
        [Key]
        public int Id { get; set; }

        public int StudentId { get; set; }
        public Student? Student { get; set; }

        public int? TeacherId { get; set; }
        public Teacher? Teacher { get; set; }

        public int? ComputerId { get; set; }
        public Computer? Computer { get; set; }

        public int? SessionRuleId { get; set; }
        public SessionRule? SessionRule { get; set; }

        [Required]
        [StringLength(50)]
        public string PCName { get; set; } = string.Empty;

        [StringLength(50)]
        public string IPAddress { get; set; } = string.Empty;

        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        public DateTime? PauseTime { get; set; }

        public int AccumulatedPauseSeconds { get; set; }

        public DateTime? EndTime { get; set; }

        public bool IsActive { get; set; } = true;

        [StringLength(20)]
        public string Status { get; set; } = "Running"; // Running | Paused | Ended

        public int? MaxDurationMinutes { get; set; }
    }
}
