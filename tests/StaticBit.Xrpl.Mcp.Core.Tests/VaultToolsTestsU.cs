using System;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Models.Common;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class VaultToolsTestsU
{
    private const string GoodVaultId = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    // --- ValidateVaultId ---

    [TestMethod]
    public void TestU_ValidateVaultId_Good()
    {
        VaultTools.ValidateVaultId(GoodVaultId);
    }

    [TestMethod]
    public void TestU_ValidateVaultId_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => VaultTools.ValidateVaultId(""));
        Assert.Throws<ArgumentException>(() => VaultTools.ValidateVaultId("   "));
    }

    [TestMethod]
    public void TestU_ValidateVaultId_WrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => VaultTools.ValidateVaultId(new string('A', 63)));
        Assert.Throws<ArgumentException>(() => VaultTools.ValidateVaultId(new string('A', 65)));
    }

    [TestMethod]
    public void TestU_ValidateVaultId_NonHex_Throws()
    {
        Assert.Throws<ArgumentException>(() => VaultTools.ValidateVaultId(new string('Z', 64)));
    }

    // --- BuildInitialAmount ---

    [TestMethod]
    public void TestU_BuildInitialAmount_Xrp_AcceptsDrops()
    {
        Currency c = VaultTools.BuildInitialAmount("XRP", null, "1000000", "rDefault");
        Assert.IsNotNull(c);
    }

    [TestMethod]
    public void TestU_BuildInitialAmount_Xrp_NonInteger_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            VaultTools.BuildInitialAmount("XRP", null, "1.5", "rDefault"));
    }

    [TestMethod]
    public void TestU_BuildInitialAmount_Iou_UsesIssuer()
    {
        Currency c = VaultTools.BuildInitialAmount("USD", "rIssuerXXXX", "100.50", "rOther");
        Assert.IsNotNull(c);
        // We can't introspect the SDK internal shape easily — confirm it parsed without throw.
    }

    [TestMethod]
    public void TestU_BuildInitialAmount_IouWithoutIssuer_FallsBackToDefault()
    {
        Currency c = VaultTools.BuildInitialAmount("USD", null, "100", "rDefaultIssuer");
        Assert.IsNotNull(c);
    }

    // --- ValidateHex ---

    [TestMethod]
    public void TestU_ValidateHex_GoodMixedCase()
    {
        VaultTools.ValidateHex("aF09", "test");
    }

    [TestMethod]
    public void TestU_ValidateHex_NonHex_Throws()
    {
        Assert.Throws<ArgumentException>(() => VaultTools.ValidateHex("XX", "test"));
    }

    [TestMethod]
    public void TestU_ShortHex_TruncatesLong()
    {
        string hex = new string('A', 64);
        string s = VaultTools.ShortHex(hex);
        StringAssert.Contains(s, "...");
    }

    [TestMethod]
    public void TestU_ShortHex_ShortPassthrough()
    {
        Assert.AreEqual("DEAD", VaultTools.ShortHex("DEAD"));
        Assert.AreEqual("<null>", VaultTools.ShortHex(null));
    }
}
