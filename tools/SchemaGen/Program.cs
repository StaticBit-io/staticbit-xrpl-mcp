using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Core.Tools;
using StaticBit.Xrpl.Mcp.Signer.Tools;

namespace StaticBit.Xrpl.Mcp.SchemaGen;

/// <summary>
/// Walks Core + Signer assemblies for <c>[McpServerToolType]</c> classes, extracts
/// every <c>[McpServerTool]</c> method's name, description, and parameter schema,
/// and emits a single JSON document to <c>docs/tools-schema.json</c>.
///
/// Format mirrors the MCP <c>tools/list</c> response shape — each tool has
/// <c>name</c>, <c>description</c>, and a JSON-Schema <c>inputSchema</c>. This
/// document is published as a stable reference for third-party agents that want
/// to know what's available without having to install the plugin or call the
/// MCP server.
///
/// Run from the repo root:
///   dotnet run --project tools/SchemaGen -- docs/tools-schema.json
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep '/' and '<' as-is
    };

    public static int Main(string[] args)
    {
        string outputPath = args.Length > 0
            ? args[0]
            : Path.Combine("docs", "tools-schema.json");

        Assembly[] assemblies =
        {
            typeof(LedgerTools).Assembly,   // StaticBit.Xrpl.Mcp.Core
            typeof(WalletTools).Assembly,   // StaticBit.Xrpl.Mcp.Signer
        };

        List<JsonObject> tools = new List<JsonObject>();
        foreach (Assembly assembly in assemblies)
        {
            foreach (Type toolType in FindToolTypes(assembly))
            {
                foreach (MethodInfo method in FindToolMethods(toolType))
                {
                    tools.Add(BuildToolDescriptor(toolType, method));
                }
            }
        }

        // Stable ordering — alphabetical by tool name. Makes the file diff-friendly.
        tools = tools.OrderBy(t => (string?)t["name"], StringComparer.Ordinal).ToList();

        JsonObject root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft-07/schema#",
            ["title"] = "StaticBit XRPL MCP — tool catalogue",
            ["description"] =
                "Auto-generated from [McpServerTool] reflection. Each entry mirrors the MCP " +
                "'tools/list' response shape: name + description + JSON-Schema inputSchema. " +
                "Regenerate via: dotnet run --project tools/SchemaGen -- docs/tools-schema.json",
            ["generatedFromAssemblies"] = new JsonArray(
                assemblies.Select(a => (JsonNode?)a.GetName().Name).ToArray()),
            ["toolCount"] = tools.Count,
            ["tools"] = new JsonArray(tools.Cast<JsonNode?>().ToArray()),
        };

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(outputPath, root.ToJsonString(JsonOptions));

        Console.WriteLine($"Emitted {tools.Count} tools → {outputPath}");
        return 0;
    }

    private static IEnumerable<Type> FindToolTypes(Assembly assembly)
    {
        foreach (Type type in assembly.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<MethodInfo> FindToolMethods(Type toolType)
    {
        foreach (MethodInfo method in toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            {
                yield return method;
            }
        }
    }

    private static JsonObject BuildToolDescriptor(Type toolType, MethodInfo method)
    {
        McpServerToolAttribute toolAttr = method.GetCustomAttribute<McpServerToolAttribute>()!;
        string toolName = toolAttr.Name ?? method.Name;
        string toolDescription = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;

        JsonObject properties = new JsonObject();
        JsonArray required = new JsonArray();

        foreach (ParameterInfo param in method.GetParameters())
        {
            // CancellationToken is a runtime concern — the MCP client never supplies it.
            if (param.ParameterType == typeof(CancellationToken)) continue;

            string paramName = param.Name ?? "_arg";
            JsonObject paramSchema = BuildParameterSchema(param);
            properties[paramName] = paramSchema;

            if (!param.IsOptional)
            {
                required.Add(paramName);
            }
        }

        JsonObject inputSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };
        if (required.Count > 0) inputSchema["required"] = required;
        inputSchema["additionalProperties"] = false;

        return new JsonObject
        {
            ["name"] = toolName,
            ["description"] = toolDescription,
            ["sourceClass"] = toolType.FullName ?? toolType.Name,
            ["inputSchema"] = inputSchema,
        };
    }

    private static JsonObject BuildParameterSchema(ParameterInfo param)
    {
        JsonObject schema = new JsonObject();

        Type paramType = Nullable.GetUnderlyingType(param.ParameterType) ?? param.ParameterType;
        (string jsonType, string? format) = MapClrToJsonSchema(paramType);
        schema["type"] = jsonType;
        if (format is not null) schema["format"] = format;

        string? desc = param.GetCustomAttribute<DescriptionAttribute>()?.Description;
        if (!string.IsNullOrEmpty(desc)) schema["description"] = desc;

        if (param.IsOptional && param.HasDefaultValue && param.DefaultValue is not null
            && param.DefaultValue is not DBNull)
        {
            schema["default"] = ToJsonNode(param.DefaultValue);
        }

        // Nullable reference / value types accept null in addition to their primary type.
        bool isNullable = param.ParameterType == typeof(string)
            ? IsNullableStringParam(param)
            : Nullable.GetUnderlyingType(param.ParameterType) is not null;
        if (isNullable)
        {
            schema["type"] = new JsonArray { jsonType, "null" };
        }

        return schema;
    }

    private static (string JsonType, string? Format) MapClrToJsonSchema(Type t)
    {
        if (t == typeof(string)) return ("string", null);
        if (t == typeof(bool)) return ("boolean", null);
        if (t == typeof(byte) || t == typeof(sbyte)
            || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint)
            || t == typeof(long) || t == typeof(ulong))
            return ("integer", t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(byte)
                ? "uint"
                : null);
        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
            return ("number", null);
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
            return ("string", "date-time");
        if (t == typeof(Guid)) return ("string", "uuid");
        if (t.IsArray || (t.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(t)))
            return ("array", null);
        // Fallback — anything else (custom DTO) is treated as opaque JSON object.
        return ("object", null);
    }

    private static JsonNode? ToJsonNode(object value)
    {
        return value switch
        {
            null => null,
            string s => JsonValue.Create(s),
            bool b => JsonValue.Create(b),
            int i => JsonValue.Create(i),
            uint u => JsonValue.Create(u),
            long l => JsonValue.Create(l),
            ulong ul => JsonValue.Create(ul),
            short sh => JsonValue.Create(sh),
            ushort ush => JsonValue.Create(ush),
            byte by => JsonValue.Create(by),
            float f => JsonValue.Create(f),
            double d => JsonValue.Create(d),
            decimal dec => JsonValue.Create(dec),
            _ => JsonValue.Create(value.ToString()),
        };
    }

    /// <summary>
    /// Best-effort detection of a nullable reference type parameter (string? vs string).
    /// .NET reflects nullability via NullableContextAttribute / NullableAttribute byte arrays;
    /// here we use the new NullabilityInfoContext API (.NET 6+).
    /// </summary>
    private static bool IsNullableStringParam(ParameterInfo param)
    {
        NullabilityInfoContext ctx = new NullabilityInfoContext();
        NullabilityInfo info = ctx.Create(param);
        return info.WriteState == NullabilityState.Nullable
            || info.ReadState == NullabilityState.Nullable;
    }
}
