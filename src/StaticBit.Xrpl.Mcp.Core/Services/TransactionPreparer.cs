using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Abstractions;
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
    private static readonly JsonSerializerOptions DictionaryJsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = null,
    };

    public TransactionPreparer(XrplClientPool pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
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

        IXrplClient client = await _pool.GetAsync(network, cancellationToken).ConfigureAwait(false);

        Dictionary<string, object> filled = await client
            .Autofill(transaction, signersCount: null, cancellationToken)
            .ConfigureAwait(false);

        string blobUnsigned = XrplBinaryCodec.Encode(filled);
        string signingData = XrplBinaryCodec.EncodeForSigning(filled);
        uint lastLedgerSequence = ExtractUInt(filled, "LastLedgerSequence");

        Dictionary<string, object?> normalizedJson = NormalizeDictionary(filled);

        return new PreparedTransaction
        {
            Network = network.Value,
            TxJson = normalizedJson,
            TxBlobUnsigned = blobUnsigned,
            SigningData = signingData,
            LastLedgerSequence = lastLedgerSequence,
            HumanSummary = humanSummary,
            RequiresUserApproval = true,
        };
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
