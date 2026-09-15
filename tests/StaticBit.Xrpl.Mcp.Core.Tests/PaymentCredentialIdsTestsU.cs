using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class PaymentCredentialIdsTestsU
{
    private const string GoodHash = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";

    // The preparer is null on purpose: reaching transaction preparation would throw
    // NullReferenceException, so an ArgumentException proves the guard rejected the input
    // before any Payment was built or autofilled.
    private static PaymentTools NewTool() => new PaymentTools(preparer: null!);

    [TestMethod]
    public async Task TestU_PaymentPrepare_MalformedInvoiceId_ThrowsNamingTheParameter()
    {
        PaymentTools tool = NewTool();

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() => tool.PaymentPrepareAsync(
            "testnet", "rA", "rB", amount: "1000", invoiceId: "not-a-hash"));

        Assert.AreEqual("invoiceId", ex.ParamName);
    }

    [TestMethod]
    public async Task TestU_PaymentPrepare_ShortInvoiceId_ThrowsNamingTheParameter()
    {
        PaymentTools tool = NewTool();

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() => tool.PaymentPrepareAsync(
            "testnet", "rA", "rB", amount: "1000", invoiceId: GoodHash.Substring(1)));

        Assert.AreEqual("invoiceId", ex.ParamName);
    }

    [TestMethod]
    public async Task TestU_PaymentPrepare_WellFormedInvoiceId_PassesTheGuard()
    {
        PaymentTools tool = NewTool();

        // Not ArgumentException: a 64-hex id clears the guard, and preparation then fails on the
        // null preparer. That distinction is the assertion — the guard neither rejects a valid id
        // nor is skipped for one.
        await Assert.ThrowsAsync<NullReferenceException>(() => tool.PaymentPrepareAsync(
            "testnet", "rA", "rB", amount: "1000", invoiceId: GoodHash));
    }

    [TestMethod]
    public void TestU_ParseCredentialIds_NullOrEmpty_ReturnsNull()
    {
        Assert.IsNull(PaymentTools.ParseCredentialIds(null));
        Assert.IsNull(PaymentTools.ParseCredentialIds(""));
        Assert.IsNull(PaymentTools.ParseCredentialIds("   "));
    }

    [TestMethod]
    public void TestU_ParseCredentialIds_NotArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => PaymentTools.ParseCredentialIds("{}"));
    }

    [TestMethod]
    public void TestU_ParseCredentialIds_EmptyArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => PaymentTools.ParseCredentialIds("[]"));
    }

    [TestMethod]
    public void TestU_ParseCredentialIds_NonString_Throws()
    {
        Assert.Throws<ArgumentException>(() => PaymentTools.ParseCredentialIds("[42]"));
    }

    [TestMethod]
    public void TestU_ParseCredentialIds_WrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PaymentTools.ParseCredentialIds("[\"" + new string('A', 63) + "\"]"));
        Assert.Throws<ArgumentException>(() =>
            PaymentTools.ParseCredentialIds("[\"" + new string('A', 65) + "\"]"));
    }

    [TestMethod]
    public void TestU_ParseCredentialIds_NonHex_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PaymentTools.ParseCredentialIds("[\"" + new string('Z', 64) + "\"]"));
    }

    [TestMethod]
    public void TestU_ParseCredentialIds_Valid_NormalizedToUpper()
    {
        List<string>? result = PaymentTools.ParseCredentialIds(
            "[\"" + GoodHash.ToLowerInvariant() + "\"]");
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(GoodHash, result[0], "Hashes must be uppercased.");
    }

    [TestMethod]
    public void TestU_ParseCredentialIds_Duplicate_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PaymentTools.ParseCredentialIds(
                "[\"" + GoodHash + "\",\"" + GoodHash.ToLowerInvariant() + "\"]"));
    }

    [TestMethod]
    public void TestU_ParseCredentialIds_TooMany_Throws()
    {
        // 9 distinct hex-valid hashes — over XLS-70 limit of 8.
        List<string> hashes = new List<string>();
        char[] hexChars = { '0', '1', '2', '3', '4', '5', '6', '7', '8' };
        foreach (char d in hexChars)
        {
            hashes.Add(new string(d, 64));
        }
        string csv = string.Join(",", hashes.ConvertAll(h => "\"" + h + "\""));
        Assert.Throws<ArgumentException>(() =>
            PaymentTools.ParseCredentialIds("[" + csv + "]"));
    }

    [TestMethod]
    public void TestU_ParseCredentialIds_EightDistinct_OK()
    {
        List<string> hashes = new List<string>();
        char[] hexChars = { '0', '1', '2', '3', '4', '5', '6', '7' };
        foreach (char d in hexChars)
        {
            hashes.Add(new string(d, 64));
        }
        string csv = string.Join(",", hashes.ConvertAll(h => "\"" + h + "\""));
        List<string>? result = PaymentTools.ParseCredentialIds("[" + csv + "]");
        Assert.IsNotNull(result);
        Assert.AreEqual(8, result.Count);
    }
}
