using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Integration.Tests;

/// <summary>
/// Prepare-smoke integration tests for the quick-wins (TicketCreate, NFTokenModify,
/// Oracle handled separately, DelegateSet) and the compliance bundle (Credentials,
/// PermissionedDomains, DID, AMMClawback). All target amendments are activated on
/// the standard testnet.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize] // SDK RequestManager has ID collisions when methods share a WebSocket
public class QuickWinsAndComplianceTestsI
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

    // --- TicketCreate ---

    [TestMethod]
    public async Task TestI_TicketCreate_Prepares()
    {
        AccountManagementTools tool = new AccountManagementTools(_preparer!);
        PreparedTransaction prep = await tool.TicketCreatePrepareAsync(
            network: "testnet", account: Funded, ticketCount: 1);
        PrepareSmokeAssert.Standard(prep, "TicketCreate", "TicketCreate");
    }

    // --- DelegateSet (XLS-75) ---

    [TestMethod]
    public async Task TestI_DelegateSet_Prepares()
    {
        AccountManagementTools tool = new AccountManagementTools(_preparer!);
        PreparedTransaction prep = await tool.DelegateSetPrepareAsync(
            network: "testnet", account: Funded,
            delegateAccount: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            permissionsCsv: "Payment,TrustSet");
        PrepareSmokeAssert.Standard(prep, "DelegateSet", "DelegateSet");
    }

    [TestMethod]
    public async Task TestI_DelegateSet_Clear_Prepares()
    {
        AccountManagementTools tool = new AccountManagementTools(_preparer!);
        PreparedTransaction prep = await tool.DelegateSetPrepareAsync(
            network: "testnet", account: Funded,
            delegateAccount: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            permissionsCsv: "");
        PrepareSmokeAssert.Standard(prep, "DelegateSet", "CLEAR");
    }

    // --- NFTokenModify (XLS-46) ---

    [TestMethod]
    public async Task TestI_NFTokenModify_Prepares()
    {
        NftTools tool = new NftTools(_pool!, _preparer!);
        PreparedTransaction prep = await tool.NftModifyPrepareAsync(
            network: "testnet", account: Funded,
            nfTokenId: new string('A', 64),
            uriHex: "DEADBEEF");
        PrepareSmokeAssert.Standard(prep, "NFTokenModify", "NFTokenModify");
    }

    [TestMethod]
    public async Task TestI_NFTokenModify_ClearUri_Prepares()
    {
        NftTools tool = new NftTools(_pool!, _preparer!);
        PreparedTransaction prep = await tool.NftModifyPrepareAsync(
            network: "testnet", account: Funded,
            nfTokenId: new string('B', 64),
            clearUri: true);
        PrepareSmokeAssert.Standard(prep, "NFTokenModify", "CLEAR URI");
    }

    // --- Credentials (XLS-70) ---

    [TestMethod]
    public async Task TestI_CredentialCreate_Prepares()
    {
        CredentialTools tool = new CredentialTools(_preparer!);
        PreparedTransaction prep = await tool.CredentialCreatePrepareAsync(
            network: "testnet", account: Funded,
            subject: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            credentialTypePlain: "KYC-Tier-1");
        PrepareSmokeAssert.Standard(prep, "CredentialCreate", "CredentialCreate");
    }

    [TestMethod]
    public async Task TestI_CredentialAccept_Prepares()
    {
        CredentialTools tool = new CredentialTools(_preparer!);
        PreparedTransaction prep = await tool.CredentialAcceptPrepareAsync(
            network: "testnet", account: Funded,
            issuer: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            credentialTypePlain: "KYC-Tier-1");
        PrepareSmokeAssert.Standard(prep, "CredentialAccept", "CredentialAccept");
    }

    [TestMethod]
    public async Task TestI_CredentialDelete_SubjectUnaccept_Prepares()
    {
        CredentialTools tool = new CredentialTools(_preparer!);
        PreparedTransaction prep = await tool.CredentialDeletePrepareAsync(
            network: "testnet", account: Funded,
            credentialTypePlain: "KYC-Tier-1",
            issuer: "rrrrrrrrrrrrrrrrrrrrrhoLvTp");
        PrepareSmokeAssert.Standard(prep, "CredentialDelete", "un-accept");
    }

    // --- PermissionedDomains (XLS-80) ---

    [TestMethod]
    public async Task TestI_PermissionedDomainSet_Create_Prepares()
    {
        PermissionedDomainTools tool = new PermissionedDomainTools(_preparer!);
        string credsJson = "[{\"issuer\":\"rrrrrrrrrrrrrrrrrrrrrhoLvTp\"," +
            "\"credentialType\":\"4B594331\"}]"; // "KYC1"
        PreparedTransaction prep = await tool.PermissionedDomainSetPrepareAsync(
            network: "testnet", account: Funded,
            acceptedCredentialsJson: credsJson);
        PrepareSmokeAssert.Standard(prep, "PermissionedDomainSet", "CREATE");
    }

    [TestMethod]
    public async Task TestI_PermissionedDomainDelete_Prepares()
    {
        PermissionedDomainTools tool = new PermissionedDomainTools(_preparer!);
        PreparedTransaction prep = await tool.PermissionedDomainDeletePrepareAsync(
            network: "testnet", account: Funded,
            domainId: PrepareSmokeAssert.Hash256("pdom-del"));
        PrepareSmokeAssert.Standard(prep, "PermissionedDomainDelete", "PermissionedDomainDelete");
    }

    // --- DID (XLS-40) ---

    [TestMethod]
    public async Task TestI_DidSet_Prepares()
    {
        DidTools tool = new DidTools(_preparer!);
        PreparedTransaction prep = await tool.DidSetPrepareAsync(
            network: "testnet", account: Funded,
            uriPlain: "did:example:alice");
        PrepareSmokeAssert.Standard(prep, "DIDSet", "DIDSet");
    }

    [TestMethod]
    public async Task TestI_DidDelete_Prepares()
    {
        DidTools tool = new DidTools(_preparer!);
        PreparedTransaction prep = await tool.DidDeletePrepareAsync(
            network: "testnet", account: Funded);
        PrepareSmokeAssert.Standard(prep, "DIDDelete", "DIDDelete");
    }

    // --- AMMClawback (XLS-37) ---

    [TestMethod]
    public async Task TestI_AmmClawback_Prepares()
    {
        AmmManagementTools tool = new AmmManagementTools(_preparer!);
        PreparedTransaction prep = await tool.AmmClawbackPrepareAsync(
            network: "testnet", account: Funded,
            holder: "rrrrrrrrrrrrrrrrrrrrrhoLvTp",
            asset1Currency: "USD", asset1Issuer: Funded,
            asset2Currency: "XRP", asset2Issuer: null);
        PrepareSmokeAssert.Standard(prep, "AMMClawback", "AMMClawback");
    }
}
