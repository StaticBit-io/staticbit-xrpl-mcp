using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Models.Ledger;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class DepositPreauthCredentialsTestsU
{
    private static AccountManagementTools NewTool() => new AccountManagementTools(preparer: null!);

    // --- ParseCredentialEntries (internal helper) ---

    [TestMethod]
    public void TestU_ParseCredentialEntries_NotArray_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseCredentialEntries("{\"issuer\":\"r\"}", "authorizeCredentialsJson"));
    }

    [TestMethod]
    public void TestU_ParseCredentialEntries_NonObjectEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseCredentialEntries("[\"not-an-object\"]", "authorizeCredentialsJson"));
    }

    [TestMethod]
    public void TestU_ParseCredentialEntries_MissingIssuer_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseCredentialEntries(
                "[{\"credentialType\":\"DEADBEEF\"}]",
                "authorizeCredentialsJson"));
    }

    [TestMethod]
    public void TestU_ParseCredentialEntries_MissingCredentialType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseCredentialEntries(
                "[{\"issuer\":\"rIssuer\"}]",
                "authorizeCredentialsJson"));
    }

    [TestMethod]
    public void TestU_ParseCredentialEntries_EmptyArray_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseCredentialEntries("[]", "authorizeCredentialsJson"));
    }

    [TestMethod]
    public void TestU_ParseCredentialEntries_TooMany_Throws()
    {
        string entries = "[" + string.Join(",", Enumerable.Range(0, 9)
            .Select(i => $"{{\"issuer\":\"rIss{i}\",\"credentialType\":\"DEAD\"}}")) + "]";
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseCredentialEntries(entries, "authorizeCredentialsJson"));
    }

    [TestMethod]
    public void TestU_ParseCredentialEntries_Valid()
    {
        List<AuthorizeCredentialEntry> result = AccountManagementTools.ParseCredentialEntries(
            "[{\"issuer\":\"rAlice\",\"credentialType\":\"DEADBEEF\"},{\"issuer\":\"rBob\",\"credentialType\":\"CAFE\"}]",
            "authorizeCredentialsJson");

        Assert.HasCount(2, result);
        Assert.AreEqual("rAlice", result[0].Credential.Issuer);
        Assert.AreEqual("DEADBEEF", result[0].Credential.CredentialType);
        Assert.AreEqual("rBob", result[1].Credential.Issuer);
    }

    // --- DepositPreauthPrepareAsync: exactly-one-of validation across 4 variants ---

    [TestMethod]
    public async Task TestU_DepositPreauth_NoneProvided_Throws()
    {
        AccountManagementTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.DepositPreauthPrepareAsync("testnet", "rOwner"));
    }

    [TestMethod]
    public async Task TestU_DepositPreauth_AuthorizeAndCredentials_Throws()
    {
        AccountManagementTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.DepositPreauthPrepareAsync("testnet", "rOwner",
                authorize: "rAlice",
                authorizeCredentialsJson: "[{\"issuer\":\"rB\",\"credentialType\":\"AB\"}]"));
    }

    [TestMethod]
    public async Task TestU_DepositPreauth_BothCredentialVariants_Throws()
    {
        AccountManagementTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            tool.DepositPreauthPrepareAsync("testnet", "rOwner",
                authorizeCredentialsJson: "[{\"issuer\":\"rA\",\"credentialType\":\"AB\"}]",
                unauthorizeCredentialsJson: "[{\"issuer\":\"rA\",\"credentialType\":\"AB\"}]"));
    }
}
