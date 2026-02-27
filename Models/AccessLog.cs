using System.ComponentModel.DataAnnotations;

namespace isp_report_api.Models;

public class AccessLog
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(2048)]
    public string? Url { get; set; }
}

