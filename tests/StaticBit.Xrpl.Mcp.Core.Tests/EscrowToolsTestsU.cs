using System;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class EscrowToolsTestsU
{
    private static EscrowTools NewTool() => new EscrowTools(preparer: null!);

    [TestMethod]
    public async Task TestU_EscrowCreate_NoTimes_Throws()
    {
        EscrowTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.EscrowCreatePrepareAsync(
            "testnet", "rA", "rB", amount: "1000",
            finishAfterUtc: null, cancelAfterUtc: null));
    }

    [TestMethod]
    public async Task TestU_EscrowCreate_OnlyCancelAfter_NoCondition_Throws()
    {
        EscrowTools tool = NewTool();
        // cancelAfter alone is insufficient: rule is "FinishAfter or Condition required".
        await Assert.ThrowsAsync<ArgumentException>(() => tool.EscrowCreatePrepareAsync(
            "testnet", "rA", "rB", amount: "1000",
            finishAfterUtc: null, cancelAfterUtc: DateTime.UtcNow.AddDays(1),
            conditionHex: null));
    }

    [TestMethod]
    public async Task TestU_EscrowFinish_OnlyConditionWithoutFulfillment_Throws()
    {
        EscrowTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.EscrowFinishPrepareAsync(
            "testnet", "rA", owner: "rB", offerSequence: 5,
            conditionHex: "DEADBEEF", fulfillmentHex: null));
    }

    [TestMethod]
    public async Task TestU_EscrowFinish_OnlyFulfillmentWithoutCondition_Throws()
    {
        EscrowTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.EscrowFinishPrepareAsync(
            "testnet", "rA", owner: "rB", offerSequence: 5,
            conditionHex: null, fulfillmentHex: "ABC123"));
    }
}
