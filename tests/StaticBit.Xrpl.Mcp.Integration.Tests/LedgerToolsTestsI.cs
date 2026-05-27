using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Integration.Tests;

/// <summary>
/// Read-only smoke tests against a live XRPL testnet. Not part of the per-PR
/// signal — run via <c>dotnet test --filter "TestCategory=Integration"</c>.
/// Set <c>XRPL_TESTNET_WS</c> to override the default rippletest.net endpoint.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize] // SDK RequestManager has ID collisions when methods share a WebSocket
public class LedgerToolsTestsI
{
    private static XrplClientPool? _pool;

    [ClassInitialize]
    public static void Init(TestContext _) => _pool = TestnetFixture.BuildPool();

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (_pool is not null) await _pool.DisposeAsync();
    }

    [TestMethod]
    public async Task TestI_ServerInfo_ReturnsNonEmptyVersion()
    {
        LedgerTools tool = new LedgerTools(_pool!);
        string response = await tool.ServerInfoAsync("testnet");

        Assert.IsFalse(string.IsNullOrEmpty(response), "ServerInfo response should not be empty.");
        // Loose check — rippled responses contain a version field. We don't pin the
        // exact shape because the SDK normalizes it slightly differently.
        StringAssert.Contains(response, "build_version", "Expected server_info to include build_version.");
    }

    [TestMethod]
    public async Task TestI_ServerState_ReturnsValidatedLedgerInfo()
    {
        LedgerTools tool = new LedgerTools(_pool!);
        string response = await tool.ServerStateAsync("testnet");
        StringAssert.Contains(response, "validated_ledger");
    }

    [TestMethod]
    public async Task TestI_Fee_ReturnsOpenLedgerFee()
    {
        LedgerTools tool = new LedgerTools(_pool!);
        string response = await tool.FeeAsync("testnet");
        // Either snake_case or PascalCase depending on SDK serialization — accept both.
        bool hasFeeField = response.Contains("open_ledger_fee") || response.Contains("OpenLedgerFee");
        Assert.IsTrue(hasFeeField, $"Expected fee response to include open-ledger fee. Got: {response[..System.Math.Min(200, response.Length)]}");
    }

    [TestMethod]
    public async Task TestI_AccountInfo_KnownFundedAccount_HasBalance()
    {
        AccountTools tool = new AccountTools(_pool!);
        string response = await tool.AccountInfoAsync("testnet", TestnetFixture.KnownFundedTestnetAccount);

        Assert.IsFalse(string.IsNullOrEmpty(response));
        // account_data → balance present on any funded account. SDK serialization
        // varies between camelCase ("balance") and PascalCase ("Balance") across
        // versions — accept either.
        bool hasBalance = response.Contains("\"balance\"") || response.Contains("\"Balance\"");
        Assert.IsTrue(hasBalance, $"Expected account_info response to include a balance field. Got: {response[..System.Math.Min(200, response.Length)]}");
    }

    [TestMethod]
    public async Task TestI_Ledger_Validated_HasHash()
    {
        LedgerTools tool = new LedgerTools(_pool!);
        string response = await tool.LedgerAsync("testnet", ledgerIndex: "validated");
        StringAssert.Contains(response, "ledger_hash");
    }
}
