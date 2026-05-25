using System;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Common;
using static Xrpl.Models.Common.Common;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class ToolDisplayTestsU
{
    [TestMethod]
    public void TestU_Truncate_Null_ReturnsPlaceholder()
    {
        Assert.AreEqual("<null>", ToolDisplay.Truncate(null));
        Assert.AreEqual("<null>", ToolDisplay.Truncate(string.Empty));
    }

    [TestMethod]
    public void TestU_Truncate_ShortString_UnchangedWhen12OrFewer()
    {
        Assert.AreEqual("rShort", ToolDisplay.Truncate("rShort"));
        Assert.AreEqual("123456789012", ToolDisplay.Truncate("123456789012"));
    }

    [TestMethod]
    public void TestU_Truncate_LongString_HeadAndTail()
    {
        string result = ToolDisplay.Truncate("rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH");
        Assert.AreEqual("rN7n7o...fzRH", result);
    }

    [TestMethod]
    public void TestU_DescribeAmount_Xrp_ShowsDrops()
    {
        Currency amount = new Currency { CurrencyCode = "XRP", Value = "10000000" };
        Assert.AreEqual("10000000 drops XRP", ToolDisplay.DescribeAmount(amount));
    }

    [TestMethod]
    public void TestU_DescribeAmount_Token_ShowsCurrencyAndIssuer()
    {
        Currency amount = new Currency
        {
            CurrencyCode = "USD",
            Value = "100.50",
            Issuer = "rIssuerAddressXXXXXXXXXXXXXXXXXX",
        };
        string formatted = ToolDisplay.DescribeAmount(amount);
        StringAssert.Contains(formatted, "100.50 USD");
        StringAssert.Contains(formatted, "rIssue"); // truncated head
    }

    [TestMethod]
    public void TestU_DescribeAmount_Null_ReturnsPlaceholder()
    {
        Assert.AreEqual("<null>", ToolDisplay.DescribeAmount(null!));
    }

    [TestMethod]
    public void TestU_BuildAsset_Xrp_IgnoresIssuer()
    {
        IssuedCurrency asset = ToolDisplay.BuildAsset("XRP", null);
        Assert.AreEqual("XRP", asset.Currency);
        Assert.IsNull(asset.Issuer);
    }

    [TestMethod]
    public void TestU_BuildAsset_Xrp_CaseInsensitive()
    {
        IssuedCurrency asset = ToolDisplay.BuildAsset("xrp", "anything");
        Assert.AreEqual("XRP", asset.Currency); // not normalized — pass-through; but issuer should be null
        Assert.IsNull(asset.Issuer);
    }

    [TestMethod]
    public void TestU_BuildAsset_Token_RequiresIssuer()
    {
        Assert.Throws<ArgumentException>(() => ToolDisplay.BuildAsset("USD", null));
    }

    [TestMethod]
    public void TestU_BuildAsset_Token_WithIssuer()
    {
        IssuedCurrency asset = ToolDisplay.BuildAsset("USD", "rIssuer");
        Assert.AreEqual("USD", asset.Currency);
        Assert.AreEqual("rIssuer", asset.Issuer);
    }

    [TestMethod]
    public void TestU_BuildAsset_EmptyCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => ToolDisplay.BuildAsset(string.Empty, "rIssuer"));
        Assert.Throws<ArgumentException>(() => ToolDisplay.BuildAsset("   ", "rIssuer"));
    }

    [TestMethod]
    public void TestU_DescribeAsset_Xrp()
    {
        IssuedCurrency asset = ToolDisplay.BuildAsset("XRP", null);
        Assert.AreEqual("XRP", ToolDisplay.DescribeAsset(asset));
    }

    [TestMethod]
    public void TestU_DescribeAsset_Token()
    {
        IssuedCurrency asset = ToolDisplay.BuildAsset("USD", "rIssuerAddressXXXXXXXXXXXXXXXXXX");
        string formatted = ToolDisplay.DescribeAsset(asset);
        StringAssert.Contains(formatted, "USD");
        StringAssert.Contains(formatted, "rIssue");
    }
}
