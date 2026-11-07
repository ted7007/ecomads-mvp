using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ecomads.WebApplication.Auth;

public sealed record RegistrationAttribution(string Source, DateTime CapturedAtUtc)
{
    private static readonly Regex SourcePattern = new(@"\A[A-Za-z0-9._-]{1,128}\z", RegexOptions.CultureInvariant);

    public static RegistrationAttribution? Parse(JsonElement? value, DateTime now)
    {
        if (value is not { ValueKind: JsonValueKind.Object } attribution ||
            attribution.EnumerateObject().Count(x => x.NameEquals("utm_source")) != 1 ||
            attribution.EnumerateObject().Count(x => x.NameEquals("captured_at_utc")) != 1 ||
            !attribution.TryGetProperty("utm_source", out var source) || source.ValueKind != JsonValueKind.String ||
            !attribution.TryGetProperty("captured_at_utc", out var captured) || captured.ValueKind != JsonValueKind.String)
            return null;

        var sourceText = source.GetString()!;
        if (sourceText.Length > 128 || !SourcePattern.IsMatch(sourceText) ||
            !captured.TryGetDateTime(out var capturedAt) || capturedAt.Kind != DateTimeKind.Utc ||
            capturedAt > now || capturedAt <= now.AddDays(-30))
            return null;

        return new RegistrationAttribution(sourceText, capturedAt);
    }
}
