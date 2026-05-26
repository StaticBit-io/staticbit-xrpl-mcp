namespace StaticBit.Xrpl.Mcp.Signer.Audit;

/// <summary>
/// Append-only audit log of signing events. Disabled when no path is configured
/// (<see cref="NullAuditLogger"/>); otherwise writes one JSONL line per event to
/// the file pointed at by <c>SignerOptions.AuditLogPath</c>.
///
/// The log is for compliance — "did I sign tx X?" — not debugging. It contains
/// metadata only (timestamps, wallet names, tx hashes, failure reasons) and
/// never key material.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Records a successful sign of a transaction.
    /// </summary>
    /// <param name="wallet">Wallet alias from the keystore.</param>
    /// <param name="index">Account index (HD wallets) or null for seed-kind.</param>
    /// <param name="signMode">"single", "multi" (signFor), or "combine".</param>
    /// <param name="txHash">XRPL transaction hash of the signed blob, when available.</param>
    /// <param name="txType">TransactionType extracted from the decoded blob, when available.</param>
    void LogSign(string wallet, int? index, string signMode, string? txHash, string? txType);

    /// <summary>
    /// Records a failed decrypt attempt (wrong passphrase, corrupted keystore, etc.).
    /// </summary>
    void LogDecryptFail(string wallet, string reason);

    /// <summary>
    /// Records a non-fatal error during a sign call (after decrypt succeeded but
    /// before completion). Useful for distinguishing crypto failures from input
    /// errors in audit review.
    /// </summary>
    void LogSignError(string wallet, string reason);
}
