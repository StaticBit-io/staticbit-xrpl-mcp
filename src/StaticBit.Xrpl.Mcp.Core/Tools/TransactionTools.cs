using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Auth.ResourceServer;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Generic transaction MCP tools: submit a signed blob to the network, decode a blob,
/// or prepare an arbitrary transaction described as JSON (escape hatch for tx types we have
/// no typed wrapper for yet — Escrow, NFT, Check, PaymentChannel, …).
/// </summary>
[McpServerToolType]
public sealed class TransactionTools
{
    private readonly XrplClientPool _pool;
    private readonly TransactionPreparer _preparer;

    public TransactionTools(XrplClientPool pool, TransactionPreparer preparer)
    {
        _pool = pool;
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_tx_submit_signed")]
    [Description("Submits a SIGNED transaction blob to the network. The blob must already be signed locally — the server NEVER signs. Optionally polls until the transaction is included in a validated ledger.")]
    public async Task<SubmitResult> SubmitSignedAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Signed transaction blob as a hex string. Produced locally by signing the tx_blob_unsigned returned by a *_prepare tool.")] string txBlobSigned,
        [Description("If true, do NOT retry or relay if the transaction fails locally (rippled fail_hard).")] bool failHard = true,
        [Description("If true, after submission poll for the transaction hash until it is in a validated ledger or LastLedgerSequence is reached.")] bool waitForValidation = false,
        [Description("Polling interval in seconds when wait_for_validation is true. Default 2.")] int pollIntervalSeconds = 2,
        [Description("Max number of polls. Default 30 (≈60 seconds at default interval, longer than the LastLedgerSequence + 20 window).")] int maxPolls = 30,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(txBlobSigned))
        {
            throw new ArgumentException("Signed transaction blob is required.", nameof(txBlobSigned));
        }

        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        SubmitRequest request = new SubmitRequest
        {
            TxBlob = txBlobSigned,
            FailHard = failHard,
        };

        Submit response = await client
            .GRequest<Submit, SubmitRequest>(request, cancellationToken)
            .ConfigureAwait(false);

        string txHash = TryGetTxHash(response.TxJson);

        SubmitResult result = new SubmitResult
        {
            EngineResult = response.EngineResult ?? string.Empty,
            EngineResultMessage = response.EngineResultMessage ?? string.Empty,
            TxHash = txHash,
            Validated = false,
            LedgerIndex = 0,
            RawResponseJson = XrplJson.Serialize(response),
        };

