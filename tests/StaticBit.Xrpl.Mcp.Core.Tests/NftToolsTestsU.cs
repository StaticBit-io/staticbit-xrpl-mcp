using System;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class NftToolsTestsU
{
    private static NftTools NewTool() => new NftTools(pool: null!, preparer: null!);

    // --- SplitOffers (internal helper) ---

    [TestMethod]
    public void TestU_SplitOffers_Single()
    {
        string[] result = NftTools.SplitOffers("ABC123");
        CollectionAssert.AreEqual(new[] { "ABC123" }, result);
    }

    [TestMethod]
    public void TestU_SplitOffers_Multiple_TrimsAndDropsEmpty()
    {
        string[] result = NftTools.SplitOffers(" ABC , DEF,, GHI ");
        CollectionAssert.AreEqual(new[] { "ABC", "DEF", "GHI" }, result);
    }

    [TestMethod]
    public void TestU_SplitOffers_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => NftTools.SplitOffers(""));
        Assert.Throws<ArgumentException>(() => NftTools.SplitOffers("   "));
    }

    [TestMethod]
    public void TestU_SplitOffers_OnlyCommas_Throws()
    {
        Assert.Throws<ArgumentException>(() => NftTools.SplitOffers(",, ,,"));
    }

    // --- NFTokenMint: URI mutual exclusion ---

    [TestMethod]
    public async Task TestU_NftMint_BothUriForms_Throws()
    {
        NftTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.NftMintPrepareAsync(
            "testnet", "rMinter", nfTokenTaxon: 0,
            uriHex: "DEADBEEF", uriPlain: "https://example.com"));
    }

    // --- NFTokenCreateOffer: sell offer must NOT have owner; buy offer MUST have owner ---

    [TestMethod]
    public async Task TestU_NftCreateOffer_Sell_WithOwner_Throws()
    {
        NftTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.NftCreateOfferPrepareAsync(
            "testnet", "rAlice", nfTokenId: "ABC", amount: "100",
            isSellOffer: true, owner: "rBob"));
    }

    [TestMethod]
    public async Task TestU_NftCreateOffer_Buy_NoOwner_Throws()
    {
        NftTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.NftCreateOfferPrepareAsync(
            "testnet", "rAlice", nfTokenId: "ABC", amount: "100",
            isSellOffer: false, owner: null));
    }

    // --- NFTokenAcceptOffer: at least one of sell/buy; brokerFee requires brokered mode ---

    [TestMethod]
    public async Task TestU_NftAcceptOffer_NeitherOfferId_Throws()
    {
        NftTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.NftAcceptOfferPrepareAsync(
            "testnet", "rAlice"));
    }

    [TestMethod]
    public async Task TestU_NftAcceptOffer_BrokerFeeWithoutBrokeredMode_Throws()
    {
        NftTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.NftAcceptOfferPrepareAsync(
            "testnet", "rAlice", sellOfferId: "SELL_ID", brokerFee: "100"));
        await Assert.ThrowsAsync<ArgumentException>(() => tool.NftAcceptOfferPrepareAsync(
            "testnet", "rAlice", buyOfferId: "BUY_ID", brokerFee: "100"));
    }
}
