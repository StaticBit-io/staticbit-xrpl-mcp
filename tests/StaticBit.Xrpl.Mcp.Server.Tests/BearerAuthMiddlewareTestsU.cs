using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using StaticBit.Xrpl.Mcp.Server.Configuration;
using StaticBit.Xrpl.Mcp.Server.Middleware;
using StaticBit.Xrpl.Mcp.Server.Services;

namespace StaticBit.Xrpl.Mcp.Server.Tests;

[TestClass]
public class BearerAuthMiddlewareTestsU
{
    private const string GoodToken = "this-is-a-32-plus-character-bearer-token-aaaa";
    private const string GoodLabel = "owner";

    private static ServerOptions BuildOptions(bool requireHttps = true) => new ServerOptions
    {
        Transport = "http",
        HttpPort = 5500,
        HttpAuth = new HttpAuthOptions
        {
            RequireHttps = requireHttps,
            Tokens = new System.Collections.Generic.List<BearerTokenConfig>
            {
                new BearerTokenConfig { Token = GoodToken, Label = GoodLabel },
            },
        },
    };

    private static (DefaultHttpContext ctx, bool nextCalled) BuildContext(string path, string scheme = "https")
    {
        DefaultHttpContext ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Scheme = scheme;
        ctx.Response.Body = new MemoryStream();
        return (ctx, false);
    }

    private static async Task<int> RunAsync(HttpContext ctx, ServerOptions options, RecordingAdminAlerter alerter, Action<HttpContext>? nextCallback = null)
    {
        bool called = false;
        RequestDelegate next = (HttpContext c) =>
        {
            called = true;
            nextCallback?.Invoke(c);
            return Task.CompletedTask;
        };

        BearerAuthMiddleware mw = new BearerAuthMiddleware(
            next,
            new StaticOptionsMonitor<ServerOptions>(options),
            TestLoggers.Null<BearerAuthMiddleware>(),
            alerter);

        await mw.InvokeAsync(ctx);
        return called ? 200 : ctx.Response.StatusCode;
    }

    // --- Health probes bypass auth ---

    [TestMethod]
    public async Task TestU_HealthzPath_BypassesAuth()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/healthz");
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();
        bool nextCalled = false;

        await RunAsync(ctx, BuildOptions(), alerter, _ => nextCalled = true);

