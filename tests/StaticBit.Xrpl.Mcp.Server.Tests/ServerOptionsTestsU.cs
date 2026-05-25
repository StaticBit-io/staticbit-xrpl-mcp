using StaticBit.Xrpl.Mcp.Server.Configuration;

namespace StaticBit.Xrpl.Mcp.Server.Tests;

[TestClass]
public class ServerOptionsTestsU
{
    [TestMethod]
    public void TestU_Defaults_MatchDocumentedValues()
    {
        ServerOptions options = new ServerOptions();

        Assert.AreEqual("stdio", options.Transport);
        Assert.AreEqual(5500, options.HttpPort);
        Assert.IsFalse(options.IsHttp);

        Assert.IsTrue(options.HttpAuth.RequireHttps);
        Assert.IsEmpty(options.HttpAuth.Tokens);

        Assert.IsTrue(options.RateLimit.Enabled);
        Assert.AreEqual(60, options.RateLimit.PermitsPerMinute);
        Assert.AreEqual(0, options.RateLimit.QueueLimit);
        Assert.AreEqual("ip", options.RateLimit.PartitionBy);

        Assert.IsFalse(options.Cors.Enabled);
        Assert.IsEmpty(options.Cors.AllowedOrigins);

        Assert.IsFalse(options.RequestLogging.Enabled);
        Assert.IsFalse(options.RequestLogging.IncludeQueryString);

        Assert.IsFalse(options.AdminAlerts.Enabled);
    }

    [TestMethod]
    public void TestU_IsHttp_True_WhenTransportHttp()
    {
        ServerOptions options = new ServerOptions { Transport = "http" };
        Assert.IsTrue(options.IsHttp);
    }

    [TestMethod]
    public void TestU_IsHttp_CaseInsensitive()
    {
        ServerOptions options = new ServerOptions { Transport = "HTTP" };
        Assert.IsTrue(options.IsHttp);
    }
}
