using System.Text.Json;
using System.Text.Json.Serialization;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> used to encode tool responses.
/// camelCase casing matches both rippled wire format and MCP client expectations.
/// </summary>
internal static class XrplJson
{
    public static JsonSerializerOptions ToolResponseOptions { get; } = new JsonSerializerOptions
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, ToolResponseOptions);
}
