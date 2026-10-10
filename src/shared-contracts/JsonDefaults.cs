using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseCheck.Contracts;

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static JsonSerializerOptions Create(bool indented = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}