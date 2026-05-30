using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Auth.ResourceServer;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

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
        AddressValidation.AssertValid(account, nameof(account));
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
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:account_escrows:{network}:{account}");
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
        AddressValidation.AssertValid(account, nameof(account));
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
            return UntrustedContent.Wrap(empty.ToJsonString(), $"xrpl:signer_list_status:{network}:{account}");
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
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:signer_list_status:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_mpt_issuances")]
    [Description("Lists every MPTokenIssuance ledger object owned by the account (i.e. issuances where the account is the issuer). Returns: id (MPTokenIssuanceID, 48-hex), assetScale, maximumAmount, outstandingAmount, lockedAmount, transferFee, flags (decoded), metadataHex, metadataAscii (best-effort), sequence, previousTxnId. Use this to enumerate issuances you control before destroy/set operations.")]
    public async Task<string> AccountMptIssuancesAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address to inspect (the issuer).")] string account,
        [Description("Page size (server-clamped). Omit for default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.MPTokenIssuance,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Limit = limit,
            Marker = marker,
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        JsonArray issuances = new JsonArray();
        if (response.AccountObjectList is not null)
        {
            foreach (object obj in response.AccountObjectList)
            {
                if (obj is not LOMPTokenIssuance entry) continue;

                JsonObject row = new JsonObject
                {
                    ["index"] = entry.Index,
                    ["id"] = entry.MPTokenIssuanceID,
                    ["issuer"] = entry.Issuer,
                    ["assetScale"] = entry.AssetScale,
                    ["maximumAmount"] = entry.MaximumAmount is null ? null : entry.MaximumAmount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["outstandingAmount"] = entry.OutstandingAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["lockedAmount"] = entry.LockedAmount is null ? null : entry.LockedAmount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["transferFee"] = entry.TransferFee,
                    ["flagsBitmask"] = entry.Flags is null ? 0u : (uint)entry.Flags.Value,
                    ["flagsDecoded"] = DecodeIssuanceFlags(entry.Flags),
                    ["metadataHex"] = entry.MPTokenMetadata,
                    ["metadataAscii"] = entry.MPTokenMetadataRow,
                    ["sequence"] = entry.Sequence,
                    ["previousTxnId"] = entry.PreviousTxnID,
                    ["previousTxnLgrSeq"] = entry.PreviousTxnLgrSeq,
                };
                issuances.Add(row);
            }
        }

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["issuances"] = issuances,
            ["issuanceCount"] = issuances.Count,
            ["marker"] = response.Marker?.ToString(),
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:account_mpt_issuances:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_mpts")]
    [Description("Lists every MPToken ledger object held by the account (i.e. balances of MPTs issued by OTHER accounts). Returns: id (MPTokenIssuanceID), amount, lockedAmount, flags (locked/authorized), previousTxnId. Use this to enumerate non-trustline token holdings.")]
    public async Task<string> AccountMptsAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address to inspect (the holder).")] string account,
        [Description("Page size (server-clamped). Omit for default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.MPToken,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Limit = limit,
            Marker = marker,
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        JsonArray holdings = new JsonArray();
        if (response.AccountObjectList is not null)
        {
            foreach (object obj in response.AccountObjectList)
            {
                if (obj is not LOMPToken entry) continue;

                bool locked = entry.Flags is not null && (entry.Flags.Value & MPTokenFlags.lsfMPTLocked) != 0;
                bool authorized = entry.Flags is not null && (entry.Flags.Value & MPTokenFlags.lsfMPTAuthorized) != 0;

                JsonObject row = new JsonObject
                {
                    ["index"] = entry.Index,
                    ["id"] = entry.MPTokenIssuanceID,
                    ["amount"] = entry.MPTAmount is null ? "0" : entry.MPTAmount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["lockedAmount"] = entry.LockedAmount is null ? null : entry.LockedAmount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["flagsBitmask"] = entry.Flags is null ? 0u : (uint)entry.Flags.Value,
                    ["locked"] = locked,
                    ["authorized"] = authorized,
                    ["previousTxnId"] = entry.PreviousTxnID,
                    ["previousTxnLgrSeq"] = entry.PreviousTxnLgrSeq,
                };
                holdings.Add(row);
            }
        }

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["holdings"] = holdings,
            ["holdingCount"] = holdings.Count,
            ["marker"] = response.Marker?.ToString(),
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:account_mpts:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_credentials")]
    [Description("Lists every Credential ledger object touching the account. Splits into 'issued' (account is the issuer of the credential — provisional until subject accepts) and 'held' (account is the subject of the credential). Each entry includes counterparty, credentialType (hex + decoded UTF-8 if printable), URI (hex + decoded), accepted flag (subject accepted = lsfAccepted=0x10000), expirationUtc, previousTxnId.")]
    public async Task<string> AccountCredentialsAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address to inspect (issuer or subject).")] string account,
        [Description("Page size (server-clamped). Omit for default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.Credential,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Limit = limit,
            Marker = marker,
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        JsonArray issued = new JsonArray();
        JsonArray held = new JsonArray();

        if (response.AccountObjectList is not null)
        {
            foreach (object obj in response.AccountObjectList)
            {
                if (obj is not LOCredential cred) continue;

                bool accepted = (cred.Flags & (uint)CredentialFlags.lsfAccepted) != 0;

                JsonObject entry = new JsonObject
                {
                    ["index"] = cred.Index,
                    ["issuer"] = cred.Issuer,
                    ["subject"] = cred.Subject,
                    ["credentialTypeHex"] = cred.CredentialType,
                    ["credentialTypeUtf8"] = cred.CredentialTypeValue,
                    ["uriHex"] = cred.URI,
                    ["uriUtf8"] = cred.URIValue,
                    ["accepted"] = accepted,
                    ["flagsBitmask"] = cred.Flags,
                    ["expirationUtc"] = cred.Expiration is null
                        ? null
                        : cred.Expiration.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                    ["previousTxnId"] = cred.PreviousTxnID,
                    ["previousTxnLgrSeq"] = cred.PreviousTxnLgrSeq,
                };

                if (string.Equals(cred.Issuer, account, StringComparison.Ordinal))
                {
                    issued.Add(entry);
                }
                else
                {
                    held.Add(entry);
                }
            }
        }

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["issued"] = issued,
            ["issuedCount"] = issued.Count,
            ["held"] = held,
            ["heldCount"] = held.Count,
            ["marker"] = response.Marker?.ToString(),
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:account_credentials:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_did")]
    [Description("Returns the single DID ledger object owned by 'account' (XLS-40). Each account owns at most one DID — returns null if none exists. Includes raw hex fields (data/didDocument/uri) plus UTF-8-decoded variants where printable.")]
    public async Task<string> AccountDidAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address to inspect.")] string account,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.DID,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        LODID? did = response.AccountObjectList?.OfType<LODID>().FirstOrDefault();

        if (did is null)
        {
            return UntrustedContent.Wrap(new JsonObject
            {
                ["account"] = account,
                ["hasDid"] = false,
                ["ledgerHash"] = response.LedgerHash,
                ["ledgerIndex"] = response.LedgerIndex,
                ["validated"] = response.Validated,
            }.ToJsonString(), $"xrpl:account_did:{network}:{account}");
        }

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["hasDid"] = true,
            ["index"] = did.Index,
            ["dataHex"] = did.Data,
            ["dataUtf8"] = HexToUtf8(did.Data),
            ["didDocumentHex"] = did.DIDDocument,
            ["didDocumentUtf8"] = HexToUtf8(did.DIDDocument),
            ["uriHex"] = did.URI,
            ["uriUtf8"] = HexToUtf8(did.URI),
            ["previousTxnId"] = did.PreviousTxnID,
            ["previousTxnLgrSeq"] = did.PreviousTxnLgrSeq,
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:account_did:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_permissioned_domains")]
    [Description("Lists every PermissionedDomain ledger object owned by 'account' (XLS-80). Each entry includes the on-chain DomainID (use it for set/delete), sequence (creator's tx sequence), AcceptedCredentials list with issuer + credentialType (hex + UTF-8 decoded), previousTxnId.")]
    public async Task<string> AccountPermissionedDomainsAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address — owner of the domains.")] string account,
        [Description("Page size (server-clamped). Omit for default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.PermissionedDomain,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Limit = limit,
            Marker = marker,
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        JsonArray domains = new JsonArray();
        if (response.AccountObjectList is not null)
        {
            foreach (object obj in response.AccountObjectList)
            {
                if (obj is not LOPermissionedDomain dom) continue;

                JsonArray accepted = new JsonArray();
                if (dom.AcceptedCredentials is not null)
                {
                    foreach (AcceptedCredentialWrapper wrapper in dom.AcceptedCredentials)
                    {
                        AcceptedCredential c = wrapper.Credential;
                        if (c is null) continue;
                        accepted.Add(new JsonObject
                        {
                            ["issuer"] = c.Issuer,
                            ["credentialTypeHex"] = c.CredentialType,
                            ["credentialTypeUtf8"] = HexToUtf8(c.CredentialType),
                        });
                    }
                }

                domains.Add(new JsonObject
                {
                    ["index"] = dom.Index,
                    ["domainId"] = dom.Index,
                    ["owner"] = dom.Owner,
                    ["sequence"] = dom.Sequence,
                    ["acceptedCredentials"] = accepted,
                    ["previousTxnId"] = dom.PreviousTxnID,
                    ["previousTxnLgrSeq"] = dom.PreviousTxnLgrSeq,
                });
            }
        }

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["domains"] = domains,
            ["domainCount"] = domains.Count,
            ["marker"] = response.Marker?.ToString(),
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:account_permissioned_domains:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_vaults")]
    [Description("Lists every Vault ledger object owned by 'account' (XLS-65). Each entry includes the 64-hex VaultID (use for set/delete/deposit/etc.), pseudo-account, asset spec, AssetsTotal / AssetsAvailable / AssetsMaximum / LossUnrealized (STNumber strings), the share-MPTokenIssuanceID (ShareMPTID), withdrawal policy, scale, data (hex + parsed VaultDataFormat {n,w} when present), and the optional permissioned-domain id.")]
    public async Task<string> AccountVaultsAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address — vault owner.")] string account,
        [Description("Page size (server-clamped). Omit for default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.Vault,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Limit = limit,
            Marker = marker,
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        JsonArray vaults = new JsonArray();
        if (response.AccountObjectList is not null)
        {
            foreach (object obj in response.AccountObjectList)
            {
                if (obj is not LOVault v) continue;

                JsonObject row = new JsonObject
                {
                    ["index"] = v.Index,
                    ["vaultId"] = v.Index,
                    ["pseudoAccount"] = v.Account,
                    ["owner"] = v.Owner,
                    ["asset"] = JsonNode.Parse(XrplJson.Serialize(v.Asset)),
                    ["assetsTotal"] = v.AssetsTotal,
                    ["assetsAvailable"] = v.AssetsAvailable,
                    ["assetsMaximum"] = v.AssetsMaximum,
                    ["lossUnrealized"] = v.LossUnrealized,
                    ["shareMptIssuanceId"] = v.ShareMPTID,
                    ["withdrawalPolicy"] = v.WithdrawalPolicy,
                    ["scale"] = v.Scale,
                    ["dataHex"] = v.Data,
                    ["dataUtf8"] = v.DataRaw,
                    ["domainId"] = v.DomainID,
                    ["sequence"] = v.Sequence,
                    ["previousTxnId"] = v.PreviousTxnID,
                    ["previousTxnLgrSeq"] = v.PreviousTxnLgrSeq,
                };
                vaults.Add(row);
            }
        }

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["vaults"] = vaults,
            ["vaultCount"] = vaults.Count,
            ["marker"] = response.Marker?.ToString(),
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:account_vaults:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_bridges")]
    [Description("Lists every Bridge ledger object owned by 'account' (XLS-38 — typically the door account). Each entry includes the bridge spec (LockingChain/IssuingChain doors + issues), SignatureReward, MinAccountCreateAmount (null = AccountCreate disabled), current XChainClaimID counter, XChainAccountCreateCount / XChainAccountClaimCount counters, previousTxnId.")]
    public async Task<string> AccountBridgesAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address — bridge owner (door account on this chain).")] string account,
        [Description("Page size (server-clamped). Omit for default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.Bridge,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Limit = limit,
            Marker = marker,
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        JsonArray bridges = new JsonArray();
        if (response.AccountObjectList is not null)
        {
            foreach (object obj in response.AccountObjectList)
            {
                if (obj is not LOBridge b) continue;

                bridges.Add(new JsonObject
                {
                    ["index"] = b.Index,
                    ["account"] = b.Account,
                    ["xchainBridge"] = JsonNode.Parse(XrplJson.Serialize(b.XChainBridge)),
                    ["signatureReward"] = JsonNode.Parse(XrplJson.Serialize(b.SignatureReward)),
                    ["minAccountCreateAmount"] = b.MinAccountCreateAmount is null ? null : JsonNode.Parse(XrplJson.Serialize(b.MinAccountCreateAmount)),
                    ["xchainClaimId"] = b.XChainClaimID,
                    ["xchainAccountCreateCount"] = b.XChainAccountCreateCount,
                    ["xchainAccountClaimCount"] = b.XChainAccountClaimCount,
                    ["previousTxnId"] = b.PreviousTxnID,
                    ["previousTxnLgrSeq"] = b.PreviousTxnLgrSeq,
                });
            }
        }

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["bridges"] = bridges,
            ["bridgeCount"] = bridges.Count,
            ["marker"] = response.Marker?.ToString(),
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:account_bridges:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_loan_brokers")]
    [Description("Lists every LoanBroker ledger object owned by 'account' (XLS-66). Each entry includes the 64-hex LoanBrokerID (use for set/delete/cover-deposit/etc.), pseudo-account, underlying VaultID, debt counters (DebtTotal / DebtMaximum, STNumber strings), CoverAvailable, cover-rate thresholds (CoverRateMinimum/Liquidation in 1/100th bp), ManagementFeeRate (1/10th bp), Data hex blob, sequence, owner count (active loans).")]
    public async Task<string> AccountLoanBrokersAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address — broker owner.")] string account,
        [Description("Page size (server-clamped). Omit for default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.LoanBroker,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Limit = limit,
            Marker = marker,
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        JsonArray brokers = new JsonArray();
        if (response.AccountObjectList is not null)
        {
            foreach (object obj in response.AccountObjectList)
            {
                if (obj is not LOLoanBroker b) continue;

                brokers.Add(new JsonObject
                {
                    ["index"] = b.Index,
                    ["loanBrokerId"] = b.Index,
                    ["pseudoAccount"] = b.Account,
                    ["owner"] = b.Owner,
                    ["vaultId"] = b.VaultID,
                    ["sequence"] = b.Sequence,
                    ["loanSequence"] = b.LoanSequence,
                    ["activeLoanCount"] = b.OwnerCount,
                    ["debtTotal"] = b.DebtTotal,
                    ["debtMaximum"] = b.DebtMaximum,
                    ["coverAvailable"] = b.CoverAvailable,
                    ["coverRateMinimum"] = b.CoverRateMinimum,
                    ["coverRateLiquidation"] = b.CoverRateLiquidation,
                    ["managementFeeRate"] = b.ManagementFeeRate,
                    ["dataHex"] = b.Data,
                    ["dataUtf8"] = HexToUtf8(b.Data),
                    ["previousTxnId"] = b.PreviousTxnID,
                    ["previousTxnLgrSeq"] = b.PreviousTxnLgrSeq,
                });
            }
        }

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["loanBrokers"] = brokers,
            ["loanBrokerCount"] = brokers.Count,
            ["marker"] = response.Marker?.ToString(),
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:account_loan_brokers:{network}:{account}");
    }

    [McpServerTool(Name = "xrpl_account_loans")]
    [Description("Lists every Loan ledger object touching 'account' (XLS-66) — typically as borrower (account == Loan.Borrower) or via the broker pseudo-account. Each entry includes 64-hex LoanID (use for manage/pay/delete), Borrower, LoanBrokerID, loan sequence, all interest/fee rates and fees, principal counters (PrincipalRequested / PrincipalOutstanding / TotalValueOutstanding), PeriodicPayment, ManagementFeeOutstanding, payment schedule (PaymentInterval / GracePeriod / PaymentRemaining), StartDate / PreviousPaymentDueDate / NextPaymentDueDate (UTC ISO-8601), LoanScale, previousTxnId.")]
    public async Task<string> AccountLoansAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Classic XRP address — borrower or broker pseudo-account.")] string account,
        [Description("Page size (server-clamped). Omit for default.")] int? limit = null,
        [Description("Pagination cursor returned by the previous call.")] string? marker = null,
        [Description(ToolDescriptions.LedgerIndex)] string? ledgerIndex = null,
        CancellationToken cancellationToken = default)
    {
        AddressValidation.AssertValid(account, nameof(account));
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        AccountObjectsRequest request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.Loan,
            LedgerIndex = LedgerIndexParser.Parse(ledgerIndex),
            Limit = limit,
            Marker = marker,
        };

        AccountObjects response = await client.AccountObjects(request, cancellationToken).ConfigureAwait(false);

        JsonArray loans = new JsonArray();
        if (response.AccountObjectList is not null)
        {
            foreach (object obj in response.AccountObjectList)
            {
                if (obj is not LOLoan l) continue;

                loans.Add(new JsonObject
                {
                    ["index"] = l.Index,
                    ["loanId"] = l.Index,
                    ["borrower"] = l.Borrower,
                    ["loanBrokerId"] = l.LoanBrokerID,
                    ["loanSequence"] = l.LoanSequence,
                    ["interestRate"] = l.InterestRate,
                    ["lateInterestRate"] = l.LateInterestRate,
                    ["closeInterestRate"] = l.CloseInterestRate,
                    ["overpaymentInterestRate"] = l.OverpaymentInterestRate,
                    ["overpaymentFee"] = l.OverpaymentFee,
                    ["principalOutstanding"] = l.PrincipalOutstanding,
                    ["principalRequested"] = l.PrincipalRequested,
                    ["totalValueOutstanding"] = l.TotalValueOutstanding,
                    ["periodicPayment"] = l.PeriodicPayment,
                    ["managementFeeOutstanding"] = l.ManagementFeeOutstanding,
                    ["loanOriginationFee"] = l.LoanOriginationFee,
                    ["loanServiceFee"] = l.LoanServiceFee,
                    ["latePaymentFee"] = l.LatePaymentFee,
                    ["closePaymentFee"] = l.ClosePaymentFee,
                    ["startDate"] = l.StartDate is null ? null : l.StartDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                    ["paymentInterval"] = l.PaymentInterval,
                    ["gracePeriod"] = l.GracePeriod,
                    ["previousPaymentDueDate"] = l.PreviousPaymentDueDate is null ? null : l.PreviousPaymentDueDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                    ["nextPaymentDueDate"] = l.NextPaymentDueDate is null ? null : l.NextPaymentDueDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                    ["paymentRemaining"] = l.PaymentRemaining,
                    ["loanScale"] = l.LoanScale,
                    ["previousTxnId"] = l.PreviousTxnID,
                    ["previousTxnLgrSeq"] = l.PreviousTxnLgrSeq,
                });
            }
        }

        JsonObject result = new JsonObject
        {
            ["account"] = account,
            ["loans"] = loans,
            ["loanCount"] = loans.Count,
            ["marker"] = response.Marker?.ToString(),
            ["ledgerHash"] = response.LedgerHash,
            ["ledgerIndex"] = response.LedgerIndex,
            ["validated"] = response.Validated,
        };
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:account_loans:{network}:{account}");
    }

    internal static string? HexToUtf8(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        if ((hex.Length & 1) != 0) return null;
        try
        {
            byte[] bytes = Convert.FromHexString(hex);
            // Only return when the result is printable ASCII or valid UTF-8 without
            // control chars — otherwise leave it for callers to handle the raw hex.
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b == 0) return null;
                if (b < 0x09) return null;
            }
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }

    internal static JsonArray DecodeIssuanceFlags(MPTokenIssuanceFlags? flags)
    {
        JsonArray arr = new JsonArray();
        if (flags is null || flags.Value == MPTokenIssuanceFlags.None) return arr;
        if ((flags.Value & MPTokenIssuanceFlags.MPTLocked) != 0) arr.Add("Locked");
        if ((flags.Value & MPTokenIssuanceFlags.MPTCanLock) != 0) arr.Add("CanLock");
        if ((flags.Value & MPTokenIssuanceFlags.MPTRequireAuth) != 0) arr.Add("RequireAuth");
        if ((flags.Value & MPTokenIssuanceFlags.MPTCanEscrow) != 0) arr.Add("CanEscrow");
        if ((flags.Value & MPTokenIssuanceFlags.MPTCanTrade) != 0) arr.Add("CanTrade");
        if ((flags.Value & MPTokenIssuanceFlags.MPTCanTransfer) != 0) arr.Add("CanTransfer");
        if ((flags.Value & MPTokenIssuanceFlags.MPTCanClawback) != 0) arr.Add("CanClawback");
        return arr;
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
