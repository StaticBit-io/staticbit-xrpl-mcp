using System;
using System.Collections.Generic;
using System.IO;
using StaticBit.Xrpl.Mcp.Signer.Configuration;
using StaticBit.Xrpl.Mcp.Signer.Keystore;
using Xrpl.Wallet;

namespace StaticBit.Xrpl.Mcp.Signer.Tests;

/// <summary>
/// End-to-end check that a wallet imported into the keystore produces a signed
/// transaction whose signature passes the SDK's own verification. Purely offline.
/// </summary>
[TestClass]
public class SignTestsU
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "xrpl-signer-sign-" + Guid.NewGuid().ToString("N"));
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
    public void TestU_RoundTrip_GeneratedWallet_SignAndVerify()
    {
        // 1. Generate a wallet via the SDK directly (this is what xrpl_wallet_generate does).
        XrplWallet wallet = XrplWallet.Generate("ed25519");
        Assert.IsNotNull(wallet.Seed);

        // 2. Persist via keystore.
        EncryptedFileKeystore keystore = new EncryptedFileKeystore(new SignerOptions
        {
            KeystorePath = Path.Combine(_tempDir, "ks.json"),
            Passphrase = "test-passphrase",
        });
        keystore.Add("test", wallet.Seed!, wallet.ClassicAddress, wallet.PublicKey, "ed25519");

        // 3. Load seed back and re-derive wallet (this is what xrpl_sign does).
        string restoredSeed = keystore.GetSeed("test");
        XrplWallet restored = XrplWallet.FromSeed(restoredSeed, masterAddress: null, algorithm: "ed25519");

        Assert.AreEqual(wallet.ClassicAddress, restored.ClassicAddress, "Address must survive keystore round-trip.");
        Assert.AreEqual(wallet.PublicKey, restored.PublicKey, "Public key must survive keystore round-trip.");

        // 4. Sign a minimal Payment transaction.
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            ["TransactionType"] = "Payment",
            ["Account"] = wallet.ClassicAddress,
            ["Destination"] = wallet.ClassicAddress,
            ["Amount"] = "1000000",
            ["Fee"] = "12",
            ["Sequence"] = 1u,
            ["LastLedgerSequence"] = 100u,
            ["SigningPubKey"] = wallet.PublicKey,
        };
        SignatureResult signed = restored.Sign(tx);

        Assert.IsFalse(string.IsNullOrEmpty(signed.TxBlob), "Signed blob must not be empty.");
        Assert.IsFalse(string.IsNullOrEmpty(signed.Hash), "Transaction hash must be populated.");

        // 5. Verify the signature with the SDK's verifier.
        Assert.IsTrue(global::Xrpl.Wallet.Signer.VerifySignature(signed.TxBlob),
            "SDK signature verifier must accept the produced blob.");
    }

    [TestMethod]
    public void TestU_RoundTrip_FromSeed_Import()
    {
        // Pre-existing seed flow (xrpl_wallet_import_seed).
        XrplWallet original = XrplWallet.Generate("ed25519");
        string seed = original.Seed!;

        EncryptedFileKeystore keystore = new EncryptedFileKeystore(new SignerOptions
        {
            KeystorePath = Path.Combine(_tempDir, "ks.json"),
            Passphrase = "pw",
        });
        keystore.Add("imported", seed, original.ClassicAddress, original.PublicKey, "ed25519");

        Assert.AreEqual(seed, keystore.GetSeed("imported"));
    }
}
