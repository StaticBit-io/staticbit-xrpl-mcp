using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Multi-Purpose Token (XLS-33) write-flow MCP tools.
///
/// MPT is a fungible, non-trustline token type: an issuer creates an
/// <c>MPTokenIssuance</c> ledger object; holders are tracked via per-account
/// <c>MPToken</c> ledger objects. All four prepare-tools return UNSIGNED, autofilled
/// transactions — signing happens off-server via <c>xrpl-signer</c>.
/// </summary>
[McpServerToolType]
public sealed class MptTools
{
    private readonly TransactionPreparer _preparer;

    public MptTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_mpt_issuance_create_prepare")]
    [Description("Prepares an UNSIGNED MPTokenIssuanceCreate (XLS-33). Creates a new MPTokenIssuance ledger object owned by 'account'. The on-chain MPTokenIssuanceID is derived from the issuer + Sequence after the tx is validated. Provide capability flags either via 'flagsBitmask' (raw uint) OR via the boolean convenience parameters (canLock/requireAuth/canEscrow/canTrade/canTransfer/canClawback) — these are mutually exclusive.")]
    public async Task<PreparedTransaction> MptIssuanceCreatePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Issuer account that will own this MPTokenIssuance.")] string account,
        [Description("Asset scale (0..10). The fractional unit equals 10^(-scale) of one standard unit. Default 0.")] uint? assetScale = null,
        [Description("Optional maximum total amount that may ever be issued. Decimal string (uint64, 0..9223372036854775807). Omit for no cap.")] string? maximumAmount = null,
        [Description("Optional transfer fee in 1/10 bps, 0..50000 (=0%..50%, increments of 0.001%). Only meaningful with tfMPTCanTransfer.")] uint? transferFee = null,
        [Description("Optional metadata bytes as a hex string. Max 1024 raw bytes (= 2048 hex chars). XLS-89 schema recommended.")] string? metadataHex = null,
        [Description("Optional raw bitmask combining MPTokenIssuanceCreateFlags (tfMPTCanLock=2, tfMPTRequireAuth=4, tfMPTCanEscrow=8, tfMPTCanTrade=16, tfMPTCanTransfer=32, tfMPTCanClawback=64). Mutually exclusive with the boolean convenience flags.")] uint? flagsBitmask = null,
        [Description("Convenience: enable tfMPTCanLock (issuer can lock balances).")] bool? canLock = null,
        [Description("Convenience: enable tfMPTRequireAuth (holders must be authorized).")] bool? requireAuth = null,
        [Description("Convenience: enable tfMPTCanEscrow (holders can escrow balances).")] bool? canEscrow = null,
        [Description("Convenience: enable tfMPTCanTrade (holders can trade via DEX/AMM).")] bool? canTrade = null,
        [Description("Convenience: enable tfMPTCanTransfer (non-issuer accounts can transfer balances).")] bool? canTransfer = null,
        [Description("Convenience: enable tfMPTCanClawback (issuer can clawback balances).")] bool? canClawback = null,
        CancellationToken cancellationToken = default)
    {
        if (assetScale.HasValue && assetScale.Value > 10)
        {
            throw new ArgumentException("assetScale must be between 0 and 10.", nameof(assetScale));
        }

        if (transferFee.HasValue && transferFee.Value > 50000)
        {
            throw new ArgumentException("transferFee must be between 0 and 50000 (=50%).", nameof(transferFee));
        }

        if (!string.IsNullOrEmpty(maximumAmount))
        {
            if (!ulong.TryParse(maximumAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedMax))
            {
                throw new ArgumentException("maximumAmount must be a decimal uint64 string.", nameof(maximumAmount));
            }
            if (parsedMax > 9223372036854775807UL)
            {
                throw new ArgumentException("maximumAmount cannot exceed 9223372036854775807 (max signed int64).", nameof(maximumAmount));
            }
        }

        if (!string.IsNullOrEmpty(metadataHex))
        {
            ValidateHex(metadataHex, nameof(metadataHex));
            if (metadataHex.Length > 2048)
            {
                throw new ArgumentException("metadataHex max length is 2048 hex chars (1024 raw bytes).", nameof(metadataHex));
            }
        }

        MPTokenIssuanceCreateFlags? composed = ComposeIssuanceCreateFlags(
            flagsBitmask, canLock, requireAuth, canEscrow, canTrade, canTransfer, canClawback);

        if (transferFee.HasValue && transferFee.Value > 0
            && (composed is null || (composed.Value & MPTokenIssuanceCreateFlags.tfMPTCanTransfer) == 0))
        {
            throw new ArgumentException(
                "transferFee>0 requires tfMPTCanTransfer (enable canTransfer=true or include 32 in flagsBitmask).",
                nameof(transferFee));
        }

        MPTokenIssuanceCreate tx = new MPTokenIssuanceCreate
        {
            Account = account,
            AssetScale = assetScale,
            MaximumAmount = string.IsNullOrEmpty(maximumAmount) ? null : maximumAmount,
            TransferFee = transferFee.HasValue ? (ushort?)transferFee.Value : null,
            MPTokenMetadata = string.IsNullOrEmpty(metadataHex) ? null : metadataHex.ToUpperInvariant(),
            Flags = composed,
        };

        string flagsDesc = composed is null ? "none" : composed.Value.ToString();
        string summary = $"MPTokenIssuanceCreate by {ToolDisplay.Truncate(account)}: assetScale={assetScale?.ToString(CultureInfo.InvariantCulture) ?? "0"}, "
            + $"maxAmount={(string.IsNullOrEmpty(maximumAmount) ? "uncapped" : maximumAmount)}, "
            + $"transferFee={transferFee?.ToString(CultureInfo.InvariantCulture) ?? "0"}, flags=[{flagsDesc}].";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_mpt_issuance_destroy_prepare")]
    [Description("Prepares an UNSIGNED MPTokenIssuanceDestroy (XLS-33). Removes an MPTokenIssuance ledger object. Allowed only when OutstandingAmount=0 — i.e. no holders currently hold any balance.")]
    public async Task<PreparedTransaction> MptIssuanceDestroyPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Issuer account that owns this MPTokenIssuance.")] string account,
        [Description("MPTokenIssuanceID — 48-hex uppercase identifier of the issuance to destroy.")] string mptokenIssuanceId,
        CancellationToken cancellationToken = default)
    {
        ValidateMptIssuanceId(mptokenIssuanceId);

        MPTokenIssuanceDestroy tx = new MPTokenIssuanceDestroy
        {
            Account = account,
            MPTokenIssuanceID = mptokenIssuanceId.ToUpperInvariant(),
        };

        string summary = $"MPTokenIssuanceDestroy by {ToolDisplay.Truncate(account)}: id={ShortMptId(mptokenIssuanceId)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_mpt_issuance_set_prepare")]
    [Description("Prepares an UNSIGNED MPTokenIssuanceSet (XLS-33). Locks or unlocks an MPTokenIssuance globally — or, when 'holder' is provided, only that holder's balance. Pass lock=true for tfMPTLock, lock=false for tfMPTUnlock, lock=null for a no-op (e.g. when the SDK adds additional non-flag fields in the future).")]
    public async Task<PreparedTransaction> MptIssuanceSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Issuer account that owns this MPTokenIssuance.")] string account,
        [Description("MPTokenIssuanceID — 48-hex uppercase.")] string mptokenIssuanceId,
        [Description("Optional holder address — applies the lock/unlock to a single holder. Omit for global lock/unlock.")] string? holder = null,
        [Description("true → tfMPTLock; false → tfMPTUnlock; null → no flag (no-op).")] bool? lockBalance = null,
        CancellationToken cancellationToken = default)
    {
        ValidateMptIssuanceId(mptokenIssuanceId);

        MPTokenIssuanceSetFlags? flags = lockBalance switch
        {
            true => MPTokenIssuanceSetFlags.tfMPTLock,
            false => MPTokenIssuanceSetFlags.tfMPTUnlock,
            null => null,
        };

        MPTokenIssuanceSet tx = new MPTokenIssuanceSet
        {
            Account = account,
            MPTokenIssuanceID = mptokenIssuanceId.ToUpperInvariant(),
            Holder = string.IsNullOrEmpty(holder) ? null : holder,
            Flags = flags,
        };

        string scope = string.IsNullOrEmpty(holder) ? "global" : $"holder={ToolDisplay.Truncate(holder)}";
        string action = lockBalance switch
        {
            true => "LOCK",
            false => "UNLOCK",
            null => "NO-OP",
        };
        string summary = $"MPTokenIssuanceSet by {ToolDisplay.Truncate(account)}: id={ShortMptId(mptokenIssuanceId)}, scope={scope}, action={action}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_mpt_authorize_prepare")]
    [Description("Prepares an UNSIGNED MPTokenAuthorize (XLS-33). Two roles: (a) a HOLDER opts in to hold an MPT by submitting with their own account and no 'holder' field; (b) an ISSUER explicitly authorizes a specific holder by submitting with the issuer account and 'holder' set to the holder's address. In either role, set unauthorize=true to revoke (tfMPTUnauthorize) — a holder with non-zero balance cannot revoke; an issuer can only revoke for MPTs that use allow-listing (tfMPTRequireAuth).")]
    public async Task<PreparedTransaction> MptAuthorizePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender account — either the holder (opting in) or the issuer (authorizing a specific holder).")] string account,
        [Description("MPTokenIssuanceID — 48-hex uppercase.")] string mptokenIssuanceId,
        [Description("Optional holder address — set when 'account' is the issuer authorizing/revoking that holder. Omit when 'account' is the holder opting in/out.")] string? holder = null,
        [Description("true → tfMPTUnauthorize (revoke); false (default) → authorize/opt-in.")] bool unauthorize = false,
        CancellationToken cancellationToken = default)
    {
        ValidateMptIssuanceId(mptokenIssuanceId);

        if (!string.IsNullOrEmpty(holder) && string.Equals(holder, account, StringComparison.Ordinal))
        {
            throw new ArgumentException("holder must differ from account; omit holder for self opt-in.", nameof(holder));
        }

        MPTokenAuthorize tx = new MPTokenAuthorize
        {
            Account = account,
            MPTokenIssuanceID = mptokenIssuanceId.ToUpperInvariant(),
            Holder = string.IsNullOrEmpty(holder) ? null : holder,
            Flags = unauthorize ? MPTokenAuthorizeFlags.tfMPTUnauthorize : null,
        };

        string role = string.IsNullOrEmpty(holder)
            ? (unauthorize ? "holder OPT-OUT" : "holder OPT-IN")
            : (unauthorize ? $"issuer REVOKE for {ToolDisplay.Truncate(holder)}" : $"issuer AUTHORIZE {ToolDisplay.Truncate(holder)}");
        string summary = $"MPTokenAuthorize by {ToolDisplay.Truncate(account)}: id={ShortMptId(mptokenIssuanceId)}, {role}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static MPTokenIssuanceCreateFlags? ComposeIssuanceCreateFlags(
        uint? bitmask,
        bool? canLock,
        bool? requireAuth,
        bool? canEscrow,
        bool? canTrade,
        bool? canTransfer,
        bool? canClawback)
    {
        bool hasBool = canLock.GetValueOrDefault() || requireAuth.GetValueOrDefault()
            || canEscrow.GetValueOrDefault() || canTrade.GetValueOrDefault()
            || canTransfer.GetValueOrDefault() || canClawback.GetValueOrDefault();

        if (bitmask.HasValue && bitmask.Value != 0 && hasBool)
        {
            throw new ArgumentException(
                "Specify either flagsBitmask OR the boolean convenience flags — not both.");
        }

        if (bitmask.HasValue && bitmask.Value != 0)
        {
            const uint allowedMask =
                (uint)(MPTokenIssuanceCreateFlags.tfMPTCanLock
                    | MPTokenIssuanceCreateFlags.tfMPTRequireAuth
                    | MPTokenIssuanceCreateFlags.tfMPTCanEscrow
                    | MPTokenIssuanceCreateFlags.tfMPTCanTrade
                    | MPTokenIssuanceCreateFlags.tfMPTCanTransfer
                    | MPTokenIssuanceCreateFlags.tfMPTCanClawback);
            if ((bitmask.Value & ~allowedMask) != 0)
            {
                throw new ArgumentException(
                    $"flagsBitmask contains unknown bits; allowed mask is 0x{allowedMask:X} (2|4|8|16|32|64).",
                    nameof(bitmask));
            }
            return (MPTokenIssuanceCreateFlags)bitmask.Value;
        }

        if (!hasBool) return null;

        MPTokenIssuanceCreateFlags result = 0;
        if (canLock == true) result |= MPTokenIssuanceCreateFlags.tfMPTCanLock;
        if (requireAuth == true) result |= MPTokenIssuanceCreateFlags.tfMPTRequireAuth;
        if (canEscrow == true) result |= MPTokenIssuanceCreateFlags.tfMPTCanEscrow;
        if (canTrade == true) result |= MPTokenIssuanceCreateFlags.tfMPTCanTrade;
        if (canTransfer == true) result |= MPTokenIssuanceCreateFlags.tfMPTCanTransfer;
        if (canClawback == true) result |= MPTokenIssuanceCreateFlags.tfMPTCanClawback;
        return result;
    }

    internal static void ValidateMptIssuanceId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("mptokenIssuanceId is required.", nameof(id));
        }
        if (id.Length != 48)
        {
            throw new ArgumentException("mptokenIssuanceId must be 48 hex chars (192-bit, XLS-33).", nameof(id));
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

    internal static string ShortMptId(string id)
    {
        if (string.IsNullOrEmpty(id)) return "<null>";
        return id.Length <= 16 ? id : $"{id.AsSpan(0, 8)}...{id.AsSpan(id.Length - 6, 6)}";
    }
}
