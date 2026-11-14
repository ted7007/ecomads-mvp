using System.Security.Cryptography;
using System.Text.Json;

namespace Ecomads.WebApplication.Services;

public sealed class RegistrationDocuments
{
    public string Privacy { get; } = Read("Legal.privacy.txt");
    public string Consent { get; } = Read("Legal.consent.txt");
    public string Version { get; }

    public RegistrationDocuments()
    {
        // Bind the form to the exact pair of documents served to the browser.
        Version = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { Privacy, Consent })));
    }

    private static string Read(string resourceName)
    {
        using var stream = typeof(RegistrationDocuments).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing registration document: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }
}
