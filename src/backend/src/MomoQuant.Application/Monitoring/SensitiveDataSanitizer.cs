using System.Text.Json;
using System.Text.RegularExpressions;

namespace MomoQuant.Application.Monitoring;

public static partial class SensitiveDataSanitizer
{
    private static readonly string[] SensitivePropertyNames =
    [
        "password", "secret", "token", "apikey", "api_key", "authorization", "jwt", "privatekey", "private_key"
    ];

    public static string? SanitizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteSanitizedElement(writer, document.RootElement);
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return RedactInlineSecrets(json);
        }
    }

    private static void WriteSanitizedElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitiveProperty(property.Name))
                    {
                        writer.WriteStringValue("[REDACTED]");
                    }
                    else
                    {
                        WriteSanitizedElement(writer, property.Value);
                    }
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteSanitizedElement(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSensitiveProperty(string propertyName)
    {
        var normalized = propertyName.Replace("_", string.Empty, StringComparison.Ordinal);
        return SensitivePropertyNames.Any(name =>
            normalized.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string RedactInlineSecrets(string value) =>
        SecretPattern().Replace(value, "[REDACTED]");

    [GeneratedRegex(@"(?i)(password|secret|token|apikey|api_key|authorization|jwt)\s*[:=]\s*""?[^"",\s}]+""?", RegexOptions.Compiled)]
    private static partial Regex SecretPattern();
}
