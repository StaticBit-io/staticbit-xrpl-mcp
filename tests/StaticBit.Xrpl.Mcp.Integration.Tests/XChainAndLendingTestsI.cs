using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Integration.Tests;

/// <summary>
/// Prepare-smoke integration tests for XLS-38 XChain bridges and XLS-66 Lending
/// (LoanBroker / Loan). XLS-38 is enabled on sidechain-aware testnets but not on
/// standard rippletest.net — those tests are <c>[Ignore]</c>'d by default. XLS-66
/// is in draft status — also <c>[Ignore]</c>'d. Flip the attributes when running
/// against a node that has the relevant amendment activated.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize] // SDK RequestManager has ID collisions when methods share a WebSocket
public class XChainAndLendingTestsI
{
    private static XrplClientPool? _pool;
    private static TransactionPreparer? _preparer;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        (XrplClientPool pool, TransactionPreparer preparer) = TestnetFixture.BuildPreparer();
        _pool = pool;
        _preparer = preparer;
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (_pool is not null) await _pool.DisposeAsync();
    }

    private static string Funded => TestnetFixture.KnownFundedTestnetAccount;

    private const string BridgeJson =
        "{\"LockingChainDoor\":\"rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe\"," +
        "\"LockingChainIssue\":{\"currency\":\"XRP\"}," +
        "\"IssuingChainDoor\":\"rrrrrrrrrrrrrrrrrrrrrhoLvTp\"," +
        "\"IssuingChainIssue\":{\"currency\":\"XRP\"}}";

    // --- XChain (XLS-38) ---

    [TestMethod]
    [Ignore("XLS-38 XChain is sidechain-only; not enabled on standard rippletest.net.")]
    public async Task TestI_XChainCreateBridge_Prepares()
    {
        XChainTools tool = new XChainTools(_preparer!);
        PreparedTransaction prep = await tool.XChainCreateBridgePrepareAsync(
            network: "testnet", account: Funded,
            bridgeJson: BridgeJson,
            signatureRewardDrops: "100");
        PrepareSmokeAssert.Standard(prep, "XChainCreateBridge", "XChainCreateBridge");
    }

    [TestMethod]
    [Ignore("XLS-38 XChain is sidechain-only; not enabled on standard rippletest.net.")]
    public async Task TestI_XChainModifyBridge_Prepares()
    {
        XChainTools tool = new XChainTools(_preparer!);
        PreparedTransaction prep = await tool.XChainModifyBridgePrepareAsync(
            network: "testnet", account: Funded,
            bridgeJson: BridgeJson,
            signatureRewardDrops: "200");
        PrepareSmokeAssert.Standard(prep, "XChainModifyBridge", "XChainModifyBridge");
    }

    [TestMethod]
    [Ignore("XLS-38 XChain is sidechain-only; not enabled on standard rippletest.net.")]
    public async Task TestI_XChainCreateClaimID_Prepares()
    {
        XChainTools tool = new XChainTools(_preparer!);
        PreparedTransaction prep = await tool.XChainCreateClaimIdPrepareAsync(
            network: "testnet", account: Funded,
            bridgeJson: BridgeJson,
            signatureRewardDrops: "100",
            otherChainSource: "rrrrrrrrrrrrrrrrrrrrrhoLvTp");
        PrepareSmokeAssert.Standard(prep, "XChainCreateClaimID", "XChainCreateClaimID");
    }

    [TestMethod]
    [Ignore("XLS-38 XChain is sidechain-only; not enabled on standard rippletest.net.")]
    public async Task TestI_XChainCommit_Prepares()
    {
        XChainTools tool = new XChainTools(_preparer!);
        PreparedTransaction prep = await tool.XChainCommitPrepareAsync(
            network: "testnet", account: Funded,
            bridgeJson: BridgeJson,
            xchainClaimId: "7", amountValue: "1000000");
        PrepareSmokeAssert.Standard(prep, "XChainCommit", "XChainCommit");
    }

    [TestMethod]
    [Ignore("XLS-38 XChain is sidechain-only; not enabled on standard rippletest.net.")]
    public async Task TestI_XChainClaim_Prepares()
    {
        XChainTools tool = new XChainTools(_preparer!);
        PreparedTransaction prep = await tool.XChainClaimPrepareAsync(
            network: "testnet", account: Funded,
            bridgeJson: BridgeJson,
            xchainClaimId: "7",
            destination: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            amountValue: "1000000");
        PrepareSmokeAssert.Standard(prep, "XChainClaim", "XChainClaim");
    }

    [TestMethod]
    [Ignore("XLS-38 XChain is sidechain-only; not enabled on standard rippletest.net.")]
    public async Task TestI_XChainAccountCreateCommit_Prepares()
    {
        XChainTools tool = new XChainTools(_preparer!);
        PreparedTransaction prep = await tool.XChainAccountCreateCommitPrepareAsync(
            network: "testnet", account: Funded,
            bridgeJson: BridgeJson,
            destination: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            amountDrops: "10000000", signatureRewardDrops: "100");
        PrepareSmokeAssert.Standard(prep, "XChainAccountCreateCommit", "XChainAccountCreateCommit");
    }

    [TestMethod]
    [Ignore("XLS-38 XChain is sidechain-only; not enabled on standard rippletest.net.")]
    public async Task TestI_XChainAddClaimAttestation_Prepares()
    {
        XChainTools tool = new XChainTools(_preparer!);
        PreparedTransaction prep = await tool.XChainAddClaimAttestationPrepareAsync(
            network: "testnet", account: Funded,
            bridgeJson: BridgeJson, xchainClaimId: "7",
            amountValue: "1000000",
            attestationRewardAccount: Funded,
            attestationSignerAccount: Funded,
            otherChainSource: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            publicKeyHex: "ED" + new string('A', 62),
            signatureHex: new string('B', 128),
            wasLockingChainSend: 1,
            destination: "rrrrrrrrrrrrrrrrrrrrrhoLvTp");
        PrepareSmokeAssert.Standard(prep, "XChainAddClaimAttestation", "XChainAddClaimAttestation");
    }

    [TestMethod]
    [Ignore("XLS-38 XChain is sidechain-only; not enabled on standard rippletest.net.")]
    public async Task TestI_XChainAddAccountCreateAttestation_Prepares()
    {
        XChainTools tool = new XChainTools(_preparer!);
        PreparedTransaction prep = await tool.XChainAddAccountCreateAttestationPrepareAsync(
            network: "testnet", account: Funded,
            bridgeJson: BridgeJson, xchainAccountCreateCount: "3",
            amountDrops: "10000000", signatureRewardDrops: "100",
            otherChainSource: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            destination: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            attestationRewardAccount: Funded,
            attestationSignerAccount: Funded,
            publicKeyHex: "ED" + new string('A', 62),
            signatureHex: new string('B', 128),
            wasLockingChainSend: 1);
        PrepareSmokeAssert.Standard(prep, "XChainAddAccountCreateAttestation", "XChainAddAccountCreateAttestation");
    }

    // --- LoanBroker (XLS-66) ---

    [TestMethod]
    [Ignore("XLS-66 LoanBroker amendment is in draft and not active on standard testnet.")]
    public async Task TestI_LoanBrokerSet_Create_Prepares()
    {
        LoanBrokerTools tool = new LoanBrokerTools(_preparer!);
        PreparedTransaction prep = await tool.LoanBrokerSetPrepareAsync(
            network: "testnet", account: Funded,
            vaultId: PrepareSmokeAssert.Hash256("vault-for-broker"),
            coverRateMinimum: 5000, coverRateLiquidation: 3000,
            managementFeeRate: 100);
        PrepareSmokeAssert.Standard(prep, "LoanBrokerSet", "LoanBrokerSet");
    }

    [TestMethod]
    [Ignore("XLS-66 LoanBroker amendment is in draft and not active on standard testnet.")]
    public async Task TestI_LoanBrokerDelete_Prepares()
    {
        LoanBrokerTools tool = new LoanBrokerTools(_preparer!);
        PreparedTransaction prep = await tool.LoanBrokerDeletePrepareAsync(
            network: "testnet", account: Funded,
            loanBrokerId: PrepareSmokeAssert.Hash256("broker-del"));
        PrepareSmokeAssert.Standard(prep, "LoanBrokerDelete", "LoanBrokerDelete");
    }

    [TestMethod]
    [Ignore("XLS-66 LoanBroker amendment is in draft and not active on standard testnet.")]
    public async Task TestI_LoanBrokerCoverDeposit_Prepares()
    {
        LoanBrokerTools tool = new LoanBrokerTools(_preparer!);
        PreparedTransaction prep = await tool.LoanBrokerCoverDepositPrepareAsync(
            network: "testnet", account: Funded,
            loanBrokerId: PrepareSmokeAssert.Hash256("broker-dep"),
            assetCurrency: "XRP", assetIssuer: null, amountValue: "10000");
        PrepareSmokeAssert.Standard(prep, "LoanBrokerCoverDeposit", "LoanBrokerCoverDeposit");
    }

    [TestMethod]
    [Ignore("XLS-66 LoanBroker amendment is in draft and not active on standard testnet.")]
    public async Task TestI_LoanBrokerCoverWithdraw_Prepares()
    {
        LoanBrokerTools tool = new LoanBrokerTools(_preparer!);
        PreparedTransaction prep = await tool.LoanBrokerCoverWithdrawPrepareAsync(
            network: "testnet", account: Funded,
            loanBrokerId: PrepareSmokeAssert.Hash256("broker-wd"),
            assetCurrency: "XRP", assetIssuer: null, amountValue: "5000");
        PrepareSmokeAssert.Standard(prep, "LoanBrokerCoverWithdraw", "LoanBrokerCoverWithdraw");
    }

    [TestMethod]
    [Ignore("XLS-66 LoanBroker amendment is in draft and not active on standard testnet.")]
    public async Task TestI_LoanBrokerCoverClawback_Prepares()
    {
        LoanBrokerTools tool = new LoanBrokerTools(_preparer!);
        PreparedTransaction prep = await tool.LoanBrokerCoverClawbackPrepareAsync(
            network: "testnet", account: Funded,
            loanBrokerId: PrepareSmokeAssert.Hash256("broker-cb"));
        PrepareSmokeAssert.Standard(prep, "LoanBrokerCoverClawback", "LoanBrokerCoverClawback");
    }

    // --- Loan (XLS-66) ---

    [TestMethod]
    [Ignore("XLS-66 Loan amendment is in draft and not active on standard testnet.")]
    public async Task TestI_LoanSet_Prepares()
    {
        LoanTools tool = new LoanTools(_preparer!);
        PreparedTransaction prep = await tool.LoanSetPrepareAsync(
            network: "testnet", account: Funded,
            loanBrokerId: PrepareSmokeAssert.Hash256("loan-broker"),
            counterparty: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            principalRequested: "10000",
            interestRate: 5000, paymentTotal: 12);
        PrepareSmokeAssert.Standard(prep, "LoanSet", "LoanSet");
    }

    [TestMethod]
    [Ignore("XLS-66 Loan amendment is in draft and not active on standard testnet.")]
    public async Task TestI_LoanManage_Default_Prepares()
    {
        LoanTools tool = new LoanTools(_preparer!);
        PreparedTransaction prep = await tool.LoanManagePrepareAsync(
            network: "testnet", account: Funded,
            loanId: PrepareSmokeAssert.Hash256("loan-manage"),
            action: "default");
        PrepareSmokeAssert.Standard(prep, "LoanManage", "DEFAULT");
    }

    [TestMethod]
    [Ignore("XLS-66 Loan amendment is in draft and not active on standard testnet.")]
    public async Task TestI_LoanPay_Scheduled_Prepares()
    {
        LoanTools tool = new LoanTools(_preparer!);
        PreparedTransaction prep = await tool.LoanPayPrepareAsync(
            network: "testnet", account: Funded,
            loanId: PrepareSmokeAssert.Hash256("loan-pay"),
            assetCurrency: "XRP", assetIssuer: null, amountValue: "1000");
        PrepareSmokeAssert.Standard(prep, "LoanPay", "scheduled");
    }

    [TestMethod]
    [Ignore("XLS-66 Loan amendment is in draft and not active on standard testnet.")]
    public async Task TestI_LoanDelete_Prepares()
    {
        LoanTools tool = new LoanTools(_preparer!);
        PreparedTransaction prep = await tool.LoanDeletePrepareAsync(
            network: "testnet", account: Funded,
            loanId: PrepareSmokeAssert.Hash256("loan-del"));
        PrepareSmokeAssert.Standard(prep, "LoanDelete", "LoanDelete");
    }
}
