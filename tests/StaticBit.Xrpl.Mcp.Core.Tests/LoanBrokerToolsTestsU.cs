using System;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class LoanBrokerToolsTestsU
{
    private const string GoodId = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";

    [TestMethod]
    public void TestU_ValidateHash256_Good()
    {
        LoanBrokerTools.ValidateHash256(GoodId, "test");
    }

    [TestMethod]
    public void TestU_ValidateHash256_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => LoanBrokerTools.ValidateHash256("", "test"));
        Assert.Throws<ArgumentException>(() => LoanBrokerTools.ValidateHash256("   ", "test"));
    }

    [TestMethod]
    public void TestU_ValidateHash256_WrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => LoanBrokerTools.ValidateHash256(new string('A', 63), "test"));
        Assert.Throws<ArgumentException>(() => LoanBrokerTools.ValidateHash256(new string('A', 65), "test"));
    }

    [TestMethod]
    public void TestU_ValidateHash256_NonHex_Throws()
    {
        Assert.Throws<ArgumentException>(() => LoanBrokerTools.ValidateHash256(new string('Z', 64), "test"));
    }

    [TestMethod]
    public void TestU_ValidateHex_Good()
    {
        LoanBrokerTools.ValidateHex("aF09", "test");
    }

    [TestMethod]
    public void TestU_ValidateHex_NonHex_Throws()
    {
        Assert.Throws<ArgumentException>(() => LoanBrokerTools.ValidateHex("XX", "test"));
    }

    [TestMethod]
    public void TestU_ShortHex_TruncatesLong()
    {
        string s = LoanBrokerTools.ShortHex(GoodId);
        StringAssert.Contains(s, "...");
        Assert.IsTrue(s.Length < GoodId.Length);
    }

    [TestMethod]
    public void TestU_ShortHex_Short_Passthrough()
    {
        Assert.AreEqual("DEAD", LoanBrokerTools.ShortHex("DEAD"));
        Assert.AreEqual("<null>", LoanBrokerTools.ShortHex(null));
    }
}
