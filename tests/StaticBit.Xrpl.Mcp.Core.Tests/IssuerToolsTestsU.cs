using System;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class IssuerToolsTestsU
{
    private static IssuerTools NewTool() => new IssuerTools(preparer: null!);

    [TestMethod]
    public async Task TestU_Clawback_IssuerEqualsHolder_Throws()
    {
        // amount.issuer = sender — meaningless ("claw back from yourself").
        IssuerTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ClawbackPrepareAsync(
            "testnet", "rIssuer",
            amount: "{\"value\":\"100\",\"currency\":\"USD\",\"issuer\":\"rIssuer\"}",
            holder: null));
    }

    [TestMethod]
    public async Task TestU_Clawback_XrpAmount_NoHolder_Throws()
    {
        // XRP drops string has no issuer; without holder we can't tell who to claw from.
        IssuerTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.ClawbackPrepareAsync(
            "testnet", "rIssuer",
            amount: "1000",
            holder: null));
    }
}
