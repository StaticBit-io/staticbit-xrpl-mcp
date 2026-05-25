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
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
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
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
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
    [Description("Prepares an UNSIGNED DepositPreauth. Pass EITHER authorize (grant) OR unauthorize (revoke); they are mutually exclusive. Only meaningful if the account has asfDepositAuth enabled.")]
    public async Task<PreparedTransaction> DepositPreauthPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
        [Description("Account granting/revoking the deposit preauthorization.")] string account,
        [Description("Address to preauthorize (mutually exclusive with unauthorize).")] string? authorize = null,
        [Description("Address whose preauthorization should be revoked (mutually exclusive with authorize).")] string? unauthorize = null,
        CancellationToken cancellationToken = default)
    {
        bool hasAuth = !string.IsNullOrEmpty(authorize);
        bool hasUnauth = !string.IsNullOrEmpty(unauthorize);
        if (hasAuth == hasUnauth)
        {
            throw new ArgumentException("Provide exactly one of 'authorize' or 'unauthorize'.");
        }

        DepositPreauth tx = new DepositPreauth
        {
            Account = account,
            Authorize = authorize,
            Unauthorize = unauthorize,
        };

        string summary = hasAuth
            ? $"DepositPreauth on {ToolDisplay.Truncate(account)}: AUTHORIZE {ToolDisplay.Truncate(authorize)}."
            : $"DepositPreauth on {ToolDisplay.Truncate(account)}: UNAUTHORIZE {ToolDisplay.Truncate(unauthorize)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_signer_list_set_prepare")]
    [Description("Prepares an UNSIGNED SignerListSet. signerQuorum=0 DELETES the signer list (must omit signerEntries). Otherwise signerEntries is a JSON array of objects: [{\"account\":\"r...\",\"weight\":1,\"walletLocator\":\"<optional 64-char hex>\"}, ...]. Quorum must be ≤ sum of weights; up to 32 entries.")]
    public async Task<PreparedTransaction> SignerListSetPrepareAsync(
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
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
        [Description("Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL.")] string network,
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
