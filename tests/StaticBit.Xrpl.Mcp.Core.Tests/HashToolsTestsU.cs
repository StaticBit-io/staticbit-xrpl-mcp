using System;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class HashToolsTestsU
{
    // --- ResolveCredentialType ---

    [TestMethod]
    public void TestU_ResolveCredentialType_BothProvided_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            HashTools.ResolveCredentialType("AB", "plain"));
    }

    [TestMethod]
    public void TestU_ResolveCredentialType_NeitherProvided_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            HashTools.ResolveCredentialType(null, null));
        Assert.Throws<ArgumentException>(() =>
            HashTools.ResolveCredentialType("", ""));
    }

    [TestMethod]
    public void TestU_ResolveCredentialType_HexUppercased()
    {
        Assert.AreEqual("ABCD", HashTools.ResolveCredentialType("abcd", null));
    }

    [TestMethod]
    public void TestU_ResolveCredentialType_HexOddLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            HashTools.ResolveCredentialType("ABC", null));
    }

    [TestMethod]
    public void TestU_ResolveCredentialType_NonHex_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            HashTools.ResolveCredentialType("XZ", null));
    }

    [TestMethod]
    public void TestU_ResolveCredentialType_PlainEncoded()
    {
        // "KYC" → 0x4B 0x59 0x43 → "4B5943"
        Assert.AreEqual("4B5943", HashTools.ResolveCredentialType(null, "KYC"));
    }

    [TestMethod]
    public void TestU_ResolveCredentialType_TooLong_Throws()
    {
        string longHex = new string('A', 130); // > 128 hex chars
        Assert.Throws<ArgumentException>(() =>
            HashTools.ResolveCredentialType(longHex, null));
    }

    [TestMethod]
    public void TestU_ResolveCredentialType_PlainEncoded_TooLong_Throws()
    {
        // 65 raw chars × 2 hex = 130 — exceeds 128 limit.
        string plain = new string('A', 65);
        Assert.Throws<ArgumentException>(() =>
            HashTools.ResolveCredentialType(null, plain));
    }

    // --- HashCredential — round-trip with SDK ---

    [TestMethod]
    public void TestU_HashCredential_Deterministic_Format()
    {
        HashTools tool = new HashTools();
        string hash = tool.HashCredential(
            subject: "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe",
            issuer:  "rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH",
            credentialTypePlain: "KYC-Tier1");

        Assert.IsNotNull(hash);
        Assert.AreEqual(64, hash.Length, "Hash256 must be 64-char hex.");
        // Uppercase hex
        foreach (char c in hash)
        {
            bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
            Assert.IsTrue(ok, $"Non-uppercase-hex char '{c}'.");
        }
    }

    [TestMethod]
    public void TestU_HashCredential_HexVsPlain_EquivalentInput()
    {
        // Plain "KYC" → hex "4B5943". Both should produce identical hash.
        HashTools tool = new HashTools();
        string subj = "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe";
        string iss = "rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH";

        string h1 = tool.HashCredential(subj, iss, credentialTypePlain: "KYC");
        string h2 = tool.HashCredential(subj, iss, credentialTypeHex: "4B5943");

        Assert.AreEqual(h1, h2);
    }

    [TestMethod]
    public void TestU_HashCredential_DifferentSubjects_DifferentHashes()
    {
        HashTools tool = new HashTools();
        string iss = "rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH";
        string h1 = tool.HashCredential("rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe", iss,
            credentialTypePlain: "KYC");
        string h2 = tool.HashCredential("rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW", iss,
            credentialTypePlain: "KYC");

        Assert.AreNotEqual(h1, h2);
    }

    [TestMethod]
    public void TestU_HashCredential_DifferentTypes_DifferentHashes()
    {
        HashTools tool = new HashTools();
        string subj = "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe";
        string iss = "rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH";

        string h1 = tool.HashCredential(subj, iss, credentialTypePlain: "KYC-Tier1");
        string h2 = tool.HashCredential(subj, iss, credentialTypePlain: "KYC-Tier2");

        Assert.AreNotEqual(h1, h2);
    }

    [TestMethod]
    public void TestU_HashCredential_MissingSubject_Throws()
    {
        HashTools tool = new HashTools();
        Assert.Throws<ArgumentException>(() =>
            tool.HashCredential("", "rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH",
                credentialTypePlain: "KYC"));
    }

    [TestMethod]
    public void TestU_HashCredential_MissingIssuer_Throws()
    {
        HashTools tool = new HashTools();
        Assert.Throws<ArgumentException>(() =>
            tool.HashCredential("rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe", "",
                credentialTypePlain: "KYC"));
    }

    [TestMethod]
    public void TestU_HashCredential_InvalidSubjectChecksum_ThrowsArgument()
    {
        // 'rfBKzgT2VK4eUJTRYBpzPJcdqnnxAGn2VK' looks like a classic address but
        // fails base58check. Must surface as a clean ArgumentException, not a
        // SDK-internal EncodingFormatException leaking through the MCP layer.
        HashTools tool = new HashTools();
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            tool.HashCredential(
                "rfBKzgT2VK4eUJTRYBpzPJcdqnnxAGn2VK",
                "rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH",
                credentialTypePlain: "KYC"));
        StringAssert.Contains(ex.Message, "subject", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(ex.Message, "base58check", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void TestU_HashCredential_InvalidIssuerChecksum_ThrowsArgument()
    {
        HashTools tool = new HashTools();
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            tool.HashCredential(
                "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe",
                "rfBKzgT2VK4eUJTRYBpzPJcdqnnxAGn2VK",
                credentialTypePlain: "KYC"));
        StringAssert.Contains(ex.Message, "issuer", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(ex.Message, "base58check", StringComparison.OrdinalIgnoreCase);
    }
}
