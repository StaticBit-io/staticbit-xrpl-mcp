using System.Collections.Generic;
using StaticBit.Xrpl.Mcp.Core.Services;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

/// <summary>
/// Unit coverage for <see cref="TransactionPreview.Render(System.Collections.Generic.IReadOnlyDictionary{string, object}, string, uint?)"/>
/// — the full-disclosure approval block. Network-free.
/// </summary>
[TestClass]
public class TransactionPreviewTestsU
{
    private const string FullAccount = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
    private const string FullDestination = "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe";

    private static Dictionary<string, object?> XrpPayment() => new Dictionary<string, object?>
    {
        ["TransactionType"] = "Payment",
        ["Account"] = FullAccount,
        ["Destination"] = FullDestination,
        ["Amount"] = "10000000",
        ["Fee"] = "12",
        ["Sequence"] = 42L,
        ["LastLedgerSequence"] = 1020L,
        ["SourceTag"] = 100010011u,
    };

    [TestMethod]
    public void TestU_XrpPayment_RendersFullDisclosure()
    {
        string preview = TransactionPreview.Render(XrpPayment(), "testnet", currentLedgerIndex: 1000);

        StringAssert.Contains(preview, "Network: testnet");
        StringAssert.Contains(preview, "Intent:");
        StringAssert.Contains(preview, "Payment");
        StringAssert.Contains(preview, "From: " + FullAccount, "From must show the FULL, un-truncated sender.");
        StringAssert.Contains(preview, "To: " + FullDestination, "To must show the FULL, un-truncated destination.");
        StringAssert.Contains(preview, "10000000 drops (10 XRP)");
        StringAssert.Contains(preview, "Fee: 12 drops");
        StringAssert.Contains(preview, "Sequence: 42");
        StringAssert.Contains(preview, "SourceTag: 100010011");
        StringAssert.Contains(preview, "LastLedgerSequence: 1020");
        StringAssert.Contains(preview, "expires in ~20 ledgers, ~80s");
    }

    [TestMethod]
    public void TestU_NormalFee_NotFlagged()
    {
        string preview = TransactionPreview.Render(XrpPayment(), "mainnet", 1000);

        Assert.IsFalse(preview.Contains('⚠'), "A 12-drop fee must not be flagged.");
    }

    [TestMethod]
    public void TestU_HighFee_Flagged()
    {
        Dictionary<string, object?> tx = XrpPayment();
        tx["Fee"] = "5000";

        string preview = TransactionPreview.Render(tx, "mainnet", 1000);

        StringAssert.Contains(preview, "⚠");
        StringAssert.Contains(preview, "unusually high");
    }

    [TestMethod]
    public void TestU_NoCurrentLedger_OmitsExpiry()
    {
        string preview = TransactionPreview.Render(XrpPayment(), "testnet", currentLedgerIndex: null);

        StringAssert.Contains(preview, "LastLedgerSequence: 1020");
        Assert.IsFalse(preview.Contains("expires in"), "Without a current ledger the estimate must be omitted.");
    }

    [TestMethod]
    public void TestU_TokenAmount_ShowsFullIssuer()
    {
        Dictionary<string, object?> tx = XrpPayment();
        tx["Amount"] = new Dictionary<string, object?>
        {
            ["value"] = "100.5",
            ["currency"] = "USD",
            ["issuer"] = FullDestination,
        };

        string preview = TransactionPreview.Render(tx, "mainnet", 1000);

        StringAssert.Contains(preview, "100.5 USD (issuer " + FullDestination + ")");
    }

    [TestMethod]
    public void TestU_Memos_DecodedAndMarkedUntrusted()
    {
        Dictionary<string, object?> tx = XrpPayment();
        tx["Memos"] = new List<object?>
        {
            new Dictionary<string, object?>
            {
                ["Memo"] = new Dictionary<string, object?>
                {
                    ["MemoType"] = "6e6f7465",        // "note"
                    ["MemoData"] = "68656c6c6f",      // "hello"
                },
            },
        };

        string preview = TransactionPreview.Render(tx, "mainnet", 1000);

        StringAssert.Contains(preview, "Memos (untrusted");
        StringAssert.Contains(preview, "[note] hello");
    }
}
