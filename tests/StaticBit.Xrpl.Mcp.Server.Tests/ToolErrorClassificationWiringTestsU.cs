using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Core.DependencyInjection;
using StaticBit.Xrpl.Mcp.Core.Tools;

namespace StaticBit.Xrpl.Mcp.Server.Tests;

/// <summary>
/// Proves the central error-classification wrapper is actually applied to EVERY tool the
/// runtime discovers via <c>WithToolsFromAssembly</c> — i.e. the post-configure step runs
/// after the SDK has populated the tool collection, and nothing is left undecorated. This is
/// the "can't forget a tool" guarantee the per-tool try/catch approach could not give.
/// </summary>
[TestClass]
public class ToolErrorClassificationWiringTestsU
{
    private static McpServerOptions BuildWrappedOptions()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddMcpServer().WithToolsFromAssembly(typeof(LedgerTools).Assembly);
        services.AddXrplToolErrorClassification();

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
    }

    [TestMethod]
    public void TestU_EveryDiscoveredTool_IsWrappedInClassifyingTool()
    {
        McpServerOptions options = BuildWrappedOptions();

        Assert.IsNotNull(options.ToolCollection);
        Assert.IsTrue(options.ToolCollection!.Any(), "Expected the assembly scan to discover tools.");

        // ClassifyingTool is internal to the Core assembly, so assert by type name.
        string[] unwrapped = options.ToolCollection!
            .Where(t => t.GetType().Name != "ClassifyingTool")
            .Select(t => t.ProtocolTool.Name)
            .ToArray();

        Assert.IsEmpty(unwrapped, $"These tools were not wrapped: {string.Join(", ", unwrapped)}");
    }

    [TestMethod]
    public void TestU_WrappedTools_StillExposeOriginalNames()
    {
        McpServerOptions options = BuildWrappedOptions();

        // A representative read tool must remain addressable under its public name.
        Assert.IsTrue(options.ToolCollection!.TryGetPrimitive("xrpl_book_offers", out _));
        Assert.IsTrue(options.ToolCollection!.TryGetPrimitive("xrpl_ripple_path_find", out _));
    }
}
