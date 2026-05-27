using System;
using System.Collections.Generic;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class PermissionedDomainToolsTestsU
{
    private const string GoodDomainId = "00000000000000000000000000000000000000000000000000000000DEADBEEF";

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials(""));
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials("   "));
    }

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_NotArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials("{}"));
    }

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_EmptyArray_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials("[]"));
    }

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_TooMany_Throws()
    {
        string entries = "[" + string.Join(",", System.Linq.Enumerable.Range(0, 11)
            .Select(i => $"{{\"issuer\":\"rIss{i}\",\"credentialType\":\"AB{i:X2}\"}}")) + "]";
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials(entries));
    }

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_MissingIssuer_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials(
            "[{\"credentialType\":\"AB\"}]"));
    }

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_MissingType_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials(
            "[{\"issuer\":\"rIss\"}]"));
    }

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_NonHexType_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials(
            "[{\"issuer\":\"rIss\",\"credentialType\":\"XX\"}]"));
    }

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_OddLengthType_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials(
            "[{\"issuer\":\"rIss\",\"credentialType\":\"ABC\"}]"));
    }

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_TooLongType_Throws()
    {
        string longType = new string('A', 130);
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials(
            "[{\"issuer\":\"rIss\",\"credentialType\":\"" + longType + "\"}]"));
    }

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_DuplicateCaseInsensitive_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ParseAcceptedCredentials(
            "[{\"issuer\":\"rIss\",\"credentialType\":\"abcd\"}," +
            "{\"issuer\":\"rIss\",\"credentialType\":\"ABCD\"}]"));
    }

    [TestMethod]
    public void TestU_ParseAcceptedCredentials_Valid_NormalizedAndDeduped()
    {
        List<AcceptedCredentialWrapper> result = PermissionedDomainTools.ParseAcceptedCredentials(
            "[{\"issuer\":\"rIssA\",\"credentialType\":\"abcd\"}," +
            "{\"issuer\":\"rIssB\",\"credentialType\":\"DEAD\"}]");
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("ABCD", result[0].Credential.CredentialType);
        Assert.AreEqual("DEAD", result[1].Credential.CredentialType);
        Assert.AreEqual("rIssA", result[0].Credential.Issuer);
    }

    // --- ValidateDomainId ---

    [TestMethod]
    public void TestU_ValidateDomainId_Good()
    {
        PermissionedDomainTools.ValidateDomainId(GoodDomainId);
    }

    [TestMethod]
    public void TestU_ValidateDomainId_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ValidateDomainId(""));
    }

    [TestMethod]
    public void TestU_ValidateDomainId_WrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ValidateDomainId(new string('A', 63)));
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ValidateDomainId(new string('A', 65)));
    }

    [TestMethod]
    public void TestU_ValidateDomainId_NonHex_Throws()
    {
        Assert.Throws<ArgumentException>(() => PermissionedDomainTools.ValidateDomainId(new string('Z', 64)));
    }
}
