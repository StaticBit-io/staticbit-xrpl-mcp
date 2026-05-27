using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Account-management write-flow tools: AccountSet, SetRegularKey, DepositPreauth,
/// SignerListSet, AccountDelete. All return UNSIGNED, autofilled transactions.
/// </summary>
[McpServerToolType]
public sealed class AccountManagementTools
{
    private readonly TransactionPreparer _preparer;

    public AccountManagementTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_account_set_prepare")]
    [Description("Prepares an UNSIGNED AccountSet. Use setFlag/clearFlag to toggle ONE asf flag per transaction (e.g. asfDefaultRipple=8, asfRequireAuth=2, asfRequireDest=1, asfDisallowXRP=3, asfDisableMaster=4, asfNoFreeze=6, asfGlobalFreeze=7, asfDepositAuth=9, asfAuthorizedNFTokenMinter=10, asfDisallowIncomingNFTokenOffer=12, asfDisallowIncomingCheck=13, asfDisallowIncomingPayChan=14, asfDisallowIncomingTrustline=15, asfAllowTrustLineClawback=16, asfAllowTrustLineLocking=17). Domain must be lowercase ASCII hex-encoded.")]
    public async Task<PreparedTransaction> AccountSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account whose settings are being modified.")] string account,
        [Description("Optional asf flag to ENABLE (one per transaction).")] uint? setFlag = null,
        [Description("Optional asf flag to DISABLE (one per transaction).")] uint? clearFlag = null,
        [Description("Optional domain (ASCII lowercase, hex-encoded; max 256 chars).")] string? domain = null,
        [Description("Optional 32-byte hex MD5 of an email address (for Gravatar).")] string? emailHash = null,
        [Description("Optional public key for encrypted messages, hex.")] string? messageKey = null,
        [Description("Optional transfer rate (1_000_000_000 = 1.0, 0 = no fee, max 2_000_000_000).")] uint? transferRate = null,
        [Description("Optional tick size for issued offers (3..15, or 0 to disable).")] uint? tickSize = null,
        [Description("Optional authorized NFToken minter (r-address).")] string? nfTokenMinter = null,
        CancellationToken cancellationToken = default)
    {
        AccountSet tx = new AccountSet
        {
            Account = account,
            SetFlag = setFlag.HasValue ? (AccountSetAsfFlags)setFlag.Value : null,
            ClearFlag = clearFlag.HasValue ? (AccountSetAsfFlags)clearFlag.Value : null,
            Domain = domain,
            EmailHash = emailHash,
            MessageKey = messageKey,
            TransferRate = transferRate,
            TickSize = tickSize,
            NFTokenMinter = nfTokenMinter,
        };

        List<string> parts = new List<string>();
        if (setFlag.HasValue) parts.Add($"SetFlag={(AccountSetAsfFlags)setFlag.Value}");
        if (clearFlag.HasValue) parts.Add($"ClearFlag={(AccountSetAsfFlags)clearFlag.Value}");
        if (!string.IsNullOrEmpty(domain)) parts.Add("Domain");
        if (transferRate.HasValue) parts.Add($"TransferRate={transferRate}");
        if (tickSize.HasValue) parts.Add($"TickSize={tickSize}");
        if (!string.IsNullOrEmpty(nfTokenMinter)) parts.Add($"NFTokenMinter={ToolDisplay.Truncate(nfTokenMinter)}");
        string changes = parts.Count == 0 ? "no-op" : string.Join(", ", parts);

        string summary = $"AccountSet on {ToolDisplay.Truncate(account)}: {changes}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_set_regular_key_prepare")]
    [Description("Prepares an UNSIGNED SetRegularKey. Pass regularKey=null (or omit) to REMOVE the existing regular key pair. Must not match the master key pair for the account.")]
    public async Task<PreparedTransaction> SetRegularKeyPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account whose regular key is being assigned/cleared.")] string account,
        [Description("New regular key address (classic r-address). Omit to REMOVE.")] string? regularKey = null,
        CancellationToken cancellationToken = default)
    {
        SetRegularKey tx = new SetRegularKey
        {
            Account = account,
            RegularKey = regularKey,
        };

        string summary = string.IsNullOrEmpty(regularKey)
            ? $"SetRegularKey on {ToolDisplay.Truncate(account)}: REMOVE existing regular key."
            : $"SetRegularKey on {ToolDisplay.Truncate(account)}: set to {ToolDisplay.Truncate(regularKey)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_deposit_preauth_prepare")]
    [Description("Prepares an UNSIGNED DepositPreauth. Pass EXACTLY ONE of: authorize (grant by address), unauthorize (revoke by address), authorizeCredentialsJson (XLS-70 grant by credential set, 1-8 entries), unauthorizeCredentialsJson (XLS-70 revoke by credential set, 1-8 entries). Only meaningful if the account has asfDepositAuth enabled. Credential entries shape: [{\"issuer\":\"r...\",\"credentialType\":\"<hex>\"}, ...].")]
    public async Task<PreparedTransaction> DepositPreauthPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account granting/revoking the deposit preauthorization.")] string account,
        [Description("Address to preauthorize (mutually exclusive with other variants).")] string? authorize = null,
        [Description("Address whose preauthorization should be revoked (mutually exclusive with other variants).")] string? unauthorize = null,
        [Description("XLS-70: JSON array of {issuer,credentialType-hex}, 1-8 entries. Holders presenting ALL of these credentials are preauthorized. Mutually exclusive with the other variants.")] string? authorizeCredentialsJson = null,
        [Description("XLS-70: JSON array of {issuer,credentialType-hex}, 1-8 entries — revoke a credential-based preauth granted earlier. Mutually exclusive with the other variants.")] string? unauthorizeCredentialsJson = null,
        CancellationToken cancellationToken = default)
    {
        bool hasAuth = !string.IsNullOrEmpty(authorize);
        bool hasUnauth = !string.IsNullOrEmpty(unauthorize);
        bool hasAuthCreds = !string.IsNullOrWhiteSpace(authorizeCredentialsJson);
        bool hasUnauthCreds = !string.IsNullOrWhiteSpace(unauthorizeCredentialsJson);

        int provided = (hasAuth ? 1 : 0) + (hasUnauth ? 1 : 0) + (hasAuthCreds ? 1 : 0) + (hasUnauthCreds ? 1 : 0);
        if (provided != 1)
        {
            throw new ArgumentException(
                "Provide exactly one of 'authorize', 'unauthorize', 'authorizeCredentialsJson' or 'unauthorizeCredentialsJson'.");
        }

        DepositPreauth tx = new DepositPreauth
        {
            Account = account,
            Authorize = authorize,
            Unauthorize = unauthorize,
            AuthorizeCredentials = hasAuthCreds ? ParseCredentialEntries(authorizeCredentialsJson!, nameof(authorizeCredentialsJson)) : null!,
            UnauthorizeCredentials = hasUnauthCreds ? ParseCredentialEntries(unauthorizeCredentialsJson!, nameof(unauthorizeCredentialsJson)) : null!,
        };

        string summary = hasAuth
            ? $"DepositPreauth on {ToolDisplay.Truncate(account)}: AUTHORIZE {ToolDisplay.Truncate(authorize)}."
            : hasUnauth
                ? $"DepositPreauth on {ToolDisplay.Truncate(account)}: UNAUTHORIZE {ToolDisplay.Truncate(unauthorize)}."
                : hasAuthCreds
                    ? $"DepositPreauth on {ToolDisplay.Truncate(account)}: AUTHORIZE via {tx.AuthorizeCredentials?.Count} credential(s)."
                    : $"DepositPreauth on {ToolDisplay.Truncate(account)}: UNAUTHORIZE {tx.UnauthorizeCredentials?.Count} credential(s).";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_ticket_create_prepare")]
    [Description("Prepares an UNSIGNED TicketCreate. Reserves ticketCount sequence numbers as Tickets that can later be consumed via the TicketSequence field on any future transaction (instead of Sequence). Each Ticket is one owner-object (+2 XRP reserve). ticketCount must be 1..250 and the account must end up owning ≤250 Tickets total.")]
    public async Task<PreparedTransaction> TicketCreatePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account that will own the new Tickets.")] string account,
        [Description("How many Tickets to create (1..250). Each reserves one sequence number + one owner-object slot (~2 XRP reserve).")] uint ticketCount,
        CancellationToken cancellationToken = default)
    {
        if (ticketCount < 1 || ticketCount > 250)
        {
            throw new ArgumentException("ticketCount must be between 1 and 250.", nameof(ticketCount));
        }

        TicketCreate tx = new TicketCreate
        {
            Account = account,
            TicketCount = ticketCount,
        };

        string summary = $"TicketCreate by {ToolDisplay.Truncate(account)}: reserve {ticketCount} Ticket(s).";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_delegate_set_prepare")]
    [Description("Prepares an UNSIGNED DelegateSet (XLS-75). Grants 'delegate' permission to submit, on behalf of 'account', transactions of the listed types. 'permissionsCsv' is a comma-separated list of transaction-type names (e.g. 'Payment,TrustSet,OfferCreate'); 1..10 entries, no duplicates. The following types CANNOT be delegated: AccountSet, SetRegularKey, SignerListSet, DelegateSet. Pass an empty/whitespace permissionsCsv to clear the delegation.")]
    public async Task<PreparedTransaction> DelegateSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account granting (or clearing) the delegation.")] string account,
        [Description("Delegatee classic XRP address — the account allowed to submit on behalf of 'account'.")] string delegateAccount,
        [Description("Comma-separated transaction-type names (1..10), e.g. 'Payment,TrustSet'. Empty string clears all delegated permissions.")] string permissionsCsv,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(delegateAccount))
        {
            throw new ArgumentException("delegateAccount is required.", nameof(delegateAccount));
        }
        if (string.Equals(account, delegateAccount, StringComparison.Ordinal))
        {
            throw new ArgumentException("delegateAccount must differ from account.", nameof(delegateAccount));
        }

        List<Dictionary<string, object>> permissions = ParseDelegatePermissions(permissionsCsv);

        Dictionary<string, object> tx = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TransactionType"] = "DelegateSet",
            ["Account"] = account,
            ["Authorize"] = delegateAccount,
            ["Permissions"] = permissions,
        };

        string summary = permissions.Count == 0
            ? $"DelegateSet by {ToolDisplay.Truncate(account)}: CLEAR delegation to {ToolDisplay.Truncate(delegateAccount)}."
            : $"DelegateSet by {ToolDisplay.Truncate(account)}: delegate to {ToolDisplay.Truncate(delegateAccount)} for {permissions.Count} tx-type(s).";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static List<Dictionary<string, object>> ParseDelegatePermissions(string csv)
    {
        List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
        if (string.IsNullOrWhiteSpace(csv)) return result;

        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
            {
                throw new ArgumentException("permissionsCsv contains an empty entry.");
            }
            if (DelegateNonDelegableTypes.Contains(raw))
            {
                throw new ArgumentException(
                    $"Transaction type '{raw}' cannot be delegated (security-critical types are blocked by rippled).");
            }
            if (!DelegateTxTypeCodes.TryGetValue(raw, out uint code))
            {
                throw new ArgumentException(
                    $"Unknown transaction type '{raw}'. Pass a canonical XRPL TransactionType name (e.g. Payment, TrustSet, OfferCreate).");
            }
            if (!seen.Add(raw))
            {
                throw new ArgumentException($"permissionsCsv contains duplicate '{raw}'.");
            }

            result.Add(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["Permission"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    // PermissionValue is the numeric XRPL TransactionType code, not the string name —
                    // the binary codec serializes this as UInt32. The map below mirrors the canonical
                    // TRANSACTION_TYPES table from rippled / xrpl.js definitions.json.
                    ["PermissionValue"] = code,
                },
            });
        }

        if (result.Count > 10)
        {
            throw new ArgumentException("permissionsCsv cannot contain more than 10 entries.");
        }

        return result;
    }

    private static readonly HashSet<string> DelegateNonDelegableTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AccountSet", "SetRegularKey", "SignerListSet", "DelegateSet",
    };

    /// <summary>
    /// Canonical XRPL TransactionType numeric codes — required for the
    /// <c>DelegateSet.Permissions[].PermissionValue</c> field, which the binary
    /// codec serializes as UInt32. Mirrors <c>TRANSACTION_TYPES</c> in rippled /
    /// xrpl.js <c>definitions.json</c>. Excludes non-delegable security-critical
    /// types (handled separately via <see cref="DelegateNonDelegableTypes"/>).
    /// </summary>
    private static readonly Dictionary<string, uint> DelegateTxTypeCodes = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["Payment"] = 0,
        ["EscrowCreate"] = 1,
        ["EscrowFinish"] = 2,
        ["EscrowCancel"] = 4,
        ["OfferCreate"] = 7,
        ["OfferCancel"] = 8,
        ["TicketCreate"] = 10,
        ["PaymentChannelCreate"] = 13,
        ["PaymentChannelFund"] = 14,
        ["PaymentChannelClaim"] = 15,
        ["CheckCreate"] = 16,
        ["CheckCash"] = 17,
        ["CheckCancel"] = 18,
        ["DepositPreauth"] = 19,
        ["TrustSet"] = 20,
        ["AccountDelete"] = 21,
        ["NFTokenMint"] = 25,
        ["NFTokenBurn"] = 26,
        ["NFTokenCreateOffer"] = 27,
        ["NFTokenCancelOffer"] = 28,
        ["NFTokenAcceptOffer"] = 29,
        ["Clawback"] = 30,
        ["AMMClawback"] = 31,
        ["AMMCreate"] = 35,
        ["AMMDeposit"] = 36,
        ["AMMWithdraw"] = 37,
        ["AMMVote"] = 38,
        ["AMMBid"] = 39,
        ["AMMDelete"] = 40,
        ["XChainCreateClaimID"] = 41,
        ["XChainCommit"] = 42,
        ["XChainClaim"] = 43,
        ["XChainAccountCreateCommit"] = 44,
        ["XChainAddClaimAttestation"] = 45,
        ["XChainAddAccountCreateAttestation"] = 46,
        ["XChainModifyBridge"] = 47,
        ["XChainCreateBridge"] = 48,
        ["DIDSet"] = 49,
        ["DIDDelete"] = 50,
        ["OracleSet"] = 51,
        ["OracleDelete"] = 52,
        ["LedgerStateFix"] = 53,
        ["MPTokenIssuanceCreate"] = 54,
        ["MPTokenIssuanceDestroy"] = 55,
        ["MPTokenIssuanceSet"] = 56,
        ["MPTokenAuthorize"] = 57,
        ["CredentialCreate"] = 58,
        ["CredentialAccept"] = 59,
        ["CredentialDelete"] = 60,
        ["NFTokenModify"] = 61,
        ["PermissionedDomainSet"] = 62,
        ["PermissionedDomainDelete"] = 63,
        ["VaultCreate"] = 65,
        ["VaultSet"] = 66,
        ["VaultDelete"] = 67,
        ["VaultDeposit"] = 68,
        ["VaultWithdraw"] = 69,
        ["VaultClawback"] = 70,
        ["Batch"] = 71,
        ["LoanBrokerSet"] = 74,
        ["LoanBrokerDelete"] = 75,
        ["LoanBrokerCoverDeposit"] = 76,
        ["LoanBrokerCoverWithdraw"] = 77,
        ["LoanBrokerCoverClawback"] = 78,
        ["LoanSet"] = 80,
        ["LoanDelete"] = 81,
        ["LoanManage"] = 82,
        ["LoanPay"] = 84,
    };

    internal static List<AuthorizeCredentialEntry> ParseCredentialEntries(string json, string paramName)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"{paramName} must be a JSON array of {{issuer, credentialType}} objects.");
        }

        List<AuthorizeCredentialEntry> result = new List<AuthorizeCredentialEntry>();
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"Each {paramName} entry must be a JSON object.");
            }

            string? issuer = el.TryGetProperty("issuer", out JsonElement i) && i.ValueKind == JsonValueKind.String
                ? i.GetString()
                : null;
            string? credentialType = el.TryGetProperty("credentialType", out JsonElement c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            if (string.IsNullOrEmpty(issuer))
            {
                throw new ArgumentException($"{paramName} entry is missing 'issuer'.");
            }
            if (string.IsNullOrEmpty(credentialType))
            {
                throw new ArgumentException($"{paramName} entry is missing 'credentialType' (hex string).");
            }

            result.Add(new AuthorizeCredentialEntry
            {
                Credential = new AuthorizeCredentialBody
                {
                    Issuer = issuer,
                    CredentialType = credentialType,
                },
            });
        }

        if (result.Count == 0)
        {
            throw new ArgumentException($"{paramName} must contain at least one entry (XLS-70 requires 1-8).");
        }
        if (result.Count > 8)
        {
            throw new ArgumentException($"{paramName} cannot contain more than 8 entries (XLS-70 limit).");
        }
        return result;
    }

    [McpServerTool(Name = "xrpl_signer_list_set_prepare")]
    [Description("Prepares an UNSIGNED SignerListSet. signerQuorum=0 DELETES the signer list (must omit signerEntries). Otherwise signerEntries is a JSON array of objects: [{\"account\":\"r...\",\"weight\":1,\"walletLocator\":\"<optional 64-char hex>\"}, ...]. Quorum must be ≤ sum of weights; up to 32 entries.")]
    public async Task<PreparedTransaction> SignerListSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account that owns this signer list.")] string account,
        [Description("Required signer weight sum. 0 = DELETE the signer list.")] uint signerQuorum,
        [Description("JSON array of signer entries: [{\"account\":\"r...\",\"weight\":1,\"walletLocator\":\"<hex>\"}]. Required when signerQuorum>0; must be omitted when signerQuorum=0.")] string? signerEntriesJson = null,
        CancellationToken cancellationToken = default)
    {
        List<SignerEntryWrapper> entries = ParseSignerEntries(signerEntriesJson, signerQuorum);

        SignerListSet tx = new SignerListSet
        {
            Account = account,
            SignerQuorum = signerQuorum,
            SignerEntries = entries,
        };

        string summary = signerQuorum == 0
            ? $"SignerListSet on {ToolDisplay.Truncate(account)}: DELETE signer list."
            : $"SignerListSet on {ToolDisplay.Truncate(account)}: quorum={signerQuorum} over {entries.Count} signer(s).";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_account_delete_prepare")]
    [Description("Prepares an UNSIGNED AccountDelete. The account must have no owned objects (trust lines, offers, escrows, etc.) and Sequence + 256 must be ≤ current ledger sequence. Reserve fee is high — typically 2 XRP — and the residual XRP balance is sent to 'destination'.")]
    public async Task<PreparedTransaction> AccountDeletePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account to be deleted.")] string account,
        [Description("Funded destination that receives leftover XRP. Must NOT equal account.")] string destination,
        [Description("Optional destination tag (uint32).")] uint? destinationTag = null,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(account, destination, StringComparison.Ordinal))
        {
            throw new ArgumentException("Destination must differ from the account being deleted.", nameof(destination));
        }

        AccountDelete tx = new AccountDelete
        {
            Account = account,
            Destination = destination,
            DestinationTag = destinationTag,
        };

        string tagSuffix = destinationTag.HasValue ? $" (DestTag {destinationTag.Value})" : string.Empty;
        string summary = $"AccountDelete: delete {ToolDisplay.Truncate(account)}, send residual XRP to {ToolDisplay.Truncate(destination)}{tagSuffix}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static List<SignerEntryWrapper> ParseSignerEntries(string? json, uint quorum)
    {
        if (quorum == 0)
        {
            if (!string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("When signerQuorum=0 (delete), signerEntriesJson must be omitted.");
            }
            return null!;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("signerEntriesJson is required when signerQuorum>0.");
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("signerEntriesJson must be a JSON array.");
        }

        List<SignerEntryWrapper> result = new List<SignerEntryWrapper>();
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Each signer entry must be a JSON object.");
            }

            string? acc = el.TryGetProperty("account", out JsonElement a) ? a.GetString() : null;
            if (string.IsNullOrEmpty(acc))
            {
                throw new ArgumentException("Each signer entry must have a non-empty 'account'.");
            }

            ushort weight;
            if (el.TryGetProperty("weight", out JsonElement w) && w.ValueKind == JsonValueKind.Number)
            {
                weight = (ushort)w.GetUInt32();
            }
            else
            {
                throw new ArgumentException($"Signer entry for {acc} must have integer 'weight'.");
            }

            string? walletLocator = el.TryGetProperty("walletLocator", out JsonElement wl) && wl.ValueKind == JsonValueKind.String
                ? wl.GetString()
                : null;

            result.Add(new SignerEntryWrapper
            {
                SignerEntry = new SignerEntry
                {
                    Account = acc,
                    SignerWeight = weight,
                    WalletLocator = walletLocator,
                },
            });
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("signerEntriesJson must contain at least one entry.");
        }
        if (result.Count > 32)
        {
            throw new ArgumentException("signerEntriesJson cannot contain more than 32 entries.");
        }

        return result;
    }
}
