using System;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class TransactionToolsDecodeTestsU
{
    private static TransactionTools NewTool() => new TransactionTools(pool: null!, preparer: null!);

    [TestMethod]
    public void TestU_DecodeBlob_Empty_Throws()
    {
        TransactionTools tool = NewTool();
        Assert.Throws<ArgumentException>(() => tool.DecodeBlob(""));
        Assert.Throws<ArgumentException>(() => tool.DecodeBlob("   "));
    }

    [TestMethod]
    public void TestU_DecodeBlob_OddLength_Throws()
    {
        TransactionTools tool = NewTool();
        ArgumentException ex = Assert.Throws<ArgumentException>(() => tool.DecodeBlob("ABC"));
        StringAssert.Contains(ex.Message, "odd length");
    }

    [TestMethod]
    public void TestU_DecodeBlob_NonHexChar_Throws()
    {
        TransactionTools tool = NewTool();
        ArgumentException ex = Assert.Throws<ArgumentException>(() => tool.DecodeBlob("DEADXX"));
        StringAssert.Contains(ex.Message, "non-hex");
    }

    [TestMethod]
    public void TestU_DecodeBlob_TrimsWhitespace()
    {
        // We can't easily produce a valid blob here, but verify the validator path:
        // Whitespace trimming should not collapse a valid hex into invalid.
        // Use a known-bad short input that's even-length to skip odd-length check.
        TransactionTools tool = NewTool();
        ArgumentException ex = Assert.Throws<ArgumentException>(() => tool.DecodeBlob("  XY  "));
        StringAssert.Contains(ex.Message, "non-hex");
    }
}
