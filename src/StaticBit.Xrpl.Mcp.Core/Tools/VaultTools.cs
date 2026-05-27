using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using static Xrpl.Models.Common.Common;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// XLS-65 Single-Asset Vault write-flow MCP tools.
///
/// A Vault is a pooled-asset DeFi primitive: the owner creates it with an asset
/// (XRP, IOU, or MPT), depositors swap asset for share-MPTs minted by the vault's
/// pseudo-account, and the vault enforces withdrawal policies and optional access
/// control via PermissionedDomains. All tools return UNSIGNED, autofilled
/// transactions. <c>VaultID</c> is a 64-hex Hash256 visible after creation via
/// <c>xrpl_account_vaults</c>.
/// </summary>
[McpServerToolType]
public sealed class VaultTools
{
    private const uint TfVaultPrivate = 0x00010000u;
    private const uint TfVaultShareNonTransferable = 0x00020000u;

    private readonly TransactionPreparer _preparer;

    public VaultTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_vault_create_prepare")]
    [Description("Prepares an UNSIGNED VaultCreate (XLS-65). Creates a new pooled-asset vault owned by 'account'. The vault auto-issues an MPT representing pool shares. assetCurrency='XRP' (with empty issuer) for an XRP vault, otherwise 3-char/40-hex currency code + issuer. amountValue is the initial deposit (decimal string in vault-asset units). 'assetsMaximum' is an STNumber decimal string cap (omit for no cap). 'metadataHex' is for the share MPT (≤2048 hex chars). 'dataHex' is arbitrary blob (≤512 hex chars = 256 bytes). Set 'isPrivate=true' to require domain-credentialed access (set only at creation). Set 'sharesNonTransferable=true' to lock shares to depositors (set only at creation). 'withdrawalPolicy' (uint) selects the strategy (e.g. 1=FirstComeFirstServed); 'scale' (uint) controls share decimal precision.")]
    public async Task<PreparedTransaction> VaultCreatePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Owner account of the new vault.")] string account,
        [Description("Vault asset currency code ('XRP', 3-char, or 40-hex).")] string assetCurrency,
        [Description("Vault asset issuer (empty for XRP).")] string? assetIssuer,
        [Description("Initial deposit as a decimal string in vault-asset units. For XRP pass drops as a decimal string.")] string amountValue,
        [Description("Optional max total assets (STNumber decimal string). Omit for uncapped.")] string? assetsMaximum = null,
        [Description("Optional hex-encoded metadata for the share MPT (≤2048 hex chars = 1024 bytes).")] string? metadataHex = null,
        [Description("Optional hex blob attached to the vault (≤512 hex chars = 256 bytes).")] string? dataHex = null,
        [Description("Optional permissioned-domain ID (64-hex).")] string? domainId = null,
        [Description("Optional withdrawal policy code (uint). Reserved values are amendment-defined.")] uint? withdrawalPolicy = null,
        [Description("Optional share precision scale (0..18 for IOU; fixed at 0 for XRP/MPT).")] uint? scale = null,
        [Description("Set true to make the vault private (domain-credentialed access). Only honoured at creation.")] bool isPrivate = false,
        [Description("Set true to make the shares non-transferable between accounts. Only honoured at creation.")] bool sharesNonTransferable = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(amountValue))
        {
            throw new ArgumentException("amountValue is required.", nameof(amountValue));
        }
        if (!string.IsNullOrEmpty(metadataHex))
        {
            ValidateHex(metadataHex, nameof(metadataHex));
            if (metadataHex.Length > 2048 || (metadataHex.Length & 1) != 0)
            {
                throw new ArgumentException("metadataHex must be even-length ≤2048 hex chars.", nameof(metadataHex));
            }
        }
        if (!string.IsNullOrEmpty(dataHex))
        {
            ValidateHex(dataHex, nameof(dataHex));
            if (dataHex.Length > 512 || (dataHex.Length & 1) != 0)
            {
                throw new ArgumentException("dataHex must be even-length ≤512 hex chars (= 256 bytes).", nameof(dataHex));
            }
        }
        if (!string.IsNullOrEmpty(domainId))
        {
            if (domainId.Length != 64)
            {
                throw new ArgumentException("domainId must be a 64-char hex string.", nameof(domainId));
            }
            ValidateHex(domainId, nameof(domainId));
        }
        if (scale.HasValue && scale.Value > 18)
        {
            throw new ArgumentException("scale must be 0..18.", nameof(scale));
        }

        IssuedCurrency asset = ToolDisplay.BuildAsset(assetCurrency, assetIssuer);
        Currency amount = BuildInitialAmount(assetCurrency, assetIssuer, amountValue, account);

        uint? flags = null;
        if (isPrivate || sharesNonTransferable)
        {
            uint f = 0u;
            if (isPrivate) f |= TfVaultPrivate;
            if (sharesNonTransferable) f |= TfVaultShareNonTransferable;
            flags = f;
        }

        VaultCreate tx = new VaultCreate
        {
            Account = account,
            Asset = asset,
            Amount = amount,
            AssetsMaximum = string.IsNullOrWhiteSpace(assetsMaximum) ? null : assetsMaximum,
            MPTokenMetadata = string.IsNullOrEmpty(metadataHex) ? null : metadataHex.ToUpperInvariant(),
            WithdrawalPolicy = withdrawalPolicy,
            Scale = scale,
            Data = string.IsNullOrEmpty(dataHex) ? null : dataHex.ToUpperInvariant(),
            DomainID = string.IsNullOrEmpty(domainId) ? null : domainId.ToUpperInvariant(),
            Flags = flags,
        };

        string flagDesc = flags is null ? "" :
            (isPrivate ? "private" : "") + (isPrivate && sharesNonTransferable ? "+" : "") + (sharesNonTransferable ? "non-transferable" : "");
        string summary = $"VaultCreate by {ToolDisplay.Truncate(account)}: asset={ToolDisplay.DescribeAsset(asset)}, "
            + $"initial={amountValue}"
            + (string.IsNullOrEmpty(assetsMaximum) ? "" : $", max={assetsMaximum}")
            + (string.IsNullOrEmpty(flagDesc) ? "" : $", flags={flagDesc}")
            + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_vault_set_prepare")]
    [Description("Prepares an UNSIGNED VaultSet (XLS-65). Modifies an existing vault's mutable fields: Data, AssetsMaximum, DomainID. At least one of those must be provided.")]
    public async Task<PreparedTransaction> VaultSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Vault owner account.")] string account,
        [Description("64-hex VaultID of the vault to modify.")] string vaultId,
        [Description("Optional new Data hex blob (≤512 hex chars). Mutually with no-op only.")] string? dataHex = null,
        [Description("Optional new AssetsMaximum (STNumber decimal string).")] string? assetsMaximum = null,
        [Description("Optional new domain ID (64-hex). Empty string clears it.")] string? domainId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateVaultId(vaultId);

        bool hasData = !string.IsNullOrEmpty(dataHex);
        bool hasMax = !string.IsNullOrEmpty(assetsMaximum);
        bool hasDomain = domainId is not null; // allow empty string to clear
        if (!hasData && !hasMax && !hasDomain)
        {
            throw new ArgumentException("Provide at least one of dataHex, assetsMaximum, domainId.");
        }

        if (hasData)
        {
            ValidateHex(dataHex!, nameof(dataHex));
            if (dataHex!.Length > 512 || (dataHex.Length & 1) != 0)
            {
                throw new ArgumentException("dataHex must be even-length ≤512 hex chars.", nameof(dataHex));
            }
        }
        if (hasDomain && domainId!.Length > 0)
        {
            if (domainId.Length != 64)
            {
                throw new ArgumentException("domainId must be a 64-char hex string (or empty to clear).", nameof(domainId));
            }
            ValidateHex(domainId, nameof(domainId));
        }

        VaultSet tx = new VaultSet
        {
            Account = account,
            VaultID = vaultId.ToUpperInvariant(),
            Data = hasData ? dataHex!.ToUpperInvariant() : null,
            AssetsMaximum = hasMax ? assetsMaximum : null,
            DomainID = hasDomain ? (domainId!.Length == 0 ? "" : domainId.ToUpperInvariant()) : null,
        };

        System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
        if (hasData) parts.Add($"Data({dataHex!.Length / 2}b)");
        if (hasMax) parts.Add($"AssetsMaximum={assetsMaximum}");
        if (hasDomain) parts.Add(domainId!.Length == 0 ? "DomainID=CLEAR" : $"DomainID={ShortHex(domainId)}");
        string summary = $"VaultSet by {ToolDisplay.Truncate(account)} on {ShortHex(vaultId)}: " + string.Join(", ", parts) + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_vault_delete_prepare")]
    [Description("Prepares an UNSIGNED VaultDelete (XLS-65). Removes an empty vault (AssetsTotal must be 0). Only the vault owner may delete.")]
    public async Task<PreparedTransaction> VaultDeletePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Vault owner account.")] string account,
        [Description("64-hex VaultID of the vault to delete.")] string vaultId,
        CancellationToken cancellationToken = default)
    {
        ValidateVaultId(vaultId);

        VaultDelete tx = new VaultDelete
        {
            Account = account,
            VaultID = vaultId.ToUpperInvariant(),
        };

        string summary = $"VaultDelete by {ToolDisplay.Truncate(account)}: {ShortHex(vaultId)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_vault_deposit_prepare")]
    [Description("Prepares an UNSIGNED VaultDeposit (XLS-65). The depositor sends the vault's asset; the vault mints share-MPTs and credits them to the depositor. For a private vault, the depositor must hold a credential from the vault's PermissionedDomain.")]
    public async Task<PreparedTransaction> VaultDepositPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Depositor account.")] string account,
        [Description("64-hex VaultID.")] string vaultId,
        [Description("Vault asset currency code ('XRP', 3-char, or 40-hex). Must match the vault's asset.")] string assetCurrency,
        [Description("Vault asset issuer (empty for XRP).")] string? assetIssuer,
        [Description("Amount to deposit (decimal string; drops for XRP).")] string amountValue,
        CancellationToken cancellationToken = default)
    {
        ValidateVaultId(vaultId);
        if (string.IsNullOrWhiteSpace(amountValue))
        {
            throw new ArgumentException("amountValue is required.", nameof(amountValue));
        }

        Currency amount = BuildInitialAmount(assetCurrency, assetIssuer, amountValue, account);

        VaultDeposit tx = new VaultDeposit
        {
            Account = account,
            VaultID = vaultId.ToUpperInvariant(),
            Amount = amount,
        };

        string summary = $"VaultDeposit by {ToolDisplay.Truncate(account)} into {ShortHex(vaultId)}: {ToolDisplay.DescribeAmount(amount)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_vault_withdraw_prepare")]
    [Description("Prepares an UNSIGNED VaultWithdraw (XLS-65). Pass amountKind='asset' to withdraw an exact amount of the underlying asset (rounded shares burned), or amountKind='shares' to redeem an exact share count for the equivalent asset. The destination receives the asset and must be able to receive it (no DepositAuth blocks, trust line if IOU). Withdrawals are subject to the vault's WithdrawalPolicy.")]
    public async Task<PreparedTransaction> VaultWithdrawPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Shareholder account.")] string account,
        [Description("64-hex VaultID.")] string vaultId,
        [Description("'asset' to specify an exact underlying-asset amount, or 'shares' to redeem an exact share amount.")] string amountKind,
        [Description("Decimal amount (drops for XRP-asset; shares for amountKind='shares').")] string amountValue,
        [Description("For amountKind='asset': vault asset currency ('XRP'/3-char/40-hex).")] string? assetCurrency = null,
        [Description("For amountKind='asset': vault asset issuer (empty for XRP).")] string? assetIssuer = null,
        [Description("For amountKind='shares': 48-hex MPTokenIssuanceID of the share MPT (vault.ShareMPTID).")] string? shareMptIssuanceId = null,
        [Description("Destination account that receives the assets. Omit to receive into 'account'.")] string? destination = null,
        [Description("Optional destination tag.")] uint? destinationTag = null,
        CancellationToken cancellationToken = default)
    {
        ValidateVaultId(vaultId);
        if (string.IsNullOrWhiteSpace(amountValue))
        {
            throw new ArgumentException("amountValue is required.", nameof(amountValue));
        }

        Currency amount;
        if (string.Equals(amountKind, "asset", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(assetCurrency))
            {
                throw new ArgumentException("assetCurrency is required when amountKind='asset'.", nameof(assetCurrency));
            }
            amount = BuildInitialAmount(assetCurrency, assetIssuer, amountValue, account);
        }
        else if (string.Equals(amountKind, "shares", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(shareMptIssuanceId) || shareMptIssuanceId.Length != 48)
            {
                throw new ArgumentException("shareMptIssuanceId must be a 48-hex MPTokenIssuanceID when amountKind='shares'.", nameof(shareMptIssuanceId));
            }
            ValidateHex(shareMptIssuanceId, nameof(shareMptIssuanceId));
            // Build an MPT amount: {"value":"...","mpt_issuance_id":"<48-hex>"}
            string json = "{\"value\":\"" + amountValue + "\",\"mpt_issuance_id\":\"" + shareMptIssuanceId.ToUpperInvariant() + "\"}";
            amount = CurrencyParser.Parse(json);
        }
        else
        {
            throw new ArgumentException("amountKind must be 'asset' or 'shares'.", nameof(amountKind));
        }

        VaultWithdraw tx = new VaultWithdraw
        {
            Account = account,
            VaultID = vaultId.ToUpperInvariant(),
            Amount = amount,
            Destination = string.IsNullOrEmpty(destination) ? account : destination,
            DestinationTag = destinationTag,
        };

        string destPart = string.IsNullOrEmpty(destination) || string.Equals(destination, account, StringComparison.Ordinal)
            ? "self"
            : ToolDisplay.Truncate(destination);
        string summary = $"VaultWithdraw by {ToolDisplay.Truncate(account)} from {ShortHex(vaultId)}: "
            + $"{amountKind}={ToolDisplay.DescribeAmount(amount)} → {destPart}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_vault_clawback_prepare")]
    [Description("Prepares an UNSIGNED VaultClawback (XLS-65). Asset issuer claws back vault-deposited assets from a holder. The issuer must own the vault's asset (via tfMPTCanClawback for MPT, or asfAllowTrustLineClawback for IOU). Omit amountValue/Currency fields to claw back the maximum available.")]
    public async Task<PreparedTransaction> VaultClawbackPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Issuer account submitting the clawback.")] string account,
        [Description("64-hex VaultID.")] string vaultId,
        [Description("Holder whose deposited assets are being clawed back.")] string holder,
        [Description("Optional vault-asset currency ('XRP' not allowed for clawback). Omit together with amountValue for max.")] string? assetCurrency = null,
        [Description("Optional asset issuer (empty allowed for MPT amounts).")] string? assetIssuer = null,
        [Description("Optional amount value (decimal string). Omit for the maximum available.")] string? amountValue = null,
        CancellationToken cancellationToken = default)
    {
        ValidateVaultId(vaultId);
        if (string.IsNullOrWhiteSpace(holder))
        {
            throw new ArgumentException("holder is required.", nameof(holder));
        }
        if (string.Equals(holder, account, StringComparison.Ordinal))
        {
            throw new ArgumentException("holder must differ from account (the issuer).", nameof(holder));
        }

        Currency? amount = null;
        if (!string.IsNullOrWhiteSpace(amountValue))
        {
            if (string.IsNullOrWhiteSpace(assetCurrency))
            {
                throw new ArgumentException("assetCurrency is required when amountValue is provided.", nameof(assetCurrency));
            }
            if (string.Equals(assetCurrency, "XRP", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("VaultClawback only applies to issued tokens — XRP cannot be clawed back.", nameof(assetCurrency));
            }
            amount = BuildInitialAmount(assetCurrency, assetIssuer, amountValue, account);
        }

        VaultClawback tx = new VaultClawback
        {
            Account = account,
            VaultID = vaultId.ToUpperInvariant(),
            Holder = holder,
            Amount = amount!,
        };

        string amtDesc = amount is null ? "max available" : ToolDisplay.DescribeAmount(amount);
        string summary = $"VaultClawback by issuer {ToolDisplay.Truncate(account)} from holder {ToolDisplay.Truncate(holder)} "
            + $"on {ShortHex(vaultId)}: amount={amtDesc}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static Currency BuildInitialAmount(string currency, string? issuer, string value, string defaultIssuer)
    {
        if (string.Equals(currency, "XRP", StringComparison.OrdinalIgnoreCase))
        {
            // XRP amount = drops string
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                throw new ArgumentException("XRP amount must be a decimal drops integer string.", nameof(value));
            }
            return CurrencyParser.Parse(value);
        }

        string iss = string.IsNullOrWhiteSpace(issuer) ? defaultIssuer : issuer!;
        string json = "{\"value\":\"" + value + "\",\"currency\":\"" + currency + "\",\"issuer\":\"" + iss + "\"}";
        return CurrencyParser.Parse(json);
    }

    internal static void ValidateVaultId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("vaultId is required.", nameof(id));
        }
        if (id.Length != 64)
        {
            throw new ArgumentException("vaultId must be a 64-char hex string (Hash256).", nameof(id));
        }
        ValidateHex(id, nameof(id));
    }

    internal static void ValidateHex(string hex, string paramName)
    {
        for (int i = 0; i < hex.Length; i++)
        {
            char c = hex[i];
            bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
            if (!ok)
            {
                throw new ArgumentException($"{paramName} contains non-hex character at position {i}.", paramName);
            }
        }
    }

    internal static string ShortHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return "<null>";
        return hex.Length <= 16 ? hex : $"{hex.AsSpan(0, 8)}...{hex.AsSpan(hex.Length - 6, 6)}";
    }
}
