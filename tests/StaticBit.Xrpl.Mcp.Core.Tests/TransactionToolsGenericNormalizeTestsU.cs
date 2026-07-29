using System.Collections.Generic;
using System.Text.Json;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

/// <summary>
/// Unit coverage for <see cref="TransactionTools.NormalizeJson"/> — the fix that converts a
/// System.Text.Json element tree into native CLR types before the generic prepare path hands the
/// dictionary to the (Newtonsoft-based) SDK serializer. Without it, every <c>xrpl_tx_prepare_generic</c>
/// call throws <c>InvalidCastException (JsonElement → String)</c>. Network-free.
/// </summary>
[TestClass]
public class TransactionToolsGenericNormalizeTestsU
{
    [TestMethod]
    public void TestU_NormalizesNestedObjectsArraysAndScalars()
    {
        using JsonDocument doc = JsonDocument.Parse(
            "{\"TransactionType\":\"Payment\",\"SourceTag\":804681468," +
            "\"Amount\":{\"value\":\"2.5\",\"currency\":\"USD\",\"issuer\":\"rIss\"}," +
            "\"Memos\":[{\"Memo\":{\"MemoData\":\"ABCD\"}}],\"Nullable\":null}");

        object normalized = TransactionTools.NormalizeJson(doc.RootElement)!;

        Dictionary<string, object> top = (Dictionary<string, object>)normalized;
        Assert.AreEqual("Payment", top["TransactionType"]);
        Assert.AreEqual(804681468L, top["SourceTag"]);                       // numbers → long
        Assert.IsInstanceOfType(top["Amount"], typeof(Dictionary<string, object>));
        Assert.AreEqual("rIss", ((Dictionary<string, object>)top["Amount"])["issuer"]);
        Assert.IsInstanceOfType(top["Memos"], typeof(List<object>));         // arrays → List
        Assert.IsFalse(top.ContainsKey("Nullable"), "null fields must be dropped");
    }

    [TestMethod]
    public void TestU_NestedMemoArraySurvivesAsDictionaries()
    {
        using JsonDocument doc = JsonDocument.Parse(
            "{\"TransactionType\":\"Payment\",\"Memos\":[{\"Memo\":{\"MemoData\":\"696E76\"}}]}");

        Dictionary<string, object> top = (Dictionary<string, object>)TransactionTools.NormalizeJson(doc.RootElement)!;

        List<object> memos = (List<object>)top["Memos"];
        Dictionary<string, object> wrapper = (Dictionary<string, object>)memos[0];
        Dictionary<string, object> memo = (Dictionary<string, object>)wrapper["Memo"];
        Assert.AreEqual("696E76", memo["MemoData"]);
    }

    [TestMethod]
    public void TestU_BooleanAndFractionalNumbersPreserved()
    {
        using JsonDocument doc = JsonDocument.Parse(
            "{\"Flag\":true,\"Quality\":1.5}");

        Dictionary<string, object> top = (Dictionary<string, object>)TransactionTools.NormalizeJson(doc.RootElement)!;

        Assert.AreEqual(true, top["Flag"]);
        Assert.AreEqual(1.5d, top["Quality"]);
    }
}
