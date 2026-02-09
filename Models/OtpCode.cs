using System.ComponentModel.DataAnnotations;

namespace isp_report_api.Models;

public class OtpCode
{
    [Key] public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(6)]
    public string Code { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime ExpiresAt { get; set; }
    
    public bool IsUsed { get; set; } = false;
}
