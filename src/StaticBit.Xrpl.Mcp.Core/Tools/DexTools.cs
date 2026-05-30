using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Auth.ResourceServer;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Decentralized exchange (order book) read-only MCP tools.
/// </summary>
[McpServerToolType]
public sealed class DexTools
{
    private readonly XrplClientPool _pool;

    public DexTools(XrplClientPool pool)
    {
        _pool = pool;
    }

    [McpServerTool(Name = "xrpl_book_offers")]
    [Description("Returns the order book (offers) for a currency pair on the XRPL DEX. Use 'XRP' currency with empty issuer for XRP.")]
    public async Task<string> BookOffersAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Currency the taker would RECEIVE. 'XRP' or 3-char/40-hex token code.")] string takerGetsCurrency,
        [Description("Issuer for the taker_gets currency. Leave empty for XRP.")] string? takerGetsIssuer,
        [Description("Currency the taker would PAY. 'XRP' or 3-char/40-hex token code.")] string takerPaysCurrency,
        [Description("Issuer for the taker_pays currency. Leave empty for XRP.")] string? takerPaysIssuer,
        [Description("Optional address used as the offer-taker's perspective (for filtering unfunded offers).")] string? taker = null,
        [Description("Page size. Server may cap this value.")] uint? limit = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        BookOffersRequest request = new BookOffersRequest
        {
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            TakerGets = BuildAmount(takerGetsCurrency, takerGetsIssuer),
            TakerPays = BuildAmount(takerPaysCurrency, takerPaysIssuer),
            Taker = taker,
            Limit = limit,
        };

        BookOffers response = await client.BookOffers(request, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(response), $"xrpl:book_offers:{network}");
    }

    private static TakerAmount BuildAmount(string currency, string? issuer)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency code is required (use 'XRP' for native XRP).", nameof(currency));
        }

        string normalizedCurrency = currency.Trim();
        bool isXrp = string.Equals(normalizedCurrency, "XRP", StringComparison.OrdinalIgnoreCase);

        return new TakerAmount
        {
            Currency = isXrp ? "XRP" : normalizedCurrency,
            Issuer = isXrp ? null! : (issuer ?? throw new ArgumentException("Issuer is required for non-XRP currencies.", nameof(issuer))),
        };
    }
}
