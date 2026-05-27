using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Price-oracle (XLS-47) write-flow MCP tools — OracleSet and OracleDelete.
///
/// Each Oracle ledger object is identified by (owner Account, OracleDocumentID). The
/// PriceDataSeries field carries 1..10 PriceData entries; the first 5 fit in a single
/// owner reserve, entries 6..10 require a second reserve slot. LastUpdateTime is a
/// Unix timestamp and must be within 300 seconds of the close-time of the ledger
/// that includes this transaction.
/// </summary>
[McpServerToolType]
public sealed class OracleTools
{
    private readonly TransactionPreparer _preparer;

    public OracleTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_oracle_set_prepare")]
    [Description("Prepares an UNSIGNED OracleSet (XLS-47). Creates a new Oracle ledger entry or updates an existing one. 'priceDataSeriesJson' is a JSON array (1..10) of PriceData objects: [{\"baseAsset\":\"XRP\",\"quoteAsset\":\"USD\",\"assetPrice\":\"...\",\"scale\":6}, ...]. 'baseAsset' / 'quoteAsset' may be a 3-char or 40-char hex currency code; 'assetPrice' must be a decimal uint64 string and is required together with 'scale' (0..10). 'lastUpdateTimeUnix' is seconds since epoch — must be within 300s of ledger close. 'provider', 'uri', 'assetClass' are ASCII strings (will be hex-encoded); provider/assetClass are required when creating, optional on updates.")]
    public async Task<PreparedTransaction> OracleSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Owner account of the Oracle entry.")] string account,
        [Description("Unique uint32 oracle id within this Account. Combined with Account it identifies the on-ledger Oracle object.")] uint oracleDocumentId,
        [Description("Unix timestamp (seconds) of when the price data was observed. Must be within 300s of the ledger's close.")] long lastUpdateTimeUnix,
        [Description("JSON array of PriceData (1..10 entries). Shape per entry: {baseAsset, quoteAsset, assetPrice?, scale?}. assetPrice and scale must both be present or both absent.")] string priceDataSeriesJson,
        [Description("Optional ASCII oracle provider name (Chainlink, Band, DIA, ...). Required on creation; optional on update. Max 256 chars before hex-encoding.")] string? provider = null,
        [Description("Optional ASCII URI for off-chain reference. Max 256 bytes before hex-encoding.")] string? uri = null,
        [Description("Optional ASCII asset class ('currency','commodity','index',...). Required on creation; optional on update. Max 16 chars before hex-encoding.")] string? assetClass = null,
        CancellationToken cancellationToken = default)
    {
        if (lastUpdateTimeUnix <= 0)
        {
            throw new ArgumentException("lastUpdateTimeUnix must be a positive Unix timestamp.", nameof(lastUpdateTimeUnix));
        }

        List<Dictionary<string, object>> priceSeries = ParsePriceDataSeries(priceDataSeriesJson);

        Dictionary<string, object> tx = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TransactionType"] = "OracleSet",
            ["Account"] = account,
            ["OracleDocumentID"] = oracleDocumentId,
            ["LastUpdateTime"] = (uint)lastUpdateTimeUnix,
            ["PriceDataSeries"] = priceSeries,
        };

        if (!string.IsNullOrEmpty(provider))
        {
            if (provider.Length > 256)
            {
                throw new ArgumentException("provider exceeds 256 chars (pre hex-encoding).", nameof(provider));
            }
            tx["Provider"] = AsciiToHex(provider, nameof(provider));
        }
        if (!string.IsNullOrEmpty(uri))
        {
            if (uri.Length > 256)
            {
                throw new ArgumentException("uri exceeds 256 chars (pre hex-encoding).", nameof(uri));
            }
            tx["URI"] = AsciiToHex(uri, nameof(uri));
        }
        if (!string.IsNullOrEmpty(assetClass))
        {
            if (assetClass.Length > 16)
            {
                throw new ArgumentException("assetClass exceeds 16 chars (pre hex-encoding).", nameof(assetClass));
            }
            tx["AssetClass"] = AsciiToHex(assetClass, nameof(assetClass));
        }

        string summary = $"OracleSet by {ToolDisplay.Truncate(account)}: id={oracleDocumentId}, "
            + $"{priceSeries.Count} price entr{(priceSeries.Count == 1 ? "y" : "ies")}, "
            + $"lastUpdate={lastUpdateTimeUnix}"
            + (string.IsNullOrEmpty(provider) ? "" : $", provider='{provider}'")
            + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_oracle_delete_prepare")]
    [Description("Prepares an UNSIGNED OracleDelete (XLS-47). Removes the Oracle ledger entry identified by (Account, OracleDocumentID). Only the owner may delete.")]
    public async Task<PreparedTransaction> OracleDeletePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Owner account of the Oracle entry.")] string account,
        [Description("Oracle document id (uint32) to delete.")] uint oracleDocumentId,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, object> tx = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TransactionType"] = "OracleDelete",
            ["Account"] = account,
            ["OracleDocumentID"] = oracleDocumentId,
        };

        string summary = $"OracleDelete by {ToolDisplay.Truncate(account)}: id={oracleDocumentId}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static List<Dictionary<string, object>> ParsePriceDataSeries(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("priceDataSeriesJson is required.", nameof(json));
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("priceDataSeriesJson must be a JSON array of PriceData objects.");
        }

        List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
        int index = 0;
        foreach (JsonElement entry in doc.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"priceDataSeriesJson[{index}] must be an object.");
            }

            string baseAsset = ReadRequiredString(entry, "baseAsset", index);
            string quoteAsset = ReadRequiredString(entry, "quoteAsset", index);

            Dictionary<string, object> priceData = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["BaseAsset"] = NormalizeCurrency(baseAsset, "baseAsset", index),
                ["QuoteAsset"] = NormalizeCurrency(quoteAsset, "quoteAsset", index),
            };

            bool hasPrice = entry.TryGetProperty("assetPrice", out JsonElement priceEl) && priceEl.ValueKind != JsonValueKind.Null;
            bool hasScale = entry.TryGetProperty("scale", out JsonElement scaleEl) && scaleEl.ValueKind != JsonValueKind.Null;

            if (hasPrice != hasScale)
            {
                throw new ArgumentException(
                    $"priceDataSeriesJson[{index}] must include both 'assetPrice' and 'scale' together, or neither.");
            }

            if (hasPrice)
            {
                string priceStr = priceEl.ValueKind == JsonValueKind.String
                    ? priceEl.GetString() ?? ""
                    : priceEl.GetRawText();
                if (!ulong.TryParse(priceStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong priceVal))
                {
                    throw new ArgumentException(
                        $"priceDataSeriesJson[{index}].assetPrice must be a uint64 decimal string.");
                }
                if (scaleEl.ValueKind != JsonValueKind.Number || !scaleEl.TryGetUInt32(out uint scaleVal))
                {
                    throw new ArgumentException(
                        $"priceDataSeriesJson[{index}].scale must be a uint number.");
                }
                if (scaleVal > 10)
                {
                    throw new ArgumentException(
                        $"priceDataSeriesJson[{index}].scale must be in range 0..10.");
                }
                priceData["AssetPrice"] = priceVal.ToString(CultureInfo.InvariantCulture);
                priceData["Scale"] = scaleVal;
            }

            result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["PriceData"] = priceData,
            });
            index++;
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("priceDataSeriesJson must contain at least one PriceData entry.");
        }
        if (result.Count > 10)
        {
            throw new ArgumentException("priceDataSeriesJson cannot contain more than 10 PriceData entries.");
        }

        return result;
    }

    internal static string NormalizeCurrency(string code, string fieldName, int index)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException($"priceDataSeriesJson[{index}].{fieldName} must not be empty.");
        }

        if (code.Length == 40)
        {
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!ok)
                {
                    throw new ArgumentException($"priceDataSeriesJson[{index}].{fieldName} is 40 chars but not valid hex.");
                }
            }
            return code.ToUpperInvariant();
        }
        if (code.Length == 3)
        {
            // 3-char ISO-like codes pass through; rippled accepts them in PriceData.
            return code;
        }
        throw new ArgumentException(
            $"priceDataSeriesJson[{index}].{fieldName} must be a 3-char or 40-char hex currency code (got '{code}').");
    }

    internal static string AsciiToHex(string ascii, string paramName)
    {
        for (int i = 0; i < ascii.Length; i++)
        {
            char c = ascii[i];
            if (c < 0x20 || c > 0x7E)
            {
                throw new ArgumentException($"{paramName} contains non-printable-ASCII char at position {i}.", paramName);
            }
        }
        return Convert.ToHexString(Encoding.ASCII.GetBytes(ascii));
    }

    private static string ReadRequiredString(JsonElement obj, string field, int index)
    {
        if (!obj.TryGetProperty(field, out JsonElement el) || el.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"priceDataSeriesJson[{index}].{field} is required and must be a string.");
        }
        return el.GetString() ?? string.Empty;
    }
}
