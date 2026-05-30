using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
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
/// Subscription + polling MCP tools.
///
/// <para>
/// <b>Important:</b> <c>xrpl_subscribe</c>/<c>xrpl_unsubscribe</c> are honest pass-throughs to
/// the rippled subscribe API. The subscription is installed on the pool's <i>shared</i>
/// WebSocket connection — the cloud server has ONE WebSocket per network shared across all
/// MCP clients. The streaming events do NOT come back to you over MCP (MCP is request/response).
/// These tools are primarily useful for stdio-local deployments where a single agent owns the
/// connection, or as plumbing for future server-side watchers / admin alerts.
/// </para>
/// <para>
/// For the common "monitor account for new transactions" use case, prefer the polling helper
/// <c>xrpl_account_tx_since</c> below — it's stateless and works identically on cloud / local / HTTP.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class SubscriptionTools
{
    private readonly XrplClientPool _pool;

    public SubscriptionTools(XrplClientPool pool)
    {
        _pool = pool;
    }

    [McpServerTool(Name = "xrpl_subscribe")]
    [Description("Installs a subscription on the pool's shared WebSocket. WARNING: streaming events are not delivered back through MCP — prefer xrpl_account_tx_since for polling-based monitoring. streamsCsv: comma-separated subset of {ledger,transactions,transactions_proposed,validations,manifests,server,peer_status,consensus,book_changes}. accountsJson / accountsProposedJson: JSON array of r-addresses.")]
    public async Task<string> SubscribeAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Comma-separated stream names. Empty to subscribe only by account/book.")] string? streamsCsv = null,
        [Description("JSON array of r-addresses to subscribe to (validated transactions affecting them).")] string? accountsJson = null,
        [Description("JSON array of r-addresses to subscribe to (proposed + validated, unfinalized).")] string? accountsProposedJson = null,
        CancellationToken cancellationToken = default)
    {
        SubscribeRequest request = new SubscribeRequest
        {
            Streams = ParseStreams(streamsCsv),
            Accounts = ParseAddresses(accountsJson, "accountsJson"),
            AccountsProposed = ParseAddresses(accountsProposedJson, "accountsProposedJson"),
        };

        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);
        object response = await client.Subscribe(request, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(response), $"xrpl:subscription:{network}");
    }

    [McpServerTool(Name = "xrpl_unsubscribe")]
    [Description("Mirror of xrpl_subscribe — removes subscriptions from the pool's shared WebSocket. Same parameter shape.")]
    public async Task<string> UnsubscribeAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Comma-separated stream names to unsubscribe.")] string? streamsCsv = null,
        [Description("JSON array of r-addresses to unsubscribe.")] string? accountsJson = null,
        [Description("JSON array of r-addresses to unsubscribe from the proposed stream.")] string? accountsProposedJson = null,
        CancellationToken cancellationToken = default)
    {
        UnsubscribeRequest request = new UnsubscribeRequest
        {
            Streams = ParseStreams(streamsCsv),
            Accounts = ParseAddresses(accountsJson, "accountsJson"),
            AccountsProposed = ParseAddresses(accountsProposedJson, "accountsProposedJson"),
        };

        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);
        object response = await client.Unsubscribe(request, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(response), $"xrpl:subscription:{network}");
    }

    [McpServerTool(Name = "xrpl_account_tx_since")]
    [Description("Polling-based account monitor. Returns transactions affecting the account starting from sinceLedger (exclusive) up to current validated ledger. Pageable via 'limit' and 'marker'. The intended pattern: caller stores the highest ledger_index it saw and passes it as sinceLedger on the next poll. Works on cloud/local/HTTP — no streaming required.")]
    public async Task<string> AccountTxSinceAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address.")] string account,
        [Description("Inclusive lower bound: return transactions from this ledger onward. Pass 0 (default) for 'earliest available'.")] int sinceLedger = 0,
        [Description("Page size (max 200).")] int? limit = 50,
        [Description("Pagination cursor returned by the previous call (continue from prior page).")] string? marker = null,
        [Description("If true, return results in ascending order (oldest first) — usually the right thing for monitoring.")] bool forward = true,
        CancellationToken cancellationToken = default)
    {
        if (sinceLedger < -1)
        {
            throw new ArgumentException("sinceLedger must be >= -1 (or 0 for 'earliest available').", nameof(sinceLedger));
        }

        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountTransactionsRequest request = new AccountTransactionsRequest(account)
        {
            LedgerIndexMin = sinceLedger == 0 ? -1 : sinceLedger,
            LedgerIndexMax = -1,
            Limit = limit,
            Marker = marker,
            Forward = forward,
        };

        AccountTransactions response = await client.AccountTransactions(request, cancellationToken).ConfigureAwait(false);
        return UntrustedContent.Wrap(XrplJson.Serialize(response), $"xrpl:account_tx_since:{network}:{account}");
    }

    internal static List<StreamType>? ParseStreams(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;

        string[] parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        List<StreamType> result = new List<StreamType>();
        foreach (string raw in parts)
        {
            StreamType parsed = MapStream(raw);
            result.Add(parsed);
        }
        return result;
    }

    internal static List<string>? ParseAddresses(string? json, string paramName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"{paramName} must be a JSON array of r-address strings.");
        }

        List<string> result = new List<string>();
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException($"{paramName} entries must be r-address strings.");
            }
            string? addr = el.GetString();
            if (string.IsNullOrWhiteSpace(addr))
            {
                throw new ArgumentException($"{paramName} contains an empty address.");
            }
            result.Add(addr);
        }

        if (result.Count == 0)
        {
            throw new ArgumentException($"{paramName} must contain at least one address (or omit the parameter).");
        }
        return result;
    }

    private static StreamType MapStream(string raw)
    {
        return raw switch
        {
            "ledger" => StreamType.Ledger,
            "transactions" => StreamType.Transactions,
            "transactions_proposed" => StreamType.TransactionsProposed,
            "validations" => StreamType.Validations,
            "manifests" => StreamType.Manifests,
            "server" => StreamType.Server,
            "peer_status" => StreamType.PeerStatus,
            "consensus" => StreamType.Consensus,
            "book_changes" => StreamType.BookChanges,
            _ => throw new ArgumentException(
                $"Unknown stream '{raw}'. Valid: ledger, transactions, transactions_proposed, validations, manifests, server, peer_status, consensus, book_changes."),
        };
    }
}
