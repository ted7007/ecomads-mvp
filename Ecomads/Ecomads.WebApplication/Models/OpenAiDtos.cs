using System.Text.Json.Serialization;

namespace Ecomads.WebApplication.Models;

public class CampaignAnalyticsDto
{
    public string Name { get; set; }
    public double Spend { get; set; }
    public double Revenue { get; set; }
    public double Drr { get; set; }
    public int Clicks { get; set; }
    public double Ctr { get; set; }
    public List<TopKeywordDto> TopKeywords { get; set; }
    public List<TopKeywordDto> WorstKeywords { get; set; }
}

public class TopKeywordDto
{
    public string Phrase { get; set; }
    public double Spend { get; set; }
    public double Revenue { get; set; }
    public double Drr { get; set; }
}

public class LlmResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("created")]
    public long? Created { get; set; }

    [JsonPropertyName("choices")]
    public List<Choice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public LlmUsage? Usage { get; set; }

    public class Choice
    {
        [JsonPropertyName("message")]
        public Message? Message { get; set; }
    }

    public class Message
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    public class LlmUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int? TotalTokens { get; set; }

        [JsonPropertyName("bothub")]
        public BothubUsage? Bothub { get; set; }
    }

    public class BothubUsage
    {
        [JsonPropertyName("caps")]
        public decimal? Caps { get; set; }
    }
}
