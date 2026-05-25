using System.Collections.Generic;

namespace StaticBit.Xrpl.Mcp.Server.Services;

/// <summary>No-op alerter used when <c>Server:AdminAlerts:Enabled</c> is false.</summary>
public sealed class NullAdminAlerter : IAdminAlerter
{
    public void Alert(AlertKind kind, string summary, IReadOnlyDictionary<string, string>? tags = null)
    {
    }
}
