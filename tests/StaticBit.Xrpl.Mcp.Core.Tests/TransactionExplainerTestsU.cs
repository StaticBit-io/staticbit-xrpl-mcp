using System;
using System.Text.Json.Nodes;
using StaticBit.Xrpl.Mcp.Core.Services;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class TransactionExplainerTestsU
{
    [TestMethod]
    public void TestU_Explain_NullTx_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TransactionExplainer.Explain(null!));
    }

    [TestMethod]
    public void TestU_Explain_Payment_Xrp()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"Payment\",\"Account\":\"rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH\"," +
            "\"Destination\":\"rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe\",\"Amount\":\"10000000\"," +
            "\"Fee\":\"12\",\"Sequence\":42}")!;

        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "Payment from rN7n7o...fzRH");
        StringAssert.Contains(summary, "to rPT1Sj...pAYe");
        StringAssert.Contains(summary, "10000000 drops XRP");
        StringAssert.Contains(summary, "fee=12 drops");
        StringAssert.Contains(summary, "seq=42");
    }

    [TestMethod]
    public void TestU_Explain_Payment_Token_WithDestTag()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"Payment\",\"Account\":\"rAlice\",\"Destination\":\"rBob\"," +
            "\"Amount\":{\"value\":\"100.50\",\"currency\":\"USD\",\"issuer\":\"rIssuerXXXXXXXXXX\"}," +
            "\"DestinationTag\":12345}")!;

        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "100.50 USD");
        StringAssert.Contains(summary, "rIssue");
        StringAssert.Contains(summary, "DestTag 12345");
    }

    [TestMethod]
    public void TestU_Explain_TrustSet()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"TrustSet\",\"Account\":\"rAlice\"," +
            "\"LimitAmount\":{\"value\":\"1000\",\"currency\":\"USD\",\"issuer\":\"rIss\"}}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "TrustSet from rAlice");
        StringAssert.Contains(summary, "limit 1000 USD");
    }

    [TestMethod]
    public void TestU_Explain_OfferCreate()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"OfferCreate\",\"Account\":\"rAlice\"," +
            "\"TakerGets\":\"10000000\"," +
            "\"TakerPays\":{\"value\":\"50\",\"currency\":\"USD\",\"issuer\":\"rIss\"}}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "OfferCreate from rAlice");
        StringAssert.Contains(summary, "give 10000000 drops XRP");
        StringAssert.Contains(summary, "for 50 USD");
    }

    [TestMethod]
    public void TestU_Explain_NFTokenCreateOffer_Sell()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"NFTokenCreateOffer\",\"Account\":\"rAlice\"," +
            "\"NFTokenID\":\"" + new string('A', 64) + "\"," +
            "\"Amount\":\"1000000\",\"Flags\":1}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "NFTokenCreateOffer (SELL)");
    }

    [TestMethod]
    public void TestU_Explain_NFTokenCreateOffer_Buy_WhenFlagsZero()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"NFTokenCreateOffer\",\"Account\":\"rAlice\"," +
            "\"NFTokenID\":\"" + new string('B', 64) + "\",\"Amount\":\"1000000\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "(BUY)");
    }

    [TestMethod]
    public void TestU_Explain_SignerListSet_Delete()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"SignerListSet\",\"Account\":\"rAlice\",\"SignerQuorum\":0}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "DELETE signer list");
    }

    [TestMethod]
    public void TestU_Explain_SignerListSet_WithEntries()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"SignerListSet\",\"Account\":\"rAlice\",\"SignerQuorum\":3," +
            "\"SignerEntries\":[{\"SignerEntry\":{\"Account\":\"rA\",\"SignerWeight\":2}}," +
            "{\"SignerEntry\":{\"Account\":\"rB\",\"SignerWeight\":1}}]}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "quorum=3");
        StringAssert.Contains(summary, "over 2 signer(s)");
    }

    [TestMethod]
    public void TestU_Explain_EscrowCreate_Conditional()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"EscrowCreate\",\"Account\":\"rAlice\",\"Destination\":\"rBob\"," +
            "\"Amount\":\"5000000\",\"Condition\":\"A0258020...\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "EscrowCreate (conditional)");
    }

    [TestMethod]
    public void TestU_Explain_EscrowCreate_TimeOnly()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"EscrowCreate\",\"Account\":\"rAlice\",\"Destination\":\"rBob\"," +
            "\"Amount\":\"5000000\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "EscrowCreate (time-only)");
    }

    [TestMethod]
    public void TestU_Explain_PaymentChannelClaim_CloseFlag()
    {
        // Flags = 131072 = 0x00020000 = tfClose
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"PaymentChannelClaim\",\"Account\":\"rAlice\"," +
            "\"Channel\":\"" + new string('C', 64) + "\",\"Flags\":131072}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "(CLOSE)");
    }

    [TestMethod]
    public void TestU_Explain_PaymentChannelClaim_RenewFlag()
    {
        // Flags = 65536 = 0x00010000 = tfRenew
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"PaymentChannelClaim\",\"Account\":\"rAlice\"," +
            "\"Channel\":\"" + new string('D', 64) + "\",\"Flags\":65536}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "(RENEW)");
    }

    [TestMethod]
    public void TestU_Explain_AccountSet_NoOp()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"AccountSet\",\"Account\":\"rAlice\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "no-op");
    }

    [TestMethod]
    public void TestU_Explain_AccountSet_WithSetFlag()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"AccountSet\",\"Account\":\"rAlice\",\"SetFlag\":8}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "SetFlag=8");
    }

    [TestMethod]
    public void TestU_Explain_SetRegularKey_Remove()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"SetRegularKey\",\"Account\":\"rAlice\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "REMOVE existing regular key");
    }

    [TestMethod]
    public void TestU_Explain_DepositPreauth_Authorize()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"DepositPreauth\",\"Account\":\"rAlice\",\"Authorize\":\"rBob\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "AUTHORIZE rBob");
    }

    [TestMethod]
    public void TestU_Explain_Clawback_HolderFromAmountIssuer()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"Clawback\",\"Account\":\"rIssuer\"," +
            "\"Amount\":{\"value\":\"100\",\"currency\":\"USD\",\"issuer\":\"rHolder\"}}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "Clawback by issuer rIssuer");
        StringAssert.Contains(summary, "from rHolder");
    }

    [TestMethod]
    public void TestU_Explain_AmmCreate_ShowsPoolAndFee()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"AMMCreate\",\"Account\":\"rAlice\"," +
            "\"Amount\":\"10000000\"," +
            "\"Amount2\":{\"value\":\"100\",\"currency\":\"USD\",\"issuer\":\"rIss\"}," +
            "\"TradingFee\":500}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "AMMCreate by rAlice");
        StringAssert.Contains(summary, "tradingFee=500");
    }

    [TestMethod]
    public void TestU_Explain_UnknownTransactionType_GenericFallback()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"SomethingNewAndExotic\",\"Account\":\"rAlice\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "SomethingNewAndExotic from rAlice");
    }

    [TestMethod]
    public void TestU_Explain_CommonSuffix_AllThreeFields()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"AccountSet\",\"Account\":\"rA\"," +
            "\"Fee\":\"15\",\"Sequence\":99,\"LastLedgerSequence\":1000}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "[fee=15 drops, seq=99, LLS=1000]");
    }
}
