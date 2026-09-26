using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using StaticBit.Xrpl.Mcp.Signer.Audit;
using StaticBit.Xrpl.Mcp.Signer.Configuration;
using StaticBit.Xrpl.Mcp.Signer.Keystore;
using StaticBit.Xrpl.Mcp.Signer.Policy;
using StaticBit.Xrpl.Mcp.Signer.Tools;
using Xrpl.Wallet;

namespace StaticBit.Xrpl.Mcp.Signer.Tests;

/// <summary>
/// Action-side invariants for <c>xrpl_sign</c>. The SKILL.md approval rules are prose the model may be
/// talked out of by injected content; these checks must hold in code no matter what the caller sends.
/// </summary>
[TestClass]
public class SignPolicyTestsU
{
    private const string OtherAddress = "rPT1Sjq2YGrBMTttX4GZHjKu9dyfzbpAYe";

    private string _tempDir = string.Empty;
    private RecordingAuditLogger _audit = new RecordingAuditLogger();
    private EncryptedFileKeystore _keystore = null!;
    private SignTools _tools = null!;
    private string _address = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "xrpl-signer-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        XrplWallet wallet = XrplWallet.Generate("ed25519");
        _keystore = new EncryptedFileKeystore(new SignerOptions
        {
            KeystorePath = Path.Combine(_tempDir, "ks.json"),
            Passphrase = "test-passphrase",
        });
        _keystore.Add("main", wallet.Seed!, wallet.ClassicAddress, wallet.PublicKey, "ed25519");

        _address = wallet.ClassicAddress;
        _audit = new RecordingAuditLogger();
        _tools = new SignTools(_keystore, _audit, new SigningPolicy(Array.Empty<string>()));
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
    public void TestU_Sign_PlainPayment_FromOwnAccount_Succeeds()
    {
        string tx = Tx("Payment", new Dictionary<string, object>
        {
            ["Destination"] = OtherAddress,
            ["Amount"] = "1000000",
        });

        _tools.Sign("main", tx);

        Assert.AreEqual(1, _audit.Signed.Count, "Control case: an ordinary payment must still be signed.");
    }

    [TestMethod]
    public void TestU_Sign_AccountNotOwnedByWallet_IsRefused()
    {
        string tx = Tx("Payment", new Dictionary<string, object>
        {
            ["Destination"] = OtherAddress,
            ["Amount"] = "1000000",
        }, account: OtherAddress);

        Assert.ThrowsExactly<ArgumentException>(() => _tools.Sign("main", tx));
        Assert.AreEqual(0, _audit.Signed.Count);
    }

