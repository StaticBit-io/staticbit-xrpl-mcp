using System.Collections.Generic;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class AccountObjectsHelperToolsTestsU
{
    [TestMethod]
    public void TestU_ParseAlreadySigned_Null_ReturnsEmpty()
    {
        HashSet<string> result = AccountObjectsHelperTools.ParseAlreadySigned(null);
        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void TestU_ParseAlreadySigned_Empty_ReturnsEmpty()
    {
        Assert.IsEmpty(AccountObjectsHelperTools.ParseAlreadySigned(""));
        Assert.IsEmpty(AccountObjectsHelperTools.ParseAlreadySigned("   "));
    }

    [TestMethod]
    public void TestU_ParseAlreadySigned_SingleAddress()
    {
        HashSet<string> result = AccountObjectsHelperTools.ParseAlreadySigned("rAlice");
        Assert.HasCount(1, result);
        Assert.IsTrue(result.Contains("rAlice"));
    }

    [TestMethod]
    public void TestU_ParseAlreadySigned_MultipleAddresses_TrimsAndDeduplicates()
    {
        HashSet<string> result = AccountObjectsHelperTools.ParseAlreadySigned(
            " rAlice , rBob ,  rCarol , rAlice ");
        Assert.HasCount(3, result);
        Assert.IsTrue(result.Contains("rAlice"));
        Assert.IsTrue(result.Contains("rBob"));
        Assert.IsTrue(result.Contains("rCarol"));
    }

    [TestMethod]
    public void TestU_ParseAlreadySigned_OnlyCommas_ReturnsEmpty()
    {
        Assert.IsEmpty(AccountObjectsHelperTools.ParseAlreadySigned(",, ,,"));
    }
}
