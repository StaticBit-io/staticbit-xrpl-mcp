using System;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class ExplainToolsTestsU
{
    private static ExplainTools NewTool() => new ExplainTools();

    [TestMethod]
    public void TestU_Explain_NeitherInput_Throws()
    {
        ExplainTools tool = NewTool();
        Assert.Throws<ArgumentException>(() => tool.Explain(null, null));
    }

    [TestMethod]
    public void TestU_Explain_BothInputs_Throws()
    {
        ExplainTools tool = NewTool();
        Assert.Throws<ArgumentException>(() => tool.Explain(
            txBlobHex: "ABCD",
            txJson: "{\"TransactionType\":\"AccountSet\"}"));
    }

    [TestMethod]
    public void TestU_Explain_OddHex_Throws()
    {
        ExplainTools tool = NewTool();
        Assert.Throws<ArgumentException>(() => tool.Explain(txBlobHex: "ABC"));
    }

    [TestMethod]
    public void TestU_Explain_NonHexChar_Throws()
    {
        ExplainTools tool = NewTool();
        Assert.Throws<ArgumentException>(() => tool.Explain(txBlobHex: "DEADBEXG"));
    }

    [TestMethod]
    public void TestU_Explain_InvalidJson_Throws()
    {
        ExplainTools tool = NewTool();
        Assert.Throws<ArgumentException>(() => tool.Explain(txJson: "not a json"));
    }

    [TestMethod]
    public void TestU_Explain_JsonNotObject_Throws()
    {
        ExplainTools tool = NewTool();
        Assert.Throws<ArgumentException>(() => tool.Explain(txJson: "[1,2,3]"));
        Assert.Throws<ArgumentException>(() => tool.Explain(txJson: "\"string\""));
    }

    [TestMethod]
    public void TestU_Explain_ValidPaymentJson_ReturnsStructuredResponse()
    {
        ExplainTools tool = NewTool();
        string result = tool.Explain(txJson:
            "{\"TransactionType\":\"Payment\",\"Account\":\"rAlice\"," +
            "\"Destination\":\"rBob\",\"Amount\":\"10000000\"}");

        StringAssert.Contains(result, "\"transactionType\":\"Payment\"");
        StringAssert.Contains(result, "\"humanSummary\"");
        StringAssert.Contains(result, "Payment from rAlice to rBob");
    }
}
