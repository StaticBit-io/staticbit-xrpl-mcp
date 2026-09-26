using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace StaticBit.Xrpl.Mcp.Signer.Policy;

/// <summary>
/// Deterministic pre-sign gate. Transactions that hand control of an account (or all of its
/// funds) to someone else in a single signature are refused unless the operator has opted in
/// through <c>XRPL_SIGNER_ALLOW_TX_TYPES</c>. The opt-in lives in the signer's process
/// environment, which the model driving the MCP session cannot change.
/// </summary>
public sealed class SigningPolicy
{
    /// <summary>Policy token for <c>AccountSet</c> with <c>SetFlag = asfDisableMaster</c>.</summary>
    public const string DisableMaster = "DisableMaster";

    private const int AsfDisableMaster = 4;

    /// <summary>Every policy token the signer understands, in canonical spelling.</summary>
    public static IReadOnlyList<string> HighRiskKinds { get; } = new[]
    {
        "SetRegularKey",
        "SignerListSet",
        "AccountDelete",
        "DelegateSet",
        DisableMaster,
    };

    private readonly HashSet<string> _allowed;

    /// <summary>
    /// Builds the policy from the operator's allow-list. Tokens are matched case-insensitively;
    /// an unknown token is rejected so a typo cannot silently leave a kind blocked or suggest
    /// that something is allowed when it is not.
    /// </summary>
    public SigningPolicy(IEnumerable<string> allowedHighRiskKinds)
    {
        ArgumentNullException.ThrowIfNull(allowedHighRiskKinds);

        _allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in allowedHighRiskKinds)
        {
            string token = raw.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            string? canonical = HighRiskKinds.FirstOrDefault(
                k => string.Equals(k, token, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                throw new ArgumentException(
                    $"Unknown high-risk transaction kind '{token}' in XRPL_SIGNER_ALLOW_TX_TYPES. " +
                    $"Known kinds: {string.Join(", ", HighRiskKinds)}.",
                    nameof(allowedHighRiskKinds));
            }
            _allowed.Add(canonical);
        }
    }

    /// <summary>High-risk kinds the operator has allowed, in canonical spelling.</summary>
    public IReadOnlyCollection<string> AllowedKinds => _allowed;

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when <paramref name="tx"/> (or any inner
    /// transaction of a <c>Batch</c>) is a high-risk kind the operator has not allowed.
    /// </summary>
    public void EnsureAllowed(IReadOnlyDictionary<string, object> tx)
    {
        ArgumentNullException.ThrowIfNull(tx);

        foreach (string kind in FindHighRiskKinds(tx))
        {
            if (!_allowed.Contains(kind))
            {
                throw new InvalidOperationException(
                    $"Refusing to sign: '{kind}' can hand control of the account to another key or " +
                    "account in one signature. The signer only signs it when the operator lists " +
                    $"'{kind}' in the XRPL_SIGNER_ALLOW_TX_TYPES environment variable of the signer " +
                    "process. This cannot be enabled from the chat.");
            }
        }
    }

    /// <summary>High-risk kinds present in <paramref name="tx"/>, including Batch inner transactions.</summary>
    public static IEnumerable<string> FindHighRiskKinds(IReadOnlyDictionary<string, object> tx)
    {
        ArgumentNullException.ThrowIfNull(tx);

        string? own = Classify(ReadString(tx, "TransactionType"), ReadInt64(tx, "SetFlag"));
        if (own is not null)
        {
            yield return own;
        }

        if (!tx.TryGetValue("RawTransactions", out object? raw) || raw is not JsonElement { ValueKind: JsonValueKind.Array } inner)
        {
            yield break;
        }

        foreach (JsonElement wrapper in inner.EnumerateArray())
        {
            if (wrapper.ValueKind != JsonValueKind.Object
                || !wrapper.TryGetProperty("RawTransaction", out JsonElement innerTx)
                || innerTx.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? innerType = innerTx.TryGetProperty("TransactionType", out JsonElement t) ? AsString(t) : null;
            long? innerSetFlag = innerTx.TryGetProperty("SetFlag", out JsonElement f) ? AsInt64(f) : null;
            string? innerKind = Classify(innerType, innerSetFlag);
            if (innerKind is not null)
            {
                yield return innerKind;
            }
        }
    }

    private static string? Classify(string? transactionType, long? setFlag)
    {
        if (transactionType is null)
        {
            return null;
        }

        if (string.Equals(transactionType, "AccountSet", StringComparison.Ordinal))
        {
            return setFlag == AsfDisableMaster ? DisableMaster : null;
        }

        return HighRiskKinds.FirstOrDefault(k => string.Equals(k, transactionType, StringComparison.Ordinal));
    }

    private static string? ReadString(IReadOnlyDictionary<string, object> tx, string field) =>
        tx.TryGetValue(field, out object? value) ? value switch
        {
            string s => s,
            JsonElement e => AsString(e),
            _ => null,
        } : null;

    private static long? ReadInt64(IReadOnlyDictionary<string, object> tx, string field) =>
        tx.TryGetValue(field, out object? value) ? value switch
        {
            JsonElement e => AsInt64(e),
            long l => l,
            int i => i,
            uint u => u,
            _ => null,
        } : null;

    private static string? AsString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static long? AsInt64(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number when element.TryGetInt64(out long n) => n,
        JsonValueKind.String when long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long s) => s,
        _ => null,
    };
}
