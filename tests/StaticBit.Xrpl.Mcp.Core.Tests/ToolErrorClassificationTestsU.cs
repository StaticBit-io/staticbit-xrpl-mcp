using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Core.DependencyInjection;
using StaticBit.Xrpl.Mcp.Core.Services;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class ToolErrorClassificationTestsU
{
    private static McpServerOptions OptionsWithTool(string name)
    {
        McpServerOptions options = new McpServerOptions
        {
            ToolCollection = new McpServerPrimitiveCollection<McpServerTool>(),
        };
        options.ToolCollection.Add(McpServerTool.Create(() => "ok", new McpServerToolCreateOptions { Name = name }));
        return options;
    }

    [TestMethod]
    public void TestU_WrapToolCollection_ReplacesEntriesWithClassifyingTool_PreservingName()
    {
        McpServerOptions options = OptionsWithTool("xrpl_demo");

        ToolErrorClassification.WrapToolCollection(options);

        Assert.HasCount(1, options.ToolCollection!);
        Assert.IsTrue(options.ToolCollection!.TryGetPrimitive("xrpl_demo", out McpServerTool? wrapped));
        Assert.IsInstanceOfType<ClassifyingTool>(wrapped);
    }

    [TestMethod]
    public void TestU_WrapToolCollection_IsIdempotent_DoesNotDoubleWrap()
    {
        McpServerOptions options = OptionsWithTool("xrpl_demo");

        ToolErrorClassification.WrapToolCollection(options);
        ToolErrorClassification.WrapToolCollection(options);

        Assert.HasCount(1, options.ToolCollection!);
        Assert.IsTrue(options.ToolCollection!.TryGetPrimitive("xrpl_demo", out McpServerTool? wrapped));
        ClassifyingTool classifying = Assert.IsInstanceOfType<ClassifyingTool>(wrapped);
        Assert.IsNotInstanceOfType<ClassifyingTool>(classifying.Inner);
    }

    [TestMethod]
    public void TestU_WrapToolCollection_NoToolCollection_IsSafeNoOp()
    {
        McpServerOptions options = new McpServerOptions();

        ToolErrorClassification.WrapToolCollection(options);

        Assert.IsNull(options.ToolCollection);
    }
}
