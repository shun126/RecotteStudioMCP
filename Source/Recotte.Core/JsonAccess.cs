using System.Text.Json.Nodes;

namespace Recotte.Core;

internal static class JsonAccess
{
    internal static bool TryGetString(JsonObject node, string name, out string value)
    {
        value = string.Empty;
        if (node[name] is not JsonValue jsonValue || !jsonValue.TryGetValue(out string? parsedValue) || parsedValue is null)
        {
            return false;
        }

        value = parsedValue;
        return true;
    }

    internal static bool TryGetInt32(JsonObject node, string name, out int value)
    {
        value = default;
        return node[name] is JsonValue jsonValue && jsonValue.TryGetValue(out value);
    }

    internal static bool TryGetDecimal(JsonObject node, string name, out decimal value)
    {
        value = default;
        if (node[name] is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue(out value))
        {
            return true;
        }

        if (jsonValue.TryGetValue(out double doubleValue) && double.IsFinite(doubleValue))
        {
            try
            {
                value = (decimal)doubleValue;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        return false;
    }

    internal static string? GetPropertyString(JsonObject node, string propertyName)
    {
        return node["properties"] is JsonObject properties &&
            properties[propertyName] is JsonObject property &&
            TryGetString(property, "p-value", out string value)
                ? value
                : null;
    }
}
