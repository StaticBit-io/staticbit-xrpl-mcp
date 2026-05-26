using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using StaticBit.Xrpl.Mcp.Core.Services;
using StaticBit.Xrpl.Mcp.Server.Configuration;
using StaticBit.Xrpl.Mcp.Server.Middleware;

namespace StaticBit.Xrpl.Mcp.Server.Tests;

[TestClass]
public class RequestLoggingMiddlewareTestsU
{
    private static ServerOptions BuildOptions(bool enabled, bool includeQs = false) => new ServerOptions
    {
        RequestLogging = new RequestLoggingOptions
        {
            Enabled = enabled,
            IncludeQueryString = includeQs,
        },
    };

    private static (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) BuildContext(string path, string? queryString = null)
    {
        DefaultHttpContext ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = "POST";
        if (!string.IsNullOrEmpty(queryString)) ctx.Request.QueryString = new QueryString(queryString);
        ctx.Response.Body = new MemoryStream();
        return (ctx, new RecordingLogger<RequestLoggingMiddleware>());
    }

    private static readonly XrplMcpMetrics SharedMetrics = new XrplMcpMetrics();

    private static async Task RunAsync(HttpContext ctx, ServerOptions options, RecordingLogger<RequestLoggingMiddleware> logger, RequestDelegate? next = null)
    {
        RequestLoggingMiddleware mw = new RequestLoggingMiddleware(
            next ?? (_ => Task.CompletedTask),
            new StaticOptionsMonitor<ServerOptions>(options),
            logger,
            SharedMetrics);
        await mw.InvokeAsync(ctx);
    }

    [TestMethod]
    public async Task TestU_Disabled_LogsNothing()
    {
        (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) = BuildContext("/mcp");
        await RunAsync(ctx, BuildOptions(enabled: false), logger);
        Assert.IsEmpty(logger.Entries);
    }

    [TestMethod]
    public async Task TestU_Enabled_LogsOneInformation()
    {
        (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) = BuildContext("/mcp");
        await RunAsync(ctx, BuildOptions(enabled: true), logger);

        Assert.HasCount(1, logger.Entries);
        Assert.AreEqual(LogLevel.Information, logger.Entries[0].Level);
        StringAssert.Contains(logger.Entries[0].Message, "POST /mcp");
    }

    [TestMethod]
    public async Task TestU_HealthzPath_NotLogged()
    {
        (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) = BuildContext("/healthz");
        await RunAsync(ctx, BuildOptions(enabled: true), logger);
        Assert.IsEmpty(logger.Entries);
    }

    [TestMethod]
    public async Task TestU_ReadyzPath_NotLogged()
    {
        (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) = BuildContext("/readyz");
        await RunAsync(ctx, BuildOptions(enabled: true), logger);
        Assert.IsEmpty(logger.Entries);
    }

    [TestMethod]
    public async Task TestU_QueryString_OmittedByDefault()
    {
        (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) = BuildContext("/mcp", "?account=rAlice");
        await RunAsync(ctx, BuildOptions(enabled: true, includeQs: false), logger);

        Assert.HasCount(1, logger.Entries);
        Assert.IsFalse(logger.Entries[0].Message.Contains("rAlice", StringComparison.Ordinal),
            "QueryString should be omitted by default to avoid leaking caller-identifying r-addresses.");
    }

    [TestMethod]
    public async Task TestU_QueryString_IncludedWhenOpted_In()
    {
        (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) = BuildContext("/mcp", "?account=rAlice");
        await RunAsync(ctx, BuildOptions(enabled: true, includeQs: true), logger);

        Assert.HasCount(1, logger.Entries);
        StringAssert.Contains(logger.Entries[0].Message, "rAlice");
    }

    [TestMethod]
    public async Task TestU_BearerLabel_AppearsInLog()
    {
        (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) = BuildContext("/mcp");
        ctx.Items[BearerAuthMiddleware.BearerLabelContextKey] = "alice";

        await RunAsync(ctx, BuildOptions(enabled: true), logger);

        StringAssert.Contains(logger.Entries[0].Message, "label=alice");
    }

    [TestMethod]
    public async Task TestU_NoBearerLabel_LogsNoauth()
    {
        (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) = BuildContext("/mcp");
        await RunAsync(ctx, BuildOptions(enabled: true), logger);
        StringAssert.Contains(logger.Entries[0].Message, "label=(noauth)");
    }

    [TestMethod]
    public async Task TestU_StatusCode_Captured()
    {
        (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) = BuildContext("/mcp");
        ctx.Items[BearerAuthMiddleware.BearerLabelContextKey] = "alice";

        await RunAsync(ctx, BuildOptions(enabled: true), logger,
            next: c =>
            {
                c.Response.StatusCode = 418;
                return Task.CompletedTask;
            });

        StringAssert.Contains(logger.Entries[0].Message, "→ 418");
    }

    [TestMethod]
    public async Task TestU_LogsEvenWhenNextThrows()
    {
        (DefaultHttpContext ctx, RecordingLogger<RequestLoggingMiddleware> logger) = BuildContext("/mcp");

        InvalidOperationException? thrown = null;
        try
        {
            await RunAsync(ctx, BuildOptions(enabled: true), logger,
                next: _ => throw new InvalidOperationException("boom"));
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        Assert.IsNotNull(thrown);
        // Even though next threw, the request log line must still be emitted.
        Assert.HasCount(1, logger.Entries);
    }
}
