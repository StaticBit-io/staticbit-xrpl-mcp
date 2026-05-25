using System;
using System.Collections.Generic;

namespace StaticBit.Xrpl.Mcp.Server.Configuration;

/// <summary>
/// Server-side options that govern HTTP transport, bearer authentication and rate limiting.
/// Bound from the <c>Server</c> configuration section. All settings are stateless —
/// no per-client secrets live here, only the bearers used to identify callers.
/// </summary>
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    /// <summary>
    /// "stdio" or "http". The CLI flag <c>--transport</c> takes precedence.
    /// </summary>
    public string Transport { get; set; } = "stdio";

    /// <summary>
    /// HTTP listening port for the in-container Kestrel server. Traefik routes to it.
    /// </summary>
    public int HttpPort { get; set; } = 5500;

    public HttpAuthOptions HttpAuth { get; set; } = new();

    public RateLimitOptions RateLimit { get; set; } = new();

    /// <summary>
    /// Optional: when enabled, the server posts operational alerts (auth failures,
    /// rate-limit hits, lifecycle events) to a Telegram chat owned by the server
    /// admin. Uses a separate bot token so it is isolated from any client bot tokens
    /// of other MCPs running on the same host.
    /// </summary>
    public AdminAlertsOptions AdminAlerts { get; set; } = new();

    public bool IsHttp => string.Equals(Transport, "http", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Bearer-token authentication options. All configured tokens have equal privileges —
/// per-call XRPL network/account is supplied by tool arguments, so there is no
/// per-token authorization model here.
/// </summary>
public sealed class HttpAuthOptions
{
    /// <summary>
    /// One or more bearer tokens accepted by the server. Each entry carries a
    /// human-readable <see cref="BearerTokenConfig.Label"/> used in structured logs
    /// so you can audit which client made which call (e.g. "owner", "alice",
    /// "cowork-routine", "ci"). Tokens must be at least 32 characters long.
    /// </summary>
    public List<BearerTokenConfig> Tokens { get; set; } = new();

    /// <summary>
    /// Reject non-HTTPS requests. Set to <c>true</c> for production; the reverse
    /// proxy (Traefik / nginx) is expected to set <c>X-Forwarded-Proto: https</c>.
    /// </summary>
    public bool RequireHttps { get; set; } = true;
}

public sealed class BearerTokenConfig
{
    public string Token { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

public sealed class RateLimitOptions
{
    public bool Enabled { get; set; } = true;

    public int PermitsPerMinute { get; set; } = 60;

    public int QueueLimit { get; set; } = 0;
}

public sealed class AdminAlertsOptions
{
    public bool Enabled { get; set; }

    /// <summary>Admin bot token — should be SEPARATE from any client bot tokens.</summary>
    public string? BotToken { get; set; }

    /// <summary>Target chat for admin alerts. Numeric chat_id or @channel_username.</summary>
    public string? ChatId { get; set; }

    public AlertEventsOptions Events { get; set; } = new();

    public AlertThrottlingOptions Throttling { get; set; } = new();
}

public sealed class AlertEventsOptions
{
    public bool AuthFailure { get; set; } = true;
    public bool RateLimit { get; set; } = true;
    public bool ToolError { get; set; } = true;
    public bool Lifecycle { get; set; } = true;
}

public sealed class AlertThrottlingOptions
{
    /// <summary>How long the same alert kind+key is suppressed before re-sending an aggregated summary.</summary>
    public int DedupWindowMinutes { get; set; } = 5;

    /// <summary>Maximum admin alerts per minute. Excess alerts are dropped (still written to stderr logs).</summary>
    public int MaxAlertsPerMinute { get; set; } = 10;

    /// <summary>Background channel capacity. Older alerts are dropped if filled.</summary>
    public int QueueCapacity { get; set; } = 1000;
}
