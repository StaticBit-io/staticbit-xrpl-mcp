using System;
using System.Globalization;
using System.Linq;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaticBit.Xrpl.Mcp.Core.DependencyInjection;
using StaticBit.Xrpl.Mcp.Core.Tools;
using StaticBit.Xrpl.Mcp.Server.Configuration;
using StaticBit.Xrpl.Mcp.Server.Middleware;
using StaticBit.Xrpl.Mcp.Server.Services;

namespace StaticBit.Xrpl.Mcp.Server;

internal static class Program
{
    private const int MinimumBearerLength = 32;

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

        ValidateBearerTokens(serverOptions);

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
                        partitionKey: ResolveClientKey(ctx),
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
                    string ip = ResolveClientKey(ctx.HttpContext);
                    string rejectedPath = ctx.HttpContext.Request.Path.ToString();
                    IAdminAlerter alerter = ctx.HttpContext.RequestServices.GetRequiredService<IAdminAlerter>();
                    alerter.Alert(AlertKind.RateLimit,
                        $"Rate limit exceeded by {ip}",
                        new Dictionary<string, string>
                        {
                            ["ip"] = ip,
                            ["path"] = rejectedPath,
                        });
                    return ValueTask.CompletedTask;
                };
            });
        }

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(LedgerTools).Assembly);

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
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/readyz", () => Results.Ok(new { status = "ready" }));

        // Bearer auth must run BEFORE the MCP endpoint so unauthenticated callers
        // never reach the protocol handler. The middleware bypasses /healthz and /readyz.
        app.UseMiddleware<BearerAuthMiddleware>();

        if (serverOptions.RateLimit.Enabled)
        {
            app.UseRateLimiter();
        }

        // MCP transport mounted at /mcp so /healthz and /readyz stay clean.
        app.MapMcp("/mcp");

        await app.RunAsync().ConfigureAwait(false);
    }

    private static void WireAdminAlertLifecycle(IServiceProvider provider, ServerOptions server)
    {
        IHostApplicationLifetime lifetime = provider.GetRequiredService<IHostApplicationLifetime>();
        IAdminAlerter alerter = provider.GetRequiredService<IAdminAlerter>();

        lifetime.ApplicationStarted.Register(() =>
        {
            alerter.Alert(AlertKind.StartUp,
                "StaticBitXrplMcp server started",
                new Dictionary<string, string>
                {
                    ["transport"] = server.Transport,
                    ["port"] = server.HttpPort.ToString(CultureInfo.InvariantCulture),
                    ["bearerTokens"] = server.HttpAuth.Tokens.Count.ToString(CultureInfo.InvariantCulture),
                });
        });

        lifetime.ApplicationStopping.Register(() =>
        {
            alerter.Alert(AlertKind.ShutDown,
                "StaticBitXrplMcp server stopping",
                new Dictionary<string, string>
                {
                    ["transport"] = server.Transport,
                });
        });
    }

    private static void ValidateBearerTokens(ServerOptions options)
    {
        if (options.HttpAuth.Tokens.Count == 0)
        {
            throw new InvalidOperationException(
                "HTTP transport requires Server:HttpAuth:Tokens to contain at least one entry. " +
                "Generate one with: openssl rand -base64 48 | tr '/+' '_-'");
        }

        for (int i = 0; i < options.HttpAuth.Tokens.Count; i++)
        {
            BearerTokenConfig token = options.HttpAuth.Tokens[i];
            string label = string.IsNullOrEmpty(token.Label) ? "(unlabeled)" : token.Label;

            if (string.IsNullOrWhiteSpace(token.Token))
            {
                throw new InvalidOperationException(
                    $"Server:HttpAuth:Tokens[{i}]:Token (label='{label}') is empty.");
            }

            if (token.Token.Length < MinimumBearerLength)
            {
                throw new InvalidOperationException(
                    $"Server:HttpAuth:Tokens[{i}]:Token (label='{label}') is shorter than {MinimumBearerLength} characters.");
            }
        }
    }

    private static string ResolveClientKey(HttpContext context)
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
