using StaticBit.Xrpl.Mcp.Abstractions;

namespace StaticBit.Xrpl.Mcp.Integration.Tests;

/// <summary>
/// Shared assertion helpers for prepare-smoke integration tests. Each test class
/// owns its own <c>XrplClientPool</c> + <c>TransactionPreparer</c> via
/// <c>[ClassInitialize]</c>/<c>[ClassCleanup]</c> for isolation.
///
/// The submitter <c>account</c> in each prepare call MUST be a real funded testnet
/// account (Autofill looks it up via <c>account_info</c>); other addresses
/// (Destination/Holder/Counterparty/etc.) can be synthetic since rippled only
/// validates them at submit time.
/// </summary>
internal static class PrepareSmokeAssert
{
    /// <summary>
    /// Standard assertions that every prepare-smoke result satisfies — non-empty blob,
    /// signing data, positive autofilled LastLedgerSequence, approval flag set,
    /// TxJson carries the expected TransactionType + Sequence + Fee, and the human
    /// summary mentions a marker word (usually the tx type or a specific field).
    /// </summary>
    public static void Standard(
        PreparedTransaction prep,
        string expectedTxType,
        string humanSummaryContains)
    {
        Assert.IsNotNull(prep, "PreparedTransaction must be returned.");
        Assert.IsFalse(string.IsNullOrEmpty(prep.TxBlobUnsigned), "TxBlobUnsigned must not be empty.");
        Assert.IsFalse(string.IsNullOrEmpty(prep.SigningData), "SigningData must not be empty.");
        Assert.IsTrue(prep.LastLedgerSequence > 0, $"LastLedgerSequence must be > 0 (got {prep.LastLedgerSequence}).");
        Assert.IsTrue(prep.RequiresUserApproval, "RequiresUserApproval must be true.");
        Assert.IsNotNull(prep.TxJson, "TxJson must not be null.");
        Assert.IsTrue(prep.TxJson.ContainsKey("TransactionType"), "TxJson must include TransactionType.");
        Assert.AreEqual(expectedTxType, prep.TxJson["TransactionType"]?.ToString(),
            $"TxJson.TransactionType must be {expectedTxType}.");
        Assert.IsTrue(prep.TxJson.ContainsKey("Sequence"), "Autofill must populate Sequence.");
        Assert.IsTrue(prep.TxJson.ContainsKey("Fee"), "Autofill must populate Fee.");
        StringAssert.Contains(prep.HumanSummary ?? "", humanSummaryContains,
            $"HumanSummary should mention '{humanSummaryContains}'.");
    }

    /// <summary>Deterministic 64-hex Hash256 for synthetic IDs (vault/broker/loan/domain).</summary>
    public static string Hash256(string seed)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return System.Convert.ToHexString(hash); // 64 chars uppercase
    }

    /// <summary>Deterministic 48-hex MPTokenIssuanceID for synthetic MPT references.</summary>
    public static string MptIssuanceId(string seed)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return System.Convert.ToHexString(hash.AsSpan(0, 24).ToArray());
    }
}
