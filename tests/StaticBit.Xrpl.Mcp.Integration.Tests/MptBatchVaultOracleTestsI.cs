using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Integration.Tests;

/// <summary>
/// Prepare-smoke integration tests for MPT (XLS-33), Batch (XLS-56), Vault (XLS-65)
/// and Oracle (XLS-47). Each test calls a <c>*_prepare</c> tool against testnet,
/// verifies that autofill populated Sequence/Fee/LastLedgerSequence and the binary
/// blob was emitted. No signing or submission.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize] // SDK RequestManager has ID collisions when methods share a WebSocket
public class MptBatchVaultOracleTestsI
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

    // --- MPT (XLS-33) ---

    [TestMethod]
    public async Task TestI_MptIssuanceCreate_Prepares()
    {
        MptTools tool = new MptTools(_preparer!);
        PreparedTransaction prep = await tool.MptIssuanceCreatePrepareAsync(
            network: "testnet",
            account: Funded,
            assetScale: 2,
            maximumAmount: "1000000",
            transferFee: 0,
            canTransfer: true);
        PrepareSmokeAssert.Standard(prep, "MPTokenIssuanceCreate", "MPTokenIssuanceCreate");
    }

    [TestMethod]
    public async Task TestI_MptIssuanceDestroy_Prepares()
    {
        MptTools tool = new MptTools(_preparer!);
        string mptId = PrepareSmokeAssert.MptIssuanceId("mpt-destroy");
        PreparedTransaction prep = await tool.MptIssuanceDestroyPrepareAsync(
            network: "testnet", account: Funded, mptokenIssuanceId: mptId);
        PrepareSmokeAssert.Standard(prep, "MPTokenIssuanceDestroy", "MPTokenIssuanceDestroy");
    }

    [TestMethod]
    public async Task TestI_MptIssuanceSet_GlobalLock_Prepares()
    {
        MptTools tool = new MptTools(_preparer!);
        string mptId = PrepareSmokeAssert.MptIssuanceId("mpt-set");
        PreparedTransaction prep = await tool.MptIssuanceSetPrepareAsync(
            network: "testnet", account: Funded,
            mptokenIssuanceId: mptId, lockBalance: true);
        PrepareSmokeAssert.Standard(prep, "MPTokenIssuanceSet", "LOCK");
    }

    [TestMethod]
    public async Task TestI_MptAuthorize_HolderOptIn_Prepares()
    {
        MptTools tool = new MptTools(_preparer!);
        string mptId = PrepareSmokeAssert.MptIssuanceId("mpt-auth");
        PreparedTransaction prep = await tool.MptAuthorizePrepareAsync(
            network: "testnet", account: Funded,
            mptokenIssuanceId: mptId);
        PrepareSmokeAssert.Standard(prep, "MPTokenAuthorize", "OPT-IN");
    }

    // --- Batch (XLS-56) ---

    [TestMethod]
    public async Task TestI_Batch_AllOrNothing_Prepares()
    {
        BatchTools tool = new BatchTools(_preparer!);
        string innerJson = "[" +
            "{\"TransactionType\":\"Payment\"," +
            "\"Account\":\"" + Funded + "\"," +
            "\"Destination\":\"rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe\"," +
            "\"Amount\":\"1000\",\"Sequence\":1}" +
            "]";
        PreparedTransaction prep = await tool.BatchPrepareAsync(
            network: "testnet", account: Funded,
            mode: "AllOrNothing", innerTransactionsJson: innerJson);
        PrepareSmokeAssert.Standard(prep, "Batch", "AllOrNothing");
    }

    // --- Vault (XLS-65) — DRAFT amendment; may not be active on all testnets ---

    [TestMethod]
    [Ignore("XLS-65 Vault amendment is draft and not active on standard testnet.")]
    public async Task TestI_VaultCreate_Prepares()
    {
        VaultTools tool = new VaultTools(_preparer!);
        PreparedTransaction prep = await tool.VaultCreatePrepareAsync(
            network: "testnet", account: Funded,
            assetCurrency: "XRP", assetIssuer: null,
            amountValue: "1000000");
        PrepareSmokeAssert.Standard(prep, "VaultCreate", "VaultCreate");
    }

    [TestMethod]
    [Ignore("XLS-65 Vault amendment is draft and not active on standard testnet.")]
    public async Task TestI_VaultDeposit_Prepares()
    {
        VaultTools tool = new VaultTools(_preparer!);
        PreparedTransaction prep = await tool.VaultDepositPrepareAsync(
            network: "testnet", account: Funded,
            vaultId: PrepareSmokeAssert.Hash256("vault-dep"),
            assetCurrency: "XRP", assetIssuer: null,
            amountValue: "1000");
        PrepareSmokeAssert.Standard(prep, "VaultDeposit", "VaultDeposit");
    }

    [TestMethod]
    [Ignore("XLS-65 Vault amendment is draft and not active on standard testnet.")]
    public async Task TestI_VaultWithdraw_AssetMode_Prepares()
    {
        VaultTools tool = new VaultTools(_preparer!);
        PreparedTransaction prep = await tool.VaultWithdrawPrepareAsync(
            network: "testnet", account: Funded,
            vaultId: PrepareSmokeAssert.Hash256("vault-wd"),
            amountKind: "asset", amountValue: "500",
            assetCurrency: "XRP", assetIssuer: null);
        PrepareSmokeAssert.Standard(prep, "VaultWithdraw", "VaultWithdraw");
    }

    [TestMethod]
    [Ignore("XLS-65 Vault amendment is draft and not active on standard testnet.")]
    public async Task TestI_VaultDelete_Prepares()
    {
        VaultTools tool = new VaultTools(_preparer!);
        PreparedTransaction prep = await tool.VaultDeletePrepareAsync(
            network: "testnet", account: Funded,
            vaultId: PrepareSmokeAssert.Hash256("vault-del"));
        PrepareSmokeAssert.Standard(prep, "VaultDelete", "VaultDelete");
    }

    [TestMethod]
    [Ignore("XLS-65 Vault amendment is draft and not active on standard testnet.")]
    public async Task TestI_VaultSet_Prepares()
    {
        VaultTools tool = new VaultTools(_preparer!);
        PreparedTransaction prep = await tool.VaultSetPrepareAsync(
            network: "testnet", account: Funded,
            vaultId: PrepareSmokeAssert.Hash256("vault-set"),
            assetsMaximum: "5000000");
        PrepareSmokeAssert.Standard(prep, "VaultSet", "VaultSet");
    }

    [TestMethod]
    [Ignore("XLS-65 Vault amendment is draft and not active on standard testnet.")]
    public async Task TestI_VaultClawback_Prepares()
    {
        VaultTools tool = new VaultTools(_preparer!);
        PreparedTransaction prep = await tool.VaultClawbackPrepareAsync(
            network: "testnet", account: Funded,
            vaultId: PrepareSmokeAssert.Hash256("vault-cb"),
            holder: "rrrrrrrrrrrrrrrrrrrrrhoLvTp");
        PrepareSmokeAssert.Standard(prep, "VaultClawback", "VaultClawback");
    }

    // --- Oracle (XLS-47) ---

    [TestMethod]
    public async Task TestI_OracleSet_Prepares()
    {
        OracleTools tool = new OracleTools(_preparer!);
        string priceJson =
            "[{\"baseAsset\":\"XRP\",\"quoteAsset\":\"USD\"," +
            "\"assetPrice\":\"155000\",\"scale\":6}]";
        PreparedTransaction prep = await tool.OracleSetPrepareAsync(
            network: "testnet", account: Funded,
            oracleDocumentId: 1,
            lastUpdateTimeUnix: System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            priceDataSeriesJson: priceJson,
            provider: "TestOracle",
            assetClass: "currency");
        PrepareSmokeAssert.Standard(prep, "OracleSet", "OracleSet");
    }

    [TestMethod]
    public async Task TestI_OracleDelete_Prepares()
    {
        OracleTools tool = new OracleTools(_preparer!);
        PreparedTransaction prep = await tool.OracleDeletePrepareAsync(
            network: "testnet", account: Funded, oracleDocumentId: 999);
        PrepareSmokeAssert.Standard(prep, "OracleDelete", "OracleDelete");
    }
}
