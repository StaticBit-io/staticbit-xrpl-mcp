using System.Collections.Generic;
using System.Text.Json;
using StaticBit.Xrpl.Mcp.Core.Options;
using StaticBit.Xrpl.Mcp.Core.Services;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

/// <summary>
/// Unit coverage for <see cref="TransactionPreparer.ApplyDefaultSourceTag"/> — the contract that
/// stamps the MCP server's default SourceTag onto prepared transactions while never clobbering a
/// caller-supplied one. Network-free: exercises the pure dictionary mutation directly.
/// </summary>
[TestClass]
public class TransactionPreparerSourceTagTestsU
{
    private const uint McpTag = XrplMcpOptions.StaticBitMcpSourceTag; // 100010011

    [TestMethod]
    public void TestU_AbsentSourceTag_StampsDefault()
    {
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            ["TransactionType"] = "Payment",
            ["Account"] = "rTest",
        };

        TransactionPreparer.ApplyDefaultSourceTag(tx, McpTag);

        Assert.IsTrue(tx.ContainsKey("SourceTag"), "Default SourceTag must be stamped when absent.");
        Assert.AreEqual(McpTag, (uint)tx["SourceTag"]);
    }

    [TestMethod]
    public void TestU_ExistingSourceTag_Preserved()
    {
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            ["SourceTag"] = 99u,
        };

        TransactionPreparer.ApplyDefaultSourceTag(tx, McpTag);

        Assert.AreEqual(99u, (uint)tx["SourceTag"], "A caller-supplied SourceTag must never be overwritten.");
    }

    [TestMethod]
    public void TestU_ExplicitZero_Preserved()
    {
        // The generic/dict prepare path yields numeric JSON as long; an explicit 0 is a deliberate
        // choice and must survive (it is NOT the same as "no tag").
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            ["SourceTag"] = 0L,
        };

        TransactionPreparer.ApplyDefaultSourceTag(tx, McpTag);

        Assert.AreEqual(0L, tx["SourceTag"], "An explicit 0 SourceTag must be preserved.");
    }

    [TestMethod]
    public void TestU_NullJsonElement_TreatedAsAbsent_StampsDefault()
    {
        using JsonDocument doc = JsonDocument.Parse("{\"SourceTag\":null}");
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            ["SourceTag"] = doc.RootElement.GetProperty("SourceTag"),
        };

        TransactionPreparer.ApplyDefaultSourceTag(tx, McpTag);

        Assert.AreEqual(McpTag, (uint)tx["SourceTag"], "A null SourceTag value must be treated as absent.");
    }

    [TestMethod]
    public void TestU_OptionDisabled_NoStamp()
    {
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            ["TransactionType"] = "Payment",
        };

        TransactionPreparer.ApplyDefaultSourceTag(tx, defaultSourceTag: null);

        Assert.IsFalse(tx.ContainsKey("SourceTag"), "A null option must disable stamping entirely.");
    }
}
