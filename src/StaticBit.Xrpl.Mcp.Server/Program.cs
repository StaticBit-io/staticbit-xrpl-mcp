using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Mcp.Auth.ResourceServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using StaticBit.Xrpl.Mcp.Core.DependencyInjection;
using StaticBit.Xrpl.Mcp.Core.Tools;
using StaticBit.Xrpl.Mcp.Server.Configuration;
using StaticBit.Xrpl.Mcp.Server.Middleware;
using StaticBit.Xrpl.Mcp.Server.Services;

namespace StaticBit.Xrpl.Mcp.Server;

internal static class Program
{
    /// <summary>
    /// Build version stamped into the assembly at publish time (CI passes the released
    /// tag via the <c>APP_VERSION</c> Docker build-arg). Surfaced on <c>/healthz</c>.
    /// </summary>
    private static readonly string AppVersion = ResolveAppVersion();

    public static async Task Main(string[] args)
    {
        string transport = ParseTransport(args);

        switch (transport)
        {
            case "stdio":
                await RunStdioAsync(args).ConfigureAwait(false);
                break;
            case "http":
                await RunHttpAsync(args).ConfigureAwait(false);
                break;
            default:
                Console.Error.WriteLine($"Unknown --transport value '{transport}'. Use 'stdio' or 'http'.");
                Environment.ExitCode = 2;
                break;
        }
    }

    private static async Task RunStdioAsync(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // STDIO is the protocol channel. All log output MUST go to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddStaticBitXrplMcp(builder.Configuration);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(LedgerTools).Assembly);

        // Wrap every tool so escaping exceptions surface as structured envelopes instead of the
        // SDK's opaque "An error occurred invoking '<tool>'." stub. Must run after WithTools*.
        builder.Services.AddXrplToolErrorClassification();

        await builder.Build().RunAsync().ConfigureAwait(false);
    }

