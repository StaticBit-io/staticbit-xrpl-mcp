using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Models.Methods;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class PathToolsTestsU
{
    private static PathTools NewTool() => new PathTools(pool: null!);

    // --- ParseSourceCurrencies (internal helper) ---

    [TestMethod]
    public void TestU_ParseSourceCurrencies_Null_ReturnsNull()
    {
        Assert.IsNull(PathTools.ParseSourceCurrencies(null));
        Assert.IsNull(PathTools.ParseSourceCurrencies("   "));
    }

    [TestMethod]
    public void TestU_ParseSourceCurrencies_NotArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => PathTools.ParseSourceCurrencies("{\"currency\":\"USD\"}"));
        Assert.Throws<ArgumentException>(() => PathTools.ParseSourceCurrencies("\"USD\""));
    }

    [TestMethod]
    public void TestU_ParseSourceCurrencies_NonObjectEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() => PathTools.ParseSourceCurrencies("[\"USD\"]"));
    }

    [TestMethod]
    public void TestU_ParseSourceCurrencies_MissingCurrency_Throws()
    {
        Assert.Throws<ArgumentException>(() => PathTools.ParseSourceCurrencies("[{\"issuer\":\"rIss\"}]"));
    }

    [TestMethod]
    public void TestU_ParseSourceCurrencies_EmptyArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => PathTools.ParseSourceCurrencies("[]"));
    }

    [TestMethod]
    public void TestU_ParseSourceCurrencies_TooMany_Throws()
    {
        string entries = "[" + string.Join(",", Enumerable.Range(0, 19)
            .Select(i => $"{{\"currency\":\"C{i:D2}\"}}")) + "]";
        Assert.Throws<ArgumentException>(() => PathTools.ParseSourceCurrencies(entries));
    }

    [TestMethod]
    public void TestU_ParseSourceCurrencies_Valid_NoIssuer()
    {
        List<SourceCurrency>? result = PathTools.ParseSourceCurrencies("[{\"currency\":\"XRP\"},{\"currency\":\"EUR\",\"issuer\":\"rIss\"}]");
        Assert.IsNotNull(result);
        Assert.HasCount(2, result);
        Assert.AreEqual("XRP", result[0].Currency);
        Assert.IsNull(result[0].Issuer);
        Assert.AreEqual("EUR", result[1].Currency);
        Assert.AreEqual("rIss", result[1].Issuer);
    }

    // --- RipplePathFind: sendMax + sourceCurrenciesJson mutual exclusion ---

    [TestMethod]
    public async Task TestU_RipplePathFind_SendMaxAndSourceCurrencies_Throws()
    {
        PathTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.RipplePathFindAsync(
            "testnet", sourceAccount: "rA", destinationAccount: "rB",
            destinationAmount: "1000",
            sendMax: "5000",
            sourceCurrenciesJson: "[{\"currency\":\"XRP\"}]"));
    }
}