        Assert.IsTrue(nextCalled);
        Assert.IsEmpty(alerter.Alerts);
    }

    [TestMethod]
    public async Task TestU_ReadyzPath_BypassesAuth()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/readyz");
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();
        bool nextCalled = false;

        await RunAsync(ctx, BuildOptions(), alerter, _ => nextCalled = true);

        Assert.IsTrue(nextCalled);
        Assert.IsEmpty(alerter.Alerts);
    }

    // --- Non-/mcp paths get 404 without alert (scanner protection) ---

    [TestMethod]
    public async Task TestU_UnknownPath_Returns404_WithoutAlert()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/.well-known/oauth-protected-resource");
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();
        ctx.Request.Headers["Authorization"] = "Bearer " + GoodToken;

        await RunAsync(ctx, BuildOptions(), alerter);

        Assert.AreEqual(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.IsEmpty(alerter.Alerts);
    }

    [TestMethod]
    public async Task TestU_WordpressLoginScanner_Returns404()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/wp-login.php");
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();

        await RunAsync(ctx, BuildOptions(), alerter);

        Assert.AreEqual(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
    }

    // --- HTTPS enforcement ---

    [TestMethod]
    public async Task TestU_NonHttps_WithRequireHttps_Returns400()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/mcp", scheme: "http");
        ctx.Request.Headers["Authorization"] = "Bearer " + GoodToken;
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();

        await RunAsync(ctx, BuildOptions(requireHttps: true), alerter);

        Assert.AreEqual(StatusCodes.Status400BadRequest, ctx.Response.StatusCode);
    }

    [TestMethod]
    public async Task TestU_NonHttps_WithXForwardedProto_Allowed()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/mcp", scheme: "http");
        ctx.Request.Headers["Authorization"] = "Bearer " + GoodToken;
        ctx.Request.Headers["X-Forwarded-Proto"] = "https";
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();
        bool nextCalled = false;

        await RunAsync(ctx, BuildOptions(requireHttps: true), alerter, _ => nextCalled = true);

        Assert.IsTrue(nextCalled);
    }

    [TestMethod]
    public async Task TestU_NonHttps_WithoutRequireHttps_Allowed()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/mcp", scheme: "http");
        ctx.Request.Headers["Authorization"] = "Bearer " + GoodToken;
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();
        bool nextCalled = false;

        await RunAsync(ctx, BuildOptions(requireHttps: false), alerter, _ => nextCalled = true);

        Assert.IsTrue(nextCalled);
    }

    // --- Empty tokens ---

    [TestMethod]
    public async Task TestU_NoTokensConfigured_Returns503()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/mcp");
        ctx.Request.Headers["Authorization"] = "Bearer " + GoodToken;
        ServerOptions options = BuildOptions();
        options.HttpAuth.Tokens.Clear();
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();

        await RunAsync(ctx, options, alerter);

        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
    }

    // --- Missing bearer ---

    [TestMethod]
    public async Task TestU_MissingAuthHeader_Returns401_AndAlerts()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/mcp");
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();

        await RunAsync(ctx, BuildOptions(), alerter);

        Assert.AreEqual(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        Assert.HasCount(1, alerter.Alerts);
        Assert.AreEqual(AlertKindFromMissing(), alerter.Alerts[0].Kind);
        Assert.AreEqual("missing", alerter.Alerts[0].Tags?["reason"]);
        Assert.AreEqual("Bearer", ctx.Response.Headers.WWWAuthenticate.ToString());
    }

    [TestMethod]
    public async Task TestU_AuthorizationWithoutBearer_Returns401()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/mcp");
        ctx.Request.Headers["Authorization"] = "Basic dXNlcjpwYXNz";
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();

        await RunAsync(ctx, BuildOptions(), alerter);

        Assert.AreEqual(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        Assert.HasCount(1, alerter.Alerts);
    }

    // --- Invalid bearer ---

    [TestMethod]
    public async Task TestU_WrongBearer_Returns401_AndAlerts()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/mcp");
        ctx.Request.Headers["Authorization"] = "Bearer wrong-token-padded-to-32-chars-bbbbb";
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();

        await RunAsync(ctx, BuildOptions(), alerter);

        Assert.AreEqual(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        Assert.HasCount(1, alerter.Alerts);
        Assert.AreEqual("invalid", alerter.Alerts[0].Tags?["reason"]);
    }

    // --- Valid bearer ---

    [TestMethod]
    public async Task TestU_ValidBearer_PassesThrough_AndSetsLabel()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/mcp");
        ctx.Request.Headers["Authorization"] = "Bearer " + GoodToken;
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();
        string? capturedLabel = null;

        await RunAsync(ctx, BuildOptions(), alerter, c =>
        {
            capturedLabel = c.Items[BearerAuthMiddleware.BearerLabelContextKey] as string;
        });

        Assert.AreEqual(GoodLabel, capturedLabel);
        Assert.IsEmpty(alerter.Alerts);
    }

    [TestMethod]
    public async Task TestU_ValidBearer_UnlabeledToken_GetsPlaceholderLabel()
    {
        ServerOptions options = BuildOptions();
        options.HttpAuth.Tokens[0].Label = string.Empty;

        (DefaultHttpContext ctx, _) = BuildContext("/mcp");
        ctx.Request.Headers["Authorization"] = "Bearer " + GoodToken;
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();
        string? capturedLabel = null;

        await RunAsync(ctx, options, alerter, c =>
        {
            capturedLabel = c.Items[BearerAuthMiddleware.BearerLabelContextKey] as string;
        });

        Assert.AreEqual("(unlabeled)", capturedLabel);
    }

    [TestMethod]
    public async Task TestU_ValidBearer_WithExtraWhitespace_Accepted()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/mcp");
        ctx.Request.Headers["Authorization"] = "Bearer   " + GoodToken + "   ";
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();
        bool nextCalled = false;

        await RunAsync(ctx, BuildOptions(), alerter, _ => nextCalled = true);

        Assert.IsTrue(nextCalled);
    }

    // --- X-Forwarded-For parsing for IP-tagged alerts ---

    [TestMethod]
    public async Task TestU_AuthFailure_UsesXForwardedFor_FirstHop()
    {
        (DefaultHttpContext ctx, _) = BuildContext("/mcp");
        ctx.Request.Headers["X-Forwarded-For"] = "203.0.113.42, 10.0.0.1, 10.0.0.2";
        RecordingAdminAlerter alerter = new RecordingAdminAlerter();

        await RunAsync(ctx, BuildOptions(), alerter);

        Assert.HasCount(1, alerter.Alerts);
        Assert.AreEqual("203.0.113.42", alerter.Alerts[0].Tags?["ip"]);
    }

    private static AlertKind AlertKindFromMissing() => AlertKind.AuthFailure;
}
