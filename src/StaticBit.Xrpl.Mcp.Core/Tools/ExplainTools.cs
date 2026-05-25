using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.BinaryCodec;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Approval-flow helpers: take a transaction (as blob or JSON) and render a one-line
/// human-readable summary. Pure local operation, no network calls.
/// </summary>
[McpServerToolType]
public sealed class ExplainTools
{
    [McpServerTool(Name = "xrpl_tx_explain")]
    [Description("Converts a transaction into a one-line human summary for approval prompts. Pass EITHER txBlobHex (signed or unsigned canonical hex) OR txJson (the JSON object directly). Returns {transactionType, humanSummary, txJson} — humanSummary is a sentence like 'Payment from rA... to rB...: 10000000 drops XRP. [fee=12 drops, seq=42, LLS=1234]'. Pure local operation — no network calls.")]
    public string Explain(
        [Description("Hex-encoded transaction blob (signed or unsigned). Mutually exclusive with txJson.")] string? txBlobHex = null,
        [Description("Transaction as a JSON object string. Mutually exclusive with txBlobHex.")] string? txJson = null)
    {
        bool hasBlob = !string.IsNullOrWhiteSpace(txBlobHex);
        bool hasJson = !string.IsNullOrWhiteSpace(txJson);
        if (hasBlob == hasJson)
        {
            throw new ArgumentException("Provide exactly one of txBlobHex or txJson.");
        }

        JsonNode tx = hasBlob ? DecodeBlobToNode(txBlobHex!) : ParseJsonToNode(txJson!);

        string txType = tx["TransactionType"]?.GetValue<string>() ?? "<unknown>";
        string summary = TransactionExplainer.Explain(tx);

        JsonObject result = new JsonObject
        {
            ["transactionType"] = txType,
            ["humanSummary"] = summary,
            ["txJson"] = tx,
        };
        return result.ToJsonString();
    }

    private static JsonNode DecodeBlobToNode(string blobHex)
    {
        string trimmed = blobHex.Trim();
        if ((trimmed.Length & 1) != 0)
        {
            throw new ArgumentException(
                $"Transaction blob has odd length ({trimmed.Length}); hex must be byte-aligned.",
                nameof(blobHex));
        }
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                throw new ArgumentException(
                    $"Transaction blob contains non-hex character '{c}' at position {i}.",
                    nameof(blobHex));
            }
        }

        JsonNode? decoded;
        try
        {
            decoded = XrplBinaryCodec.Decode(trimmed);
        }
        catch (Exception ex)
        {
            throw new ArgumentException(
                $"Failed to decode transaction blob: {ex.Message}",
                nameof(blobHex), ex);
        }

        return decoded
            ?? throw new ArgumentException(
                "Transaction blob decoded to null — likely truncated or not a valid serialized XRPL transaction.",
                nameof(blobHex));
    }

    private static JsonNode ParseJsonToNode(string json)
    {
        try
        {
            JsonNode? parsed = JsonNode.Parse(json);
            if (parsed is null)
            {
                throw new ArgumentException("txJson parsed to null.", nameof(json));
            }
            if (parsed is not JsonObject)
            {
                throw new ArgumentException("txJson must be a JSON object.", nameof(json));
            }
            return parsed;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"txJson is not valid JSON: {ex.Message}", nameof(json), ex);
        }
    }
}
