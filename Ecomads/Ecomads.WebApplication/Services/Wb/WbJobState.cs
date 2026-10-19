using System.Text.Json;

namespace Ecomads.WebApplication.Services.Wb;

public static class WbJobState
{
    public static bool SameJson(string? left, string? right)
    {
        if (left == right) return true;
        if (left == null || right == null) return false;
        using var first = JsonDocument.Parse(left);
        using var second = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(first.RootElement, second.RootElement);
    }
}
