using System.ComponentModel.DataAnnotations;

namespace Server.Models
{
    public class LanConfiguration
    {
        [Key]
        public int LanConfigurationId { get; set; }

        [StringLength(50)]
        public string ServerAddress { get; set; } = string.Empty;

        public int ServerPort { get; set; } = 5000;

        [StringLength(50)]
        public string? DhcpRangeStart { get; set; }

        [StringLength(50)]
        public string? DhcpRangeEnd { get; set; }

        [StringLength(50)]
        public string? Gateway { get; set; }

        [StringLength(50)]
        public string? DnsServer { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
