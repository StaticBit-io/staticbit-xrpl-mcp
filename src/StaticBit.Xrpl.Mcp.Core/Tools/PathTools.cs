using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Pathfinding MCP tools. <c>xrpl_ripple_path_find</c> is the standard one-shot pathfinder
/// (works on WebSocket and JSON-RPC). The <c>xrpl_path_find_*</c> trio drives the WebSocket-only
/// long-running variant — useful only on stdio/local connections where the pool's WebSocket
/// can stay subscribed.
/// </summary>
[McpServerToolType]
public sealed class PathTools
{
    private readonly XrplClientPool _pool;

    public PathTools(XrplClientPool pool)
    {
        _pool = pool;
    }

    [McpServerTool(Name = "xrpl_ripple_path_find")]
    [Description("One-shot cross-currency pathfinder. Returns the alternatives array — every entry has a 'source_amount' you can drop straight into a Payment as SendMax along with its 'paths_computed' as Paths. destinationAmount uses the same format as xrpl_payment_prepare (drops string for XRP or JSON {value,currency,issuer} for tokens). sourceCurrenciesJson optionally restricts what the source can spend: JSON array of {currency,issuer?} objects (max 18).")]
    public async Task<string> RipplePathFindAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Source account address.")] string sourceAccount,
        [Description("Destination account address.")] string destinationAccount,
        [Description("Destination amount: drops string for XRP, JSON {value,currency,issuer} for tokens. Pass value='-1' to ask 'deliver as much as possible up to sendMax'.")] string destinationAmount,
        [Description("Optional: maximum the source is willing to spend. Same format as destinationAmount. Mutually exclusive with sourceCurrenciesJson per docs.")] string? sendMax = null,
        [Description("Optional: JSON array of {currency,issuer?} the source is willing to spend (max 18).")] string? sourceCurrenciesJson = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(sendMax) && !string.IsNullOrWhiteSpace(sourceCurrenciesJson))
        {
            throw new ArgumentException("sendMax and sourceCurrenciesJson are mutually exclusive per the rippled API.");
        }

        Currency parsedDestination = CurrencyParser.Parse(destinationAmount);

        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        RipplePathFindRequest request = new RipplePathFindRequest(sourceAccount, destinationAccount, parsedDestination)
        {
            SendMax = sendMax is null ? null! : CurrencyParser.Parse(sendMax),
            SourceCurrencies = ParseSourceCurrencies(sourceCurrenciesJson),
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
        };

        RipplePathFindResponse response = await client.RipplePathFind(request, cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    [McpServerTool(Name = "xrpl_path_find_create")]
    [Description("WebSocket-only. Opens a long-running pathfinding subscription on the pool's shared connection. Returns the initial set of alternatives; subsequent updates are delivered to the pool's WebSocket but NOT relayed back through MCP — for ongoing updates, re-poll via xrpl_path_find_status. Only one open path_find request per WebSocket; calling this twice replaces the previous one.")]
    public async Task<string> PathFindCreateAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL. JSON-RPC URLs WILL NOT work with this method.")] string network,
        [Description("Source account address.")] string sourceAccount,
        [Description("Destination account address.")] string destinationAccount,
        [Description("Destination amount (same format as xrpl_payment_prepare).")] string destinationAmount,
        [Description("Optional: maximum the source is willing to spend.")] string? sendMax = null,
        CancellationToken cancellationToken = default)
    {
        Currency parsedDestination = CurrencyParser.Parse(destinationAmount);

        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        PathFindCreateRequest request = new PathFindCreateRequest(sourceAccount, destinationAccount, parsedDestination)
        {
            SendMax = sendMax is null ? null! : CurrencyParser.Parse(sendMax),
        };

        PathFindResponse response = await client.PathFind(request, cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    [McpServerTool(Name = "xrpl_path_find_status")]
    [Description("WebSocket-only. Requests an immediate update for the currently-open path_find subscription on the pool's connection. Returns the latest alternatives. No-op if there is no open path_find.")]
    public async Task<string> PathFindStatusAsync(
        [Description("Network identifier — must match the one used in xrpl_path_find_create.")] string network,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);
        PathFindResponse response = await client.PathFindStatus(new PathFindStatusRequest(), cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    [McpServerTool(Name = "xrpl_path_find_close")]
    [Description("WebSocket-only. Closes the currently-open path_find subscription on the pool's connection.")]
    public async Task<string> PathFindCloseAsync(
        [Description("Network identifier — must match the one used in xrpl_path_find_create.")] string network,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);
        PathFindResponse response = await client.PathFindClose(new PathFindCloseRequest(), cancellationToken).ConfigureAwait(false);
        return XrplJson.Serialize(response);
    }

    internal static List<SourceCurrency>? ParseSourceCurrencies(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("sourceCurrenciesJson must be a JSON array.");
        }

        List<SourceCurrency> result = new List<SourceCurrency>();
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Each source-currency entry must be a JSON object.");
            }

            string? currency = el.TryGetProperty("currency", out JsonElement c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
            if (string.IsNullOrEmpty(currency))
            {
                throw new ArgumentException("Each source-currency entry must have a non-empty 'currency'.");
            }

            string? issuer = el.TryGetProperty("issuer", out JsonElement i) && i.ValueKind == JsonValueKind.String
                ? i.GetString()
                : null;

            result.Add(new SourceCurrency { Currency = currency, Issuer = issuer });
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("sourceCurrenciesJson must contain at least one entry (or omit the parameter).");
        }
        if (result.Count > 18)
        {
            throw new ArgumentException("sourceCurrenciesJson cannot contain more than 18 entries (rippled API limit).");
        }
        return result;
    }
}
