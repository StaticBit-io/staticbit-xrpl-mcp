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

    [TestMethod]
    public void TestU_Explain_MPTokenIssuanceCreate_DecodesFlagsAndScalars()
    {
        // Flags = 2|4|32 = CanLock|RequireAuth|CanTransfer
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"MPTokenIssuanceCreate\",\"Account\":\"rIssuerXXXXXXXXX\"," +
            "\"AssetScale\":2,\"MaximumAmount\":\"1000000\",\"TransferFee\":1000,\"Flags\":38}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "MPTokenIssuanceCreate by");
        StringAssert.Contains(summary, "assetScale=2");
        StringAssert.Contains(summary, "maxAmount=1000000");
        StringAssert.Contains(summary, "transferFee=1000");
        StringAssert.Contains(summary, "CanLock");
        StringAssert.Contains(summary, "RequireAuth");
        StringAssert.Contains(summary, "CanTransfer");
    }

    [TestMethod]
    public void TestU_Explain_MPTokenIssuanceCreate_UncappedWhenNoMax()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"MPTokenIssuanceCreate\",\"Account\":\"rIss\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "maxAmount=uncapped");
        StringAssert.Contains(summary, "[none]");
    }

    [TestMethod]
    public void TestU_Explain_MPTokenIssuanceDestroy_ShortensId()
    {
        string id = new string('A', 48);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"MPTokenIssuanceDestroy\",\"Account\":\"rIss\"," +
            "\"MPTokenIssuanceID\":\"" + id + "\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "MPTokenIssuanceDestroy");
        StringAssert.Contains(summary, "...");
    }

    [TestMethod]
    public void TestU_Explain_MPTokenIssuanceSet_GlobalLock()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"MPTokenIssuanceSet\",\"Account\":\"rIss\"," +
            "\"MPTokenIssuanceID\":\"" + new string('B', 48) + "\",\"Flags\":1}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "scope=global");
        StringAssert.Contains(summary, "action=LOCK");
    }

    [TestMethod]
    public void TestU_Explain_MPTokenIssuanceSet_HolderUnlock()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"MPTokenIssuanceSet\",\"Account\":\"rIss\"," +
            "\"MPTokenIssuanceID\":\"" + new string('B', 48) + "\"," +
            "\"Holder\":\"rHolder\",\"Flags\":2}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "scope=holder=rHolde");
        StringAssert.Contains(summary, "action=UNLOCK");
    }

    [TestMethod]
    public void TestU_Explain_MPTokenAuthorize_HolderOptIn()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"MPTokenAuthorize\",\"Account\":\"rHolder\"," +
            "\"MPTokenIssuanceID\":\"" + new string('C', 48) + "\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "holder OPT-IN");
    }

    [TestMethod]
    public void TestU_Explain_MPTokenAuthorize_IssuerRevoke()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"MPTokenAuthorize\",\"Account\":\"rIss\"," +
            "\"MPTokenIssuanceID\":\"" + new string('C', 48) + "\"," +
            "\"Holder\":\"rTarget\",\"Flags\":1}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "issuer REVOKE for rTarge");
    }

    [TestMethod]
    public void TestU_Explain_Batch_AllOrNothing_WithInner()
    {
        // 0x00010000 = 65536 = AllOrNothing
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"Batch\",\"Account\":\"rOuter\",\"Flags\":65536," +
            "\"RawTransactions\":[" +
            "{\"RawTransaction\":{\"TransactionType\":\"Payment\",\"Account\":\"rAlice\",\"Destination\":\"rBob\",\"Amount\":\"1000\"}}," +
            "{\"RawTransaction\":{\"TransactionType\":\"Payment\",\"Account\":\"rAlice\",\"Destination\":\"rCarol\",\"Amount\":\"2000\"}}" +
            "]}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "Batch by rOuter");
        StringAssert.Contains(summary, "mode=AllOrNothing");
        StringAssert.Contains(summary, "2 inner tx");
        StringAssert.Contains(summary, "Payment from rAlice");
    }

    [TestMethod]
    public void TestU_Explain_Batch_OnlyOne_ShowsCorrectMode()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"Batch\",\"Account\":\"rOuter\",\"Flags\":131072," +
            "\"RawTransactions\":[" +
            "{\"RawTransaction\":{\"TransactionType\":\"Payment\",\"Account\":\"rA\",\"Destination\":\"rB\",\"Amount\":\"1\"}}" +
            "]}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "mode=OnlyOne");
    }

    [TestMethod]
    public void TestU_Explain_Batch_WithBatchSigners_Count()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"Batch\",\"Account\":\"rOuter\",\"Flags\":524288," +
            "\"RawTransactions\":[" +
            "{\"RawTransaction\":{\"TransactionType\":\"Payment\",\"Account\":\"rA\",\"Destination\":\"rB\",\"Amount\":\"1\"}}" +
            "]," +
            "\"BatchSigners\":[{\"BatchSigner\":{\"Account\":\"rCo\"}}]}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "mode=Independent");
        StringAssert.Contains(summary, "1 batchSigner(s)");
    }

    [TestMethod]
    public void TestU_Explain_TicketCreate_ShowsCount()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"TicketCreate\",\"Account\":\"rAlice\",\"TicketCount\":5}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "TicketCreate by rAlice");
        StringAssert.Contains(summary, "reserve 5 Ticket(s)");
    }

    [TestMethod]
    public void TestU_Explain_NFTokenModify_NewUri()
    {
        string nftId = new string('A', 64);
        // Hex for 4 bytes
        string newUri = "DEADBEEF";
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"NFTokenModify\",\"Account\":\"rAlice\"," +
            "\"NFTokenID\":\"" + nftId + "\",\"URI\":\"" + newUri + "\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "NFTokenModify by rAlice");
        StringAssert.Contains(summary, "new URI (4 bytes)");
    }

    [TestMethod]
    public void TestU_Explain_NFTokenModify_ClearUri()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"NFTokenModify\",\"Account\":\"rAlice\"," +
            "\"NFTokenID\":\"" + new string('B', 64) + "\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "CLEAR URI");
    }

    [TestMethod]
    public void TestU_Explain_NFTokenModify_WithOwner()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"NFTokenModify\",\"Account\":\"rIssuer\"," +
            "\"NFTokenID\":\"" + new string('C', 64) + "\",\"Owner\":\"rHolder\",\"URI\":\"AB\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "held by rHolde");
    }

    [TestMethod]
    public void TestU_Explain_OracleSet_ShowsCountsAndId()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"OracleSet\",\"Account\":\"rOracle\",\"OracleDocumentID\":42," +
            "\"LastUpdateTime\":1700000000," +
            "\"PriceDataSeries\":[" +
            "{\"PriceData\":{\"BaseAsset\":\"XRP\",\"QuoteAsset\":\"USD\"}}," +
            "{\"PriceData\":{\"BaseAsset\":\"BTC\",\"QuoteAsset\":\"USD\"}}" +
            "]}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "OracleSet by rOracle");
        StringAssert.Contains(summary, "id=42");
        StringAssert.Contains(summary, "2 price entries");
        StringAssert.Contains(summary, "lastUpdate=1700000000");
    }

    [TestMethod]
    public void TestU_Explain_OracleSet_SinglePriceUsesSingular()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"OracleSet\",\"Account\":\"rO\",\"OracleDocumentID\":1," +
            "\"LastUpdateTime\":1700000000," +
            "\"PriceDataSeries\":[{\"PriceData\":{\"BaseAsset\":\"XRP\",\"QuoteAsset\":\"USD\"}}]}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "1 price entry");
    }

    [TestMethod]
    public void TestU_Explain_OracleDelete_ShowsId()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"OracleDelete\",\"Account\":\"rO\",\"OracleDocumentID\":99}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "OracleDelete by rO");
        StringAssert.Contains(summary, "id=99");
    }

    [TestMethod]
    public void TestU_Explain_DelegateSet_ShowsTypeNames()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"DelegateSet\",\"Account\":\"rOwner\",\"Authorize\":\"rDeleg\"," +
            "\"Permissions\":[" +
            "{\"Permission\":{\"PermissionValue\":\"Payment\"}}," +
            "{\"Permission\":{\"PermissionValue\":\"TrustSet\"}}" +
            "]}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "DelegateSet by rOwner");
        StringAssert.Contains(summary, "delegate to rDeleg");
        StringAssert.Contains(summary, "2 tx-type(s)");
        StringAssert.Contains(summary, "[Payment,TrustSet]");
    }

    [TestMethod]
    public void TestU_Explain_DelegateSet_EmptyPermissions_Clear()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"DelegateSet\",\"Account\":\"rOwner\",\"Authorize\":\"rDeleg\"," +
            "\"Permissions\":[]}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "CLEAR delegation");
        StringAssert.Contains(summary, "to rDeleg");
    }

    [TestMethod]
    public void TestU_Explain_CredentialCreate()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"CredentialCreate\",\"Account\":\"rIssuerXXXXXX\"," +
            "\"Subject\":\"rSubjectYYYY\",\"CredentialType\":\"4B5943\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "CredentialCreate by issuer rIssue");
        StringAssert.Contains(summary, "for subject rSubje");
        StringAssert.Contains(summary, "type=4B5943");
    }

    [TestMethod]
    public void TestU_Explain_CredentialAccept()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"CredentialAccept\",\"Account\":\"rSubject\"," +
            "\"Issuer\":\"rIssuerZZZ\",\"CredentialType\":\"AB\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "CredentialAccept by subject rSubject");
        StringAssert.Contains(summary, "from rIssue");
    }

    [TestMethod]
    public void TestU_Explain_CredentialDelete_IssuerRevoke()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"CredentialDelete\",\"Account\":\"rIssuer\"," +
            "\"Subject\":\"rSubjectQQ\",\"CredentialType\":\"AB\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "CredentialDelete by rIssuer");
        StringAssert.Contains(summary, "issuer revoke for rSubje");
    }

    [TestMethod]
    public void TestU_Explain_CredentialDelete_SubjectUnaccept()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"CredentialDelete\",\"Account\":\"rSubject\"," +
            "\"Issuer\":\"rIssuerAA\",\"CredentialType\":\"AB\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "subject un-accept of rIssue");
    }

    [TestMethod]
    public void TestU_Explain_CredentialDelete_ExpirySweep()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"CredentialDelete\",\"Account\":\"rAnyone\"," +
            "\"Subject\":\"rSub\",\"Issuer\":\"rIss\",\"CredentialType\":\"AB\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "expiry sweep");
    }

    [TestMethod]
    public void TestU_Explain_PermissionedDomainSet_Create()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"PermissionedDomainSet\",\"Account\":\"rOwner\"," +
            "\"AcceptedCredentials\":[{\"Credential\":{\"Issuer\":\"rIss\",\"CredentialType\":\"AB\"}}," +
            "{\"Credential\":{\"Issuer\":\"rIss2\",\"CredentialType\":\"CD\"}}]}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "PermissionedDomainSet by rOwner");
        StringAssert.Contains(summary, "CREATE");
        StringAssert.Contains(summary, "2 accepted credential(s)");
    }

    [TestMethod]
    public void TestU_Explain_PermissionedDomainSet_Modify()
    {
        string domainId = new string('B', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"PermissionedDomainSet\",\"Account\":\"rOwner\"," +
            "\"DomainID\":\"" + domainId + "\"," +
            "\"AcceptedCredentials\":[{\"Credential\":{\"Issuer\":\"rIss\",\"CredentialType\":\"AB\"}}]}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "MODIFY");
        StringAssert.Contains(summary, "1 accepted credential(s)");
    }

    [TestMethod]
    public void TestU_Explain_PermissionedDomainDelete()
    {
        string domainId = new string('C', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"PermissionedDomainDelete\",\"Account\":\"rOwner\"," +
            "\"DomainID\":\"" + domainId + "\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "PermissionedDomainDelete by rOwner");
    }

    [TestMethod]
    public void TestU_Explain_DIDSet_ShowsFields()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"DIDSet\",\"Account\":\"rOwner\"," +
            "\"Data\":\"DEADBEEF\",\"URI\":\"AABBCCDD\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "DIDSet on rOwner");
        StringAssert.Contains(summary, "Data(4b)");
        StringAssert.Contains(summary, "URI(4b)");
    }

    [TestMethod]
    public void TestU_Explain_DIDSet_NoFields_NoOp()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"DIDSet\",\"Account\":\"rOwner\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "no-op");
    }

    [TestMethod]
    public void TestU_Explain_DIDDelete()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"DIDDelete\",\"Account\":\"rOwner\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "DIDDelete on rOwner");
    }

    [TestMethod]
    public void TestU_Explain_AmmClawback_WithAmount()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"AMMClawback\",\"Account\":\"rIssuer\",\"Holder\":\"rHolderZZZ\"," +
            "\"Asset\":{\"currency\":\"USD\",\"issuer\":\"rIssuer\"}," +
            "\"Asset2\":{\"currency\":\"XRP\"}," +
            "\"Amount\":{\"value\":\"50\",\"currency\":\"USD\",\"issuer\":\"rIssuer\"}}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "AMMClawback by issuer rIssuer");
        StringAssert.Contains(summary, "from holder rHolde");
        StringAssert.Contains(summary, "USD");
        StringAssert.Contains(summary, "XRP");
        StringAssert.Contains(summary, "amount=50 USD");
    }

    [TestMethod]
    public void TestU_Explain_AmmClawback_NoAmount_ShowsMaxAvailable()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"AMMClawback\",\"Account\":\"rIssuer\",\"Holder\":\"rHolderZZZ\"," +
            "\"Asset\":{\"currency\":\"USD\",\"issuer\":\"rIssuer\"}," +
            "\"Asset2\":{\"currency\":\"XRP\"}}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "amount=max available");
    }

    // --- Vault (XLS-65) ---

    [TestMethod]
    public void TestU_Explain_VaultCreate_WithFlags()
    {
        // 0x00010000 | 0x00020000 = 0x00030000 = 196608
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"VaultCreate\",\"Account\":\"rOwner\"," +
            "\"Asset\":{\"currency\":\"USD\",\"issuer\":\"rIss\"}," +
            "\"Amount\":{\"value\":\"100\",\"currency\":\"USD\",\"issuer\":\"rIss\"}," +
            "\"AssetsMaximum\":\"100000\",\"Flags\":196608}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "VaultCreate by rOwner");
        StringAssert.Contains(summary, "asset=USD");
        StringAssert.Contains(summary, "initial=100 USD");
        StringAssert.Contains(summary, "max=100000");
        StringAssert.Contains(summary, "private+non-transferable");
    }

    [TestMethod]
    public void TestU_Explain_VaultSet_ChangeDataAndMax()
    {
        string vid = new string('A', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"VaultSet\",\"Account\":\"rOwner\"," +
            "\"VaultID\":\"" + vid + "\"," +
            "\"Data\":\"DEADBEEF\",\"AssetsMaximum\":\"200000\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "VaultSet by rOwner");
        StringAssert.Contains(summary, "Data(4b)");
        StringAssert.Contains(summary, "AssetsMaximum=200000");
    }

    [TestMethod]
    public void TestU_Explain_VaultSet_ClearDomain()
    {
        string vid = new string('B', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"VaultSet\",\"Account\":\"rOwner\"," +
            "\"VaultID\":\"" + vid + "\"," +
            "\"DomainID\":\"\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "DomainID=CLEAR");
    }

    [TestMethod]
    public void TestU_Explain_VaultDelete()
    {
        string vid = new string('C', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"VaultDelete\",\"Account\":\"rOwner\",\"VaultID\":\"" + vid + "\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "VaultDelete by rOwner");
    }

    [TestMethod]
    public void TestU_Explain_VaultDeposit()
    {
        string vid = new string('D', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"VaultDeposit\",\"Account\":\"rDepositor\"," +
            "\"VaultID\":\"" + vid + "\",\"Amount\":\"5000000\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "VaultDeposit by rDepositor");
        StringAssert.Contains(summary, "5000000 drops XRP");
    }

    [TestMethod]
    public void TestU_Explain_VaultWithdraw_SelfDestination()
    {
        string vid = new string('E', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"VaultWithdraw\",\"Account\":\"rShareholder\"," +
            "\"VaultID\":\"" + vid + "\",\"Amount\":\"1000000\",\"Destination\":\"rShareholder\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "VaultWithdraw by rShareholder");
        StringAssert.Contains(summary, "→ self");
    }

    [TestMethod]
    public void TestU_Explain_VaultClawback_MaxAvailable()
    {
        string vid = new string('F', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"VaultClawback\",\"Account\":\"rIssuer\"," +
            "\"VaultID\":\"" + vid + "\",\"Holder\":\"rHolderQQ\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "VaultClawback by issuer rIssuer");
        StringAssert.Contains(summary, "from rHolde");
        StringAssert.Contains(summary, "amount=max available");
    }

    // --- XChain (XLS-38) ---

    private const string BridgeNodeJson =
        "\"XChainBridge\":{\"LockingChainDoor\":\"rLockerXXXX\"," +
        "\"LockingChainIssue\":{\"currency\":\"XRP\"}," +
        "\"IssuingChainDoor\":\"rIssuerXXXX\"," +
        "\"IssuingChainIssue\":{\"currency\":\"XRP\"}}";

    [TestMethod]
    public void TestU_Explain_XChainCreateBridge_WithMinCreate()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"XChainCreateBridge\",\"Account\":\"rDoor\"," +
            BridgeNodeJson + ",\"SignatureReward\":\"100\",\"MinAccountCreateAmount\":\"5000000\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "XChainCreateBridge by rDoor");
        StringAssert.Contains(summary, "rLocke");
        StringAssert.Contains(summary, "rIssue");
        StringAssert.Contains(summary, "reward=100 drops XRP");
        StringAssert.Contains(summary, "minCreate=5000000 drops XRP");
    }

    [TestMethod]
    public void TestU_Explain_XChainModifyBridge_ClearMinCreate()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"XChainModifyBridge\",\"Account\":\"rDoor\"," +
            BridgeNodeJson + ",\"Flags\":65536}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "XChainModifyBridge by rDoor");
        StringAssert.Contains(summary, "CLEAR minCreate");
    }

    [TestMethod]
    public void TestU_Explain_XChainCreateClaimID()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"XChainCreateClaimID\",\"Account\":\"rRecip\"," +
            BridgeNodeJson + ",\"SignatureReward\":\"100\",\"OtherChainSource\":\"rSourceQQQ\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "XChainCreateClaimID by rRecip");
        StringAssert.Contains(summary, "otherChainSource=rSourc");
        StringAssert.Contains(summary, "reward=100 drops XRP");
    }

    [TestMethod]
    public void TestU_Explain_XChainCommit_WithDestination()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"XChainCommit\",\"Account\":\"rSender\"," +
            BridgeNodeJson + ",\"XChainClaimID\":\"7\",\"Amount\":\"1000000\"," +
            "\"OtherChainDestination\":\"rDestinationQQ\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "XChainCommit by rSender");
        StringAssert.Contains(summary, "claimId=7");
        StringAssert.Contains(summary, "1000000 drops XRP");
        StringAssert.Contains(summary, "→ rDesti");
    }

    [TestMethod]
    public void TestU_Explain_XChainCommit_ExplicitClaim()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"XChainCommit\",\"Account\":\"rSender\"," +
            BridgeNodeJson + ",\"XChainClaimID\":\"7\",\"Amount\":\"1000000\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "→ explicit-claim");
    }

    [TestMethod]
    public void TestU_Explain_XChainClaim()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"XChainClaim\",\"Account\":\"rRecip\"," +
            BridgeNodeJson + ",\"XChainClaimID\":\"7\"," +
            "\"Destination\":\"rDestQQQQ\",\"Amount\":\"1000000\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "XChainClaim by rRecip");
        StringAssert.Contains(summary, "→ rDestQ");
    }

    [TestMethod]
    public void TestU_Explain_XChainAccountCreateCommit()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"XChainAccountCreateCommit\",\"Account\":\"rSender\"," +
            BridgeNodeJson + ",\"Destination\":\"rNewQQQQ\"," +
            "\"Amount\":\"5000000\",\"SignatureReward\":\"100\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "XChainAccountCreateCommit by rSender");
        StringAssert.Contains(summary, "create rNewQQ");
        StringAssert.Contains(summary, "5000000 drops XRP");
    }

    [TestMethod]
    public void TestU_Explain_XChainAddClaimAttestation_WasLockingTrue()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"XChainAddClaimAttestation\",\"Account\":\"rWitness\"," +
            BridgeNodeJson + ",\"XChainClaimID\":\"7\",\"Amount\":\"1000000\"," +
            "\"AttestationSignerAccount\":\"rSigner123\",\"WasLockingChainSend\":1}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "XChainAddClaimAttestation by witness rWitness");
        StringAssert.Contains(summary, "claimId=7");
        StringAssert.Contains(summary, "signer=rSigne");
        StringAssert.Contains(summary, "wasLocking=yes");
    }

    [TestMethod]
    public void TestU_Explain_XChainAddAccountCreateAttestation()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"XChainAddAccountCreateAttestation\",\"Account\":\"rWitness\"," +
            BridgeNodeJson + ",\"XChainAccountCreateCount\":\"3\"," +
            "\"Amount\":\"5000000\",\"Destination\":\"rNewQQQQ\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "XChainAddAccountCreateAttestation by witness rWitness");
        StringAssert.Contains(summary, "count=3");
        StringAssert.Contains(summary, "create rNewQQ");
    }

    // --- LoanBroker / Loan (XLS-66) ---

    [TestMethod]
    public void TestU_Explain_LoanBrokerSet_Create()
    {
        string vid = new string('1', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanBrokerSet\",\"Account\":\"rOwner\"," +
            "\"VaultID\":\"" + vid + "\",\"CoverRateMinimum\":5000," +
            "\"CoverRateLiquidation\":3000,\"ManagementFeeRate\":100,\"DebtMaximum\":\"1000000\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "LoanBrokerSet by rOwner");
        StringAssert.Contains(summary, "CREATE");
        StringAssert.Contains(summary, "coverMin=5000");
        StringAssert.Contains(summary, "coverLiq=3000");
        StringAssert.Contains(summary, "feeRate=100");
        StringAssert.Contains(summary, "debtMax=1000000");
    }

    [TestMethod]
    public void TestU_Explain_LoanBrokerSet_Modify()
    {
        string vid = new string('1', 64);
        string bid = new string('2', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanBrokerSet\",\"Account\":\"rOwner\"," +
            "\"VaultID\":\"" + vid + "\",\"LoanBrokerID\":\"" + bid + "\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "MODIFY");
    }

    [TestMethod]
    public void TestU_Explain_LoanBrokerDelete()
    {
        string bid = new string('3', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanBrokerDelete\",\"Account\":\"rOwner\",\"LoanBrokerID\":\"" + bid + "\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "LoanBrokerDelete by rOwner");
    }

    [TestMethod]
    public void TestU_Explain_LoanBrokerCoverDeposit()
    {
        string bid = new string('4', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanBrokerCoverDeposit\",\"Account\":\"rLp\"," +
            "\"LoanBrokerID\":\"" + bid + "\",\"Amount\":\"500000\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "LoanBrokerCoverDeposit by rLp");
        StringAssert.Contains(summary, "500000 drops XRP");
    }

    [TestMethod]
    public void TestU_Explain_LoanBrokerCoverWithdraw_SelfDest()
    {
        string bid = new string('5', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanBrokerCoverWithdraw\",\"Account\":\"rLp\"," +
            "\"LoanBrokerID\":\"" + bid + "\",\"Amount\":\"100000\",\"Destination\":\"rLp\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "→ self");
    }

    [TestMethod]
    public void TestU_Explain_LoanBrokerCoverClawback_BrokerOnly()
    {
        string bid = new string('6', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanBrokerCoverClawback\",\"Account\":\"rIssuer\"," +
            "\"LoanBrokerID\":\"" + bid + "\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "LoanBrokerCoverClawback by issuer rIssuer");
        StringAssert.Contains(summary, "amount=max available");
    }

    [TestMethod]
    public void TestU_Explain_LoanBrokerCoverClawback_AmountOnly()
    {
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanBrokerCoverClawback\",\"Account\":\"rIssuer\"," +
            "\"Amount\":{\"value\":\"50\",\"currency\":\"USD\",\"issuer\":\"rIssuer\"}}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "any-broker");
        StringAssert.Contains(summary, "50 USD");
    }

    [TestMethod]
    public void TestU_Explain_LoanSet_WithOverpayment()
    {
        string bid = new string('7', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanSet\",\"Account\":\"rLender\"," +
            "\"LoanBrokerID\":\"" + bid + "\",\"Counterparty\":\"rBorrower\"," +
            "\"PrincipalRequested\":\"10000\",\"PaymentTotal\":12,\"Flags\":65536}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "LoanSet by lender rLender");
        StringAssert.Contains(summary, "→ borrower rBorro");
        StringAssert.Contains(summary, "principal=10000");
        StringAssert.Contains(summary, "12 payment(s)");
        StringAssert.Contains(summary, "overpayment-allowed");
    }

    [TestMethod]
    public void TestU_Explain_LoanManage_Default()
    {
        string lid = new string('8', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanManage\",\"Account\":\"rBroker\"," +
            "\"LoanID\":\"" + lid + "\",\"Flags\":65536}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "LoanManage by rBroker");
        StringAssert.Contains(summary, "DEFAULT loan");
    }

    [TestMethod]
    public void TestU_Explain_LoanManage_Impair()
    {
        string lid = new string('8', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanManage\",\"Account\":\"rBroker\"," +
            "\"LoanID\":\"" + lid + "\",\"Flags\":131072}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "IMPAIR loan");
    }

    [TestMethod]
    public void TestU_Explain_LoanManage_Unimpair()
    {
        string lid = new string('8', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanManage\",\"Account\":\"rBroker\"," +
            "\"LoanID\":\"" + lid + "\",\"Flags\":262144}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "UNIMPAIR loan");
    }

    [TestMethod]
    public void TestU_Explain_LoanPay_Scheduled()
    {
        string lid = new string('9', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanPay\",\"Account\":\"rBorrower\"," +
            "\"LoanID\":\"" + lid + "\",\"Amount\":\"1000\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "LoanPay by borrower rBorrower");
        StringAssert.Contains(summary, "(scheduled)");
    }

    [TestMethod]
    public void TestU_Explain_LoanPay_FullPayment()
    {
        string lid = new string('9', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanPay\",\"Account\":\"rBorrower\"," +
            "\"LoanID\":\"" + lid + "\",\"Amount\":\"5000\",\"Flags\":131072}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "(full)");
    }

    [TestMethod]
    public void TestU_Explain_LoanPay_Late()
    {
        string lid = new string('9', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanPay\",\"Account\":\"rBorrower\"," +
            "\"LoanID\":\"" + lid + "\",\"Amount\":\"1100\",\"Flags\":262144}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "(late)");
    }

    [TestMethod]
    public void TestU_Explain_LoanDelete()
    {
        string lid = new string('A', 64);
        JsonNode tx = JsonNode.Parse(
            "{\"TransactionType\":\"LoanDelete\",\"Account\":\"rBroker\",\"LoanID\":\"" + lid + "\"}")!;
        string summary = TransactionExplainer.Explain(tx);
        StringAssert.Contains(summary, "LoanDelete by rBroker");
    }
}
