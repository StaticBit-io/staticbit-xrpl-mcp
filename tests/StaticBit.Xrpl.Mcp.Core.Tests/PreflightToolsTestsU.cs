using System;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class PreflightToolsTestsU
{
    private static PreflightTools NewTool() => new PreflightTools(pool: null!);

    [TestMethod]
    public async Task TestU_Preflight_EmptyJson_Throws()
    {
        PreflightTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.PreflightAsync("testnet", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => tool.PreflightAsync("testnet", "   "));
    }

    [TestMethod]
    public async Task TestU_Preflight_InvalidJson_Throws()
    {
        PreflightTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.PreflightAsync(
            "testnet", "not a json"));
    }

    [TestMethod]
    public async Task TestU_Preflight_JsonNotObject_Throws()
    {
        PreflightTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.PreflightAsync(
            "testnet", "[1,2,3]"));
    }

    [TestMethod]
    public async Task TestU_Simulate_EmptyJson_Throws()
    {
        PreflightTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.SimulateAsync("testnet", ""));
    }

    [TestMethod]
    public async Task TestU_Simulate_InvalidJson_Throws()
    {
        PreflightTools tool = NewTool();
        await Assert.ThrowsAsync<ArgumentException>(() => tool.SimulateAsync(
            "testnet", "{broken"));
    }
}
