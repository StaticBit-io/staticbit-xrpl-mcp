using System;
using System.IO;
using StaticBit.Xrpl.Mcp.Signer.Configuration;
using StaticBit.Xrpl.Mcp.Signer.Keystore;

namespace StaticBit.Xrpl.Mcp.Signer.Tests;

/// <summary>
/// Crypto round-trip and tamper-resistance checks for <see cref="EncryptedFileKeystore"/>.
/// Each test uses an isolated temporary file so they can run in parallel safely.
/// </summary>
[TestClass]
public class KeystoreTestsU
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "xrpl-signer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void TestU_Add_Then_GetSeed_Returns_Same_Value()
    {
        EncryptedFileKeystore sut = CreateSut("correct-horse-battery-staple");
        sut.Add("owner", "sEdRandomSeedForTestingOnly1234567890", "rTestAddr", "ED1234", "ed25519");

        string seed = sut.GetSeed("owner");

        Assert.AreEqual("sEdRandomSeedForTestingOnly1234567890", seed);
    }

    [TestMethod]
    public void TestU_GetSeed_Wrong_Passphrase_Throws()
    {
        // Create with one passphrase, attempt to read with another — must fail.
        EncryptedFileKeystore writer = CreateSut("first-passphrase");
        writer.Add("owner", "sEdSecretValue123", "rAddr", "EDpk", "ed25519");

        EncryptedFileKeystore reader = CreateSut("WRONG-passphrase");
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => reader.GetSeed("owner"));
        StringAssert.Contains(ex.Message, "passphrase");
    }

    [TestMethod]
    public void TestU_Add_Same_Name_Twice_Throws()
    {
        EncryptedFileKeystore sut = CreateSut("pw");
        sut.Add("dup", "sEdSeed1", "rAddr", "EDpk", "ed25519");

        Assert.Throws<InvalidOperationException>(
            () => sut.Add("dup", "sEdSeed2", "rOther", "EDpk2", "ed25519"));
    }

    [TestMethod]
    public void TestU_Remove_Is_Idempotent_For_Missing_Name()
    {
        EncryptedFileKeystore sut = CreateSut("pw");
        Assert.IsFalse(sut.Remove("nonexistent"));
    }

    [TestMethod]
    public void TestU_List_Contains_Added_Wallets_Without_Secrets()
    {
        EncryptedFileKeystore sut = CreateSut("pw");
        sut.Add("a", "sEdOne", "rA", "EDpkA", "ed25519");
        sut.Add("b", "sEdTwo", "rB", "EDpkB", "secp256k1");

        System.Collections.Generic.IReadOnlyList<WalletMetadata> list = sut.List();

        Assert.AreEqual(2, list.Count);
        Assert.IsTrue(list.Any(w => w.Name == "a" && w.Address == "rA" && w.Algorithm == "ed25519"));
        Assert.IsTrue(list.Any(w => w.Name == "b" && w.Address == "rB" && w.Algorithm == "secp256k1"));
    }

    [TestMethod]
    public void TestU_Survives_Process_Restart()
    {
        // Write with one instance, drop, read with another — keystore must persist.
        string path = Path.Combine(_tempDir, "ks.json");
        EncryptedFileKeystore writer = new EncryptedFileKeystore(
            new SignerOptions { KeystorePath = path, Passphrase = "pw" });
        writer.Add("owner", "sEdPersistedSeed", "rPersisted", "EDpk", "ed25519");

        EncryptedFileKeystore reader = new EncryptedFileKeystore(
            new SignerOptions { KeystorePath = path, Passphrase = "pw" });
        Assert.AreEqual("sEdPersistedSeed", reader.GetSeed("owner"));
    }

    [TestMethod]
    public void TestU_Invalid_Wallet_Name_Throws()
    {
        EncryptedFileKeystore sut = CreateSut("pw");
        Assert.Throws<ArgumentException>(
            () => sut.Add("has spaces", "sEdSeed", "rAddr", "EDpk", "ed25519"));
        Assert.Throws<ArgumentException>(
            () => sut.Add("with/slash", "sEdSeed", "rAddr", "EDpk", "ed25519"));
        Assert.Throws<ArgumentException>(
            () => sut.Add("", "sEdSeed", "rAddr", "EDpk", "ed25519"));
    }

    [TestMethod]
    public void TestU_Different_Wallets_Use_Different_Salts()
    {
        // Same passphrase, two wallets → ciphertext for identical seeds must differ
        // (per-record salt/IV ensures isolation).
        EncryptedFileKeystore sut = CreateSut("same-passphrase");
        sut.Add("a", "sEdIdenticalSeed12345", "rA", "EDpkA", "ed25519");
        sut.Add("b", "sEdIdenticalSeed12345", "rB", "EDpkB", "ed25519");

        string json = File.ReadAllText(Path.Combine(_tempDir, "ks.json"));
        // Both ciphertext blobs must be present in the file, and they must NOT be equal
        // (cheap structural check — full bytes inspection would parse JSON).
        Assert.IsTrue(json.Contains("\"a\""));
        Assert.IsTrue(json.Contains("\"b\""));
        Assert.IsTrue(sut.GetSeed("a") == sut.GetSeed("b"));   // same plaintext
        Assert.AreNotEqual(
            ExtractCiphertext(json, "\"a\""),
            ExtractCiphertext(json, "\"b\""),
            "Per-record salts should produce different ciphertexts for the same seed.");
    }

    // ────────────────────────────────────────────────────────────────────────

    private EncryptedFileKeystore CreateSut(string passphrase)
    {
        SignerOptions options = new SignerOptions
        {
            KeystorePath = Path.Combine(_tempDir, "ks.json"),
            Passphrase = passphrase,
        };
        return new EncryptedFileKeystore(options);
    }

    private static string ExtractCiphertext(string json, string nameKey)
    {
        int nameIdx = json.IndexOf(nameKey, StringComparison.Ordinal);
        int ctIdx = json.IndexOf("\"ciphertext\"", nameIdx, StringComparison.Ordinal);
        int colon = json.IndexOf(':', ctIdx);
        int firstQuote = json.IndexOf('"', colon + 1);
        int secondQuote = json.IndexOf('"', firstQuote + 1);
        return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
    }
}
