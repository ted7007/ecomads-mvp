using System.ComponentModel.DataAnnotations;

namespace Ecomads.WebApplication.Data.Models;

public sealed class PilotApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string Contact { get; set; } = "";
    [MaxLength(20)] public string? Stores { get; set; }
    [MaxLength(20)] public string? Budget { get; set; }
    [MaxLength(20)] public string? Jam { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "new";
    [MaxLength(2000)] public string Note { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime ContactConsentAtUtc { get; set; }
}
