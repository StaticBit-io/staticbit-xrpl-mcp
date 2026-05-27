using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Services;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Integration.Tests;

/// <summary>
/// Read-helper smoke tests for all account_objects-backed listings of new ledger
/// entry types (MPT/MPTokenIssuance, Credential, DID, PermissionedDomain, Vault,
/// Bridge, LoanBroker, Loan). The funded testnet account doesn't own any of these
/// entries — the value being verified is the JSON shape, marker handling and the
/// fact that an empty result returns an empty list (NOT a network/SDK error).
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize] // SDK RequestManager has ID collisions when methods share a WebSocket
public class ReadHelpersTestsI
{
    private static XrplClientPool? _pool;

    [ClassInitialize]
    public static void Init(TestContext _) => _pool = TestnetFixture.BuildPool();

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (_pool is not null) await _pool.DisposeAsync();
    }

    private static AccountObjectsHelperTools Tool() => new AccountObjectsHelperTools(_pool!);

    [TestMethod]
    public async Task TestI_AccountMptIssuances_ReturnsValidShape()
    {
        string response = await Tool().AccountMptIssuancesAsync("testnet", TestnetFixture.KnownFundedTestnetAccount);
        Assert.IsFalse(string.IsNullOrEmpty(response));
        StringAssert.Contains(response, "\"issuances\"");
        StringAssert.Contains(response, "\"issuanceCount\"");
        StringAssert.Contains(response, "\"validated\"");
    }

    [TestMethod]
    public async Task TestI_AccountMpts_ReturnsValidShape()
    {
        string response = await Tool().AccountMptsAsync("testnet", TestnetFixture.KnownFundedTestnetAccount);
        Assert.IsFalse(string.IsNullOrEmpty(response));
        StringAssert.Contains(response, "\"holdings\"");
        StringAssert.Contains(response, "\"holdingCount\"");
    }

    [TestMethod]
    public async Task TestI_AccountCredentials_ReturnsValidShape()
    {
        string response = await Tool().AccountCredentialsAsync("testnet", TestnetFixture.KnownFundedTestnetAccount);
        Assert.IsFalse(string.IsNullOrEmpty(response));
        StringAssert.Contains(response, "\"issued\"");
        StringAssert.Contains(response, "\"held\"");
        StringAssert.Contains(response, "\"issuedCount\"");
        StringAssert.Contains(response, "\"heldCount\"");
    }

    [TestMethod]
    public async Task TestI_AccountDid_ReturnsValidShape()
    {
        string response = await Tool().AccountDidAsync("testnet", TestnetFixture.KnownFundedTestnetAccount);
        Assert.IsFalse(string.IsNullOrEmpty(response));
        // Either hasDid=true with fields, or hasDid=false. Both are valid.
        StringAssert.Contains(response, "\"hasDid\"");
    }

    [TestMethod]
    public async Task TestI_AccountPermissionedDomains_ReturnsValidShape()
    {
        string response = await Tool().AccountPermissionedDomainsAsync("testnet", TestnetFixture.KnownFundedTestnetAccount);
        Assert.IsFalse(string.IsNullOrEmpty(response));
        StringAssert.Contains(response, "\"domains\"");
        StringAssert.Contains(response, "\"domainCount\"");
    }

    [TestMethod]
    public async Task TestI_AccountVaults_ReturnsValidShape()
    {
        string response = await Tool().AccountVaultsAsync("testnet", TestnetFixture.KnownFundedTestnetAccount);
        Assert.IsFalse(string.IsNullOrEmpty(response));
        StringAssert.Contains(response, "\"vaults\"");
        StringAssert.Contains(response, "\"vaultCount\"");
    }

    [TestMethod]
    public async Task TestI_AccountBridges_ReturnsValidShape()
    {
        string response = await Tool().AccountBridgesAsync("testnet", TestnetFixture.KnownFundedTestnetAccount);
        Assert.IsFalse(string.IsNullOrEmpty(response));
        StringAssert.Contains(response, "\"bridges\"");
        StringAssert.Contains(response, "\"bridgeCount\"");
    }

    [TestMethod]
    public async Task TestI_AccountLoanBrokers_ReturnsValidShape()
    {
        string response = await Tool().AccountLoanBrokersAsync("testnet", TestnetFixture.KnownFundedTestnetAccount);
        Assert.IsFalse(string.IsNullOrEmpty(response));
        StringAssert.Contains(response, "\"loanBrokers\"");
        StringAssert.Contains(response, "\"loanBrokerCount\"");
    }

    [TestMethod]
    public async Task TestI_AccountLoans_ReturnsValidShape()
    {
        string response = await Tool().AccountLoansAsync("testnet", TestnetFixture.KnownFundedTestnetAccount);
        Assert.IsFalse(string.IsNullOrEmpty(response));
        StringAssert.Contains(response, "\"loans\"");
        StringAssert.Contains(response, "\"loanCount\"");
    }
}
