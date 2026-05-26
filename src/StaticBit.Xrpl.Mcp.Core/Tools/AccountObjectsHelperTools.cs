using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Convenience read-helpers built on top of <c>account_objects</c> — they filter
/// by ledger entry type and reshape the response into a more agent-friendly form.
/// </summary>
[McpServerToolType]
public sealed class AccountObjectsHelperTools
{
    private readonly XrplClientPool _pool;

    public AccountObjectsHelperTools(XrplClientPool pool)
    {
        _pool = pool;
    }

    [McpServerTool(Name = "xrpl_account_escrows")]
    [Description("Lists every Escrow ledger object owned by the account. Splits into 'sent' (account is the source/funder, waiting for Finish or Cancel) and 'received' (account is the destination, can call EscrowFinish once FinishAfter passes). Each entry includes counterparty, amount, finishAfter, cancelAfter, condition (if conditional), and the EscrowCreate Sequence (use as offerSequence in xrpl_escrow_finish_prepare / xrpl_escrow_cancel_prepare).")]
    public async Task<string> AccountEscrowsAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address to inspect.")] string account,
        [Description("Page size (server-clamped). Omit for default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.Escrow,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Limit = limit,
            Marker = marker,
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        // Reshape from typed LOEscrow entries into a compact agent-readable form.
        // We split by direction so the agent can present "what others owe me" vs
        // "what I'm holding" without re-traversing the array.
        JsonArray sent = new JsonArray();
        JsonArray received = new JsonArray();

        if (response.AccountObjectList is not null)
        {
            foreach (object obj in response.AccountObjectList)
            {
                // AccountObjects.AccountObjects is List<object> — items are typed
                // ledger-entry instances after the SDK's polymorphic deserialization.
                if (obj is not LOEscrow escrow) continue;

                JsonNode? amountNode = JsonNode.Parse(XrplJson.Serialize(escrow.Amount));
                string amountDesc = TransactionExplainer.DescribeAmountNode(amountNode);

                JsonObject entry = new JsonObject
                {
                    ["index"] = escrow.Index,
                    ["account"] = escrow.Account,
                    ["destination"] = escrow.Destination,
                    ["amount"] = amountNode,
                    ["amountHuman"] = amountDesc,
                    ["finishAfter"] = escrow.FinishAfter is null ? null : escrow.FinishAfter.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                    ["cancelAfter"] = escrow.CancelAfter is null ? null : escrow.CancelAfter.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                    ["condition"] = escrow.Condition,
                    ["destinationTag"] = escrow.DestinationTag,
                    ["sourceTag"] = escrow.SourceTag,
                    ["previousTxnId"] = escrow.PreviousTxnID,
                    ["previousTxnLgrSeq"] = escrow.PreviousTxnLgrSeq,
                    // The EscrowCreate's Sequence is encoded into the ledger-object Index
                    // (rippled derives the object key from Owner + Sequence) but is NOT
                    // returned directly. Callers normally use 'previousTxnId' to look up
                    // the originating EscrowCreate via xrpl_tx_lookup if they need the
                    // exact OfferSequence to finish/cancel.
                };

                if (string.Equals(escrow.Account, account, StringComparison.Ordinal))
                {
                    sent.Add(entry);
                }
                else
                {
                    received.Add(entry);
                }
            }
        }

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["sent"] = sent,
            ["sentCount"] = sent.Count,
            ["received"] = received,
            ["receivedCount"] = received.Count,
            ["marker"] = response.Marker?.ToString(),
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return result.ToJsonString();
    }

    [McpServerTool(Name = "xrpl_signer_list_status")]
    [Description("Reads the account's SignerList ledger object and returns multi-sign status: quorum, total available weight (sum of all signer weights), and per-signer breakdown. Pass alreadySignedAccountsCsv to compute the weight already collected and how much more is needed to reach quorum — useful when collecting signatures for a multi-sign transaction.")]
    public async Task<string> SignerListStatusAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address whose SignerList to inspect.")] string account,
        [Description("Optional comma-separated list of signer addresses that have already signed; used to compute deltaToQuorum.")] string? alreadySignedAccountsCsv = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.SignerList,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        LOSignerList? signerList = response.AccountObjectList?
            .OfType<LOSignerList>()
            .FirstOrDefault();

        if (signerList is null)
        {
            JsonObject empty = new JsonObject
            {
                ["account"] = account,
                ["hasSignerList"] = false,
                ["message"] = "Account has no SignerList — multi-sign is not enabled.",
            };
            return empty.ToJsonString();
        }

        HashSet<string> already = ParseAlreadySigned(alreadySignedAccountsCsv);

        JsonArray signers = new JsonArray();
        uint totalAvailableWeight = 0;
        uint collectedWeight = 0;
        List<string> unknownSigners = new List<string>();

        if (signerList.SignerEntries is not null)
        {
            foreach (SignerEntryWrapper wrapper in signerList.SignerEntries)
            {
                if (wrapper.SignerEntry is not SignerEntry entry) continue;

                totalAvailableWeight += entry.SignerWeight;
                bool hasSigned = already.Contains(entry.Account);
                if (hasSigned) collectedWeight += entry.SignerWeight;

                signers.Add(new JsonObject
                {
                    ["account"] = entry.Account,
                    ["weight"] = entry.SignerWeight,
                    ["walletLocator"] = entry.WalletLocator,
                    ["hasSigned"] = hasSigned,
                });
            }
        }

        // Verify that the already-signed list doesn't include accounts that are
        // not actually on the SignerList — those signatures wouldn't count on-chain.
        if (already.Count > 0)
        {
            HashSet<string> listMembers = new HashSet<string>(
                signerList.SignerEntries?.Select(w => w.SignerEntry?.Account ?? "") ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            foreach (string a in already)
            {
                if (!listMembers.Contains(a)) unknownSigners.Add(a);
            }
        }

        long deltaToQuorum = (long)signerList.SignerQuorum - (long)collectedWeight;

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["hasSignerList"] = true,
            ["quorum"] = signerList.SignerQuorum,
            ["totalAvailableWeight"] = totalAvailableWeight,
            ["collectedWeight"] = collectedWeight,
            ["deltaToQuorum"] = deltaToQuorum < 0 ? 0 : deltaToQuorum,
            ["quorumReached"] = collectedWeight >= signerList.SignerQuorum,
            ["signers"] = signers,
            ["unknownSignersIgnored"] = new JsonArray(unknownSigners.Select(s => (JsonNode?)s).ToArray()),
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return result.ToJsonString();
    }

    internal static HashSet<string> ParseAlreadySigned(string? csv)
    {
        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(csv)) return result;
        foreach (string s in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            result.Add(s);
        }
        return result;
    }
}
