using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Integration.Tests;

/// <summary>
/// Regression coverage for the <c>xrpl_tx_prepare_generic</c> JsonElement→native fix: a Payment with
/// nested <c>Amount</c>(IOU) + <c>SendMax</c> + <c>Memos</c> + an explicit <c>SourceTag</c> must
/// autofill, binary-encode, and survive a decode round-trip with every field intact. Before the fix
/// this path threw <c>InvalidCastException (JsonElement → String)</c> for ANY input. Hits live testnet
/// (Autofill reads the funded account); does NOT sign or submit.
/// </summary>
[TestClass]
public class GenericPrepareRoundTripTestsI
{
    private static XrplClientPool? _pool;
    private static TransactionPreparer? _preparer;

    private const string MemoDataHex = "696E762D6531";  // hex("inv-e1")
    private const string InvoiceIdHex = "A1B2C3D4E5F600112233445566778899AABBCCDDEEFF00112233445566778899";

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

    [TestMethod]
    public async Task TestI_GenericPrepare_IouWithSendMaxAndMemo_RoundTrips()
    {
        string funded = TestnetFixture.KnownFundedTestnetAccount;
        TransactionTools tools = new TransactionTools(_pool!, _preparer!);

        string txJson =
            "{" +
            "\"TransactionType\":\"Payment\"," +
            $"\"Account\":\"{funded}\"," +
            $"\"Destination\":\"{funded}\"," +
            "\"Amount\":{\"value\":\"2.5\",\"currency\":\"USD\",\"issuer\":\"" + funded + "\"}," +
            "\"SendMax\":{\"value\":\"2.5\",\"currency\":\"USD\",\"issuer\":\"" + funded + "\"}," +
            "\"SourceTag\":804681468," +
            $"\"InvoiceID\":\"{InvoiceIdHex}\"," +
            "\"Memos\":[{\"Memo\":{\"MemoData\":\"" + MemoDataHex + "\"}}]" +
            "}";

        PreparedTransaction prep = await tools.PrepareGenericAsync("testnet", txJson, "x402 generic round-trip");

        Assert.IsFalse(string.IsNullOrEmpty(prep.TxBlobUnsigned), "TxBlobUnsigned must not be empty.");
        Assert.IsTrue(prep.LastLedgerSequence > 0, "Autofill must populate LastLedgerSequence.");

        string decoded = tools.DecodeBlob(prep.TxBlobUnsigned);

        StringAssert.Contains(decoded, MemoDataHex, "Memo MemoData must survive into the unsigned blob.");
        StringAssert.Contains(decoded, InvoiceIdHex, "InvoiceID must survive into the unsigned blob.");
        StringAssert.Contains(decoded, "804681468", "Explicit SourceTag must survive into the unsigned blob.");
        StringAssert.Contains(decoded, "SendMax", "SendMax must survive into the unsigned blob.");
    }
}
