using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// XLS-40 Decentralized Identifier (DID) write-flow MCP tools.
///
/// Each account owns at most one DID ledger entry. DIDSet creates it on first
/// submission and updates fields on subsequent calls. All three storage fields
/// (Data, DIDDocument, URI) are hex-blobs ≤256 raw bytes; at least one must be
/// non-empty in a DIDSet. DIDDelete removes the DID entirely.
/// </summary>
[McpServerToolType]
public sealed class DidTools
{
    private const int MaxDidFieldHexLength = 512;

    private readonly TransactionPreparer _preparer;

    public DidTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_did_set_prepare")]
    [Description("Prepares an UNSIGNED DIDSet (XLS-40). Creates or updates the DID associated with 'account'. At least one of (data, didDocument, uri) must be provided in some form. For each field you can pass either the *Hex variant (raw hex string, ≤512 hex chars = 256 bytes) or the *Plain variant (auto-UTF-8-hex-encoded) — *Hex and *Plain for the same logical field are mutually exclusive.")]
    public async Task<PreparedTransaction> DidSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account that owns the DID.")] string account,
        [Description("Optional 'Data' field as hex string (≤512 hex chars). Mutually exclusive with dataPlain.")] string? dataHex = null,
        [Description("Optional 'Data' field as plain text (auto-hex-encoded). Mutually exclusive with dataHex.")] string? dataPlain = null,
        [Description("Optional 'DIDDocument' field as hex string. Mutually exclusive with didDocumentPlain.")] string? didDocumentHex = null,
        [Description("Optional 'DIDDocument' field as plain text. Mutually exclusive with didDocumentHex.")] string? didDocumentPlain = null,
        [Description("Optional 'URI' field as hex string. Mutually exclusive with uriPlain.")] string? uriHex = null,
        [Description("Optional 'URI' field as plain text. Mutually exclusive with uriHex.")] string? uriPlain = null,
        CancellationToken cancellationToken = default)
    {
        string? finalData = CredentialTools.ResolveHexParam(dataHex, dataPlain,
            nameof(dataHex), nameof(dataPlain), MaxDidFieldHexLength, required: false);
        string? finalDoc = CredentialTools.ResolveHexParam(didDocumentHex, didDocumentPlain,
            nameof(didDocumentHex), nameof(didDocumentPlain), MaxDidFieldHexLength, required: false);
        string? finalUri = CredentialTools.ResolveHexParam(uriHex, uriPlain,
            nameof(uriHex), nameof(uriPlain), MaxDidFieldHexLength, required: false);

        if (finalData is null && finalDoc is null && finalUri is null)
        {
            throw new ArgumentException("DIDSet requires at least one of (data*, didDocument*, uri*) fields.");
        }

        DIDSet tx = new DIDSet
        {
            Account = account,
            Data = finalData,
            DIDDocument = finalDoc,
            URI = finalUri,
        };

        System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
        if (finalData is not null) parts.Add($"Data({finalData.Length / 2}b)");
        if (finalDoc is not null) parts.Add($"DIDDocument({finalDoc.Length / 2}b)");
        if (finalUri is not null) parts.Add($"URI({finalUri.Length / 2}b)");
        string summary = $"DIDSet on {ToolDisplay.Truncate(account)}: " + string.Join(", ", parts) + ".";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_did_delete_prepare")]
    [Description("Prepares an UNSIGNED DIDDelete (XLS-40). Removes the DID ledger entry associated with 'account'. No additional fields.")]
    public async Task<PreparedTransaction> DidDeletePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Account whose DID is being deleted.")] string account,
        CancellationToken cancellationToken = default)
    {
        DIDDelete tx = new DIDDelete
        {
            Account = account,
        };

        string summary = $"DIDDelete on {ToolDisplay.Truncate(account)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }
}
