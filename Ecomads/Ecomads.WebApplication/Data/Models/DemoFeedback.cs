using System.ComponentModel.DataAnnotations;

namespace Ecomads.WebApplication.Data.Models;

public class DemoFeedback
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    [Required]
    public string GeneralComment { get; set; } = null!;

    [Required]
    public string AnswersJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; }
}
