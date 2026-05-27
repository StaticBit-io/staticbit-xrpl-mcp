using System;
using System.Collections.Generic;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class OracleToolsTestsU
{
    [TestMethod]
    public void TestU_ParsePriceDataSeries_NotArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.ParsePriceDataSeries("{}"));
    }

    [TestMethod]
    public void TestU_ParsePriceDataSeries_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.ParsePriceDataSeries("[]"));
    }

    [TestMethod]
    public void TestU_ParsePriceDataSeries_NullOrBlank_Throws()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.ParsePriceDataSeries(""));
        Assert.Throws<ArgumentException>(() => OracleTools.ParsePriceDataSeries("   "));
    }

    [TestMethod]
    public void TestU_ParsePriceDataSeries_TooMany_Throws()
    {
        string entries = "[" + string.Join(",", System.Linq.Enumerable.Range(0, 11)
            .Select(_ => "{\"baseAsset\":\"XRP\",\"quoteAsset\":\"USD\"}")) + "]";
        Assert.Throws<ArgumentException>(() => OracleTools.ParsePriceDataSeries(entries));
    }

    [TestMethod]
    public void TestU_ParsePriceDataSeries_MissingBaseAsset_Throws()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.ParsePriceDataSeries(
            "[{\"quoteAsset\":\"USD\"}]"));
    }

    [TestMethod]
    public void TestU_ParsePriceDataSeries_PriceWithoutScale_Throws()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.ParsePriceDataSeries(
            "[{\"baseAsset\":\"XRP\",\"quoteAsset\":\"USD\",\"assetPrice\":\"1000\"}]"));
    }

    [TestMethod]
    public void TestU_ParsePriceDataSeries_ScaleWithoutPrice_Throws()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.ParsePriceDataSeries(
            "[{\"baseAsset\":\"XRP\",\"quoteAsset\":\"USD\",\"scale\":3}]"));
    }

    [TestMethod]
    public void TestU_ParsePriceDataSeries_ScaleOutOfRange_Throws()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.ParsePriceDataSeries(
            "[{\"baseAsset\":\"XRP\",\"quoteAsset\":\"USD\",\"assetPrice\":\"1000\",\"scale\":11}]"));
    }

    [TestMethod]
    public void TestU_ParsePriceDataSeries_ValidPriceAndScale_Builds()
    {
        List<Dictionary<string, object>> series = OracleTools.ParsePriceDataSeries(
            "[{\"baseAsset\":\"XRP\",\"quoteAsset\":\"USD\",\"assetPrice\":\"155000\",\"scale\":6}]");
        Assert.AreEqual(1, series.Count);
        Dictionary<string, object> wrapper = series[0];
        Assert.IsTrue(wrapper.ContainsKey("PriceData"));
        Dictionary<string, object> data = (Dictionary<string, object>)wrapper["PriceData"];
        Assert.AreEqual("XRP", data["BaseAsset"]);
        Assert.AreEqual("USD", data["QuoteAsset"]);
        Assert.AreEqual("155000", data["AssetPrice"]);
        Assert.AreEqual(6u, data["Scale"]);
    }

    [TestMethod]
    public void TestU_ParsePriceDataSeries_PriceNotUint64_Throws()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.ParsePriceDataSeries(
            "[{\"baseAsset\":\"XRP\",\"quoteAsset\":\"USD\",\"assetPrice\":\"abc\",\"scale\":2}]"));
    }

    [TestMethod]
    public void TestU_NormalizeCurrency_UpperCasesHex()
    {
        string hex = new string('a', 40);
        string normalized = OracleTools.NormalizeCurrency(hex, "test", 0);
        Assert.AreEqual(new string('A', 40), normalized);
    }

    [TestMethod]
    public void TestU_NormalizeCurrency_AcceptsThreeChar()
    {
        Assert.AreEqual("USD", OracleTools.NormalizeCurrency("USD", "test", 0));
    }

    [TestMethod]
    public void TestU_NormalizeCurrency_RejectsWrongLength()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.NormalizeCurrency("FOOBAR", "test", 0));
    }

    [TestMethod]
    public void TestU_NormalizeCurrency_Reject40CharNonHex()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.NormalizeCurrency(new string('Z', 40), "test", 0));
    }

    [TestMethod]
    public void TestU_AsciiToHex_AsciiOnly_Works()
    {
        // "Hello" → 48656C6C6F
        Assert.AreEqual("48656C6C6F", OracleTools.AsciiToHex("Hello", "test"));
    }

    [TestMethod]
    public void TestU_AsciiToHex_NonAscii_Throws()
    {
        Assert.Throws<ArgumentException>(() => OracleTools.AsciiToHex("ПривеТ", "test"));
        // Tab is below 0x20, also rejected
        Assert.Throws<ArgumentException>(() => OracleTools.AsciiToHex("a\tb", "test"));
    }
}
