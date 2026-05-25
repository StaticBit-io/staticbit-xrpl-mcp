using System;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Helpers that convert a free-form ledger index string used by MCP tools
/// (<c>"validated"</c>, <c>"current"</c>, <c>"closed"</c>, or a numeric sequence)
/// to the strongly-typed <see cref="LedgerIndex"/> object expected by the Xrpl SDK.
/// </summary>
public static class LedgerIndexParser
{
    /// <summary>
    /// Parses the supplied value. <c>null</c>/empty defaults to <c>validated</c>.
    /// </summary>
    public static LedgerIndex Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new LedgerIndex(LedgerIndexType.Validated);
        }

        string trimmed = value.Trim();

        if (string.Equals(trimmed, "validated", StringComparison.OrdinalIgnoreCase))
        {
            return new LedgerIndex(LedgerIndexType.Validated);
        }

        if (string.Equals(trimmed, "current", StringComparison.OrdinalIgnoreCase))
        {
            return new LedgerIndex(LedgerIndexType.Current);
        }

        if (string.Equals(trimmed, "closed", StringComparison.OrdinalIgnoreCase))
        {
            return new LedgerIndex(LedgerIndexType.Closed);
        }

        if (uint.TryParse(trimmed, out uint numeric))
        {
            return new LedgerIndex(numeric);
        }

        throw new ArgumentException(
            $"Invalid ledger_index value '{value}'. Use 'validated', 'current', 'closed', or a numeric ledger sequence.",
            nameof(value));
    }
}
