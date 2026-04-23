using System.Net.Http.Json;

namespace TestConsoleApp;

public class LlmClient
{
    private readonly HttpClient _http;

    public LlmClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> GetRecommendation(string prompt)
    {
        var request = new
        {
            model = "gpt-4o-mini", // или что даст bothub
            messages = new[]
            {
                new { role = "system", content = "Ты эксперт по рекламе Wildberries." },
                new { role = "user", content = prompt }
            },
            temperature = 0.7
        };

        var response = await _http.PostAsJsonAsync("/v1/chat/completions", request);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<LlmResponse>();

        return json.choices[0].message.content;
    }
}

public class LlmResponse
{
    public Choice[] choices { get; set; }

    public class Choice
    {
        public Message message { get; set; }
    }

    public class Message
    {
        public string content { get; set; }
    }
}