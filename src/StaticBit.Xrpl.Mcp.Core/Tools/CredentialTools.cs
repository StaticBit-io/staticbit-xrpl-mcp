using System;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Models.Transactions;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// XLS-70 Credentials write-flow MCP tools.
///
/// A Credential is a two-step ledger object: the issuer submits CredentialCreate
/// (provisional state, reserve held by issuer); the subject then submits
/// CredentialAccept to make it valid (reserve transfers to subject). Either side
/// can delete it via CredentialDelete; once expired anyone can delete it.
///
/// Use case: ties together with <c>xrpl_deposit_preauth_prepare</c>
/// (authorizeCredentialsJson) and <c>xrpl_permissioned_domain_set_prepare</c>
/// for credential-gated payments and domain access.
/// </summary>
[McpServerToolType]
public sealed class CredentialTools
{
    private const int MaxCredentialTypeHexLength = 128;
    private const int MaxCredentialUriHexLength = 512;

    private readonly TransactionPreparer _preparer;

    public CredentialTools(TransactionPreparer preparer)
    {
        _preparer = preparer;
    }

    [McpServerTool(Name = "xrpl_credential_create_prepare")]
    [Description("Prepares an UNSIGNED CredentialCreate (XLS-70). 'account' is the ISSUER; 'subject' is the recipient. Provide credentialType as either credentialTypeHex (≤128 hex chars = 64 raw bytes) or credentialTypePlain (UTF-8 auto-hex-encoded; ≤64 chars) — mutually exclusive. Optional URI via uriHex (≤512 hex chars = 256 raw bytes) or uriPlain (auto-hex). Optional expirationUtc — credential is automatically deletable after that time. Reserve is held by the issuer until the subject calls CredentialAccept.")]
    public async Task<PreparedTransaction> CredentialCreatePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Issuer account (transaction sender).")] string account,
        [Description("Subject account that receives the credential. Must differ from account.")] string subject,
        [Description("Credential type as a hex string (1..128 hex chars). Mutually exclusive with credentialTypePlain.")] string? credentialTypeHex = null,
        [Description("Credential type as a plain ASCII/UTF-8 string (auto-hex-encoded). Mutually exclusive with credentialTypeHex.")] string? credentialTypePlain = null,
        [Description("Optional URI as a hex string (≤512 hex chars). Mutually exclusive with uriPlain.")] string? uriHex = null,
        [Description("Optional URI as a plain string (auto-hex-encoded). Mutually exclusive with uriHex.")] string? uriPlain = null,
        [Description("Optional UTC expiration. After this, the credential is auto-revoked and any account may delete it.")] DateTime? expirationUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("subject is required.", nameof(subject));
        }
        if (string.Equals(account, subject, StringComparison.Ordinal))
        {
            throw new ArgumentException("subject must differ from account (the issuer).", nameof(subject));
        }

        string finalCredType = ResolveHexParam(credentialTypeHex, credentialTypePlain,
            nameof(credentialTypeHex), nameof(credentialTypePlain), MaxCredentialTypeHexLength, required: true)!;
        string? finalUri = ResolveHexParam(uriHex, uriPlain,
            nameof(uriHex), nameof(uriPlain), MaxCredentialUriHexLength, required: false);

        CredentialCreate tx = new CredentialCreate
        {
            Account = account,
            Subject = subject,
            CredentialType = finalCredType,
            URI = finalUri,
            Expiration = expirationUtc,
        };

        string expiry = expirationUtc.HasValue
            ? $", expires {expirationUtc.Value:yyyy-MM-ddTHH:mm:ssZ}"
            : "";
        string summary = $"CredentialCreate by issuer {ToolDisplay.Truncate(account)} for subject {ToolDisplay.Truncate(subject)}: "
            + $"type={ShortHex(finalCredType)}{expiry}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_credential_accept_prepare")]
    [Description("Prepares an UNSIGNED CredentialAccept (XLS-70). The credential SUBJECT submits this to ratify a provisionally-issued credential; once accepted, the reserve transfers from the issuer to the subject and the credential becomes usable for DepositPreauth and PermissionedDomain access. Provide credentialType as hex OR plain text.")]
    public async Task<PreparedTransaction> CredentialAcceptPrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Subject account (transaction sender) that accepts the credential.")] string account,
        [Description("Issuer that originally created the credential. Must differ from account.")] string issuer,
        [Description("Credential type as hex (1..128 hex chars). Mutually exclusive with credentialTypePlain.")] string? credentialTypeHex = null,
        [Description("Credential type as plain string (auto-hex). Mutually exclusive with credentialTypeHex.")] string? credentialTypePlain = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new ArgumentException("issuer is required.", nameof(issuer));
        }
        if (string.Equals(account, issuer, StringComparison.Ordinal))
        {
            throw new ArgumentException("issuer must differ from account (the subject).", nameof(issuer));
        }

        string finalCredType = ResolveHexParam(credentialTypeHex, credentialTypePlain,
            nameof(credentialTypeHex), nameof(credentialTypePlain), MaxCredentialTypeHexLength, required: true)!;

        CredentialAccept tx = new CredentialAccept
        {
            Account = account,
            Issuer = issuer,
            CredentialType = finalCredType,
        };

        string summary = $"CredentialAccept by subject {ToolDisplay.Truncate(account)} of credential from {ToolDisplay.Truncate(issuer)}: type={ShortHex(finalCredType)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "xrpl_credential_delete_prepare")]
    [Description("Prepares an UNSIGNED CredentialDelete (XLS-70). Submittable by: the issuer (revoke), the subject (un-accept), or anyone after expiration. Exactly one of 'subject'/'issuer' must be provided — the OTHER party (not the transaction sender). If account=issuer, supply subject; if account=subject, supply issuer; if anyone deleting an expired credential, supply both (otherwise the chain will infer 'account' as one side).")]
    public async Task<PreparedTransaction> CredentialDeletePrepareAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Transaction sender — issuer, subject, or any account if the credential has expired.")] string account,
        [Description("Credential type as hex (1..128 hex chars). Mutually exclusive with credentialTypePlain.")] string? credentialTypeHex = null,
        [Description("Credential type as plain string. Mutually exclusive with credentialTypeHex.")] string? credentialTypePlain = null,
        [Description("Subject of the credential. Omit ONLY if account==subject.")] string? subject = null,
        [Description("Issuer of the credential. Omit ONLY if account==issuer.")] string? issuer = null,
        CancellationToken cancellationToken = default)
    {
        bool hasSubject = !string.IsNullOrEmpty(subject);
        bool hasIssuer = !string.IsNullOrEmpty(issuer);
        if (!hasSubject && !hasIssuer)
        {
            throw new ArgumentException("Provide at least one of 'subject' or 'issuer' (the side that is NOT the transaction sender).");
        }
        if (hasSubject && string.Equals(subject, account, StringComparison.Ordinal))
        {
            throw new ArgumentException("subject must differ from account (omit subject when account IS the subject).", nameof(subject));
        }
        if (hasIssuer && string.Equals(issuer, account, StringComparison.Ordinal))
        {
            throw new ArgumentException("issuer must differ from account (omit issuer when account IS the issuer).", nameof(issuer));
        }

        string finalCredType = ResolveHexParam(credentialTypeHex, credentialTypePlain,
            nameof(credentialTypeHex), nameof(credentialTypePlain), MaxCredentialTypeHexLength, required: true)!;

        CredentialDelete tx = new CredentialDelete
        {
            Account = account,
            Subject = hasSubject ? subject : null,
            Issuer = hasIssuer ? issuer : null,
            CredentialType = finalCredType,
        };

        string role = hasSubject && hasIssuer ? "expiry sweep"
            : hasSubject ? $"issuer revoke for {ToolDisplay.Truncate(subject)}"
            : $"subject un-accept of {ToolDisplay.Truncate(issuer)}";
        string summary = $"CredentialDelete by {ToolDisplay.Truncate(account)}: {role}, type={ShortHex(finalCredType)}.";

        return await _preparer
            .PrepareAsync(new NetworkRef(network), tx, summary, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static string? ResolveHexParam(
        string? hex,
        string? plain,
        string hexParamName,
        string plainParamName,
        int maxHexLength,
        bool required)
    {
        bool hasHex = !string.IsNullOrEmpty(hex);
        bool hasPlain = !string.IsNullOrEmpty(plain);

        if (hasHex && hasPlain)
        {
            throw new ArgumentException($"Provide only one of {hexParamName} or {plainParamName}.");
        }
        if (!hasHex && !hasPlain)
        {
            if (required)
            {
                throw new ArgumentException($"One of {hexParamName} or {plainParamName} is required.");
            }
            return null;
        }

        string result;
        if (hasHex)
        {
            ValidateHex(hex!, hexParamName);
            if ((hex!.Length & 1) != 0)
            {
                throw new ArgumentException($"{hexParamName} must have an even number of hex chars.", hexParamName);
            }
            result = hex.ToUpperInvariant();
        }
        else
        {
            result = Convert.ToHexString(Encoding.UTF8.GetBytes(plain!));
        }

        if (result.Length == 0)
        {
            throw new ArgumentException($"{hexParamName}/{plainParamName} must not produce an empty value.");
        }
        if (result.Length > maxHexLength)
        {
            throw new ArgumentException($"{hexParamName}/{plainParamName} exceeds {maxHexLength} hex chars (= {maxHexLength / 2} raw bytes).");
        }
        return result;
    }

    internal static void ValidateHex(string hex, string paramName)
    {
        for (int i = 0; i < hex.Length; i++)
        {
            char c = hex[i];
            bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
            if (!ok)
            {
                throw new ArgumentException($"{paramName} contains non-hex character at position {i}.", paramName);
            }
        }
    }

    internal static string ShortHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return "<null>";
        return hex.Length <= 16 ? hex : $"{hex.AsSpan(0, 8)}...{hex.AsSpan(hex.Length - 6, 6)}";
    }
}
