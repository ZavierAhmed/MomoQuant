using System.Text.Json;
using System.Text.Json.Serialization;

namespace MomoQuant.IntegrationTests;

internal static class IntegrationTestJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
