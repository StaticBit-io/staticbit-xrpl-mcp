using System.Collections.Generic;

namespace StaticBit.Xrpl.Mcp.Abstractions;

/// <summary>
/// Result of a <c>*_prepare</c> tool — an autofilled, canonically encoded but UNSIGNED transaction.
/// The client/agent/wallet is expected to sign <see cref="TxBlobUnsigned"/> locally and call
/// <c>xrpl_tx_submit_signed</c> with the resulting signed blob.
/// </summary>
/// <remarks>
/// The server never sees a private key. Signing is always client-side.
/// </remarks>
public sealed class PreparedTransaction
{
    /// <summary>
    /// Canonical transaction JSON (after Autofill) — fields suitable for human review.
    /// </summary>
    public Dictionary<string, object?> TxJson { get; set; } = new Dictionary<string, object?>();

    /// <summary>
    /// Unsigned binary serialization of the transaction (hex). What a wallet signs.
    /// </summary>
    public string TxBlobUnsigned { get; set; } = string.Empty;

    /// <summary>
    /// Single-signer "EncodeForSigning" payload (hex). Useful for hardware wallets
    /// that need the signing-pre-image rather than the raw blob.
    /// </summary>
    public string SigningData { get; set; } = string.Empty;

    /// <summary>
    /// Network the transaction was prepared against. Submit must happen on the same network.
    /// </summary>
    public string Network { get; set; } = string.Empty;

    /// <summary>
    /// Last ledger after which the transaction will expire if not validated.
    /// </summary>
    public uint LastLedgerSequence { get; set; }

    /// <summary>
    /// Human-readable summary for the user-approval prompt in the host UI.
    /// </summary>
    public string HumanSummary { get; set; } = string.Empty;

    /// <summary>
    /// Always true for write-flow tools — signals the host that user confirmation is required
    /// before signing/submitting.
    /// </summary>
    public bool RequiresUserApproval { get; set; } = true;
}
