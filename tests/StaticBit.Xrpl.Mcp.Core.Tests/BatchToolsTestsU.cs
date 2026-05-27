using System;
using System.Collections.Generic;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class BatchToolsTestsU
{
    private const uint TfInnerBatchTxn = 0x40000000u;

    [TestMethod]
    public void TestU_ParseMode_AllVariants()
    {
        Assert.AreEqual(0x00010000u, BatchTools.ParseMode("AllOrNothing"));
        Assert.AreEqual(0x00020000u, BatchTools.ParseMode("OnlyOne"));
        Assert.AreEqual(0x00040000u, BatchTools.ParseMode("UntilFailure"));
        Assert.AreEqual(0x00080000u, BatchTools.ParseMode("Independent"));
    }

    [TestMethod]
    public void TestU_ParseMode_TrimsWhitespace()
    {
        Assert.AreEqual(0x00010000u, BatchTools.ParseMode("  AllOrNothing  "));
    }

    [TestMethod]
    public void TestU_ParseMode_Unknown_Throws()
    {
        Assert.Throws<ArgumentException>(() => BatchTools.ParseMode("Nope"));
        Assert.Throws<ArgumentException>(() => BatchTools.ParseMode(""));
        Assert.Throws<ArgumentException>(() => BatchTools.ParseMode(null!));
    }

    [TestMethod]
    public void TestU_ModeName_RoundTrips()
    {
        foreach (string mode in new[] { "AllOrNothing", "OnlyOne", "UntilFailure", "Independent" })
        {
            Assert.AreEqual(mode, BatchTools.ModeName(BatchTools.ParseMode(mode)));
        }
    }

    [TestMethod]
    public void TestU_NormalizeInnerForBatch_ForcesFields()
    {
        Dictionary<string, object> inner = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TransactionType"] = "Payment",
            ["Account"] = "rAlice",
            ["Destination"] = "rBob",
            ["Amount"] = "1000",
            ["Sequence"] = 7L,
            ["Fee"] = "12",                 // wrong, should be forced to "0"
            ["SigningPubKey"] = "ABCDEF",    // wrong, should be empty
            ["TxnSignature"] = "deadbeef",   // must be removed
            ["Signers"] = new List<object>(), // must be removed
        };

        BatchTools.NormalizeInnerForBatch(inner, 0);

        Assert.AreEqual("0", inner["Fee"]);
        Assert.AreEqual("", inner["SigningPubKey"]);
        Assert.IsFalse(inner.ContainsKey("TxnSignature"));
        Assert.IsFalse(inner.ContainsKey("Signers"));

        uint flags = (uint)inner["Flags"];
        Assert.AreEqual(TfInnerBatchTxn, flags & TfInnerBatchTxn,
            "tfInnerBatchTxn must be present after normalization.");
    }

    [TestMethod]
    public void TestU_NormalizeInnerForBatch_PreservesExistingFlags()
    {
        Dictionary<string, object> inner = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TransactionType"] = "OfferCreate",
            ["Account"] = "rAlice",
            ["Sequence"] = 5L,
            ["Flags"] = 0x00020000u, // tfImmediateOrCancel-ish bit
        };

        BatchTools.NormalizeInnerForBatch(inner, 0);

        uint flags = (uint)inner["Flags"];
        Assert.AreEqual(0x00020000u | TfInnerBatchTxn, flags,
            "existing flags must be OR'd, not overwritten.");
    }

    [TestMethod]
    public void TestU_NormalizeInnerForBatch_MissingType_Throws()
    {
        Dictionary<string, object> inner = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Account"] = "rAlice",
            ["Sequence"] = 1L,
        };
        Assert.Throws<ArgumentException>(() => BatchTools.NormalizeInnerForBatch(inner, 0));
    }

    [TestMethod]
    public void TestU_NormalizeInnerForBatch_NestedBatch_Throws()
    {
        Dictionary<string, object> inner = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TransactionType"] = "Batch",
            ["Account"] = "rAlice",
            ["Sequence"] = 1L,
        };
        Assert.Throws<ArgumentException>(() => BatchTools.NormalizeInnerForBatch(inner, 0));
    }

    [TestMethod]
    public void TestU_NormalizeInnerForBatch_MissingAccount_Throws()
    {
        Dictionary<string, object> inner = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TransactionType"] = "Payment",
            ["Sequence"] = 1L,
        };
        Assert.Throws<ArgumentException>(() => BatchTools.NormalizeInnerForBatch(inner, 0));
    }

    [TestMethod]
    public void TestU_NormalizeInnerForBatch_MissingSequenceAndTicket_Throws()
    {
        Dictionary<string, object> inner = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TransactionType"] = "Payment",
            ["Account"] = "rAlice",
        };
        Assert.Throws<ArgumentException>(() => BatchTools.NormalizeInnerForBatch(inner, 0));
    }

    [TestMethod]
    public void TestU_NormalizeInnerForBatch_TicketSequenceAccepted()
    {
        Dictionary<string, object> inner = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TransactionType"] = "Payment",
            ["Account"] = "rAlice",
            ["TicketSequence"] = 42L,
        };
        BatchTools.NormalizeInnerForBatch(inner, 0); // no throw
    }

    [TestMethod]
    public void TestU_BuildWrappedInners_TooMany_Throws()
    {
        // 9 inner tx — 1 over the XLS-56 limit.
        string innerJson = string.Join(",", System.Linq.Enumerable.Range(0, 9)
            .Select(i => $"{{\"TransactionType\":\"Payment\",\"Account\":\"r{i}\",\"Sequence\":{i + 1}}}"));
        string json = "[" + innerJson + "]";
        Assert.Throws<ArgumentException>(() => BatchTools.BuildWrappedInners(json));
    }

    [TestMethod]
    public void TestU_BuildWrappedInners_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => BatchTools.BuildWrappedInners("[]"));
    }

    [TestMethod]
    public void TestU_BuildWrappedInners_NotArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => BatchTools.BuildWrappedInners("{}"));
    }

    [TestMethod]
    public void TestU_BuildWrappedInners_WrapsCorrectly()
    {
        string json = "[{\"TransactionType\":\"Payment\",\"Account\":\"rAlice\",\"Sequence\":1,\"Amount\":\"1000\",\"Destination\":\"rBob\"}]";
        List<Dictionary<string, object>> wrapped = BatchTools.BuildWrappedInners(json);

        Assert.AreEqual(1, wrapped.Count);
        Assert.IsTrue(wrapped[0].ContainsKey("RawTransaction"));
        Dictionary<string, object> inner = (Dictionary<string, object>)wrapped[0]["RawTransaction"];
        Assert.AreEqual("Payment", inner["TransactionType"]);
        Assert.AreEqual("0", inner["Fee"]);
        Assert.AreEqual("", inner["SigningPubKey"]);
    }

    [TestMethod]
    public void TestU_BuildWrappedBatchSigners_RequiresAccount()
    {
        string json = "[{\"signingPubKey\":\"AB\"}]";
        Assert.Throws<ArgumentException>(() => BatchTools.BuildWrappedBatchSigners(json));
    }

    [TestMethod]
    public void TestU_BuildWrappedBatchSigners_WrapsCorrectly()
    {
        string json = "[{\"account\":\"rAlice\",\"signingPubKey\":\"ABCDEF\",\"txnSignature\":\"DEADBEEF\"}]";
        List<Dictionary<string, object>> wrapped = BatchTools.BuildWrappedBatchSigners(json);

        Assert.AreEqual(1, wrapped.Count);
        Dictionary<string, object> signerWrapper = wrapped[0];
        Assert.IsTrue(signerWrapper.ContainsKey("BatchSigner"));
        Dictionary<string, object> signer = (Dictionary<string, object>)signerWrapper["BatchSigner"];
        Assert.AreEqual("rAlice", signer["Account"]);
        Assert.AreEqual("ABCDEF", signer["SigningPubKey"]);
        Assert.AreEqual("DEADBEEF", signer["TxnSignature"]);
    }
}
