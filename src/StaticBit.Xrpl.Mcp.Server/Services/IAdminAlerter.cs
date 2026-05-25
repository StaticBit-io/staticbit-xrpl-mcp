using System.Collections.Generic;

namespace StaticBit.Xrpl.Mcp.Server.Services;

/// <summary>
/// Fire-and-forget admin-alert sink. Implementations decide whether to actually
/// dispatch (<see cref="AdminAlerter"/> when enabled, <see cref="NullAdminAlerter"/>
/// otherwise). The call site never blocks; delivery happens on a background worker.
/// </summary>
public interface IAdminAlerter
{
    /// <summary>
    /// Submit an alert. Tags are used for deduplication: alerts sharing the same
    /// kind + ordered tag values within the dedup window are aggregated into one
    /// outgoing message with a count.
    /// </summary>
    void Alert(AlertKind kind, string summary, IReadOnlyDictionary<string, string>? tags = null);
}
