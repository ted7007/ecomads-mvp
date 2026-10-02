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
    public int? WbStatus { get; set; }

    public DateTime? LastSeenAt { get; set; }
    public DateTime? WbCreatedAtUtc { get; set; }
    public DateTime? WbStartedAtUtc { get; set; }
    public DateTime? WbDeletedAtUtc { get; set; }
    public DateTime? WbUpdatedAtUtc { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Связь с магазином
    [Required]
    public Guid StoreId { get; set; }
    
    [ForeignKey("StoreId")]
    public virtual Store Store { get; set; } = null!;
}
