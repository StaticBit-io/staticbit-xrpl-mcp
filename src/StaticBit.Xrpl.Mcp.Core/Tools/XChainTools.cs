using System;
using System.ComponentModel;
using System.Text.Json;
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
/// XLS-38 cross-chain bridge write-flow MCP tools.
///
/// Bridge flow (door account on locking-chain mirrors door on issuing-chain):
/// 1. Bridge mgr submits <c>XChainCreateBridge</c> on BOTH chains (mirrored).
/// 2. To transfer, recipient submits <c>XChainCreateClaimID</c> on destination chain.
/// 3. Sender submits <c>XChainCommit</c> on source chain with the claim id.
/// 4. Witness servers each submit <c>XChainAddClaimAttestation</c> on destination
///    chain until quorum is reached, then funds release (or recipient submits
///    <c>XChainClaim</c> explicitly).
/// Account-creation has an analogous flow (<c>XChainAccountCreateCommit</c> +
/// <c>XChainAddAccountCreateAttestation</c>) — XRP-XRP bridges only.
/// </summary>
[McpServerToolType]
public sealed class XChainTools
{
    private const uint TfClearAccountCreateAmount = 0x00010000u;

    private readonly TransactionPreparer _preparer;

    public XChainTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_xchain_create_bridge_prepare")]
    [Description("Prepares an UNSIGNED XChainCreateBridge (XLS-38). Submitted by the door account on each chain (mirrored on both chains). 'bridgeJson' is the bridge spec: {\"LockingChainDoor\":\"r...\",\"LockingChainIssue\":{currency,issuer?},\"IssuingChainDoor\":\"r...\",\"IssuingChainIssue\":{currency,issuer?}}. For XRP-XRP bridges, both Issues use {currency:'XRP'}. 'signatureRewardDrops' is the per-attestation reward in XRP drops. 'minAccountCreateDrops' (XRP drops) — if set, XChainAccountCreateCommit becomes available (XRP-XRP only); omit to disable.")]
    public async Task<PreparedTransaction> XChainCreateBridgePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Door account submitting the create (the bridge owner on this chain).")] string account,
        [Description("Bridge spec JSON {LockingChainDoor, LockingChainIssue, IssuingChainDoor, IssuingChainIssue}.")] string bridgeJson,
        [Description("Signature reward in XRP drops (string).")] string signatureRewardDrops,
        [Description("Optional MinAccountCreateAmount in XRP drops. Omit to leave AccountCreateCommit disabled.")] string? minAccountCreateDrops = null,
        CancellationToken cancellationToken = default)
    {
        XChainBridgeModel bridge = ParseBridge(bridgeJson);
        Currency reward = ParseXrpDrops(signatureRewardDrops, nameof(signatureRewardDrops));
        Currency? minCreate = string.IsNullOrWhiteSpace(minAccountCreateDrops)
            ? null
            : ParseXrpDrops(minAccountCreateDrops!, nameof(minAccountCreateDrops));

        XChainCreateBridge tx = new XChainCreateBridge
        {
            Account = account,
            XChainBridge = bridge,
            SignatureReward = reward,
            MinAccountCreateAmount = minCreate!,
        };

        string summary = $"XChainCreateBridge by {ToolDisplay.Truncate(account)}: "
            + DescribeBridge(bridge) + $", reward={signatureRewardDrops} drops"
            + (minCreate is null ? "" : $", minCreate={minAccountCreateDrops} drops")
            + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_xchain_modify_bridge_prepare")]
    [Description("Prepares an UNSIGNED XChainModifyBridge (XLS-38). Modifies signatureReward and/or minAccountCreateAmount of an existing bridge. Pass clearMinAccountCreate=true to set tfClearAccountCreateAmount (removes the AccountCreate parameter).")]
    public async Task<PreparedTransaction> XChainModifyBridgePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Door account submitting the modification.")] string account,
        [Description("Bridge spec JSON identifying the bridge to modify.")] string bridgeJson,
        [Description("Optional new signature reward in XRP drops.")] string? signatureRewardDrops = null,
        [Description("Optional new MinAccountCreateAmount in XRP drops.")] string? minAccountCreateDrops = null,
        [Description("If true, sets tfClearAccountCreateAmount — removes the existing MinAccountCreateAmount. Mutually exclusive with minAccountCreateDrops.")] bool clearMinAccountCreate = false,
        CancellationToken cancellationToken = default)
    {
        XChainBridgeModel bridge = ParseBridge(bridgeJson);

        if (clearMinAccountCreate && !string.IsNullOrWhiteSpace(minAccountCreateDrops))
        {
            throw new ArgumentException("clearMinAccountCreate=true is mutually exclusive with minAccountCreateDrops.");
        }
        if (!clearMinAccountCreate && string.IsNullOrWhiteSpace(signatureRewardDrops) && string.IsNullOrWhiteSpace(minAccountCreateDrops))
        {
            throw new ArgumentException("Provide at least one of: signatureRewardDrops, minAccountCreateDrops, or clearMinAccountCreate=true.");
        }

        XChainModifyBridge tx = new XChainModifyBridge
        {
            Account = account,
            XChainBridge = bridge,
            SignatureReward = string.IsNullOrWhiteSpace(signatureRewardDrops) ? null! : ParseXrpDrops(signatureRewardDrops!, nameof(signatureRewardDrops)),
            MinAccountCreateAmount = string.IsNullOrWhiteSpace(minAccountCreateDrops) ? null! : ParseXrpDrops(minAccountCreateDrops!, nameof(minAccountCreateDrops)),
            Flags = clearMinAccountCreate ? XChainModifyBridgeFlags.tfClearAccountCreateAmount : null,
        };

        System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(signatureRewardDrops)) parts.Add($"reward={signatureRewardDrops}");
        if (!string.IsNullOrWhiteSpace(minAccountCreateDrops)) parts.Add($"minCreate={minAccountCreateDrops}");
        if (clearMinAccountCreate) parts.Add("CLEAR minCreate");
        string summary = $"XChainModifyBridge by {ToolDisplay.Truncate(account)}: " + DescribeBridge(bridge)
            + " — " + string.Join(", ", parts) + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_xchain_create_claim_id_prepare")]
    [Description("Prepares an UNSIGNED XChainCreateClaimID (XLS-38). The recipient on the destination chain reserves a claim ID for an upcoming cross-chain transfer. 'otherChainSource' is the address on the source chain that will submit the matching XChainCommit. 'signatureRewardDrops' must match the bridge's SignatureReward.")]
    public async Task<PreparedTransaction> XChainCreateClaimIdPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Recipient account on the destination chain (will own the claim id).")] string account,
        [Description("Bridge spec JSON.")] string bridgeJson,
        [Description("Signature reward in XRP drops (must match the on-ledger bridge value).")] string signatureRewardDrops,
        [Description("Address on the SOURCE chain that will send the matching XChainCommit.")] string otherChainSource,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(otherChainSource))
        {
            throw new ArgumentException("otherChainSource is required.", nameof(otherChainSource));
        }

        XChainBridgeModel bridge = ParseBridge(bridgeJson);
        Currency reward = ParseXrpDrops(signatureRewardDrops, nameof(signatureRewardDrops));

        XChainCreateClaimID tx = new XChainCreateClaimID
        {
            Account = account,
            XChainBridge = bridge,
            SignatureReward = reward,
            OtherChainSource = otherChainSource,
        };

        string summary = $"XChainCreateClaimID by {ToolDisplay.Truncate(account)}: "
            + DescribeBridge(bridge) + $", otherChainSource={ToolDisplay.Truncate(otherChainSource)}, reward={signatureRewardDrops} drops.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_xchain_commit_prepare")]
    [Description("Prepares an UNSIGNED XChainCommit (XLS-38). On the SOURCE chain, the sender commits the cross-chain transfer amount referencing a previously-created claim ID on the destination chain. 'amountValue' is decimal drops (for XRP bridges) or a JSON IOU amount object (for IOU bridges). 'otherChainDestination' optionally specifies the destination on the destination chain — omit to require an explicit XChainClaim later.")]
    public async Task<PreparedTransaction> XChainCommitPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender account on the source chain.")] string account,
        [Description("Bridge spec JSON.")] string bridgeJson,
        [Description("XChainClaimID — decimal string for XRP-XRP bridges, or hex Hash256 depending on bridge convention.")] string xchainClaimId,
        [Description("Amount to transfer: drops decimal string for XRP-asset, or JSON {value,currency,issuer} for IOU-asset.")] string amountValue,
        [Description("Optional destination address on the destination chain. Omit to require an explicit XChainClaim.")] string? otherChainDestination = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xchainClaimId))
        {
            throw new ArgumentException("xchainClaimId is required.", nameof(xchainClaimId));
        }
        if (string.IsNullOrWhiteSpace(amountValue))
        {
            throw new ArgumentException("amountValue is required.", nameof(amountValue));
        }

        XChainBridgeModel bridge = ParseBridge(bridgeJson);
        Currency amount = CurrencyParser.Parse(amountValue);

        XChainCommit tx = new XChainCommit
        {
            Account = account,
            XChainBridge = bridge,
            XChainClaimID = xchainClaimId,
            Amount = amount,
            OtherChainDestination = string.IsNullOrEmpty(otherChainDestination) ? null : otherChainDestination,
        };

        string destPart = string.IsNullOrEmpty(otherChainDestination) ? "explicit-claim" : ToolDisplay.Truncate(otherChainDestination);
        string summary = $"XChainCommit by {ToolDisplay.Truncate(account)}: claimId={xchainClaimId}, "
            + $"amount={ToolDisplay.DescribeAmount(amount)} → {destPart} on " + DescribeBridge(bridge) + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_xchain_claim_prepare")]
    [Description("Prepares an UNSIGNED XChainClaim (XLS-38). On the destination chain, the claim id owner finalizes the transfer after attestations reach quorum. Used when XChainCommit did NOT include OtherChainDestination (so the recipient must claim explicitly), or to redirect to a different destination. The amount must match the attested amount.")]
    public async Task<PreparedTransaction> XChainClaimPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account on the destination chain that owns the claim id.")] string account,
        [Description("Bridge spec JSON.")] string bridgeJson,
        [Description("XChainClaimID (same value used in XChainCommit).")] string xchainClaimId,
        [Description("Final destination on the destination chain.")] string destination,
        [Description("Amount to claim — must match the attested amount.")] string amountValue,
        [Description("Optional destination tag.")] uint? destinationTag = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xchainClaimId))
        {
            throw new ArgumentException("xchainClaimId is required.", nameof(xchainClaimId));
        }
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("destination is required.", nameof(destination));
        }
        if (string.IsNullOrWhiteSpace(amountValue))
        {
            throw new ArgumentException("amountValue is required.", nameof(amountValue));
        }

        XChainBridgeModel bridge = ParseBridge(bridgeJson);
        Currency amount = CurrencyParser.Parse(amountValue);

        XChainClaim tx = new XChainClaim
        {
            Account = account,
            XChainBridge = bridge,
            XChainClaimID = xchainClaimId,
            Destination = destination,
            DestinationTag = destinationTag,
            Amount = amount,
        };

        string summary = $"XChainClaim by {ToolDisplay.Truncate(account)}: claimId={xchainClaimId}, "
            + $"amount={ToolDisplay.DescribeAmount(amount)} → {ToolDisplay.Truncate(destination)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_xchain_account_create_commit_prepare")]
    [Description("Prepares an UNSIGNED XChainAccountCreateCommit (XLS-38). XRP-XRP bridges only. On the source chain, locks XRP to create a NEW destination account on the destination chain. 'amountDrops' must be ≥ the bridge's MinAccountCreateAmount; 'signatureRewardDrops' must match the bridge's SignatureReward.")]
    public async Task<PreparedTransaction> XChainAccountCreateCommitPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Sender on the source chain.")] string account,
        [Description("Bridge spec JSON (must be XRP-XRP).")] string bridgeJson,
        [Description("Destination address on the destination chain (will be created if absent).")] string destination,
        [Description("Amount in XRP drops to fund the new account (≥ bridge.MinAccountCreateAmount).")] string amountDrops,
        [Description("Signature reward in XRP drops (must match bridge value).")] string signatureRewardDrops,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("destination is required.", nameof(destination));
        }

        XChainBridgeModel bridge = ParseBridge(bridgeJson);
        Currency amount = ParseXrpDrops(amountDrops, nameof(amountDrops));
        Currency reward = ParseXrpDrops(signatureRewardDrops, nameof(signatureRewardDrops));

        XChainAccountCreateCommit tx = new XChainAccountCreateCommit
        {
            Account = account,
            XChainBridge = bridge,
            Destination = destination,
            Amount = amount,
            SignatureReward = reward,
        };

        string summary = $"XChainAccountCreateCommit by {ToolDisplay.Truncate(account)}: "
            + $"create {ToolDisplay.Truncate(destination)} on issuing chain with {amountDrops} drops (reward {signatureRewardDrops}).";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_xchain_add_claim_attestation_prepare")]
    [Description("Prepares an UNSIGNED XChainAddClaimAttestation (XLS-38). Witness-server step — submits proof on the DESTINATION chain that an XChainCommit happened on the SOURCE chain. wasLockingChainSend=1 if the source was the locking chain, 0 if the source was the issuing chain.")]
    public async Task<PreparedTransaction> XChainAddClaimAttestationPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Witness account submitting the attestation.")] string account,
        [Description("Bridge spec JSON.")] string bridgeJson,
        [Description("XChainClaimID the attestation is for.")] string xchainClaimId,
        [Description("Committed amount from the source chain (drops string or IOU JSON).")] string amountValue,
        [Description("Address that should receive this signer's share of the SignatureReward.")] string attestationRewardAccount,
        [Description("Witness signer-list account on the door account.")] string attestationSignerAccount,
        [Description("Address on the SOURCE chain that submitted the XChainCommit.")] string otherChainSource,
        [Description("Public key used to verify the attestation signature (hex).")] string publicKeyHex,
        [Description("Signature attesting to the event on the other chain (hex).")] string signatureHex,
        [Description("0 if the source was the issuing chain, 1 if the source was the locking chain.")] byte wasLockingChainSend,
        [Description("Destination of the funds on the destination chain.")] string destination,
        CancellationToken cancellationToken = default)
    {
        ValidateAttestationCommon(attestationRewardAccount, attestationSignerAccount, otherChainSource,
            publicKeyHex, signatureHex, wasLockingChainSend, destination);
        if (string.IsNullOrWhiteSpace(xchainClaimId))
        {
            throw new ArgumentException("xchainClaimId is required.", nameof(xchainClaimId));
        }

        XChainBridgeModel bridge = ParseBridge(bridgeJson);
        Currency amount = CurrencyParser.Parse(amountValue);

        XChainAddClaimAttestation tx = new XChainAddClaimAttestation
        {
            Account = account,
            XChainBridge = bridge,
            XChainClaimID = xchainClaimId,
            Amount = amount,
            AttestationRewardAccount = attestationRewardAccount,
            AttestationSignerAccount = attestationSignerAccount,
            Destination = destination,
            OtherChainSource = otherChainSource,
            PublicKey = publicKeyHex.ToUpperInvariant(),
            Signature = signatureHex.ToUpperInvariant(),
            WasLockingChainSend = wasLockingChainSend,
        };

        string summary = $"XChainAddClaimAttestation by witness {ToolDisplay.Truncate(account)}: "
            + $"claimId={xchainClaimId}, amount={ToolDisplay.DescribeAmount(amount)}, "
            + $"signer={ToolDisplay.Truncate(attestationSignerAccount)}, "
            + $"wasLocking={(wasLockingChainSend == 1 ? "yes" : "no")}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_xchain_add_account_create_attestation_prepare")]
    [Description("Prepares an UNSIGNED XChainAddAccountCreateAttestation (XLS-38). Witness-server step — attests that an XChainAccountCreateCommit happened on the source chain (XRP-XRP bridges only). 'xchainAccountCreateCount' is the bridge's sequence counter for account-creates (order of processing).")]
    public async Task<PreparedTransaction> XChainAddAccountCreateAttestationPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Witness account submitting the attestation.")] string account,
        [Description("Bridge spec JSON.")] string bridgeJson,
        [Description("XChainAccountCreateCount value (decimal string).")] string xchainAccountCreateCount,
        [Description("Amount committed on the source chain (XRP drops decimal string).")] string amountDrops,
        [Description("Signature reward in XRP drops (must match bridge value).")] string signatureRewardDrops,
        [Description("Address on the source chain that submitted the AccountCreateCommit.")] string otherChainSource,
        [Description("Destination address on the destination chain (account to be created).")] string destination,
        [Description("Address that should receive this signer's share of the SignatureReward.")] string attestationRewardAccount,
        [Description("Witness signer-list account on the door account.")] string attestationSignerAccount,
        [Description("Public key (hex).")] string publicKeyHex,
        [Description("Signature (hex).")] string signatureHex,
        [Description("0 if the source was the issuing chain, 1 if the source was the locking chain.")] byte wasLockingChainSend,
        CancellationToken cancellationToken = default)
    {
        ValidateAttestationCommon(attestationRewardAccount, attestationSignerAccount, otherChainSource,
            publicKeyHex, signatureHex, wasLockingChainSend, destination);
        if (string.IsNullOrWhiteSpace(xchainAccountCreateCount))
        {
            throw new ArgumentException("xchainAccountCreateCount is required.", nameof(xchainAccountCreateCount));
        }

        XChainBridgeModel bridge = ParseBridge(bridgeJson);
        Currency amount = ParseXrpDrops(amountDrops, nameof(amountDrops));
        Currency reward = ParseXrpDrops(signatureRewardDrops, nameof(signatureRewardDrops));

        XChainAddAccountCreateAttestation tx = new XChainAddAccountCreateAttestation
        {
            Account = account,
            XChainBridge = bridge,
            XChainAccountCreateCount = xchainAccountCreateCount,
            Amount = amount,
            SignatureReward = reward,
            OtherChainSource = otherChainSource,
            Destination = destination,
            AttestationRewardAccount = attestationRewardAccount,
            AttestationSignerAccount = attestationSignerAccount,
            PublicKey = publicKeyHex.ToUpperInvariant(),
            Signature = signatureHex.ToUpperInvariant(),
            WasLockingChainSend = wasLockingChainSend,
        };

        string summary = $"XChainAddAccountCreateAttestation by witness {ToolDisplay.Truncate(account)}: "
            + $"count={xchainAccountCreateCount}, amount={amountDrops} drops, "
            + $"create {ToolDisplay.Truncate(destination)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static XChainBridgeModel ParseBridge(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("bridgeJson is required.", nameof(json));
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("bridgeJson must be a JSON object.");
        }

        string lockingDoor = ReadAccountField(doc.RootElement, "LockingChainDoor");
        string issuingDoor = ReadAccountField(doc.RootElement, "IssuingChainDoor");
        IssuedCurrency lockingIssue = ReadIssueField(doc.RootElement, "LockingChainIssue");
        IssuedCurrency issuingIssue = ReadIssueField(doc.RootElement, "IssuingChainIssue");

        if (string.Equals(lockingDoor, issuingDoor, StringComparison.Ordinal))
        {
            throw new ArgumentException("LockingChainDoor and IssuingChainDoor must differ.");
        }

        return new XChainBridgeModel
        {
            LockingChainDoor = lockingDoor,
            LockingChainIssue = lockingIssue,
            IssuingChainDoor = issuingDoor,
            IssuingChainIssue = issuingIssue,
        };
    }

    private static string ReadAccountField(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out JsonElement el) || el.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException($"bridgeJson.{key} is required (string r-address).");
        }
        string? value = el.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"bridgeJson.{key} must not be empty.");
        }
        return value;
    }

    private static IssuedCurrency ReadIssueField(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out JsonElement el) || el.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"bridgeJson.{key} is required (object {{currency, issuer?}}).");
        }
        string? currency = el.TryGetProperty("currency", out JsonElement c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException($"bridgeJson.{key}.currency is required.");
        }
        string? issuer = el.TryGetProperty("issuer", out JsonElement i) && i.ValueKind == JsonValueKind.String
            ? i.GetString()
            : null;
        return ToolDisplay.BuildAsset(currency, issuer);
    }

    private static Currency ParseXrpDrops(string drops, string paramName)
    {
        if (!long.TryParse(drops, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long _))
        {
            throw new ArgumentException($"{paramName} must be a decimal XRP-drops integer string.", paramName);
        }
        return CurrencyParser.Parse(drops);
    }

    private static void ValidateAttestationCommon(
        string rewardAcct,
        string signerAcct,
        string otherChainSource,
        string publicKeyHex,
        string signatureHex,
        byte wasLockingChainSend,
        string destination)
    {
        if (string.IsNullOrWhiteSpace(rewardAcct)) throw new ArgumentException("attestationRewardAccount is required.", nameof(rewardAcct));
        if (string.IsNullOrWhiteSpace(signerAcct)) throw new ArgumentException("attestationSignerAccount is required.", nameof(signerAcct));
        if (string.IsNullOrWhiteSpace(otherChainSource)) throw new ArgumentException("otherChainSource is required.", nameof(otherChainSource));
        if (string.IsNullOrWhiteSpace(publicKeyHex)) throw new ArgumentException("publicKeyHex is required.", nameof(publicKeyHex));
        if (string.IsNullOrWhiteSpace(signatureHex)) throw new ArgumentException("signatureHex is required.", nameof(signatureHex));
        if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("destination is required.", nameof(destination));
        if (wasLockingChainSend > 1) throw new ArgumentException("wasLockingChainSend must be 0 or 1.", nameof(wasLockingChainSend));

        ValidateHex(publicKeyHex, nameof(publicKeyHex));
        if ((publicKeyHex.Length & 1) != 0)
        {
            throw new ArgumentException("publicKeyHex must have an even number of hex chars.", nameof(publicKeyHex));
        }
        ValidateHex(signatureHex, nameof(signatureHex));
        if ((signatureHex.Length & 1) != 0)
        {
            throw new ArgumentException("signatureHex must have an even number of hex chars.", nameof(signatureHex));
        }
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

    private static string DescribeBridge(XChainBridgeModel bridge)
    {
        return $"{ToolDisplay.Truncate(bridge.LockingChainDoor)} [{ToolDisplay.DescribeAsset(bridge.LockingChainIssue)}] ↔ "
            + $"{ToolDisplay.Truncate(bridge.IssuingChainDoor)} [{ToolDisplay.DescribeAsset(bridge.IssuingChainIssue)}]";
    }
}
