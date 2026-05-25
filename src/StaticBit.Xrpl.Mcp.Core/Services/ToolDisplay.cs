using System;
using Xrpl.Models.Common;
using static Xrpl.Models.Common.Common;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Shared formatting helpers for human-readable transaction summaries
/// surfaced to the user in approval prompts.
/// </summary>
public static class ToolDisplay
{
    public static string Truncate(string? address)
    {
        if (string.IsNullOrEmpty(address)) return "<null>";
        return address.Length <= 12
            ? address
            : $"{address.AsSpan(0, 6)}...{address.AsSpan(address.Length - 4, 4)}";
    }

    public static string DescribeAmount(Currency amount)
    {
        if (amount is null) return "<null>";
        return string.Equals(amount.CurrencyCode, "XRP", StringComparison.OrdinalIgnoreCase)
            ? $"{amount.Value} drops XRP"
            : $"{amount.Value} {amount.CurrencyCode} ({Truncate(amount.Issuer)})";
    }

    public static string DescribeAsset(IssuedCurrency asset)
    {
        if (asset is null) return "<null>";
        return string.Equals(asset.Currency, "XRP", StringComparison.OrdinalIgnoreCase)
            ? "XRP"
            : $"{asset.Currency} ({Truncate(asset.Issuer)})";
    }

    public static IssuedCurrency BuildAsset(string currency, string? issuer)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Asset currency is required.", nameof(currency));
        }

        string normalized = currency.Trim();
        bool isXrp = string.Equals(normalized, "XRP", StringComparison.OrdinalIgnoreCase);

        return new IssuedCurrency
        {
            Currency = isXrp ? "XRP" : normalized,
            Issuer = isXrp ? null! : (issuer ?? throw new ArgumentException("Issuer is required for non-XRP assets.", nameof(issuer))),
        };
    }
}
