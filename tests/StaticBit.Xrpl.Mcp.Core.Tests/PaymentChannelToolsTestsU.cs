using System;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class PaymentChannelToolsTestsU
{
    private static PaymentChannelTools NewTool() => new PaymentChannelTools(preparer: null!);

    [TestMethod]
    public async Task TestU_PayChanCreate_EmptyAmount_Throws()
    {
        PaymentChannelTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.PaymentChannelCreatePrepareAsync(
            "testnet", "rA", "rB", amountDrops: "", settleDelaySeconds: 86400, publicKeyHex: "ABCD"));
    }

    [TestMethod]
    public async Task TestU_PayChanCreate_EmptyPublicKey_Throws()
    {
        PaymentChannelTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.PaymentChannelCreatePrepareAsync(
            "testnet", "rA", "rB", amountDrops: "1000", settleDelaySeconds: 86400, publicKeyHex: ""));
    }

    [TestMethod]
    public async Task TestU_PayChanClaim_RenewAndClose_Throws()
    {
        PaymentChannelTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.PaymentChannelClaimPrepareAsync(
            "testnet", "rA", channelId: "CHAN", renew: true, close: true));
    }

    [TestMethod]
    public async Task TestU_PayChanClaim_OnlySignatureWithoutPublicKey_Throws()
    {
        PaymentChannelTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.PaymentChannelClaimPrepareAsync(
            "testnet", "rA", channelId: "CHAN",
            signatureHex: "SIG", publicKeyHex: null));
    }

    [TestMethod]
    public async Task TestU_PayChanClaim_OnlyPublicKeyWithoutSignature_Throws()
    {
        PaymentChannelTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.PaymentChannelClaimPrepareAsync(
            "testnet", "rA", channelId: "CHAN",
            signatureHex: null, publicKeyHex: "PUB"));
    }
}
