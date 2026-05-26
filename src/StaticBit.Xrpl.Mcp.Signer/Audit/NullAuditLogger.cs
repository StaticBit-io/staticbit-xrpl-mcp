namespace StaticBit.Xrpl.Mcp.Signer.Audit;

/// <summary>
/// No-op implementation registered when <c>SignerOptions.AuditLogPath</c> is empty.
/// Lets call sites depend on <see cref="IAuditLogger"/> unconditionally.
/// </summary>
public sealed class NullAuditLogger : IAuditLogger
{
    public void LogSign(string wallet, int? index, string signMode, string? txHash, string? txType) { }
    public void LogDecryptFail(string wallet, string reason) { }
    public void LogSignError(string wallet, string reason) { }
}