    // Each of these hands control of the account (or all of its funds) to someone else in one signature.
    // Without an explicit, operator-configured opt-in the signer must refuse them outright.
    [TestMethod]
    [DataRow("SetRegularKey")]
    [DataRow("SignerListSet")]
    [DataRow("AccountDelete")]
    [DataRow("AccountSetDisableMaster")]
    public void TestU_Sign_AccountTakeoverTransaction_IsRefusedByDefault(string kind)
    {
        string tx = kind switch
        {
            "SetRegularKey" => Tx("SetRegularKey", new Dictionary<string, object>
            {
                ["RegularKey"] = OtherAddress,
            }),
            "SignerListSet" => Tx("SignerListSet", new Dictionary<string, object>
            {
                ["SignerQuorum"] = 1,
                ["SignerEntries"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["SignerEntry"] = new Dictionary<string, object>
                        {
                            ["Account"] = OtherAddress,
                            ["SignerWeight"] = 1,
                        },
                    },
                },
            }),
            "AccountDelete" => Tx("AccountDelete", new Dictionary<string, object>
            {
                ["Destination"] = OtherAddress,
            }),
            "AccountSetDisableMaster" => Tx("AccountSet", new Dictionary<string, object>
            {
                ["SetFlag"] = 4,
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => _tools.Sign("main", tx));
        Assert.AreEqual(0, _audit.Signed.Count, $"{kind} must not produce a signature.");
    }

    [TestMethod]
    public void TestU_Sign_AccountSetWithHarmlessFlag_Succeeds()
    {
        // asfDefaultRipple (8) is not a takeover flag; only asfDisableMaster (4) is gated.
        string tx = Tx("AccountSet", new Dictionary<string, object>
        {
            ["SetFlag"] = 8,
        });

        _tools.Sign("main", tx);

        Assert.AreEqual(1, _audit.Signed.Count);
    }

    [TestMethod]
    public void TestU_Sign_OperatorAllowedKind_Succeeds()
    {
        SignTools tools = new SignTools(_keystore, _audit, new SigningPolicy(new[] { "setregularkey" }));
        string tx = Tx("SetRegularKey", new Dictionary<string, object>
        {
            ["RegularKey"] = OtherAddress,
        });

        tools.Sign("main", tx);

        CollectionAssert.AreEqual(new[] { "SetRegularKey" }, _audit.Signed);
    }

    [TestMethod]
    public void TestU_Sign_AllowingOneKind_StillRefusesOthers()
    {
        SignTools tools = new SignTools(_keystore, _audit, new SigningPolicy(new[] { "SetRegularKey" }));
        string tx = Tx("AccountDelete", new Dictionary<string, object>
        {
            ["Destination"] = OtherAddress,
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => tools.Sign("main", tx));
        Assert.AreEqual(0, _audit.Signed.Count);
    }

    [TestMethod]
    public void TestU_Sign_BatchWrappingTakeoverTransaction_IsRefused()
    {
        string tx = Tx("Batch", new Dictionary<string, object>
        {
            ["Flags"] = 65536,
            ["RawTransactions"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["RawTransaction"] = new Dictionary<string, object>
                    {
                        ["TransactionType"] = "SetRegularKey",
                        ["Account"] = _address,
                        ["RegularKey"] = OtherAddress,
                        ["Flags"] = 1073741824,
                        ["Fee"] = "0",
                        ["Sequence"] = 2,
                        ["SigningPubKey"] = "",
                    },
                },
            },
        });

        InvalidOperationException ex = Assert.ThrowsExactly<InvalidOperationException>(() => _tools.Sign("main", tx));
        StringAssert.Contains(ex.Message, "SetRegularKey");
        Assert.AreEqual(0, _audit.Signed.Count);
    }

    [TestMethod]
    public void TestU_SignMulti_TakeoverTransaction_IsRefused()
    {
        string tx = Tx("SignerListSet", new Dictionary<string, object>
        {
            ["SignerQuorum"] = 0,
            ["SigningPubKey"] = "",
        }, account: OtherAddress);

        Assert.ThrowsExactly<InvalidOperationException>(() => _tools.SignMulti("main", tx));
        Assert.AreEqual(0, _audit.Signed.Count);
    }

    [TestMethod]
    public void TestU_Sign_Refusal_IsAudited()
    {
        string tx = Tx("SetRegularKey", new Dictionary<string, object>
        {
            ["RegularKey"] = OtherAddress,
        });

        Assert.ThrowsExactly<InvalidOperationException>(() => _tools.Sign("main", tx));

        Assert.AreEqual(1, _audit.Errors.Count);
        StringAssert.Contains(_audit.Errors[0], "SetRegularKey");
    }

    [TestMethod]
    public void TestU_Policy_UnknownKind_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new SigningPolicy(new[] { "SetRegularKy" }));
    }

    [TestMethod]
    public void TestU_Options_AuditLog_DefaultsNextToKeystore()
    {
        string keystorePath = Path.Combine(_tempDir, "ks.json");

        Assert.AreEqual(Path.Combine(_tempDir, "signer-audit.log"), SignerOptions.ResolveAuditLogPath(null, keystorePath));
        Assert.AreEqual(Path.Combine(_tempDir, "signer-audit.log"), SignerOptions.ResolveAuditLogPath("  ", keystorePath));
        Assert.AreEqual(string.Empty, SignerOptions.ResolveAuditLogPath("off", keystorePath));
        Assert.AreEqual(string.Empty, SignerOptions.ResolveAuditLogPath("NONE", keystorePath));
        Assert.AreEqual("/var/log/a.log", SignerOptions.ResolveAuditLogPath("/var/log/a.log", keystorePath));
    }

    [TestMethod]
    public void TestU_Options_AllowList_SplitsOnCommaAndSemicolon()
    {
        CollectionAssert.AreEqual(
            new[] { "SetRegularKey", "AccountDelete", "DisableMaster" },
            SignerOptions.ParseList(" SetRegularKey, AccountDelete ;DisableMaster ,").ToArray());
        Assert.AreEqual(0, SignerOptions.ParseList(null).Count);
    }

    private string Tx(string type, Dictionary<string, object> fields, string? account = null)
    {
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            ["TransactionType"] = type,
            ["Account"] = account ?? _address,
            ["Fee"] = "12",
            ["Sequence"] = 1,
            ["LastLedgerSequence"] = 100,
        };
        foreach (KeyValuePair<string, object> field in fields)
        {
            tx[field.Key] = field.Value;
        }
        return JsonSerializer.Serialize(tx);
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<string?> Signed { get; } = new List<string?>();

        public List<string> Errors { get; } = new List<string>();

        public void LogSign(string wallet, int? index, string signMode, string? txHash, string? txType) =>
            Signed.Add(txType);

        public void LogDecryptFail(string wallet, string reason)
        {
        }

        public void LogSignError(string wallet, string reason) =>
            Errors.Add(reason);
    }
}
