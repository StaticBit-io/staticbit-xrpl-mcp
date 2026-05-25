using Microsoft.AspNetCore.Http;
using StaticBit.Xrpl.Mcp.Server;
using StaticBit.Xrpl.Mcp.Server.Middleware;

namespace StaticBit.Xrpl.Mcp.Server.Tests;

[TestClass]
public class RateLimitPartitionKeyTestsU
{
    private static DefaultHttpContext BuildContext(string ip, string? label)
    {
        DefaultHttpContext ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Forwarded-For"] = ip;
        if (label is not null) ctx.Items[BearerAuthMiddleware.BearerLabelContextKey] = label;
        return ctx;
    }

    [TestMethod]
    public void TestU_PartitionBy_Ip_UsesXForwardedFor()
    {
        DefaultHttpContext ctx = BuildContext("203.0.113.42", label: "alice");
        string key = Program.ResolveRateLimitPartitionKey(ctx, "ip");
        Assert.AreEqual("203.0.113.42", key);
    }

    [TestMethod]
    public void TestU_PartitionBy_Ip_UnknownMode_FallsBackToIp()
    {
        DefaultHttpContext ctx = BuildContext("1.2.3.4", label: "alice");
        string key = Program.ResolveRateLimitPartitionKey(ctx, "made-up-mode");
        Assert.AreEqual("1.2.3.4", key);
    }

    [TestMethod]
    public void TestU_PartitionBy_Ip_NullMode_FallsBackToIp()
    {
        DefaultHttpContext ctx = BuildContext("1.2.3.4", label: null);
        string key = Program.ResolveRateLimitPartitionKey(ctx, partitionBy: null!);
        Assert.AreEqual("1.2.3.4", key);
    }

    [TestMethod]
    public void TestU_PartitionBy_Token_UsesLabel()
    {
        DefaultHttpContext ctx = BuildContext("1.2.3.4", label: "alice");
        string key = Program.ResolveRateLimitPartitionKey(ctx, "token");
        Assert.AreEqual("alice", key);
    }

    [TestMethod]
    public void TestU_PartitionBy_Token_NoLabel_FallsBackToNoauthIp()
    {
        DefaultHttpContext ctx = BuildContext("1.2.3.4", label: null);
        string key = Program.ResolveRateLimitPartitionKey(ctx, "token");
        Assert.AreEqual("noauth:1.2.3.4", key);
    }

    [TestMethod]
    public void TestU_PartitionBy_Both_CombinesLabelAndIp()
    {
        DefaultHttpContext ctx = BuildContext("1.2.3.4", label: "alice");
        string key = Program.ResolveRateLimitPartitionKey(ctx, "both");
        Assert.AreEqual("alice|1.2.3.4", key);
    }

    [TestMethod]
    public void TestU_PartitionBy_Both_NoLabel_UsesNoauthPrefix()
    {
        DefaultHttpContext ctx = BuildContext("1.2.3.4", label: null);
        string key = Program.ResolveRateLimitPartitionKey(ctx, "both");
        Assert.AreEqual("noauth|1.2.3.4", key);
    }

    [TestMethod]
    public void TestU_PartitionBy_Mode_CaseInsensitive()
    {
        DefaultHttpContext ctx = BuildContext("1.2.3.4", label: "alice");
        string keyUpper = Program.ResolveRateLimitPartitionKey(ctx, "TOKEN");
        string keyMixed = Program.ResolveRateLimitPartitionKey(ctx, "  Token  ");
        Assert.AreEqual("alice", keyUpper);
        Assert.AreEqual("alice", keyMixed);
    }
}