        if (!waitForValidation || string.IsNullOrEmpty(txHash))
        {
            return result;
        }

        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, pollIntervalSeconds));
        for (int attempt = 0; attempt < Math.Max(1, maxPolls); attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

            try
            {
                TransactionResponse lookup = await client
                    .Tx(new TxRequest(txHash), cancellationToken)
                    .ConfigureAwait(false);

                if (lookup is null)
                {
                    continue;
                }

                // Serialize once and walk fields off a JsonNode — avoids two extra
                // re-serialize round-trips that the old ExtractBool/ExtractUInt did per call.
                string lookupJson = XrplJson.Serialize(lookup);
                JsonNode? lookupNode = JsonNode.Parse(lookupJson);

                bool validated = ReadBool(lookupNode, "Validated", "validated");
                if (!validated)
                {
                    continue;
                }

                result.Validated = true;
                result.LedgerIndex = ReadUInt(lookupNode, "ledger_index", "LedgerIndex");
                result.RawResponseJson = lookupJson;
                break;
            }
            catch (OperationCanceledException)
            {
                // Honor host shutdown / client cancellation — re-throw rather than
                // silently keep polling. Without this, the outer poll-loop would
                // swallow cancellation until the next ThrowIfCancellationRequested
                // tick (up to one full interval later).
                throw;
            }
            catch (Exception)
            {
                // Transaction may not yet be visible — keep polling until maxPolls.
            }
        }

        return result;
    }

    [McpServerTool(Name = "xrpl_tx_decode_blob")]
    [Description("Decodes a binary transaction blob (signed or unsigned) into JSON for inspection. Pure local operation — no network calls.")]
    public string DecodeBlob(
        [Description("Hex-encoded transaction blob.")] string txBlob)
    {
        if (string.IsNullOrWhiteSpace(txBlob))
        {
            throw new ArgumentException("Transaction blob is required.", nameof(txBlob));
        }

        string trimmed = txBlob.Trim();

        // Hex-format pre-check — XrplBinaryCodec.Decode swallows malformed input
        // and just returns an empty/null node, which is unhelpful for callers.
        if ((trimmed.Length & 1) != 0)
        {
            throw new ArgumentException(
                $"Transaction blob has odd length ({trimmed.Length}); hex must be byte-aligned.",
                nameof(txBlob));
        }
        for (int i = 0; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                throw new ArgumentException(
                    $"Transaction blob contains non-hex character '{c}' at position {i}.",
                    nameof(txBlob));
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
                nameof(txBlob), ex);
        }

        if (decoded is null)
        {
            throw new ArgumentException(
                "Transaction blob decoded to null — likely truncated or not a valid serialized XRPL transaction.",
                nameof(txBlob));
        }

        return UntrustedContent.Wrap(decoded.ToJsonString(), "xrpl:tx_decode_blob");
    }

    [McpServerTool(Name = "xrpl_tx_prepare_generic")]
    [Description("Escape hatch: prepares any XRPL transaction described as a JSON object (TransactionType + fields). Autofills Sequence/Fee/LastLedgerSequence and returns unsigned blob + signing data. Use for tx types not covered by dedicated *_prepare tools (Escrow, NFToken, Check, PaymentChannel, AccountSet, …).")]
    public async Task<PreparedTransaction> PrepareGenericAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Raw transaction as a JSON object, e.g. {\"TransactionType\":\"AccountSet\",\"Account\":\"r...\",\"SetFlag\":8}.")] string txJson,
        [Description("Optional one-line human summary shown to the user in the approval prompt.")] string? humanSummary = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(txJson))
        {
            throw new ArgumentException("Transaction JSON is required.", nameof(txJson));
        }

        using JsonDocument doc = JsonDocument.Parse(txJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Transaction JSON must be a JSON object.", nameof(txJson));
        }

        Dictionary<string, object> dict = (Dictionary<string, object>)NormalizeJson(doc.RootElement)!;

        if (!dict.TryGetValue("TransactionType", out object? typeValue) || typeValue is null)
        {
            throw new ArgumentException("Transaction JSON must include TransactionType.", nameof(txJson));
        }

        string summary = humanSummary ?? $"Generic transaction of type {typeValue}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), dict, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Recursively converts a System.Text.Json element tree into native CLR types
    /// (string / long / double / bool / <see cref="Dictionary{TKey,TValue}"/> / <see cref="List{T}"/>)
    /// so the downstream (Newtonsoft-based) SDK serializer never sees a raw <see cref="JsonElement"/>.
    /// Null-valued fields are dropped — XRPL transactions must not carry null members.
    /// </summary>
    internal static object? NormalizeJson(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object => el.EnumerateObject()
            .Where(p => p.Value.ValueKind != JsonValueKind.Null)
            .ToDictionary(p => p.Name, p => NormalizeJson(p.Value)!),
        JsonValueKind.Array => el.EnumerateArray()
            .Where(v => v.ValueKind != JsonValueKind.Null)
            .Select(v => NormalizeJson(v)!)
            .ToList(),
        JsonValueKind.String => el.GetString()!,
        JsonValueKind.Number => el.TryGetInt64(out long l) ? (object)l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText(),
    };

    private static string TryGetTxHash(object? txJson)
    {
        if (txJson is null) return string.Empty;

        try
        {
            string json = txJson is string s ? s : XrplJson.Serialize(txJson);
            JsonNode? root = JsonNode.Parse(json);
            if (root is null) return string.Empty;
            foreach (string key in new[] { "hash", "Hash", "tx_hash" })
            {
                if (root[key] is JsonValue v && v.TryGetValue<string>(out string? hash) && !string.IsNullOrEmpty(hash))
                {
                    return hash;
                }
            }
        }
        catch
        {
            // ignore — server still returns engine_result without a hash for some failures
        }

        return string.Empty;
    }

    /// <summary>
    /// Walks the JsonNode once, returns the first boolean-valued match by key.
    /// Returns false if the node is null, the key is missing, or the value isn't a bool.
    /// </summary>
    internal static bool ReadBool(JsonNode? node, params string[] keys)
    {
        if (node is null) return false;
        foreach (string key in keys)
        {
            JsonNode? child = node[key];
            if (child is JsonValue v && v.TryGetValue<bool>(out bool b))
            {
                return b;
            }
        }
        return false;
    }

    /// <summary>
    /// Walks the JsonNode once, returns the first uint-valued match by key.
    /// Accepts numbers within uint range and numeric strings; otherwise returns 0.
    /// </summary>
    internal static uint ReadUInt(JsonNode? node, params string[] keys)
    {
        if (node is null) return 0u;
        foreach (string key in keys)
        {
            JsonNode? child = node[key];
            if (child is JsonValue v)
            {
                if (v.TryGetValue<uint>(out uint u)) return u;
                if (v.TryGetValue<long>(out long l) && l >= 0L && l <= uint.MaxValue) return (uint)l;
                if (v.TryGetValue<string>(out string? s)
                    && uint.TryParse(s, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out uint p))
                {
                    return p;
                }
            }
        }
        return 0u;
    }
}
