using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// XLS-80 Permissioned Domains write-flow MCP tools.
///
/// A PermissionedDomain ledger object lists 1..10 (issuer, credentialType) pairs.
/// Accounts holding ANY of those accepted credentials are members of the domain;
/// downstream features (permissioned DEX, permissioned AMM) can restrict access
/// to domain members.
/// </summary>
[McpServerToolType]
public sealed class PermissionedDomainTools
{
    private readonly TransactionPreparer _preparer;

    public PermissionedDomainTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_permissioned_domain_set_prepare")]
    [Description("Prepares an UNSIGNED PermissionedDomainSet (XLS-80). Creates a new permissioned domain (omit domainId) or modifies an existing one (provide its 64-hex DomainID). 'acceptedCredentialsJson' is a JSON array of 1..10 entries: [{\"issuer\":\"r...\",\"credentialType\":\"<hex>\"}, ...]. No duplicates by (issuer, credentialType). On modify, the new list FULLY REPLACES the previous one.")]
    public async Task<PreparedTransaction> PermissionedDomainSetPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Owner account of the permissioned domain.")] string account,
        [Description("JSON array of 1..10 accepted credentials: [{\"issuer\":\"r...\",\"credentialType\":\"<hex 1..128 chars>\"}].")] string acceptedCredentialsJson,
        [Description("Optional 64-hex DomainID of an existing domain to modify. Omit to create a new domain.")] string? domainId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(domainId))
        {
            ValidateDomainId(domainId);
        }

        List<AcceptedCredentialWrapper> credentials = ParseAcceptedCredentials(acceptedCredentialsJson);

        PermissionedDomainSet tx = new PermissionedDomainSet
        {
            Account = account,
            DomainID = string.IsNullOrEmpty(domainId) ? null : domainId.ToUpperInvariant(),
            AcceptedCredentials = credentials,
        };

        string mode = string.IsNullOrEmpty(domainId) ? "CREATE" : $"MODIFY {ShortHex(domainId!)}";
        string summary = $"PermissionedDomainSet by {ToolDisplay.Truncate(account)}: {mode}, "
            + $"{credentials.Count} accepted credential(s).";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_permissioned_domain_delete_prepare")]
    [Description("Prepares an UNSIGNED PermissionedDomainDelete (XLS-80). Removes a permissioned domain owned by the account. domainId is the 64-hex DomainID returned at creation (or visible in account_objects).")]
    public async Task<PreparedTransaction> PermissionedDomainDeletePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Owner account of the permissioned domain.")] string account,
        [Description("64-hex DomainID of the domain to delete.")] string domainId,
        CancellationToken cancellationToken = default)
    {
        ValidateDomainId(domainId);

        PermissionedDomainDelete tx = new PermissionedDomainDelete
        {
            Account = account,
            DomainID = domainId.ToUpperInvariant(),
        };

        string summary = $"PermissionedDomainDelete by {ToolDisplay.Truncate(account)}: {ShortHex(domainId)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static List<AcceptedCredentialWrapper> ParseAcceptedCredentials(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("acceptedCredentialsJson is required.", nameof(json));
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("acceptedCredentialsJson must be a JSON array.");
        }

        List<AcceptedCredentialWrapper> result = new List<AcceptedCredentialWrapper>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"acceptedCredentialsJson[{index}] must be a JSON object.");
            }

            string? issuer = el.TryGetProperty("issuer", out JsonElement i) && i.ValueKind == JsonValueKind.String
                ? i.GetString()
                : null;
            string? credType = el.TryGetProperty("credentialType", out JsonElement c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            if (string.IsNullOrEmpty(issuer))
            {
                throw new ArgumentException($"acceptedCredentialsJson[{index}].issuer is required.");
            }
            if (string.IsNullOrEmpty(credType))
            {
                throw new ArgumentException($"acceptedCredentialsJson[{index}].credentialType is required (hex 1..128 chars).");
            }

            CredentialTools.ValidateHex(credType, $"acceptedCredentialsJson[{index}].credentialType");
            if ((credType.Length & 1) != 0)
            {
                throw new ArgumentException($"acceptedCredentialsJson[{index}].credentialType must have an even number of hex chars.");
            }
            if (credType.Length > 128)
            {
                throw new ArgumentException($"acceptedCredentialsJson[{index}].credentialType exceeds 128 hex chars (= 64 raw bytes).");
            }

            string normalizedType = credType.ToUpperInvariant();
            string key = issuer + ":" + normalizedType;
            if (!seen.Add(key))
            {
                throw new ArgumentException($"acceptedCredentialsJson[{index}] is a duplicate ({issuer}, {normalizedType}).");
            }

            result.Add(new AcceptedCredentialWrapper
            {
                Credential = new AcceptedCredential
                {
                    Issuer = issuer,
                    CredentialType = normalizedType,
                },
            });
            index++;
        }

        if (result.Count == 0)
        {
            throw new ArgumentException("acceptedCredentialsJson must contain at least one credential.");
        }
        if (result.Count > 10)
        {
            throw new ArgumentException("acceptedCredentialsJson cannot contain more than 10 credentials.");
        }
        return result;
    }

    internal static void ValidateDomainId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("domainId is required.", nameof(id));
        }
        if (id.Length != 64)
        {
            throw new ArgumentException("domainId must be a 64-char hex string.", nameof(id));
        }
        CredentialTools.ValidateHex(id, nameof(id));
    }

    private static string ShortHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return "<null>";
        return hex.Length <= 16 ? hex : $"{hex.AsSpan(0, 8)}...{hex.AsSpan(hex.Length - 6, 6)}";
    }
}
