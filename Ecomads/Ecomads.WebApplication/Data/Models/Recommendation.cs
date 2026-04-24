using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Ecomads.WebApplication.Data.Models;

public class Recommendation
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid CampaignId { get; set; }
    
    [ForeignKey("CampaignId")]
    public virtual Compaign Campaign { get; set; }
    
    [Required]
    public DateTime CreatedAt { get; set; }

    [Required]
    public string Goal { get; set; }

    // Полный текст запроса к LLM
    [Required]
    public string Prompt { get; set; }

    // Полный неразобранный ответ LLM
    [Required]
    public string FullResponse { get; set; }

    // Распарсенные части ответа
    public string Problem { get; set; } = string.Empty;
    
    public string RecommendationText { get; set; } = string.Empty;
    
    public string ExpectedEffect { get; set; } = string.Empty;
    
    // Дополнительные данные в формате JSON
    // Гибкое поле для хранения дополнительных структурированных данных
    public string AdditionalData { get; set; } = "{}";
    
    // Метаданные о запросе (модель, температура и т.д.)
    public string RequestMetadata { get; set; } = "{}";
    
    // Статус рекомендации (новая, просмотрена, применена, отклонена и т.д.)
    public string Status { get; set; } = "новая";
    
    // Дата и время обновления статуса
    public DateTime? StatusUpdatedAt { get; set; }
    
    // Комментарий пользователя к рекомендации
    public string UserComment { get; set; } = string.Empty;

    // Вспомогательные методы для работы с JSON-полями
    public T? GetAdditionalData<T>() where T : class
    {
        if (string.IsNullOrEmpty(AdditionalData))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(AdditionalData);
        }
        catch
        {
            return default;
        }
    }

    public void SetAdditionalData<T>(T data) where T : class
    {
        AdditionalData = data != null 
            ? JsonSerializer.Serialize(data)
            : "{}";
    }

    public T? GetRequestMetadata<T>() where T : class
    {
        if (string.IsNullOrEmpty(RequestMetadata))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(RequestMetadata);
        }
        catch
        {
            return default;
        }
    }

    public void SetRequestMetadata<T>(T metadata) where T : class
    {
        RequestMetadata = metadata != null 
            ? JsonSerializer.Serialize(metadata)
            : "{}";
    }
}