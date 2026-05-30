using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Auth.ResourceServer;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Methods;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Account-scoped read-only MCP tools.
/// All tools default to the <c>validated</c> ledger for confirmed data.
/// </summary>
[McpServerToolType]
public sealed class AccountTools
{
    private readonly XrplClientPool _pool;

    public AccountTools(XrplClientPool pool)
    {
        _pool = pool;
    }

    [McpServerTool(Name = "xrpl_account_info")]
    [Description("Account root data: XRP balance (drops), sequence, flags, reserves.")]
    public async Task<string> AccountInfoAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address (starts with 'r...').")] string account,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        [Description("If true, the server checks the account against an STRICT mode (verifies the address).")] bool strict = true,
        [Description("If true, include queued transactions for this account.")] bool queue = false,
        [Description("If true, include signer-list ledger entries.")] bool signerLists = false,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountInfoRequest request = new AccountInfoRequest(account)
        {
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Strict = strict,
            Queue = queue,
            SignerLists = signerLists,
        };

        AccountInfo response = await client.AccountInfo(request, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(response), $"xrpl:account_info:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_lines")]
    [Description("Trust lines for an account. Use 'limit' and 'marker' for pagination.")]
    public async Task<string> AccountLinesAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address.")] string account,
        [Description("Optional peer address to filter lines to a single counterparty.")] string? peer = null,
        [Description("Page size (max ~400). Omit to use the server default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        if (peer != null)
        {
            AddressValidation.AssertValid(peer, nameof(peer));
        }
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountLinesRequest request = new AccountLinesRequest(account)
        {
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Peer = peer,
            Limit = limit,
            Marker = marker,
        };

        AccountLines response = await client.AccountLines(request, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(response), $"xrpl:account_lines:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_tx")]
    [Description("Transaction history for an account, paginated via marker. Returns latest first by default.")]
    public async Task<string> AccountTxAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address.")] string account,
        [Description("Lower bound of the ledger range. -1 means earliest available.")] int? ledgerIndexMin = -1,
        [Description("Upper bound of the ledger range. -1 means latest available.")] int? ledgerIndexMax = -1,
        [Description("Page size (max 200).")] int? limit = 50,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description("If true, return results in ascending (oldest first) order.")] bool forward = false,
        [Description("If true, return raw binary blobs instead of expanded JSON.")] bool binary = false,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountTransactionsRequest request = new AccountTransactionsRequest(account)
        {
            LedgerIndexMin = ledgerIndexMin,
            LedgerIndexMax = ledgerIndexMax,
            Limit = limit,
            Marker = marker,
            Forward = forward,
            Binary = binary,
        };

        AccountTransactions response = await client.AccountTransactions(request, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(response), $"xrpl:account_tx:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_offers")]
    [Description("Active DEX offers owned by the account.")]
    public async Task<string> AccountOffersAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address.")] string account,
        [Description("Page size.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountOffersRequest request = new AccountOffersRequest(account)
        {
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Limit = limit,
            Marker = marker,
        };

        AccountOffers response = await client.AccountOffers(request, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(response), $"xrpl:account_offers:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_objects")]
    [Description("All ledger objects owned by the account (trust lines, offers, escrows, checks, channels, NFTs, etc.).")]
    public async Task<string> AccountObjectsAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address.")] string account,
        [Description("Optional object type filter (e.g. 'offer', 'state', 'escrow', 'check', 'payment_channel', 'signer_list', 'nft_offer').")] string? type = null,
        [Description("Page size.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description("If true, return only objects that prevent account deletion.")] bool deletionBlockersOnly = false,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        LedgerEntryType? parsedType = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            if (!Enum.TryParse(type, ignoreCase: true, out LedgerEntryType parsed))
            {
                throw new ArgumentException(
                    $"Unknown ledger entry type '{type}'. Valid values: {string.Join(", ", Enum.GetNames<LedgerEntryType>())}.",
                    nameof(type));
            }

            parsedType = parsed;
        }

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Type = parsedType,
            Limit = limit,
            Marker = marker,
            DeletionBlockersOnly = deletionBlockersOnly,
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(response), $"xrpl:account_objects:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_gateway_balances")]
    [Description("Issuer-side balance summary: total obligations (tokens this account has issued and are held by non-excluded addresses), assets held that were issued by others, and balances held by the listed hotwallets. Pass hotwalletsJson as a JSON array of r-addresses to exclude operational/hot wallets from obligations. Strict mode rejects non-r-address inputs.")]
    public async Task<string> GatewayBalancesAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Issuer address to inspect.")] string account,
        [Description("Optional JSON array of r-addresses to treat as hotwallets and exclude from obligations.")] string? hotwalletsJson = null,
        [Description("If true, only accept r-address/public-key for account.")] bool strict = true,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        GatewayBalancesRequest request = new GatewayBalancesRequest(account)
        {
            Strict = strict,
            HotWallet = ParseHotwallets(hotwalletsJson)!,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
        };

        GatewayBalancesResponse response = await client.GatewayBalances(request, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(response), $"xrpl:gateway_balances:{network}:{account}");
    }

    internal static object? ParseHotwallets(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            throw new ArgumentException("hotwalletsJson must be a JSON array of r-address strings.");
        }

        List<string> result = new List<string>();
        foreach (System.Text.Json.JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                throw new ArgumentException("hotwalletsJson entries must be r-address strings.");
            }
            string? addr = el.GetString();
            if (string.IsNullOrWhiteSpace(addr))
            {
                throw new ArgumentException("hotwalletsJson contains an empty address.");
            }
            result.Add(addr);
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("hotwalletsJson must contain at least one address (or omit the parameter).");
        }
        return result;
    }

    [McpServerTool(Name = "xrpl_xrp_balance")]
    [Description("Convenience: returns the spendable XRP balance for an account, as a decimal XRP string.")]
    public async Task<string> XrpBalanceAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address.")] string account,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);
        string balance = await client.GetXrpBalance(account, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(new { account, balanceXrp = balance }), $"xrpl:xrp_balance:{network}:{account}");
    }
}