    private static async Task RunHttpAsync(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services.AddStaticBitXrplMcp(builder.Configuration);

        // Server-side options: transport, bearer auth, rate limit.
        builder.Services
            .AddOptions<ServerOptions>()
            .Bind(builder.Configuration.GetSection(ServerOptions.SectionName))
            .ValidateOnStart();

        ServerOptions serverOptions = builder.Configuration
            .GetSection(ServerOptions.SectionName)
            .Get<ServerOptions>() ?? new ServerOptions();

        // OAuth 2.1 resource server replaces the static-bearer gate. Validate the
        // issuer/resource up front so the container exits before binding with bad config.
        McpResourceServerOptions oauth = builder.Configuration
            .GetSection(McpResourceServerOptions.SectionName)
            .Get<McpResourceServerOptions>() ?? new McpResourceServerOptions();
        ValidateOAuth(oauth);
        builder.Services.AddMcpResourceServer(builder.Configuration);

        // Bind Kestrel to the configured HTTP port. Defaults to 5500 inside the container.
        builder.WebHost.UseUrls(string.Create(
            CultureInfo.InvariantCulture,
            $"http://0.0.0.0:{serverOptions.HttpPort}"));

        // Admin alerts — only register the real channel-based alerter when enabled,
        // otherwise a no-op so every call site can depend on IAdminAlerter unconditionally.
        if (serverOptions.AdminAlerts.Enabled)
        {
            builder.Services.AddSingleton<AdminAlerter>();
            builder.Services.AddSingleton<IAdminAlerter>(sp => sp.GetRequiredService<AdminAlerter>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<AdminAlerter>());
        }
        else
        {
            builder.Services.TryAddSingleton<IAdminAlerter, NullAdminAlerter>();
        }

        if (serverOptions.RateLimit.Enabled)
        {
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: ResolveRateLimitPartitionKey(ctx, serverOptions.RateLimit.PartitionBy),
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = serverOptions.RateLimit.PermitsPerMinute,
                            QueueLimit = serverOptions.RateLimit.QueueLimit,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            Window = TimeSpan.FromMinutes(1),
                            AutoReplenishment = true,
                        }));
                options.OnRejected = (ctx, _) =>
                {
                    string ip = ResolveClientIp(ctx.HttpContext);
                    string label = ctx.HttpContext.Items.TryGetValue(BearerAuthMiddleware.BearerLabelContextKey, out object? l)
                        ? (l as string ?? "(unlabeled)")
                        : "(noauth)";
                    string rejectedPath = ctx.HttpContext.Request.Path.ToString();
                    IAdminAlerter alerter = ctx.HttpContext.RequestServices.GetRequiredService<IAdminAlerter>();
                    alerter.Alert(AlertKind.RateLimit,
                        $"Rate limit exceeded ip={ip} label={label}",
                        new Dictionary<string, string>
                        {
                            ["ip"] = ip,
                            ["label"] = label,
                            ["path"] = rejectedPath,
                        });
                    return ValueTask.CompletedTask;
                };
            });
        }

        if (serverOptions.Metrics.Enabled)
        {
            // Wire OpenTelemetry into the "StaticBitXrplMcp" Meter defined in Core.
            // Prometheus scrape exporter is mounted later (after build) on the
            // configured Metrics.Path. Process / runtime metrics come along free
            // from the AddProcessInstrumentation / AddRuntimeInstrumentation hooks
            // — we skip them here to keep the surface minimal, callers can opt in.
            builder.Services
                .AddOpenTelemetry()
                .WithMetrics(b => b
                    .AddMeter(StaticBit.Xrpl.Mcp.Core.Services.XrplMcpMetrics.MeterName)
                    .AddPrometheusExporter());
        }

        if (serverOptions.Cors.Enabled)
        {
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    string[] origins = serverOptions.Cors.AllowedOrigins.ToArray();
                    bool isWildcardOrigin = origins.Length == 1 && origins[0] == "*";
                    if (isWildcardOrigin)
                    {
                        policy.AllowAnyOrigin();
                    }
                    else if (origins.Length > 0)
                    {
                        policy.WithOrigins(origins);
                    }

                    policy.WithHeaders(serverOptions.Cors.AllowedHeaders.ToArray());
                    policy.WithMethods(serverOptions.Cors.AllowedMethods.ToArray());

                    if (serverOptions.Cors.AllowCredentials && !isWildcardOrigin)
                    {
                        policy.AllowCredentials();
                    }
                });
            });
        }

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(LedgerTools).Assembly);

        // Wrap every tool so escaping exceptions surface as structured envelopes instead of the
        // SDK's opaque "An error occurred invoking '<tool>'." stub. Must run after WithTools*.
        builder.Services.AddXrplToolErrorClassification();

        WebApplication app = builder.Build();

        ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("StaticBitXrplMcp");
        startupLogger.LogInformation(
            "StaticBitXrplMcp HTTP listening on port {Port}, RequireHttps={RequireHttps}, RateLimit={Rate}/min, BearerTokens={Count}, AdminAlerts={AdminAlerts}",
            serverOptions.HttpPort,
            serverOptions.HttpAuth.RequireHttps,
            serverOptions.RateLimit.Enabled ? serverOptions.RateLimit.PermitsPerMinute : 0,
            serverOptions.HttpAuth.Tokens.Count,
            serverOptions.AdminAlerts.Enabled);

        WireAdminAlertLifecycle(app.Services, serverOptions);

        // Health endpoints — used by Docker HEALTHCHECK and reverse proxies.
        // Kept on dedicated paths so they never collide with the MCP transport.
        // The auth middleware lets them through without a bearer.
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok", version = AppVersion }));
        app.MapGet("/readyz", () => Results.Ok(new { status = "ready", version = AppVersion }));

        // Favicon — anonymous, so MCP connector clients can show an icon.
        // Like /healthz, a plain MapGet here is not gated by the bearer middleware.
        string faviconPath = Path.Combine(AppContext.BaseDirectory, "favicon.ico");
        if (File.Exists(faviconPath))
        {
            byte[] faviconBytes = File.ReadAllBytes(faviconPath);
            app.MapGet("/favicon.ico", () => Results.Bytes(faviconBytes, "image/x-icon"));
        }

        // Prometheus scrape endpoint — opt-in via ServerOptions.Metrics.Enabled.
        // The auth middleware also bypasses this path (see BearerAuthMiddleware
        // updates below). Lock it down at the reverse proxy if you don't want
        // pool size / reconnect counts public.
        if (serverOptions.Metrics.Enabled)
        {
            app.UseOpenTelemetryPrometheusScrapingEndpoint(serverOptions.Metrics.Path);
        }

        // CORS first, so OPTIONS preflights from browsers don't need a bearer.
        if (serverOptions.Cors.Enabled)
        {
            app.UseCors();
        }

        // Request logging wraps the whole pipeline so we capture status code and duration
        // regardless of what fails downstream. Bodies are NEVER captured.
        if (serverOptions.RequestLogging.Enabled)
        {
            app.UseMiddleware<RequestLoggingMiddleware>();
        }

        // OAuth 2.1 resource server: JWT validation against the central AS replaces the
        // static-bearer gate. Health/metrics endpoints are anonymous (no RequireAuthorization).
        app.UseAuthentication();

        // Bridge: expose the access token subject (sub) as the bearer label so the existing
        // rate-limit partitioning / admin-alert plumbing keeps working per authenticated user.
        app.Use(async (ctx, next) =>
        {
            string? sub = ctx.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(sub))
            {
                ctx.Items[BearerAuthMiddleware.BearerLabelContextKey] = sub;
            }
            await next(ctx);
        });

        if (serverOptions.RateLimit.Enabled)
        {
            // Rate-limiter sits AFTER the label bridge so token-based partitioning has a
            // label available in HttpContext.Items.
            app.UseRateLimiter();
        }

        app.UseAuthorization();

        // RFC 9728 protected-resource metadata — anonymous; points clients at the AS.
        app.MapMcpProtectedResourceMetadata();
        // MCP transport mounted at /mcp so /healthz and /readyz stay clean.
        app.MapMcp("/mcp").RequireAuthorization(McpAuth.Policy);

        await app.RunAsync().ConfigureAwait(false);
    }

    private static void WireAdminAlertLifecycle(IServiceProvider provider, ServerOptions server)
    {
        IHostApplicationLifetime lifetime = provider.GetRequiredService<IHostApplicationLifetime>();
        IAdminAlerter alerter = provider.GetRequiredService<IAdminAlerter>();

        lifetime.ApplicationStarted.Register(() =>
        {
            alerter.Alert(AlertKind.StartUp,
                "xrpl-mcp server started",
                new Dictionary<string, string>
                {
                    ["transport"] = server.Transport,
                    ["auth"] = "oauth",
                });
        });

        lifetime.ApplicationStopping.Register(() =>
        {
            alerter.Alert(AlertKind.ShutDown,
                "xrpl-mcp server stopping",
                new Dictionary<string, string>
                {
                    ["transport"] = server.Transport,
                });
        });
    }

    private static void ValidateOAuth(McpResourceServerOptions oauth)
    {
        if (!Uri.TryCreate(oauth.Issuer, UriKind.Absolute, out Uri? issuer) || issuer.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "OAuth:Issuer must be an absolute https URL (the authorization server), e.g. https://auth.mcp.staticbit.ai.");
        }

        if (!Uri.TryCreate(oauth.Resource, UriKind.Absolute, out Uri? resource) || resource.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "OAuth:Resource must be this server's absolute https canonical URI (the token audience), e.g. https://xrpl.mcp.staticbit.ai/mcp.");
        }
    }

    private static string ResolveClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out Microsoft.Extensions.Primitives.StringValues fwd))
        {
            string raw = fwd.ToString();
            int comma = raw.IndexOf(',');
            string head = comma > 0 ? raw[..comma].Trim() : raw.Trim();
            if (!string.IsNullOrEmpty(head)) return head;
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    internal static string ResolveRateLimitPartitionKey(HttpContext context, string partitionBy)
    {
        // Bearer auth runs first, so the label is already populated for /mcp requests.
        string? label = context.Items.TryGetValue(BearerAuthMiddleware.BearerLabelContextKey, out object? l)
            ? l as string
            : null;
        string ip = ResolveClientIp(context);

        string mode = (partitionBy ?? "ip").Trim().ToLowerInvariant();
        return mode switch
        {
            "token" => label ?? "noauth:" + ip,  // fall back to IP for pre-auth paths (/healthz, scanners)
            "both" => (label ?? "noauth") + "|" + ip,
            _ => ip,
        };
    }

    private static string ResolveAppVersion()
    {
        Assembly? entry = Assembly.GetEntryAssembly();
        if (entry is null)
        {
            return "unknown";
        }

        string? informational = entry.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }

        return entry.GetName().Version?.ToString() ?? "unknown";
    }

    private static string ParseTransport(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--transport", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1].Trim().ToLowerInvariant();
            }
        }

        // Default: stdio (most common MCP deployment mode).
        return "stdio";
    }
}
