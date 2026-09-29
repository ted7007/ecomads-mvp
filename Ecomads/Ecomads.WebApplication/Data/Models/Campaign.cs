using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ecomads.WebApplication.Data.Models;

public class Campaign
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string WbCampaignId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime? LastSeenAt { get; set; }
    
    [MaxLength(255)]
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? StartDate { get; set; }
    
    public DateTime? EndDate { get; set; }
    
    // Бюджет кампании
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Budget { get; set; }
    
    // Связь с магазином
    [Required]
    public Guid StoreId { get; set; }
    
    [ForeignKey("StoreId")]
    public virtual Store Store { get; set; } = null!;
}
