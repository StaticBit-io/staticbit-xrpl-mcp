namespace StaticBit.Xrpl.Mcp.Abstractions;

/// <summary>
/// Result of <c>xrpl_tx_submit_signed</c>.
/// </summary>
public sealed class SubmitResult
{
    /// <summary>
    /// Engine result from rippled (e.g. <c>tesSUCCESS</c>, <c>tecUNFUNDED_PAYMENT</c>).
    /// </summary>
    public string EngineResult { get; set; } = string.Empty;

    /// <summary>
    /// Free-form description of the engine result.
    /// </summary>
    public string EngineResultMessage { get; set; } = string.Empty;

    /// <summary>
    /// Transaction hash (set even when not yet validated).
    /// </summary>
    public string TxHash { get; set; } = string.Empty;

    /// <summary>
    /// True when the transaction was confirmed in a validated ledger (only set when
    /// the caller requested wait-for-validation).
    /// </summary>
    public bool Validated { get; set; }

    /// <summary>
    /// Index of the validated ledger that included this transaction. 0 if unknown.
    /// </summary>
    public uint LedgerIndex { get; set; }

    /// <summary>
    /// Raw rippled response payload, kept opaque to allow forward compatibility.
    /// </summary>
    public string? RawResponseJson { get; set; }
}
