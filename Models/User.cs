using System.ComponentModel.DataAnnotations;

namespace isp_report_api.Models;

public class User
{
    [Key] public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [MaxLength(100)] public string? Name { get; set; }

    [MaxLength(255)] public string? PasswordHash { get; set; }

    public bool EmailVerified { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLoginAt { get; set; }

    public bool IsActive { get; set; } = true;
}