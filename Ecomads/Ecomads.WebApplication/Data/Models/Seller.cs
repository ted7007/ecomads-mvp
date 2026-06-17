using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Ecomads.WebApplication.Data.Models;

public class Seller
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;
    
    // Новые поля для авторизации
    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;
    
    [MaxLength(50)]
    public string? Phone { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? LastLoginAt { get; set; }

    public bool IsDemoUser { get; set; }

    public UserAccessType AccessType { get; set; } = UserAccessType.Regular;

    public DemoAccessStatus DemoStatus { get; set; } = DemoAccessStatus.None;

    public DateTime? DemoStartedAtUtc { get; set; }

    public DateTime? DemoExpiresAtUtc { get; set; }

    public DateTime? DemoFeedbackSubmittedAtUtc { get; set; }

    public DateTime? MvpAccessGrantedAtUtc { get; set; }
    
    // Навигационные свойства
    public virtual ICollection<Store> Stores { get; set; } = new List<Store>();
}
