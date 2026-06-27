using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Wallet;

namespace StaticBit.Xrpl.Mcp.Integration.Tests;

/// <summary>
/// End-to-end coverage for the x402 client mechanism the <c>xrpl-x402-payments</c> skill drives:
/// build the x402 Payment exactly as the skill describes (t54 exact scheme — InvoiceID = SHA-256(invoiceId),
/// a single hex Memo, the enforced SourceTag), prepare it through the (fixed) generic-prepare path,
/// sign it offline, submit it, and confirm it settles on-ledger with every x402 field intact.
///
/// Runs against a local standalone rippled (genesis-funded). Point at it via the <c>XRPL_STANDALONE_WS</c>
/// environment variable; defaults to <c>ws://localhost:6006</c>. The HTTP/merchant/facilitator layer is the
/// already-tested SDK code (Examples/X402.MerchantServer) — this asserts the build+sign+settle half.
/// </summary>
[TestClass]
public class X402PaymentE2ETestsI
{
    private const string Net = "standalone";
    private const string GenesisAddress = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
    private const string GenesisSeed = "snoPBrXtMeMyMHUVTgbuqAfg1SUTb"; // well-known standalone master passphrase
    private const string Destination = "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe";
    private const uint X402SourceTag = 804681468;

    private static XrplClientPool? _pool;
    private static TransactionPreparer? _preparer;
    private static bool _available;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        string ws = Environment.GetEnvironmentVariable("XRPL_STANDALONE_WS") ?? "ws://localhost:6006";
        // Bind a dedicated "standalone" network WITHOUT mutating XRPL_TESTNET_WS — otherwise other
        // integration test classes sharing this process would be silently redirected to this node.
        (XrplClientPool pool, TransactionPreparer preparer) = TestnetFixture.BuildPreparer(Net, ws);
        _pool = pool;
        _preparer = preparer;
        _available = await ProbeStandaloneAsync();
    }

    /// <summary>True when a standalone rippled answers on the RPC port — otherwise these settlement
    /// E2E tests are inconclusive (CI without a standalone node must not fail here).</summary>
    private static async Task<bool> ProbeStandaloneAsync()
    {
        string rpc = Environment.GetEnvironmentVariable("XRPL_STANDALONE_RPC") ?? "http://localhost:5005/";
        try
        {
            using System.Threading.CancellationTokenSource cts = new(TimeSpan.FromSeconds(4));
            using System.Net.Http.HttpContent body = new System.Net.Http.StringContent("{\"method\":\"server_info\"}");
            using System.Net.Http.HttpResponseMessage resp = await _rpc.PostAsync(rpc, body, cts.Token);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void RequireStandalone()
    {
        if (!_available)
        {
            Assert.Inconclusive("No standalone rippled reachable (set XRPL_STANDALONE_WS/RPC) — skipping x402 settlement E2E.");
        }
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (_pool is not null) await _pool.DisposeAsync();
    }

    [TestMethod]
    public async Task TestI_X402XrpPayment_BuildsSignsSettlesOnLedger()
    {
        RequireStandalone();
        const string invoiceId = "inv-e2e-xrp-1";
        string memoHex = Convert.ToHexString(Encoding.UTF8.GetBytes(invoiceId));                    // hexUpper(invoiceId)
        string invoiceIdField = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invoiceId))); // SHA-256 upper

        XrplWallet payer = XrplWallet.FromSeed(GenesisSeed, masterAddress: null, algorithm: null);
        Assert.AreEqual(GenesisAddress, payer.ClassicAddress, "Genesis seed must derive the genesis account (secp256k1).");

        // Build the Payment exactly as the skill's wire-format prescribes.
        string txJson =
            "{" +
            "\"TransactionType\":\"Payment\"," +
            $"\"Account\":\"{payer.ClassicAddress}\"," +
            $"\"Destination\":\"{Destination}\"," +
            "\"Amount\":\"20000000\"," +                       // 20 XRP (>= base reserve, funds the destination)
            $"\"SourceTag\":{X402SourceTag}," +
            $"\"InvoiceID\":\"{invoiceIdField}\"," +
            "\"Memos\":[{\"Memo\":{\"MemoData\":\"" + memoHex + "\"}}]" +
            "}";

        TransactionTools tools = new TransactionTools(_pool!, _preparer!);

        // Build via the FIXED generic path, sign offline, submit + settle (ledger-accept resilient).
        (SubmitResult res, string blob) = await Run(tools, txJson, payer);
        Assert.AreEqual("tesSUCCESS", res.EngineResult, $"engine: {res.EngineResult} — {res.EngineResultMessage}");

        // The x402 binding must have landed on-chain — confirm via the signed blob decode.
        string decoded = tools.DecodeBlob(blob);
        StringAssert.Contains(decoded, memoHex, "Memo (hex invoiceId) must be on the settled tx.");
        StringAssert.Contains(decoded, invoiceIdField, "InvoiceID (SHA-256) must be on the settled tx.");
        StringAssert.Contains(decoded, "804681468", "Enforced x402 SourceTag must be on the settled tx.");
    }

    [TestMethod]
    public async Task TestI_X402RlusdPayment_BuildsSignsSettlesWithSendMax()
    {
        RequireStandalone();
        const string rlusd = "524C555344000000000000000000000000000000"; // "RLUSD" 40-hex
        const string invoiceId = "inv-e2e-rlusd-1";
        string memoHex = Convert.ToHexString(Encoding.UTF8.GetBytes(invoiceId));
        string invoiceIdField = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invoiceId)));

        TransactionTools tools = new TransactionTools(_pool!, _preparer!);
        XrplWallet genesis = XrplWallet.FromSeed(GenesisSeed, masterAddress: null, algorithm: null); // payer/holder
        XrplWallet issuer = XrplWallet.Generate("ed25519");
        XrplWallet merchant = XrplWallet.Generate("ed25519");                                        // destination/holder

        // 1. Genesis funds issuer + merchant with XRP (existence + reserves).
        await Run(tools, Payment(genesis.ClassicAddress, issuer.ClassicAddress, "200000000"), genesis);
        await Run(tools, Payment(genesis.ClassicAddress, merchant.ClassicAddress, "200000000"), genesis);

        // 2. Issuer enables DefaultRipple so issued balances can ripple between holders.
        await Run(tools,
            $"{{\"TransactionType\":\"AccountSet\",\"Account\":\"{issuer.ClassicAddress}\",\"SetFlag\":8}}",
            issuer);

        // 3. Holders trust the issuer for RLUSD (created after DefaultRipple → ripple-enabled).
        await Run(tools, TrustSet(genesis.ClassicAddress, issuer.ClassicAddress, rlusd, "1000000"), genesis);
        await Run(tools, TrustSet(merchant.ClassicAddress, issuer.ClassicAddress, rlusd, "1000000"), merchant);

        // 4. Issuer issues 100 RLUSD to the payer (genesis).
        await Run(tools, IouPayment(issuer.ClassicAddress, genesis.ClassicAddress, "100", rlusd, issuer.ClassicAddress), issuer);

        // 5. The x402 payment: payer → merchant, 2.5 RLUSD with a matching SendMax + binding.
        string amt = $"{{\"value\":\"2.5\",\"currency\":\"{rlusd}\",\"issuer\":\"{issuer.ClassicAddress}\"}}";
        string txJson =
            "{" +
            "\"TransactionType\":\"Payment\"," +
            $"\"Account\":\"{genesis.ClassicAddress}\"," +
            $"\"Destination\":\"{merchant.ClassicAddress}\"," +
            $"\"Amount\":{amt}," +
            $"\"SendMax\":{amt}," +
            $"\"SourceTag\":{X402SourceTag}," +
            $"\"InvoiceID\":\"{invoiceIdField}\"," +
            "\"Memos\":[{\"Memo\":{\"MemoData\":\"" + memoHex + "\"}}]" +
            "}";

        (SubmitResult res, string blob) = await Run(tools, txJson, genesis);
        Assert.AreEqual("tesSUCCESS", res.EngineResult, $"x402 rlusd payment: {res.EngineResult} — {res.EngineResultMessage}");

        string decoded = tools.DecodeBlob(blob);
        StringAssert.Contains(decoded, "SendMax", "IOU payment must carry SendMax.");
        StringAssert.Contains(decoded, memoHex, "Memo (hex invoiceId) must be on the settled tx.");
        StringAssert.Contains(decoded, invoiceIdField, "InvoiceID (SHA-256) must be on the settled tx.");
    }

    private static readonly System.Net.Http.HttpClient _rpc = new System.Net.Http.HttpClient();

    /// <summary>
    /// Forces the standalone node to close a ledger so the just-applied tx validates and the next
    /// autofill reads an advanced account sequence (mirrors the SDK's FundAccount → LedgerAccept).
    /// </summary>
    private static async Task LedgerAccept()
    {
        string rpc = Environment.GetEnvironmentVariable("XRPL_STANDALONE_RPC") ?? "http://localhost:5005/";
        try
        {
            using System.Net.Http.HttpContent body = new System.Net.Http.StringContent("{\"method\":\"ledger_accept\"}");
            await _rpc.PostAsync(rpc, body);
        }
        catch { /* the node also auto-accepts every few seconds; ignore */ }
    }

    private static async Task<(SubmitResult res, string blob)> Run(TransactionTools tools, string txJson, XrplWallet signer)
    {
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            PreparedTransaction prep = await tools.PrepareGenericAsync(Net, txJson, "x402 e2e");
            Dictionary<string, object> tx = prep.TxJson
                .Where(kv => kv.Value is not null)
                .ToDictionary(kv => kv.Key, kv => kv.Value!);
            SignatureResult sig = signer.Sign(tx);
            SubmitResult res = await tools.SubmitSignedAsync(Net, sig.TxBlob, failHard: true, waitForValidation: false);
            await LedgerAccept();
            await Task.Delay(800);

            if (res.EngineResult == "tesSUCCESS") return (res, sig.TxBlob);

            string txType = prep.TxJson.TryGetValue("TransactionType", out object? t) ? t?.ToString() ?? "?" : "?";
            if (attempt < 5 && res.EngineResult is "tefPAST_SEQ" or "terPRE_SEQ" or "telCAN_NOT_QUEUE")
            {
                continue; // stale sequence — re-autofill after the ledger advanced
            }
            Assert.Fail($"{txType}: {res.EngineResult} — {res.EngineResultMessage}");
        }
        Assert.Fail("Run exhausted retries");
        return default; // unreachable
    }

    private static string Payment(string from, string to, string drops) =>
        $"{{\"TransactionType\":\"Payment\",\"Account\":\"{from}\",\"Destination\":\"{to}\",\"Amount\":\"{drops}\"}}";

    private static string IouPayment(string from, string to, string value, string currency, string issuer) =>
        $"{{\"TransactionType\":\"Payment\",\"Account\":\"{from}\",\"Destination\":\"{to}\"," +
        $"\"Amount\":{{\"value\":\"{value}\",\"currency\":\"{currency}\",\"issuer\":\"{issuer}\"}}}}";

    private static string TrustSet(string account, string issuer, string currency, string limit) =>
        $"{{\"TransactionType\":\"TrustSet\",\"Account\":\"{account}\"," +
        $"\"LimitAmount\":{{\"currency\":\"{currency}\",\"issuer\":\"{issuer}\",\"value\":\"{limit}\"}}}}";
}
