using System;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Models.Common;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class XChainToolsTestsU
{
    private const string GoodBridgeJson =
        "{\"LockingChainDoor\":\"rLocker0000000000000000000000000\"," +
        "\"LockingChainIssue\":{\"currency\":\"XRP\"}," +
        "\"IssuingChainDoor\":\"rIssuer0000000000000000000000000\"," +
        "\"IssuingChainIssue\":{\"currency\":\"XRP\"}}";

    [TestMethod]
    public void TestU_ParseBridge_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => XChainTools.ParseBridge(""));
        Assert.Throws<ArgumentException>(() => XChainTools.ParseBridge("   "));
    }

    [TestMethod]
    public void TestU_ParseBridge_NotObject_Throws()
    {
        Assert.Throws<ArgumentException>(() => XChainTools.ParseBridge("[]"));
    }

    [TestMethod]
    public void TestU_ParseBridge_MissingLockingDoor_Throws()
    {
        string json =
            "{\"LockingChainIssue\":{\"currency\":\"XRP\"}," +
            "\"IssuingChainDoor\":\"rIss\"," +
            "\"IssuingChainIssue\":{\"currency\":\"XRP\"}}";
        Assert.Throws<ArgumentException>(() => XChainTools.ParseBridge(json));
    }

    [TestMethod]
    public void TestU_ParseBridge_SameDoors_Throws()
    {
        string json =
            "{\"LockingChainDoor\":\"rSame\"," +
            "\"LockingChainIssue\":{\"currency\":\"XRP\"}," +
            "\"IssuingChainDoor\":\"rSame\"," +
            "\"IssuingChainIssue\":{\"currency\":\"XRP\"}}";
        Assert.Throws<ArgumentException>(() => XChainTools.ParseBridge(json));
    }

    [TestMethod]
    public void TestU_ParseBridge_MissingIssue_Throws()
    {
        string json =
            "{\"LockingChainDoor\":\"rA\"," +
            "\"IssuingChainDoor\":\"rB\"," +
            "\"IssuingChainIssue\":{\"currency\":\"XRP\"}}";
        Assert.Throws<ArgumentException>(() => XChainTools.ParseBridge(json));
    }

    [TestMethod]
    public void TestU_ParseBridge_Good_XrpXrp()
    {
        XChainBridgeModel bridge = XChainTools.ParseBridge(GoodBridgeJson);
        Assert.AreEqual("rLocker0000000000000000000000000", bridge.LockingChainDoor);
        Assert.AreEqual("rIssuer0000000000000000000000000", bridge.IssuingChainDoor);
        Assert.IsNotNull(bridge.LockingChainIssue);
        Assert.IsNotNull(bridge.IssuingChainIssue);
    }

    [TestMethod]
    public void TestU_ParseBridge_IouIou_ResolvesIssuer()
    {
        string json =
            "{\"LockingChainDoor\":\"rLockerXXXXX\"," +
            "\"LockingChainIssue\":{\"currency\":\"USD\",\"issuer\":\"rIssuerL\"}," +
            "\"IssuingChainDoor\":\"rIssuerXXXXX\"," +
            "\"IssuingChainIssue\":{\"currency\":\"USD\",\"issuer\":\"rIssuerI\"}}";
        XChainBridgeModel bridge = XChainTools.ParseBridge(json);
        Assert.IsNotNull(bridge.LockingChainIssue);
        Assert.IsNotNull(bridge.IssuingChainIssue);
    }

    // --- ValidateHex ---

    [TestMethod]
    public void TestU_ValidateHex_Good()
    {
        XChainTools.ValidateHex("AB09cdef", "test");
    }

    [TestMethod]
    public void TestU_ValidateHex_NonHex_Throws()
    {
        Assert.Throws<ArgumentException>(() => XChainTools.ValidateHex("XX", "test"));
    }
}
