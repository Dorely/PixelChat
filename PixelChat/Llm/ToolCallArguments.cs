using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PixelChat.Llm;

public static class ToolCallArguments
{
    public static AIFunctionArguments Create(IDictionary<string, object?>? arguments, string? argumentsJson = null) =>
        new(ParseObject(argumentsJson) ?? Normalize(arguments));

    public static IDictionary<string, object?>? ParseObjectOrNull(string? argumentsJson) =>
        ParseObject(argumentsJson);

    public static string Serialize(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return "{}";
        return JsonSerializer.Serialize(Normalize(arguments));
    }

    private static Dictionary<string, object?> Normalize(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
            return [];

        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
            normalized[key] = NormalizeValue(value);
        return normalized;
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is null)
            return null;
        if (value is string text && Guid.TryParse(text, out var guid))
            return guid;
        if (value is JsonElement element)
            return NormalizeElement(element);
        if (value is JsonDocument document)
            return NormalizeElement(document.RootElement);

        try
        {
            return NormalizeElement(JsonSerializer.SerializeToElement(value, value.GetType()));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return value;
        }
    }

    private static object? NormalizeElement(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => NormalizeElement(property.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(NormalizeElement).ToList(),
            JsonValueKind.String => NormalizeString(element.GetString()),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.Clone()
        };

    private static object? NormalizeString(string? value) =>
        value is not null && Guid.TryParse(value, out var guid) ? guid : value;

    private static Dictionary<string, object?>? ParseObject(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return null;

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                arguments[property.Name] = NormalizeElement(property.Value);
            return arguments;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
