using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Options;
using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Builds a <see cref="PreparedTransaction"/> from a typed transaction object.
///
/// The flow is:
/// <list type="number">
/// <item>Convert the typed transaction to its canonical dictionary form.</item>
/// <item>Run <c>Autofill</c> so the rippled node fills <c>Sequence</c>, <c>Fee</c> and <c>LastLedgerSequence</c>.</item>
/// <item>Encode the filled transaction as both an unsigned blob and a signing pre-image.</item>
/// </list>
///
/// The result is signed off-server — by the calling agent, wallet or hardware device.
/// </summary>
public sealed class TransactionPreparer
{
    private readonly XrplClientPool _pool;
    private readonly XrplMcpOptions _options;
    private static readonly JsonSerializerOptions DictionaryJsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = null,
    };

    public TransactionPreparer(XrplClientPool pool, IOptions<XrplMcpOptions> options)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<PreparedTransaction> PrepareAsync(
        NetworkRef network,
        ITransactionRequest transaction,
        string humanSummary,
        CancellationToken cancellationToken)
    {
        if (transaction is null) throw new ArgumentNullException(nameof(transaction));
        return PrepareAsync(network, transaction.ToDictionary(), humanSummary, cancellationToken);
    }

    public async Task<PreparedTransaction> PrepareAsync(
        NetworkRef network,
        Dictionary<string, object> transaction,
        string humanSummary,
        CancellationToken cancellationToken)
    {
        if (transaction is null) throw new ArgumentNullException(nameof(transaction));

        // Validate the Account field up front — every XRPL transaction carries
        // one, and an invalid base58check value would otherwise surface as a
        // deep SDK EncodingFormatException. AssertValid throws a clean
        // McpException envelope (via XrplToolError) naming the field.
        if (transaction.TryGetValue("Account", out object? accountObj)
            && accountObj is string accountStr)
        {
            AddressValidation.AssertValid(accountStr, "Account");
        }

        try
        {
            IXrplClient client = await _pool.GetAsync(network, cancellationToken).ConfigureAwait(false);

            // Fetch the current validated ledger index once: it pre-seeds LastLedgerSequence (below)
            // and feeds the preview's "expires in ~N ledgers" estimate. Non-fatal on failure.
            uint? currentLedgerIndex = null;
            try
            {
                currentLedgerIndex = await client.GetLedgerIndex(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Leave null — Autofill computes LastLedgerSequence itself; the preview omits the estimate.
            }

            // Honor the configured LastLedgerSequenceOffset by pre-seeding the field;
            // SDK's Autofill only fills it when absent so this overrides the SDK's hardcoded offset.
            if (_options.LastLedgerSequenceOffset > 0
                && !transaction.ContainsKey("LastLedgerSequence")
                && currentLedgerIndex is uint seedLedger)
            {
                transaction["LastLedgerSequence"] = seedLedger + _options.LastLedgerSequenceOffset;
            }

            // Stamp the server's default SourceTag before Autofill so it becomes part of the
            // canonical, signed transaction. Top-level only — an XLS-56 Batch's inner
            // transactions (nested under RawTransactions) stay byte-for-byte as the caller built them.
            ApplyDefaultSourceTag(transaction, _options.DefaultSourceTag);

            Dictionary<string, object> filled = await client
                .Autofill(transaction, signersCount: null, cancellationToken)
                .ConfigureAwait(false);

            ApplyFeeBump(filled);

            string blobUnsigned = XrplBinaryCodec.Encode(filled);
            string signingData = XrplBinaryCodec.EncodeForSigning(filled);
            uint lastLedgerSequence = ExtractUInt(filled, "LastLedgerSequence");

            Dictionary<string, object?> normalizedJson = NormalizeDictionary(filled);
            string preview = TransactionPreview.Render(normalizedJson, network.Value, currentLedgerIndex);

            return new PreparedTransaction
            {
                Network = network.Value,
                TxJson = normalizedJson,
                TxBlobUnsigned = blobUnsigned,
                SigningData = signingData,
                LastLedgerSequence = lastLedgerSequence,
                HumanSummary = humanSummary,
                Preview = preview,
                RequiresUserApproval = true,
            };
        }
        catch (OperationCanceledException)
        {
            // Honour cooperative cancellation — do not reclassify as a tool error.
            throw;
        }
        catch (Exception ex)
        {
            // RippledException (actNotFound, tecPATH_DRY, malformedAddress, etc.)
            // and any other autofill/encode failure → structured envelope so the
            // agent gets category / isRetryable / fieldName instead of an opaque stub.
            XrplToolError.ThrowMcp(ex);
            throw; // unreachable — ThrowMcp always throws.
        }
    }

    /// <summary>
    /// If <see cref="XrplMcpOptions.FeeBumpMultiplier"/> &gt; 1.0, replace the autofilled
    /// <c>Fee</c> with <c>ceil(Fee × multiplier)</c>. Preserves drops-string formatting.
    /// </summary>
    private void ApplyFeeBump(IDictionary<string, object> filled)
    {
        if (_options.FeeBumpMultiplier <= 1.0m) return;
        if (!filled.TryGetValue("Fee", out object? feeVal) || feeVal is null) return;

        string? feeStr = feeVal switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => feeVal.ToString(),
        };
        if (string.IsNullOrEmpty(feeStr)) return;
        if (!long.TryParse(feeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long currentDrops)) return;

        decimal scaled = currentDrops * _options.FeeBumpMultiplier;
        long bumped = (long)Math.Ceiling(scaled);
        if (bumped <= currentDrops) return;

        filled["Fee"] = bumped.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Stamp <paramref name="defaultSourceTag"/> onto <paramref name="transaction"/> when it does
    /// not already carry a <c>SourceTag</c>. A value the caller supplied — including an explicit
    /// <c>0</c> — is preserved, and a <c>null</c> option disables stamping. Operates on the
    /// top-level transaction only, so an XLS-56 Batch keeps its caller-signed inner transactions
    /// (nested under <c>RawTransactions</c>) intact.
    /// </summary>
    internal static void ApplyDefaultSourceTag(IDictionary<string, object> transaction, uint? defaultSourceTag)
    {
        if (defaultSourceTag is null) return;

        if (transaction.TryGetValue("SourceTag", out object? existing)
            && existing is not null
            && !(existing is JsonElement je && je.ValueKind == JsonValueKind.Null))
        {
            return;
        }

        transaction["SourceTag"] = defaultSourceTag.Value;
    }

    private static uint ExtractUInt(IDictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out object? value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            uint u => u,
            int i => (uint)i,
            long l => (uint)l,
            string s when uint.TryParse(s, out uint parsed) => parsed,
            JsonElement je when je.ValueKind == JsonValueKind.Number && je.TryGetUInt32(out uint pe) => pe,
            _ => 0,
        };
    }

    private static Dictionary<string, object?> NormalizeDictionary(IDictionary<string, object> source)
    {
        Dictionary<string, object?> result = new Dictionary<string, object?>(source.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> kvp in source)
        {
            result[kvp.Key] = Normalize(kvp.Value);
        }
        return result;
    }

    private static object? Normalize(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement je => NormalizeJsonElement(je),
            IDictionary<string, object> dict => NormalizeDictionary(dict),
            _ => value,
        };
    }

    private static object? NormalizeJsonElement(JsonElement element)
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
            case JsonValueKind.Number:
                if (element.TryGetInt64(out long l)) return l;
                if (element.TryGetDouble(out double d)) return d;
                return element.GetRawText();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Array:
                List<object?> list = new List<object?>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    list.Add(NormalizeJsonElement(item));
                }
                return list;
            case JsonValueKind.Object:
                Dictionary<string, object?> dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (JsonProperty prop in element.EnumerateObject())
                {
                    dict[prop.Name] = NormalizeJsonElement(prop.Value);
                }
                return dict;
            default:
                return element.GetRawText();
        }
    }
}
