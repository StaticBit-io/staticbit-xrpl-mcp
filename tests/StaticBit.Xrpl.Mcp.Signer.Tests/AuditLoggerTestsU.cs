using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using StaticBit.Xrpl.Mcp.Signer.Audit;

namespace StaticBit.Xrpl.Mcp.Signer.Tests;

[TestClass]
public class AuditLoggerTestsU
{
    private string _tempDir = string.Empty;
    private string _logPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "xrpl-audit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _logPath = Path.Combine(_tempDir, "audit.log");
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void TestU_NullAudit_DoesNothing()
    {
        NullAuditLogger sut = new NullAuditLogger();
        sut.LogSign("alice", null, "single", "HASH", "Payment");
        sut.LogDecryptFail("bob", "wrong_pass");
        sut.LogSignError("alice", "tx_malformed");
        // Не падает, ничего не пишет — сам факт того что не выбросило достаточно.
    }

    [TestMethod]
    public void TestU_FileAudit_LogSign_WritesJsonLineWithExpectedFields()
    {
        FileAuditLogger sut = new FileAuditLogger(_logPath, NullLogger<FileAuditLogger>.Instance);
        sut.LogSign("alice", index: 3, signMode: "single",
            txHash: "ABCDEF0123456789", txType: "Payment");

        string content = File.ReadAllText(_logPath);
        StringAssert.Contains(content, "\"event\":\"sign\"");
        StringAssert.Contains(content, "\"wallet\":\"alice\"");
        StringAssert.Contains(content, "\"result\":\"ok\"");
        StringAssert.Contains(content, "\"signMode\":\"single\"");
        StringAssert.Contains(content, "\"index\":3");
        StringAssert.Contains(content, "\"txHash\":\"ABCDEF0123456789\"");
        StringAssert.Contains(content, "\"txType\":\"Payment\"");
        Assert.EndsWith("\n", content, "JSONL entry must terminate with \\n.");
    }

    [TestMethod]
    public void TestU_FileAudit_LogSign_OmitsIndex_ForSeedKind()
    {
        FileAuditLogger sut = new FileAuditLogger(_logPath, NullLogger<FileAuditLogger>.Instance);
        sut.LogSign("legacy", index: null, signMode: "single", txHash: null, txType: null);

        string content = File.ReadAllText(_logPath);
        Assert.DoesNotContain("\"index\"", content,
            "Seed-kind wallets must omit the 'index' field to avoid implying HD semantics.");
        Assert.DoesNotContain("\"txHash\"", content, "Null fields should be omitted.");
        Assert.DoesNotContain("\"txType\"", content, "Null fields should be omitted.");
    }

    [TestMethod]
    public void TestU_FileAudit_LogDecryptFail()
    {
        FileAuditLogger sut = new FileAuditLogger(_logPath, NullLogger<FileAuditLogger>.Instance);
        sut.LogDecryptFail("alice", "InvalidOperationException");

        string content = File.ReadAllText(_logPath);
        StringAssert.Contains(content, "\"event\":\"decrypt_fail\"");
        StringAssert.Contains(content, "\"wallet\":\"alice\"");
        StringAssert.Contains(content, "\"result\":\"decrypt_failed\"");
        StringAssert.Contains(content, "\"reason\":\"InvalidOperationException\"");
    }

    [TestMethod]
    public void TestU_FileAudit_LogSignError_DistinctFromOk()
    {
        FileAuditLogger sut = new FileAuditLogger(_logPath, NullLogger<FileAuditLogger>.Instance);
        sut.LogSignError("alice", "tx_malformed");

        string content = File.ReadAllText(_logPath);
        StringAssert.Contains(content, "\"event\":\"sign\"");
        StringAssert.Contains(content, "\"result\":\"error\"");
        StringAssert.Contains(content, "\"reason\":\"tx_malformed\"");
    }

    [TestMethod]
    public void TestU_FileAudit_MultipleEvents_AppendInOrder()
    {
        FileAuditLogger sut = new FileAuditLogger(_logPath, NullLogger<FileAuditLogger>.Instance);
        sut.LogSign("alice", null, "single", "H1", "Payment");
        sut.LogSign("bob", null, "single", "H2", "TrustSet");
        sut.LogSign("alice", null, "multi", "H3", "OfferCreate");

        string[] lines = File.ReadAllLines(_logPath);
        Assert.HasCount(3, lines);
        StringAssert.Contains(lines[0], "\"txHash\":\"H1\"");
        StringAssert.Contains(lines[1], "\"txHash\":\"H2\"");
        StringAssert.Contains(lines[2], "\"txHash\":\"H3\"");
    }

    [TestMethod]
    public void TestU_FileAudit_NestedDirectory_AutoCreated()
    {
        string nestedPath = Path.Combine(_tempDir, "subdir", "deeper", "audit.log");
        FileAuditLogger sut = new FileAuditLogger(nestedPath, NullLogger<FileAuditLogger>.Instance);
        sut.LogSign("alice", null, "single", null, null);

        Assert.IsTrue(File.Exists(nestedPath));
    }

    [TestMethod]
    public void TestU_FileAudit_Constructor_RejectsEmptyPath()
    {
        Assert.Throws<ArgumentException>(() =>
            new FileAuditLogger("", NullLogger<FileAuditLogger>.Instance));
        Assert.Throws<ArgumentException>(() =>
            new FileAuditLogger("   ", NullLogger<FileAuditLogger>.Instance));
    }
}
