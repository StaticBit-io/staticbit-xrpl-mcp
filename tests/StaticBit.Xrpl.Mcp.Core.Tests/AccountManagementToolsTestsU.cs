using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Models.Ledger;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class AccountManagementToolsTestsU
{
    private static AccountManagementTools NewTool() => new AccountManagementTools(preparer: null!);

    // --- ParseSignerEntries (internal helper) ---

    [TestMethod]
    public void TestU_ParseSignerEntries_QuorumZero_NoJson_ReturnsNull()
    {
        List<SignerEntryWrapper> result = AccountManagementTools.ParseSignerEntries(json: null, quorum: 0);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TestU_ParseSignerEntries_QuorumZero_WithJson_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseSignerEntries("[{\"account\":\"r1\",\"weight\":1}]", quorum: 0));
    }

    [TestMethod]
    public void TestU_ParseSignerEntries_QuorumPositive_NoJson_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseSignerEntries(json: null, quorum: 1));
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseSignerEntries(json: "   ", quorum: 1));
    }

    [TestMethod]
    public void TestU_ParseSignerEntries_NotArray_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseSignerEntries("{\"account\":\"r1\"}", quorum: 1));
    }

    [TestMethod]
    public void TestU_ParseSignerEntries_EmptyArray_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseSignerEntries("[]", quorum: 1));
    }

    [TestMethod]
    public void TestU_ParseSignerEntries_TooMany_Throws()
    {
        string entries = "[" + string.Join(",", Enumerable.Range(0, 33)
            .Select(i => $"{{\"account\":\"r{i}\",\"weight\":1}}")) + "]";
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseSignerEntries(entries, quorum: 1));
    }

    [TestMethod]
    public void TestU_ParseSignerEntries_MissingAccount_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseSignerEntries("[{\"weight\":1}]", quorum: 1));
    }

    [TestMethod]
    public void TestU_ParseSignerEntries_MissingWeight_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseSignerEntries("[{\"account\":\"r1\"}]", quorum: 1));
    }

    [TestMethod]
    public void TestU_ParseSignerEntries_NonObjectEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseSignerEntries("[\"not-an-object\"]", quorum: 1));
    }

    [TestMethod]
    public void TestU_ParseSignerEntries_Valid_Parsed()
    {
        List<SignerEntryWrapper> result = AccountManagementTools.ParseSignerEntries(
            "[{\"account\":\"rAlice\",\"weight\":2},{\"account\":\"rBob\",\"weight\":3,\"walletLocator\":\"" +
                new string('A', 64) + "\"}]",
            quorum: 4);

        Assert.HasCount(2, result);
        Assert.AreEqual("rAlice", result[0].SignerEntry.Account);
        Assert.AreEqual((ushort)2, result[0].SignerEntry.SignerWeight);
        Assert.IsNull(result[0].SignerEntry.WalletLocator);
        Assert.AreEqual("rBob", result[1].SignerEntry.Account);
        Assert.AreEqual((ushort)3, result[1].SignerEntry.SignerWeight);
        Assert.IsNotNull(result[1].SignerEntry.WalletLocator);
    }

    // --- DepositPreauth: exactly one of authorize / unauthorize ---

    [TestMethod]
    public async Task TestU_DepositPreauth_BothProvided_Throws()
    {
        AccountManagementTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.DepositPreauthPrepareAsync(
            "testnet", "rOwner", authorize: "rA", unauthorize: "rB"));
    }

    [TestMethod]
    public async Task TestU_DepositPreauth_NeitherProvided_Throws()
    {
        AccountManagementTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.DepositPreauthPrepareAsync(
            "testnet", "rOwner"));
    }

    // --- AccountDelete: destination != account ---

    [TestMethod]
    public async Task TestU_AccountDelete_SelfDestination_Throws()
    {
        AccountManagementTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.AccountDeletePrepareAsync(
            "testnet", "rSame", destination: "rSame"));
    }
}
