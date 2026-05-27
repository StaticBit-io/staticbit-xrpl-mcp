using System;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class MptToolsTestsU
{
    private const string ValidIssuanceId = "00000539B0A8A3A7A4D7C7E8F9A0B1C2D3E4F5A6B7C8D9E0";

    [TestMethod]
    public void TestU_ComposeIssuanceCreateFlags_NoInput_ReturnsNull()
    {
        MPTokenIssuanceCreateFlags? result = MptTools.ComposeIssuanceCreateFlags(
            bitmask: null, canLock: null, requireAuth: null, canEscrow: null,
            canTrade: null, canTransfer: null, canClawback: null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TestU_ComposeIssuanceCreateFlags_AllBooleans_OrsBits()
    {
        MPTokenIssuanceCreateFlags? result = MptTools.ComposeIssuanceCreateFlags(
            bitmask: null, canLock: true, requireAuth: true, canEscrow: true,
            canTrade: true, canTransfer: true, canClawback: true);
        Assert.IsNotNull(result);
        Assert.AreEqual((uint)(2 | 4 | 8 | 16 | 32 | 64), (uint)result.Value);
    }

    [TestMethod]
    public void TestU_ComposeIssuanceCreateFlags_OnlyTransfer()
    {
        MPTokenIssuanceCreateFlags? result = MptTools.ComposeIssuanceCreateFlags(
            bitmask: null, canLock: null, requireAuth: null, canEscrow: null,
            canTrade: null, canTransfer: true, canClawback: null);
        Assert.AreEqual(MPTokenIssuanceCreateFlags.tfMPTCanTransfer, result);
    }

    [TestMethod]
    public void TestU_ComposeIssuanceCreateFlags_BitmaskAndBoolean_Throws()
    {
        Assert.Throws<ArgumentException>(() => MptTools.ComposeIssuanceCreateFlags(
            bitmask: 32u, canLock: null, requireAuth: null, canEscrow: null,
            canTrade: null, canTransfer: true, canClawback: null));
    }

    [TestMethod]
    public void TestU_ComposeIssuanceCreateFlags_UnknownBitmaskBits_Throws()
    {
        Assert.Throws<ArgumentException>(() => MptTools.ComposeIssuanceCreateFlags(
            bitmask: 0x100u, canLock: null, requireAuth: null, canEscrow: null,
            canTrade: null, canTransfer: null, canClawback: null));
    }

    [TestMethod]
    public void TestU_ComposeIssuanceCreateFlags_ZeroBitmaskTreatedAsNoInput()
    {
        // Bitmask=0 with no booleans -> null (no flags). Matches "user explicitly cleared".
        MPTokenIssuanceCreateFlags? result = MptTools.ComposeIssuanceCreateFlags(
            bitmask: 0u, canLock: null, requireAuth: null, canEscrow: null,
            canTrade: null, canTransfer: null, canClawback: null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TestU_ValidateMptIssuanceId_GoodFormat()
    {
        // 48-char uppercase hex passes without exception
        MptTools.ValidateMptIssuanceId(ValidIssuanceId);
    }

    [TestMethod]
    public void TestU_ValidateMptIssuanceId_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => MptTools.ValidateMptIssuanceId(""));
        Assert.Throws<ArgumentException>(() => MptTools.ValidateMptIssuanceId("   "));
    }

    [TestMethod]
    public void TestU_ValidateMptIssuanceId_WrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => MptTools.ValidateMptIssuanceId("AB"));
        Assert.Throws<ArgumentException>(() => MptTools.ValidateMptIssuanceId(new string('A', 47)));
        Assert.Throws<ArgumentException>(() => MptTools.ValidateMptIssuanceId(new string('A', 49)));
    }

    [TestMethod]
    public void TestU_ValidateMptIssuanceId_NonHexChar_Throws()
    {
        string bad = new string('A', 47) + "Z";
        Assert.Throws<ArgumentException>(() => MptTools.ValidateMptIssuanceId(bad));
    }

    [TestMethod]
    public void TestU_ValidateHex_Accepts_LowerUpper()
    {
        MptTools.ValidateHex("aF09", "test");
    }

    [TestMethod]
    public void TestU_ValidateHex_RejectsNonHex()
    {
        Assert.Throws<ArgumentException>(() => MptTools.ValidateHex("XX", "test"));
    }

    [TestMethod]
    public void TestU_ShortMptId_TruncatesLongId()
    {
        string id = new string('A', 48);
        string shortId = MptTools.ShortMptId(id);
        StringAssert.Contains(shortId, "...");
        Assert.IsTrue(shortId.Length < id.Length);
    }

    [TestMethod]
    public void TestU_ShortMptId_ShortIdReturnedAsIs()
    {
        Assert.AreEqual("DEAD", MptTools.ShortMptId("DEAD"));
    }
}
