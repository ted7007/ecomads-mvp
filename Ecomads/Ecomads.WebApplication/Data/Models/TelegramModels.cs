namespace Ecomads.WebApplication.Data.Models;

public sealed class TelegramLinkCode
{
    public Guid Id { get; set; }
    public Guid SellerId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}

public sealed class TelegramChat
{
    public Guid Id { get; set; }
    public Guid SellerId { get; set; }
    public long ChatId { get; set; }
    public string? DisplayName { get; set; }
    public DateTime LinkedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TelegramDelivery
{
    public Guid Id { get; set; }
    public Guid StoreId { get; set; }
    public Guid ChatId { get; set; }
    public DateOnly ReportDate { get; set; }
    public string Status { get; set; } = "pending";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? ErrorCode { get; set; }
}

public sealed class TelegramBotState
{
    public int Id { get; set; } = 1;
    public long NextUpdateId { get; set; }
}
