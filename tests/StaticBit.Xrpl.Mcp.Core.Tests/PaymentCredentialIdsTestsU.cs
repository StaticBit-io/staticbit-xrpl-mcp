using System;
using System.Collections.Generic;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class PaymentCredentialIdsTestsU
{
    private const string GoodHash = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";

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
