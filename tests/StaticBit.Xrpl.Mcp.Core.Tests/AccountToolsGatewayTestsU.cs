using System;
using System.Collections.Generic;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class AccountToolsGatewayTestsU
{
    // --- ParseHotwallets (internal helper) ---

    [TestMethod]
    public void TestU_ParseHotwallets_Null_ReturnsNull()
    {
        Assert.IsNull(AccountTools.ParseHotwallets(null));
        Assert.IsNull(AccountTools.ParseHotwallets("   "));
    }

    [TestMethod]
    public void TestU_ParseHotwallets_NotArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => AccountTools.ParseHotwallets("\"rA\""));
        Assert.Throws<ArgumentException>(() => AccountTools.ParseHotwallets("{\"a\":\"rA\"}"));
    }

    [TestMethod]
    public void TestU_ParseHotwallets_NonStringEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() => AccountTools.ParseHotwallets("[\"rA\", 42]"));
    }

    [TestMethod]
    public void TestU_ParseHotwallets_EmptyAddress_Throws()
    {
        Assert.Throws<ArgumentException>(() => AccountTools.ParseHotwallets("[\"rA\", \"\"]"));
    }

    [TestMethod]
    public void TestU_ParseHotwallets_EmptyArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => AccountTools.ParseHotwallets("[]"));
    }

    [TestMethod]
    public void TestU_ParseHotwallets_Valid()
    {
        object? result = AccountTools.ParseHotwallets("[\"rAlice\",\"rBob\"]");
        Assert.IsNotNull(result);
        List<string> list = (List<string>)result;
        Assert.HasCount(2, list);
        Assert.AreEqual("rAlice", list[0]);
        Assert.AreEqual("rBob", list[1]);
    }
}
