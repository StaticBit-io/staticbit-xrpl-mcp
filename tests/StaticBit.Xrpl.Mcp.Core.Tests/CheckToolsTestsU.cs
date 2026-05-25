using System;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class CheckToolsTestsU
{
    private static CheckTools NewTool() => new CheckTools(preparer: null!);

    [TestMethod]
    public async Task TestU_CheckCash_NeitherAmountNorDeliverMin_Throws()
    {
        CheckTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.CheckCashPrepareAsync(
            "testnet", "rA", checkId: "ABC", amount: null, deliverMin: null));
    }

    [TestMethod]
    public async Task TestU_CheckCash_BothAmountAndDeliverMin_Throws()
    {
        CheckTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.CheckCashPrepareAsync(
            "testnet", "rA", checkId: "ABC", amount: "1000", deliverMin: "500"));
    }
}
