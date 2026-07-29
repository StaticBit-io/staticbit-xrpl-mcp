using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Core.Services;

namespace StaticBit.Xrpl.Mcp.Core.DependencyInjection;

/// <summary>
/// Wires the central tool-error classifier into the MCP server: every registered tool is wrapped
/// in a <see cref="ClassifyingTool"/> so that exceptions escaping a tool body surface to the
/// client as structured envelopes instead of the SDK's opaque generic message.
/// </summary>
/// <remarks>
/// Call AFTER <c>WithTools</c>/<c>WithToolsFromAssembly</c>. The wrap runs in a
/// <see cref="OptionsBuilder{TOptions}"/> post-configure step, which the runtime executes after
/// the SDK's own configuration has populated <see cref="McpServerOptions.ToolCollection"/> from
/// the DI-registered tools.
/// </remarks>
public static class ToolErrorClassification
{
    public static IServiceCollection AddXrplToolErrorClassification(this IServiceCollection services)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));

        services.PostConfigure<McpServerOptions>(WrapToolCollection);
        return services;
    }

    /// <summary>
    /// Replaces each entry of <paramref name="options"/>'s tool collection with a
    /// <see cref="ClassifyingTool"/> decorator. Idempotent: tools already decorated are left
    /// as-is, so repeated configuration passes never double-wrap.
    /// </summary>
    internal static void WrapToolCollection(McpServerOptions options)
    {
        McpServerPrimitiveCollection<McpServerTool>? tools = options.ToolCollection;
        if (tools is null)
        {
            return;
        }

        List<McpServerTool> snapshot = new List<McpServerTool>();
        foreach (McpServerTool tool in tools)
        {
            snapshot.Add(tool);
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        tools.Clear();
        foreach (McpServerTool tool in snapshot)
        {
            tools.Add(tool is ClassifyingTool ? tool : new ClassifyingTool(tool));
        }
    }
}
