using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Models.Ledger;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class AmmManagementToolsTestsU
{
    private static AmmManagementTools NewTool() => new AmmManagementTools(preparer: null!);

    // --- ParseAuthAccounts (internal helper) ---

    [TestMethod]
    public void TestU_ParseAuthAccounts_Null_ReturnsNull()
    {
        Assert.IsNull(AmmManagementTools.ParseAuthAccounts(null, "rSender"));
        Assert.IsNull(AmmManagementTools.ParseAuthAccounts("   ", "rSender"));
    }

    [TestMethod]
    public void TestU_ParseAuthAccounts_NotArray_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AmmManagementTools.ParseAuthAccounts("\"rA\"", "rSender"));
        Assert.Throws<ArgumentException>(() =>
            AmmManagementTools.ParseAuthAccounts("{\"r\":\"A\"}", "rSender"));
    }

    [TestMethod]
    public void TestU_ParseAuthAccounts_NonStringEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AmmManagementTools.ParseAuthAccounts("[\"rA\", 42]", "rSender"));
    }

    [TestMethod]
    public void TestU_ParseAuthAccounts_EmptyAddress_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AmmManagementTools.ParseAuthAccounts("[\"rA\", \"\"]", "rSender"));
    }

    [TestMethod]
    public void TestU_ParseAuthAccounts_IncludesSender_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AmmManagementTools.ParseAuthAccounts("[\"rSender\"]", "rSender"));
    }

    [TestMethod]
    public void TestU_ParseAuthAccounts_TooMany_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AmmManagementTools.ParseAuthAccounts("[\"r1\",\"r2\",\"r3\",\"r4\",\"r5\"]", "rSender"));
    }

    [TestMethod]
    public void TestU_ParseAuthAccounts_Valid()
    {
        List<AuthAccount>? result = AmmManagementTools.ParseAuthAccounts(
            "[\"rAlice\",\"rBob\",\"rCarol\",\"rDave\"]", "rSender");
        Assert.IsNotNull(result);
        Assert.HasCount(4, result);
        Assert.AreEqual("rAlice", result[0].Account);
        Assert.AreEqual("rDave", result[3].Account);
    }

    // --- AMMCreate / AMMVote: tradingFee bounds ---

    [TestMethod]
    public async Task TestU_AmmCreate_TradingFeeOverMax_Throws()
    {
        AmmManagementTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.AmmCreatePrepareAsync(
            "testnet", "rA", amount: "1000", amount2: "2000",
            tradingFeeBasisPoints: 1001));
    }

    [TestMethod]
    public async Task TestU_AmmVote_TradingFeeOverMax_Throws()
    {
        AmmManagementTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.AmmVotePrepareAsync(
            "testnet", "rA",
            asset1Currency: "XRP", asset1Issuer: null,
            asset2Currency: "USD", asset2Issuer: "rIss",
            tradingFeeBasisPoints: 1001));
    }
}
