using System;
using System.Text.Json;
using System.Threading.Tasks;
using StaticBit.Xrpl.Mcp.Core.Services;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Integration.Tests;

/// <summary>
/// Acceptance test for transparent rippled errors. The public XRPL cluster
/// <c>wss://xrplcluster.com</c> disables <c>ripple_path_find</c>, so the node answers with
/// <c>error = "noPermission"</c>. Before this change the SDK exception was swallowed by the MCP
/// runtime into the opaque <c>"An error occurred invoking 'xrpl_ripple_path_find'"</c> stub;
/// now <see cref="XrplToolError"/> classifies it into an envelope that preserves the real code.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public class PathFindNoPermissionTestsI
{
    private const string MainnetClusterWs = "wss://xrplcluster.com";

    // The XPM issuer from the reproduction. Used as both source and destination — the cluster
    // rejects the disabled command regardless of the path arguments.
    private const string Address = "rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa";
    private const string DestinationAmount =
        "{\"value\":\"100\",\"currency\":\"XPM\",\"issuer\":\"rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa\"}";

    private static XrplClientPool? _pool;

    [ClassInitialize]
    public static void Init(TestContext _) => _pool = TestnetFixture.BuildPool("mainnet", MainnetClusterWs);

    [ClassCleanup]
    public static async Task Cleanup()
    {
        if (_pool is not null) await _pool.DisposeAsync();
    }

    [TestMethod]
    public async Task TestI_RipplePathFind_OnClusterNode_EnvelopeCarriesNoPermission()
    {
        PathTools tool = new PathTools(_pool!);

        Exception? captured = null;
        try
        {
            await tool.RipplePathFindAsync("mainnet", Address, Address, DestinationAmount);
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        Assert.IsNotNull(captured, "Expected ripple_path_find to fail on a node that disables the command.");

        // This is exactly what the ClassifyingTool decorator does with the escaping exception.
        string envelope = XrplToolError.SerializeError(captured!);
        StringAssert.Contains(envelope, "noPermission");

        // ...and the structured fields make the cause machine-distinguishable: the real rippled
        // code in rawError, and a precise category instead of "Unknown / internal error".
        using JsonDocument doc = JsonDocument.Parse(envelope);
        JsonElement root = doc.RootElement;
        Assert.AreEqual("noPermission", root.GetProperty("rawError").GetString());
        Assert.AreEqual("UnsupportedRequest", root.GetProperty("category").GetString());
    }
}
