using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using StaticBit.Xrpl.Mcp.Server.Configuration;

namespace StaticBit.Xrpl.Mcp.Server.Middleware;

/// <summary>
/// Structured request logger. Emits one log line per request with method, path,
/// status, duration, client IP and the bearer label (resolved by <see cref="BearerAuthMiddleware"/>
/// upstream). Request and response bodies are NEVER captured — they can carry
/// XRPL r-addresses and amounts that are caller-identifying.
///
/// Health probe paths are skipped to keep log volume sane.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        IOptionsMonitor<ServerOptions> options,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        RequestLoggingOptions opts = _options.CurrentValue.RequestLogging;
        if (!opts.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        PathString path = context.Request.Path;
        if (path.StartsWithSegments("/healthz") || path.StartsWithSegments("/readyz"))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        long startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            string label = context.Items.TryGetValue(BearerAuthMiddleware.BearerLabelContextKey, out object? l)
                ? (l as string ?? "(unlabeled)")
                : "(noauth)";
            string ip = ResolveClientIp(context);
            string fullPath = opts.IncludeQueryString
                ? path + context.Request.QueryString
                : path.ToString();

            _logger.LogInformation(
                "{Method} {Path} → {Status} in {DurationMs}ms ip={Ip} label={Label}",
                context.Request.Method,
                fullPath,
                context.Response.StatusCode,
                Math.Round(elapsed.TotalMilliseconds, 1),
                ip,
                label);
        }
    }

    internal static string ResolveClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out StringValues fwd))
        {
            string raw = fwd.ToString();
            int comma = raw.IndexOf(',');
            string head = comma > 0 ? raw[..comma].Trim() : raw.Trim();
            if (!string.IsNullOrEmpty(head)) return head;
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
