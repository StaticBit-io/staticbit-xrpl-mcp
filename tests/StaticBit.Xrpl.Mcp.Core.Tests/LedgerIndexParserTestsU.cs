using System;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class LedgerIndexParserTestsU
{
    [TestMethod]
    public void TestU_Parse_NullOrEmpty_DefaultsToValidated()
    {
        LedgerIndex result1 = LedgerIndexParser.Parse(null);
        LedgerIndex result2 = LedgerIndexParser.Parse("");

        Assert.AreEqual(LedgerIndexType.Validated, result1.LedgerIndexType);
        Assert.AreEqual(LedgerIndexType.Validated, result2.LedgerIndexType);
    }

    [TestMethod]
    [DataRow("validated", LedgerIndexType.Validated)]
    [DataRow("VALIDATED", LedgerIndexType.Validated)]
    [DataRow("current", LedgerIndexType.Current)]
    [DataRow("closed", LedgerIndexType.Closed)]
    public void TestU_Parse_WellKnownNames(string input, LedgerIndexType expected)
    {
        LedgerIndex result = LedgerIndexParser.Parse(input);
        Assert.AreEqual(expected, result.LedgerIndexType);
    }

    [TestMethod]
    public void TestU_Parse_NumericIndex_ReturnsIndex()
    {
        LedgerIndex result = LedgerIndexParser.Parse("12345678");
        Assert.AreEqual(12345678u, result.Index);
    }

    [TestMethod]
    public void TestU_Parse_InvalidString_Throws()
    {
        Assert.Throws<ArgumentException>(() => LedgerIndexParser.Parse("not-a-ledger"));
    }
}
