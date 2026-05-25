using StaticBit.Xrpl.Mcp.Core.Options;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class XrplMcpOptionsTestsU
{
    [TestMethod]
    public void TestU_Defaults_MatchDocumentedValues()
    {
        XrplMcpOptions options = new XrplMcpOptions();

        Assert.AreEqual("mainnet", options.DefaultNetwork);
        Assert.AreEqual((uint)20, options.LastLedgerSequenceOffset);
        Assert.AreEqual(1.0m, options.FeeBumpMultiplier);
        Assert.AreEqual(30, options.RequestTimeoutSeconds);
    }

    [TestMethod]
    public void TestU_DefaultNetworks_HasThreeWellKnown()
    {
        Assert.IsTrue(XrplMcpOptions.DefaultNetworks.ContainsKey("mainnet"));
        Assert.IsTrue(XrplMcpOptions.DefaultNetworks.ContainsKey("testnet"));
        Assert.IsTrue(XrplMcpOptions.DefaultNetworks.ContainsKey("devnet"));
        StringAssert.StartsWith(XrplMcpOptions.DefaultNetworks["mainnet"], "wss://");
    }
}
