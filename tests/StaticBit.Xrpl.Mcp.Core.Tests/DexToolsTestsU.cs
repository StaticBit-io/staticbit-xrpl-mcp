using System;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class DexToolsTestsU
{
    // pool is never reached: every case below must fail on input validation FIRST,
    // before any connection is opened. A null pool would NRE if validation ran late.
    private static DexTools NewTool() => new DexTools(pool: null!);

    [TestMethod]
    public async Task TestU_BookOffers_MissingNetwork_NamesNetwork()
    {
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            NewTool().BookOffersAsync("", "XRP", null, "USD", "rIssuer"));

        StringAssert.Contains(ex.Message, "Network");
    }

    [TestMethod]
    public async Task TestU_BookOffers_MissingTakerGetsCurrency_NamesField()
    {
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            NewTool().BookOffersAsync("testnet", "", null, "USD", "rIssuer"));

        StringAssert.Contains(ex.Message, "takerGetsCurrency");
    }

    [TestMethod]
    public async Task TestU_BookOffers_MissingTakerPaysIssuer_NamesField()
    {
        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            NewTool().BookOffersAsync("testnet", "XRP", null, "USD", takerPaysIssuer: null));

        StringAssert.Contains(ex.Message, "takerPaysIssuer");
    }

    [TestMethod]
    public async Task TestU_BookOffers_XrpPair_NoValidationError_ReachesPool()
    {
        // Both sides valid (XRP needs no issuer) → validation passes, so the null pool is
        // dereferenced. Proves we did NOT over-validate a legitimate XRP/XRP request.
        await Assert.ThrowsAsync<NullReferenceException>(() =>
            NewTool().BookOffersAsync("testnet", "XRP", null, "XRP", null));
    }
}
