using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ecomads.WebApplication.Data.Models;

public class Store
{
    [Key]
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(255)]
    public string? Description { get; set; }
    
    // Ссылка на маркетплейс (Wildberries, Ozon и т.д.)
    [MaxLength(50)]
    public string Marketplace { get; set; } = "Wildberries";
    
    // Внешний идентификатор магазина на маркетплейсе
    [MaxLength(100)]
    public string? ExternalId { get; set; }
    
    // API ключ для интеграции с маркетплейсом
    public string? ApiKey { get; set; }

    [MaxLength(4)]
    public string? TokenLastFour { get; set; }

    public DateTime? TokenExpiresAtUtc { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? LastSyncAt { get; set; }
    public DateTime? CampaignsRefreshedAtUtc { get; set; }

    public string JamStatus { get; set; } = "unknown";
    public DateTime? JamCheckedAtUtc { get; set; }
    
    // Связь с продавцом
    [Required]
    public Guid SellerId { get; set; }
    
    [ForeignKey("SellerId")]
    public virtual Seller Seller { get; set; } = null!;
    
    // Навигационные свойства
    public virtual ICollection<Campaign> Campaigns { get; set; } = new List<Campaign>();
}
