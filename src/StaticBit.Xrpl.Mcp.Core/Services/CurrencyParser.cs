using System;
using System.Text.Json;
using Xrpl.Models.Common;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Parses an MCP-friendly amount representation into an Xrpl <see cref="Currency"/> object.
/// Accepts either:
/// <list type="bullet">
/// <item>A pure number string — interpreted as XRP drops (1 XRP = 1_000_000 drops).</item>
/// <item>A JSON object string <c>{"value":"100.50","currency":"USD","issuer":"r..."}</c> — interpreted as an issued token amount.</item>
/// </list>
/// </summary>
public static class CurrencyParser
{
    /// <summary>
    /// Parses <paramref name="amount"/> into a Currency. Throws on malformed input.
    /// </summary>
    public static Currency Parse(string amount)
    {
        if (string.IsNullOrWhiteSpace(amount))
        {
            throw new ArgumentException("Amount is required.", nameof(amount));
        }

        string trimmed = amount.Trim();

        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return ParseJsonObject(trimmed);
        }

        if (!IsDigitsOnly(trimmed))
        {
            throw new ArgumentException(
                $"Amount '{amount}' is neither a numeric drops string nor a JSON token object. " +
                "Use a positive integer for XRP drops, or {\"value\":\"...\",\"currency\":\"...\",\"issuer\":\"...\"} for tokens.",
                nameof(amount));
        }

        return new Currency
        {
            CurrencyCode = "XRP",
            Value = trimmed,
        };
    }

    private static Currency ParseJsonObject(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        string? value = GetString(root, "value");
        string? currency = GetString(root, "currency");
        string? issuer = GetString(root, "issuer");

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Token amount must include 'value'.", nameof(json));
        }
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Token amount must include 'currency'.", nameof(json));
        }
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new ArgumentException("Token amount must include 'issuer'.", nameof(json));
        }

        return new Currency
        {
            Value = value!,
            CurrencyCode = currency!,
            Issuer = issuer!,
        };
    }

    private static string? GetString(JsonElement root, string name)
    {
        foreach (JsonProperty prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
            }
        }
        return null;
    }

    private static bool IsDigitsOnly(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsDigit(value[i])) return false;
        }
        return value.Length > 0;
    }
}
