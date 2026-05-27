using System;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class CredentialToolsTestsU
{
    // --- ResolveHexParam ---

    [TestMethod]
    public void TestU_ResolveHexParam_BothProvided_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CredentialTools.ResolveHexParam("AB", "plain", "hex", "plain", 128, true));
    }

    [TestMethod]
    public void TestU_ResolveHexParam_NeitherProvided_RequiredThrows()
    {
        Assert.Throws<ArgumentException>(() =>
            CredentialTools.ResolveHexParam(null, null, "hex", "plain", 128, true));
    }

    [TestMethod]
    public void TestU_ResolveHexParam_NeitherProvided_NotRequired_Null()
    {
        string? result = CredentialTools.ResolveHexParam(null, null, "hex", "plain", 128, false);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TestU_ResolveHexParam_HexUppercased()
    {
        string? result = CredentialTools.ResolveHexParam("ab", null, "hex", "plain", 128, true);
        Assert.AreEqual("AB", result);
    }

    [TestMethod]
    public void TestU_ResolveHexParam_NonHex_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CredentialTools.ResolveHexParam("XZ", null, "hex", "plain", 128, true));
    }

    [TestMethod]
    public void TestU_ResolveHexParam_OddLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CredentialTools.ResolveHexParam("ABC", null, "hex", "plain", 128, true));
    }

    [TestMethod]
    public void TestU_ResolveHexParam_ExceedsMax_Throws()
    {
        string longHex = new string('A', 130); // 130 > 128
        Assert.Throws<ArgumentException>(() =>
            CredentialTools.ResolveHexParam(longHex, null, "hex", "plain", 128, true));
    }

    [TestMethod]
    public void TestU_ResolveHexParam_Plain_Utf8Encoded()
    {
        // "KYC" → 0x4B 0x59 0x43 → "4B5943"
        string? result = CredentialTools.ResolveHexParam(null, "KYC", "hex", "plain", 128, true);
        Assert.AreEqual("4B5943", result);
    }

    [TestMethod]
    public void TestU_ResolveHexParam_PlainExceedsMaxAfterEncoding_Throws()
    {
        // 65 chars → 130 hex chars → exceeds 128 limit
        string plain = new string('A', 65);
        Assert.Throws<ArgumentException>(() =>
            CredentialTools.ResolveHexParam(null, plain, "hex", "plain", 128, true));
    }

    [TestMethod]
    public void TestU_ValidateHex_Accepts_LowerUpper()
    {
        CredentialTools.ValidateHex("aF09", "test");
    }

    [TestMethod]
    public void TestU_ValidateHex_RejectsNonHex()
    {
        Assert.Throws<ArgumentException>(() => CredentialTools.ValidateHex("XX", "test"));
    }

    [TestMethod]
    public void TestU_ShortHex_TruncatesLong()
    {
        string hex = new string('A', 64);
        string s = CredentialTools.ShortHex(hex);
        StringAssert.Contains(s, "...");
        Assert.IsTrue(s.Length < hex.Length);
    }

    [TestMethod]
    public void TestU_ShortHex_ShortPassthrough()
    {
        Assert.AreEqual("DEAD", CredentialTools.ShortHex("DEAD"));
        Assert.AreEqual("<null>", CredentialTools.ShortHex(null));
    }
}
