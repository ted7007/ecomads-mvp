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
    public List<Choice> choices { get; set; }

    public class Choice
    {
        public Message message { get; set; }
    }

    public class Message
    {
        public string content { get; set; }
    }
}
