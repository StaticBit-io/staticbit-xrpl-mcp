using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using StaticBit.Xrpl.Mcp.Server.Configuration;
using StaticBit.Xrpl.Mcp.Server.Services;

namespace StaticBit.Xrpl.Mcp.Server.Middleware;

/// <summary>
/// Validates the <c>Authorization: Bearer …</c> header against the configured list of
/// tokens. Enforces HTTPS (or a trusted <c>X-Forwarded-Proto: https</c> from a
/// reverse proxy). Logs every auth event (success/failure) with the client IP and
/// matched Label for forensic audit.
///
/// Health and readiness endpoints are bypassed so probes from Docker / Traefik
/// don't need credentials.
/// </summary>
public sealed class BearerAuthMiddleware
{
    public const string BearerLabelContextKey = "StaticBitXrplMcp.BearerLabel";

    private const string AuthHeader = "Authorization";
    private const string BearerPrefix = "Bearer ";

    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly ILogger<BearerAuthMiddleware> _logger;
    private readonly IAdminAlerter _alerter;

    public BearerAuthMiddleware(
        RequestDelegate next,
        IOptionsMonitor<ServerOptions> options,
        ILogger<BearerAuthMiddleware> logger,
        IAdminAlerter alerter)
    {
        _next = next;
        _options = options;
        _logger = logger;
        _alerter = alerter;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        PathString path = context.Request.Path;

        // Health probes (used by Docker HEALTHCHECK / Traefik / k8s) bypass auth.
        // The Prometheus scrape path also bypasses — gated by the reverse proxy
        // / firewall, since the metrics endpoint can't easily participate in
        // bearer flow used by MCP clients.
        ServerOptions current = _options.CurrentValue;
        string metricsPath = current.Metrics.Path ?? "/metrics";
        if (path.StartsWithSegments("/healthz")
            || path.StartsWithSegments("/readyz")
            || (current.Metrics.Enabled && path.StartsWithSegments(metricsPath)))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Anything outside the MCP transport is unknown to us. Returning 404 without
        // running the bearer check (and without admin alerts) keeps the audit signal
        // clean: scanners probing /.well-known/oauth-protected-resource, /wp-login.php,
        // /.env, etc. don't drown the alert channel. Real attacks target /mcp, which
        // still runs through the full auth path below.
        if (!path.StartsWithSegments("/mcp"))
        {
            _logger.LogDebug(
                "Scanner hit ignored from {Ip}, path {Path}",
                GetClientIp(context), path);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        HttpAuthOptions auth = _options.CurrentValue.HttpAuth;

        if (auth.RequireHttps && !context.Request.IsHttps && !IsTrustedForwardedHttps(context))
        {
            _logger.LogWarning("Rejected non-HTTPS request from {Ip} to {Path}", GetClientIp(context), path);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("HTTPS required.").ConfigureAwait(false);
            return;
        }

        if (auth.Tokens.Count == 0)
        {
            _logger.LogError("Server:HttpAuth:Tokens is empty; refusing all requests");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Server bearer tokens are not configured.").ConfigureAwait(false);
            return;
        }

        string? presented = ExtractBearer(context);
        if (presented is null)
        {
            string ip = GetClientIp(context);
            _logger.LogWarning(
                "Auth failure from {Ip}, path {Path}, presented=false",
                ip, path);
            _alerter.Alert(AlertKind.AuthFailure,
                $"Missing bearer from {ip}",
                new Dictionary<string, string>
                {
                    ["ip"] = ip,
                    ["path"] = path.ToString(),
                    ["reason"] = "missing",
                });
            Reject(context);
            await context.Response.WriteAsync("Unauthorized.").ConfigureAwait(false);
            return;
        }

        BearerTokenConfig? match = null;
        foreach (BearerTokenConfig configured in auth.Tokens)
        {
            if (string.IsNullOrEmpty(configured.Token))
            {
                continue;
            }
            if (ConstantTimeEquals(configured.Token, presented))
            {
                match = configured;
                break;
            }
        }

        if (match is null)
        {
            string ip = GetClientIp(context);
            _logger.LogWarning(
                "Auth failure from {Ip}, path {Path}, presented=true",
                ip, path);
            _alerter.Alert(AlertKind.AuthFailure,
                $"Invalid bearer from {ip}",
                new Dictionary<string, string>
                {
                    ["ip"] = ip,
                    ["path"] = path.ToString(),
                    ["reason"] = "invalid",
                });
            Reject(context);
            await context.Response.WriteAsync("Unauthorized.").ConfigureAwait(false);
            return;
        }

        context.Items[BearerLabelContextKey] = string.IsNullOrEmpty(match.Label) ? "(unlabeled)" : match.Label;

        _logger.LogDebug(
            "Auth success label={Label} ip={Ip} path={Path}",
            match.Label, GetClientIp(context), path);

        await _next(context).ConfigureAwait(false);
    }

    private static void Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
    }

    private static string? ExtractBearer(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(AuthHeader, out StringValues header))
        {
            return null;
        }

        string? value = header.ToString();
        if (string.IsNullOrEmpty(value) ||
            !value.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return value[BearerPrefix.Length..].Trim();
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        byte[] aBytes = Encoding.UTF8.GetBytes(a);
        byte[] bBytes = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    private static bool IsTrustedForwardedHttps(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out StringValues proto))
        {
            return string.Equals(proto.ToString(), "https", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static string GetClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out StringValues fwd))
        {
            string raw = fwd.ToString();
            int comma = raw.IndexOf(',');
            return comma > 0 ? raw[..comma].Trim() : raw.Trim();
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
