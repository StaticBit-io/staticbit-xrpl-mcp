using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Models;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class SubscriptionToolsTestsU
{
    private static SubscriptionTools NewTool() => new SubscriptionTools(pool: null!);

    // --- ParseStreams (internal helper) ---

    [TestMethod]
    public void TestU_ParseStreams_Null_ReturnsNull()
    {
        Assert.IsNull(SubscriptionTools.ParseStreams(null));
        Assert.IsNull(SubscriptionTools.ParseStreams("   "));
    }

    [TestMethod]
    public void TestU_ParseStreams_OnlyCommas_ReturnsNull()
    {
        Assert.IsNull(SubscriptionTools.ParseStreams(",,  ,"));
    }

    [TestMethod]
    public void TestU_ParseStreams_UnknownStream_Throws()
    {
        Assert.Throws<ArgumentException>(() => SubscriptionTools.ParseStreams("ledger,unknown_stream"));
    }

    [TestMethod]
    public void TestU_ParseStreams_Valid_TrimsAndMaps()
    {
        List<StreamType>? result = SubscriptionTools.ParseStreams(" ledger , transactions ,validations ");
        Assert.IsNotNull(result);
        Assert.HasCount(3, result);
        Assert.AreEqual(StreamType.Ledger, result[0]);
        Assert.AreEqual(StreamType.Transactions, result[1]);
        Assert.AreEqual(StreamType.Validations, result[2]);
    }

    [TestMethod]
    public void TestU_ParseStreams_AllSupportedValues()
    {
        List<StreamType>? result = SubscriptionTools.ParseStreams(
            "ledger,transactions,transactions_proposed,validations,manifests,server,peer_status,consensus,book_changes");
        Assert.IsNotNull(result);
        Assert.HasCount(9, result);
    }

    // --- ParseAddresses (internal helper) ---

    [TestMethod]
    public void TestU_ParseAddresses_Null_ReturnsNull()
    {
        Assert.IsNull(SubscriptionTools.ParseAddresses(null, "accountsJson"));
        Assert.IsNull(SubscriptionTools.ParseAddresses("   ", "accountsJson"));
    }

    [TestMethod]
    public void TestU_ParseAddresses_NotArray_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            SubscriptionTools.ParseAddresses("\"rA\"", "accountsJson"));
    }

    [TestMethod]
    public void TestU_ParseAddresses_NonStringEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            SubscriptionTools.ParseAddresses("[\"rA\", 42]", "accountsJson"));
    }

    [TestMethod]
    public void TestU_ParseAddresses_EmptyAddress_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            SubscriptionTools.ParseAddresses("[\"\"]", "accountsJson"));
    }

    [TestMethod]
    public void TestU_ParseAddresses_EmptyArray_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            SubscriptionTools.ParseAddresses("[]", "accountsJson"));
    }

    [TestMethod]
    public void TestU_ParseAddresses_Valid()
    {
        List<string>? result = SubscriptionTools.ParseAddresses("[\"rAlice\",\"rBob\"]", "accountsJson");
        Assert.IsNotNull(result);
        Assert.HasCount(2, result);
        Assert.AreEqual("rAlice", result[0]);
        Assert.AreEqual("rBob", result[1]);
    }

    // --- AccountTxSince: sinceLedger lower bound ---

    [TestMethod]
    public async Task TestU_AccountTxSince_NegativeSince_Throws()
    {
        SubscriptionTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.AccountTxSinceAsync(
            "testnet", "rA", sinceLedger: -5));
    }
}
