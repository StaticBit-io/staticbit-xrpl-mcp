using System;
using System.IO;
using StaticBit.Xrpl.Mcp.Signer.Configuration;
using StaticBit.Xrpl.Mcp.Signer.Keystore;

namespace StaticBit.Xrpl.Mcp.Signer.Tests;

/// <summary>
/// Coverage for the HD (mnemonic-kind) round-trip in <see cref="EncryptedFileKeystore"/>.
/// Old (seed-kind) round-trip lives in <see cref="KeystoreTestsU"/>; this file focuses
/// on the new fields (Kind, mnemonic ciphertext, separately-encrypted BIP-39 passphrase,
/// derivation path template).
/// </summary>
[TestClass]
public class HdKeystoreTestsU
{
    private const string SampleMnemonic =
        "draw attack antique swing base employ blur above palace lucky glide clap";

    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "xrpl-signer-hd-tests-" + Guid.NewGuid().ToString("N"));
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
    public void TestU_AddMnemonic_Then_GetMnemonic_Roundtrip()
    {
        EncryptedFileKeystore sut = CreateSut("hd-pass-1");
        sut.AddMnemonic(
            "alice", SampleMnemonic,
            previewAddress: "rPreview", previewPublicKey: "EDpreview",
            algorithm: "secp256k1",
            bip39Passphrase: null,
            derivationPathTemplate: "m/44'/144'/{i}'/0/0");

        Assert.AreEqual(SampleMnemonic, sut.GetMnemonic("alice"));
        Assert.IsNull(sut.GetBip39Passphrase("alice"));
    }

    [TestMethod]
    public void TestU_AddMnemonic_WithBip39Passphrase_Roundtrip()
    {
        EncryptedFileKeystore sut = CreateSut("hd-pass-2");
        sut.AddMnemonic(
            "bob", SampleMnemonic,
            previewAddress: "rPreview", previewPublicKey: "EDpreview",
            algorithm: "secp256k1",
            bip39Passphrase: "extra-protection-words",
            derivationPathTemplate: "m/44'/144'/{i}'/0/0");

        Assert.AreEqual(SampleMnemonic, sut.GetMnemonic("bob"));
        Assert.AreEqual("extra-protection-words", sut.GetBip39Passphrase("bob"));
    }

    [TestMethod]
    public void TestU_GetSeed_OnMnemonicKind_Throws()
    {
        EncryptedFileKeystore sut = CreateSut("hd-pass-3");
        sut.AddMnemonic("alice", SampleMnemonic, "rA", "ED", "secp256k1", null, "m/44'/144'/{i}'/0/0");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.GetSeed("alice"));
        StringAssert.Contains(ex.Message, "not 'seed'");
    }

    [TestMethod]
    public void TestU_GetMnemonic_OnSeedKind_Throws()
    {
        EncryptedFileKeystore sut = CreateSut("hd-pass-4");
        sut.Add("legacy", "sEdSomeSeed12345", "rL", "EDpk", "ed25519");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => sut.GetMnemonic("legacy"));
        StringAssert.Contains(ex.Message, "not 'mnemonic'");
    }

    [TestMethod]
    public void TestU_AddMnemonic_RequiresPlaceholderInTemplate()
    {
        EncryptedFileKeystore sut = CreateSut("hd-pass-5");
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            sut.AddMnemonic("alice", SampleMnemonic, "rA", "ED", "secp256k1", null, "m/44'/144'/0'/0/0"));
        StringAssert.Contains(ex.Message, "{i}");
    }

    [TestMethod]
    public void TestU_GetMnemonic_WrongPassphrase_Throws()
    {
        EncryptedFileKeystore writer = CreateSut("first");
        writer.AddMnemonic("alice", SampleMnemonic, "rA", "ED", "secp256k1", null, "m/44'/144'/{i}'/0/0");

        EncryptedFileKeystore reader = CreateSut("second");
        Assert.Throws<InvalidOperationException>(() => reader.GetMnemonic("alice"));
    }

    [TestMethod]
    public void TestU_AddMnemonic_Sets_KindToMnemonic_InMetadata()
    {
        EncryptedFileKeystore sut = CreateSut("hd-pass-6");
        sut.AddMnemonic("alice", SampleMnemonic, "rA", "ED", "secp256k1", null, "m/44'/144'/{i}'/0/0");

        WalletMetadata? meta = sut.GetMetadata("alice");
        Assert.IsNotNull(meta);
        Assert.AreEqual("mnemonic", meta!.Kind);
        Assert.AreEqual("m/44'/144'/{i}'/0/0", meta.DerivationPathTemplate);
    }

    [TestMethod]
    public void TestU_Add_Sets_KindToSeed_InMetadata()
    {
        EncryptedFileKeystore sut = CreateSut("hd-pass-7");
        sut.Add("legacy", "sEdAbc", "rL", "EDpk", "ed25519");

        WalletMetadata? meta = sut.GetMetadata("legacy");
        Assert.IsNotNull(meta);
        Assert.AreEqual("seed", meta!.Kind);
        Assert.IsNull(meta.DerivationPathTemplate);
    }

    [TestMethod]
    public void TestU_AddMnemonic_BumpsFileVersion_To2()
    {
        EncryptedFileKeystore sut = CreateSut("hd-pass-8");
        // Start with a seed-kind entry → file version stays 1 (it's only bumped on HD writes).
        sut.Add("legacy", "sEdAbc", "rL", "EDpk", "ed25519");
        string json1 = File.ReadAllText(Path.Combine(_tempDir, "ks.json"));
        StringAssert.Contains(json1, "\"version\": 1");

        sut.AddMnemonic("alice", SampleMnemonic, "rA", "ED", "secp256k1", null, "m/44'/144'/{i}'/0/0");
        string json2 = File.ReadAllText(Path.Combine(_tempDir, "ks.json"));
        StringAssert.Contains(json2, "\"version\": 2");
    }

    [TestMethod]
    public void TestU_AddMnemonic_Twice_SameName_Throws()
    {
        EncryptedFileKeystore sut = CreateSut("hd-pass-9");
        sut.AddMnemonic("alice", SampleMnemonic, "rA", "ED", "secp256k1", null, "m/44'/144'/{i}'/0/0");
        Assert.Throws<InvalidOperationException>(() =>
            sut.AddMnemonic("alice", SampleMnemonic, "rB", "ED", "secp256k1", null, "m/44'/144'/{i}'/0/0"));
    }

    private EncryptedFileKeystore CreateSut(string passphrase)
    {
        SignerOptions options = new SignerOptions
        {
            KeystorePath = Path.Combine(_tempDir, "ks.json"),
            Passphrase = passphrase,
        };
        return new EncryptedFileKeystore(options);
    }
}
