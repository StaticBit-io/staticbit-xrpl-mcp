using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Decorates an <see cref="McpServerTool"/> so that any exception escaping the tool body is
/// classified into a structured, client-visible <see cref="McpException"/> envelope (via
/// <see cref="XrplToolError"/>) instead of collapsing to the SDK's opaque
/// <c>"An error occurred invoking '&lt;tool&gt;'."</c> string.
/// </summary>
/// <remarks>
/// The MCP SDK (1.3.0) surfaces a thrown exception's message to the client ONLY when it is an
/// <see cref="McpException"/>; every other type — the XRPL SDK's <c>RippledException</c>,
/// <c>ArgumentException</c> from input validation, socket / timeout failures — is replaced with
/// the generic stub. Wrapping every tool at a single point guarantees the real cause (rippled
/// error code, offending field, connection problem) always reaches the agent, without editing
/// each of the ~150 individual tool methods. The success path is a verbatim pass-through, so
/// well-formed responses are byte-identical to the undecorated tool.
/// </remarks>
internal sealed class ClassifyingTool : McpServerTool
{
    public ClassifyingTool(McpServerTool inner)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>The wrapped tool. Exposed so the wiring step never double-wraps.</summary>
    public McpServerTool Inner { get; }

    public override Tool ProtocolTool => Inner.ProtocolTool;

    public override IReadOnlyList<object> Metadata => Inner.Metadata;

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Inner.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Genuine client-driven cancellation — propagate as-is so the SDK reports
            // cancellation rather than a fabricated "timed out" envelope.
            throw;
        }
        catch (Exception ex)
        {
            // RippledException (noPermission, actNotFound, tecPATH_DRY, ...), ArgumentException
            // from input validation, socket / timeout failures, and anything else → a structured
            // McpException the SDK surfaces verbatim. Inputs that are already McpException (e.g.
            // from AddressValidation) pass through ThrowMcp unchanged — no double-wrapping.
            XrplToolError.ThrowMcp(ex);
            throw; // unreachable — ThrowMcp always throws.
        }
    }
}
