using System;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Common;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class CurrencyParserTestsU
{
    [TestMethod]
    public void TestU_Parse_XrpDrops_ReturnsXrpCurrency()
    {
        Currency result = CurrencyParser.Parse("10000000");
        Assert.AreEqual("XRP", result.CurrencyCode);
        Assert.AreEqual("10000000", result.Value);
        Assert.IsTrue(string.IsNullOrEmpty(result.Issuer));
    }

    [TestMethod]
    public void TestU_Parse_TokenJson_ReturnsTokenCurrency()
    {
        Currency result = CurrencyParser.Parse(
            "{\"value\":\"100.50\",\"currency\":\"USD\",\"issuer\":\"rIssuerAddressXXXXXXXXXXXXXXXXXX\"}");

        Assert.AreEqual("USD", result.CurrencyCode);
        Assert.AreEqual("100.50", result.Value);
        Assert.AreEqual("rIssuerAddressXXXXXXXXXXXXXXXXXX", result.Issuer);
    }

    [TestMethod]
    public void TestU_Parse_TokenJson_CaseInsensitiveProperties()
    {
        Currency result = CurrencyParser.Parse(
            "{\"Value\":\"1\",\"Currency\":\"EUR\",\"Issuer\":\"rIssuer\"}");

        Assert.AreEqual("EUR", result.CurrencyCode);
        Assert.AreEqual("1", result.Value);
        Assert.AreEqual("rIssuer", result.Issuer);
    }

    [TestMethod]
    public void TestU_Parse_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => CurrencyParser.Parse(""));
    }

    [TestMethod]
    public void TestU_Parse_NonNumericString_Throws()
    {
        Assert.Throws<ArgumentException>(() => CurrencyParser.Parse("not-a-number"));
    }

    [TestMethod]
    public void TestU_Parse_TokenJsonWithoutIssuer_Throws()
    {
        Assert.Throws<ArgumentException>(() => CurrencyParser.Parse(
            "{\"value\":\"1\",\"currency\":\"USD\"}"));
    }

    [TestMethod]
    public void TestU_Parse_TokenJsonWithoutValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => CurrencyParser.Parse(
            "{\"currency\":\"USD\",\"issuer\":\"r...\"}"));
    }
}
