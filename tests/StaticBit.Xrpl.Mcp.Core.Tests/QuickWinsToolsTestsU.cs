using System;
using System.Collections.Generic;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class QuickWinsToolsTestsU
{
    // --- DelegateSet.ParseDelegatePermissions ---

    [TestMethod]
    public void TestU_ParseDelegatePermissions_Empty_ReturnsEmptyList()
    {
        List<Dictionary<string, object>> result = AccountManagementTools.ParseDelegatePermissions("");
        Assert.AreEqual(0, result.Count);
        result = AccountManagementTools.ParseDelegatePermissions("   ");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void TestU_ParseDelegatePermissions_SingleTrim()
    {
        List<Dictionary<string, object>> result = AccountManagementTools.ParseDelegatePermissions("  Payment  ");
        Assert.AreEqual(1, result.Count);
        Dictionary<string, object> wrapper = result[0];
        Dictionary<string, object> perm = (Dictionary<string, object>)wrapper["Permission"];
        // Payment maps to numeric TransactionType code 0 (per rippled definitions.json).
        Assert.AreEqual(0u, perm["PermissionValue"]);
    }

    [TestMethod]
    public void TestU_ParseDelegatePermissions_UnknownType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseDelegatePermissions("SomethingNotInProtocol"));
    }

    [TestMethod]
    public void TestU_ParseDelegatePermissions_MultipleEntries()
    {
        List<Dictionary<string, object>> result = AccountManagementTools.ParseDelegatePermissions("Payment,TrustSet,OfferCreate");
        Assert.AreEqual(3, result.Count);
    }

    [TestMethod]
    public void TestU_ParseDelegatePermissions_Duplicate_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseDelegatePermissions("Payment,Payment"));
    }

    [TestMethod]
    public void TestU_ParseDelegatePermissions_DuplicateCaseInsensitive_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseDelegatePermissions("Payment,payment"));
    }

    [TestMethod]
    public void TestU_ParseDelegatePermissions_NonDelegable_Throws()
    {
        foreach (string blocked in new[] { "AccountSet", "SetRegularKey", "SignerListSet", "DelegateSet" })
        {
            Assert.Throws<ArgumentException>(
                () => AccountManagementTools.ParseDelegatePermissions(blocked),
                $"'{blocked}' must be rejected.");
        }
    }

    [TestMethod]
    public void TestU_ParseDelegatePermissions_NonDelegable_CaseInsensitive_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AccountManagementTools.ParseDelegatePermissions("accountset"));
    }

    [TestMethod]
    public void TestU_ParseDelegatePermissions_TooMany_Throws()
    {
        // Use 11 real, distinct delegable tx-type names — the entry-count check fires
        // before the unknown-type check would.
        string csv = "Payment,TrustSet,OfferCreate,OfferCancel,CheckCreate,CheckCash,CheckCancel," +
                     "EscrowCreate,EscrowFinish,EscrowCancel,TicketCreate";
        Assert.Throws<ArgumentException>(() => AccountManagementTools.ParseDelegatePermissions(csv));
    }
}
