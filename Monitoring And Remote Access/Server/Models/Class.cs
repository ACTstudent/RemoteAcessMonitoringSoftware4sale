using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models
{
    public class Class
    {
        [Key]
        public int ClassId { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Section Name")]
        public string ClassName { get; set; } = string.Empty;

        [StringLength(50)]
        public string Section { get; set; } = string.Empty;

        [StringLength(100)]
        public string Subject { get; set; } = string.Empty;

        [StringLength(50)]
        public string GradeLevel { get; set; } = string.Empty;

        [StringLength(100)]
        public string Schedule { get; set; } = string.Empty;

        [Display(Name = "Academic Year")]
        public string AcademicYear { get; set; } = "2026-2027";

        public string Status { get; set; } = "Active";

        public bool IsArchived { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Foreign Key -> Teacher (Adviser / Instructor)
        [Display(Name = "Teacher / Adviser")]
        public int? TeacherId { get; set; }

        [ForeignKey("TeacherId")]
        public Teacher? Teacher { get; set; }

        // Navigation: Direct collection of enrolled students
        public ICollection<Student> Students { get; set; } = new List<Student>();

        public ICollection<ClassStudent> ClassStudents { get; set; } = new List<ClassStudent>();
    }
}