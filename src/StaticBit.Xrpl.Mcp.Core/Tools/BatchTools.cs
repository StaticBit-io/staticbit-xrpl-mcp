using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// XLS-56 atomic multi-transaction MCP tool. A Batch executes up to 8 inner
/// transactions under one of four execution modes (AllOrNothing / OnlyOne /
/// UntilFailure / Independent). The outer batch account pays the network fee;
/// inner transactions have their own Account, Sequence/TicketSequence and any
/// additional BatchSigners required for multi-account batches.
/// </summary>
[McpServerToolType]
public sealed class BatchTools
{
    private const uint TfInnerBatchTxn = 0x40000000u;

    private const uint TfAllOrNothing = 0x00010000u;
    private const uint TfOnlyOne = 0x00020000u;
    private const uint TfUntilFailure = 0x00040000u;
    private const uint TfIndependent = 0x00080000u;

    private readonly TransactionPreparer _preparer;

    public BatchTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_batch_prepare")]
    [Description("Prepares an UNSIGNED Batch transaction (XLS-56) that atomically executes up to 8 inner transactions. Modes: 'AllOrNothing' (all must succeed), 'OnlyOne' (first success wins, others not attempted), 'UntilFailure' (apply in order, stop at first failure), 'Independent' (apply all, each evaluated independently). 'innerTransactionsJson' is a JSON array of inner tx objects — each must have Account/Sequence (or TicketSequence) populated; Fee/SigningPubKey/TxnSignature/Signers and the tfInnerBatchTxn flag are forced by this tool. For multi-account batches, supply 'batchSignersJson' — a JSON array of {account, signingPubKey?, txnSignature?, signers?} entries (one per non-outer inner Account).")]
    public async Task<PreparedTransaction> BatchPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Outer batch account — pays the network fee and submits the Batch.")] string account,
        [Description("Execution mode: 'AllOrNothing', 'OnlyOne', 'UntilFailure', or 'Independent'.")] string mode,
        [Description("JSON array of inner tx objects (1..8). Each must include its own Account and Sequence/TicketSequence. Inner-only fields (Fee, SigningPubKey, tfInnerBatchTxn flag) are forced.")] string innerTransactionsJson,
        [Description("Optional JSON array of BatchSigner entries: [{\"account\":\"r...\",\"signingPubKey\":\"...\",\"txnSignature\":\"...\",\"signers\":[...]}]. Required when any inner tx Account differs from the outer 'account'.")] string? batchSignersJson = null,
        CancellationToken cancellationToken = default)
    {
        uint modeFlag = ParseMode(mode);

        List<Dictionary<string, object>> wrappedInners = BuildWrappedInners(innerTransactionsJson);
        List<Dictionary<string, object>>? wrappedSigners = string.IsNullOrWhiteSpace(batchSignersJson)
            ? null
            : BuildWrappedBatchSigners(batchSignersJson!);

        Dictionary<string, object> tx = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TransactionType"] = "Batch",
            ["Account"] = account,
            ["Flags"] = modeFlag,
            ["RawTransactions"] = wrappedInners,
        };
        if (wrappedSigners is not null)
        {
            tx["BatchSigners"] = wrappedSigners;
        }

        string summary = $"Batch by {ToolDisplay.Truncate(account)}: mode={ModeName(modeFlag)}, "
            + $"{wrappedInners.Count} inner tx"
            + (wrappedSigners is null ? "" : $", {wrappedSigners.Count} batchSigner(s)") + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static uint ParseMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            throw new ArgumentException("mode is required (AllOrNothing|OnlyOne|UntilFailure|Independent).", nameof(mode));
        }

        return mode.Trim() switch
        {
            "AllOrNothing" => TfAllOrNothing,
            "OnlyOne" => TfOnlyOne,
            "UntilFailure" => TfUntilFailure,
            "Independent" => TfIndependent,
            _ => throw new ArgumentException(
                $"Unknown mode '{mode}'. Allowed: AllOrNothing, OnlyOne, UntilFailure, Independent.",
                nameof(mode)),
        };
    }

    internal static string ModeName(uint modeFlag) => modeFlag switch
    {
        TfAllOrNothing => "AllOrNothing",
        TfOnlyOne => "OnlyOne",
        TfUntilFailure => "UntilFailure",
        TfIndependent => "Independent",
        _ => $"0x{modeFlag:X}",
    };

    internal static List<Dictionary<string, object>> BuildWrappedInners(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("innerTransactionsJson is required.", nameof(json));
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("innerTransactionsJson must be a JSON array of tx objects.");
        }

        List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
        int index = 0;
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"innerTransactionsJson[{index}] must be a JSON object.");
            }

            Dictionary<string, object> inner = JsonElementToDictionary(el);
            NormalizeInnerForBatch(inner, index);
            result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["RawTransaction"] = inner,
            });
            index++;
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("innerTransactionsJson must contain at least one inner transaction.");
        }
        if (result.Count > 8)
        {
            throw new ArgumentException("XLS-56 Batch is limited to 8 inner transactions.");
        }

        return result;
    }

    internal static void NormalizeInnerForBatch(Dictionary<string, object> inner, int index)
    {
        if (!inner.TryGetValue("TransactionType", out object? typeObj)
            || typeObj is not string typeStr
            || string.IsNullOrWhiteSpace(typeStr))
        {
            throw new ArgumentException($"innerTransactionsJson[{index}].TransactionType is required.");
        }
        if (string.Equals(typeStr, "Batch", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"innerTransactionsJson[{index}] cannot be a Batch (nesting is forbidden).");
        }

        if (!inner.ContainsKey("Account"))
        {
            throw new ArgumentException($"innerTransactionsJson[{index}].Account is required.");
        }

        // Each inner tx must carry the tfInnerBatchTxn global flag; OR it on top of
        // any caller-supplied flags rather than overwriting them.
        uint existingFlags = TryReadUInt(inner, "Flags") ?? 0u;
        inner["Flags"] = existingFlags | TfInnerBatchTxn;

        // Fee/SigningPubKey are forced; TxnSignature and Signers must NOT be present.
        inner["Fee"] = "0";
        inner["SigningPubKey"] = "";
        inner.Remove("TxnSignature");
        inner.Remove("Signers");

        // LastLedgerSequence on inner tx is allowed; Sequence/TicketSequence is the
        // signer's responsibility — we don't autofill across accounts.
        bool hasSequence = inner.ContainsKey("Sequence") || inner.ContainsKey("TicketSequence");
        if (!hasSequence)
        {
            throw new ArgumentException(
                $"innerTransactionsJson[{index}] must include a Sequence or TicketSequence (cannot autofill across accounts).");
        }
    }

    internal static List<Dictionary<string, object>> BuildWrappedBatchSigners(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("batchSignersJson must be a JSON array.");
        }

        List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
        int index = 0;
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"batchSignersJson[{index}] must be a JSON object.");
            }

            string? account = el.TryGetProperty("account", out JsonElement accEl) && accEl.ValueKind == JsonValueKind.String
                ? accEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(account))
            {
                throw new ArgumentException($"batchSignersJson[{index}].account is required.");
            }

            Dictionary<string, object> signer = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["Account"] = account!,
            };

            if (el.TryGetProperty("signingPubKey", out JsonElement spkEl) && spkEl.ValueKind == JsonValueKind.String)
            {
                signer["SigningPubKey"] = spkEl.GetString() ?? string.Empty;
            }
            if (el.TryGetProperty("txnSignature", out JsonElement sigEl) && sigEl.ValueKind == JsonValueKind.String)
            {
                signer["TxnSignature"] = sigEl.GetString() ?? string.Empty;
            }
            if (el.TryGetProperty("signers", out JsonElement signersEl) && signersEl.ValueKind == JsonValueKind.Array)
            {
                List<object?> arr = new List<object?>();
                foreach (JsonElement entry in signersEl.EnumerateArray())
                {
                    arr.Add(JsonElementToObject(entry));
                }
                signer["Signers"] = arr;
            }

            result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["BatchSigner"] = signer,
            });
            index++;
        }

        return result;
    }

    private static Dictionary<string, object> JsonElementToDictionary(JsonElement element)
    {
        Dictionary<string, object> dict = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            object? value = JsonElementToObject(prop.Value);
            if (value is not null)
            {
                dict[prop.Name] = value;
            }
        }
        return dict;
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out long l)) return l;
                if (element.TryGetUInt64(out ulong u)) return u;
                if (element.TryGetDouble(out double d)) return d;
                return element.GetRawText();
            case JsonValueKind.Array:
                List<object?> arr = new List<object?>();
                foreach (JsonElement entry in element.EnumerateArray())
                {
                    arr.Add(JsonElementToObject(entry));
                }
                return arr;
            case JsonValueKind.Object:
                return JsonElementToDictionary(element);
            default:
                return element.GetRawText();
        }
    }

    private static uint? TryReadUInt(IDictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out object? raw) || raw is null) return null;

        return raw switch
        {
            uint u => u,
            int i when i >= 0 => (uint)i,
            long l when l >= 0 && l <= uint.MaxValue => (uint)l,
            ulong ul when ul <= uint.MaxValue => (uint)ul,
            string s when uint.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint parsed) => parsed,
            _ => null,
        };
    }
}
