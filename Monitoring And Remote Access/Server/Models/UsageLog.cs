using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class UsageLog
    {
        [Key]
        public int UsageLogId { get; set; }

        public int? StudentId { get; set; }
        public Student? Student { get; set; }

        [StringLength(100)]
        public string PcName { get; set; } = string.Empty;

        [StringLength(300)]
        public string AppName { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
