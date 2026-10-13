using System.Text.Json.Nodes;

namespace RecotteStudio.Core;

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

    internal static bool TryGetDecimal(JsonObject node, string name, out decimal value) => TryGetDecimal(node[name], out value);

    internal static bool TryGetDecimal(JsonNode? node, out decimal value)
    {
        value = default;
        if (node is not JsonValue jsonValue)
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

    /// <summary>Reads the [x, y, width, height] Bounds entry of a property bag.</summary>
    internal static bool TryGetBounds(JsonObject properties, out decimal x, out decimal y, out decimal width, out decimal height)
    {
        x = y = width = height = default;
        if (properties["Bounds"] is not JsonObject bounds || bounds["p-value"] is not JsonArray values || values.Count != 4)
        {
            return false;
        }

        return TryGetDecimal(values[0], out x) && TryGetDecimal(values[1], out y) &&
            TryGetDecimal(values[2], out width) && TryGetDecimal(values[3], out height);
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
