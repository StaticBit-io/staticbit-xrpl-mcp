using System;
using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Utils.Hashes;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Pure-local hash computation tools — no network calls. Used to produce
/// canonical XRPL ledger-object identifiers (e.g. Credential entry ID for
/// XLS-70 Payment.CredentialIDs) on the client side, where the agent already
/// knows the input components and doesn't need a round-trip to rippled.
/// </summary>
[McpServerToolType]
public sealed class HashTools
{
    [McpServerTool(Name = "xrpl_hash_credential")]
    [Description("Computes the canonical XRPL Credential ledger-object identifier (SHA-512/2, 64-hex Hash256) from (subject, issuer, credentialType). Used to build Payment.CredentialIDs for XLS-70 credential-gated deposits. credentialType may be provided as hex (even-length, ≤128 chars) OR as plain UTF-8 text — mutually exclusive. Pure local computation, no network call.")]
    public string HashCredential(
        [Description("Credential subject — classic XRP r-address (the credential holder).")] string subject,
        [Description("Credential issuer — classic XRP r-address.")] string issuer,
        [Description("Credential type as hex (1..128 hex chars, even length). Mutually exclusive with credentialTypePlain.")] string? credentialTypeHex = null,
        [Description("Credential type as plain UTF-8 text (auto-hex-encoded). Mutually exclusive with credentialTypeHex.")] string? credentialTypePlain = null)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("subject is required.", nameof(subject));
        }
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new ArgumentException("issuer is required.", nameof(issuer));
        }

        string credentialTypeHexResolved = ResolveCredentialType(credentialTypeHex, credentialTypePlain);

        // SDK helper computes:
        //   SHA-512/2(LedgerSpace.Credential || subject_addr_hex || issuer_addr_hex || credentialType_hex)
        // Returns uppercase 64-hex Hash256.
        return Hashes.HashCredential(subject, issuer, credentialTypeHexResolved);
    }

    internal static string ResolveCredentialType(string? hex, string? plain)
    {
        bool hasHex = !string.IsNullOrEmpty(hex);
        bool hasPlain = !string.IsNullOrEmpty(plain);

        if (hasHex && hasPlain)
        {
            throw new ArgumentException("Provide only one of credentialTypeHex or credentialTypePlain.");
        }
        if (!hasHex && !hasPlain)
        {
            throw new ArgumentException("One of credentialTypeHex or credentialTypePlain is required.");
        }

        string result;
        if (hasHex)
        {
            if ((hex!.Length & 1) != 0)
            {
                throw new ArgumentException("credentialTypeHex must have an even number of hex chars.", nameof(hex));
            }
            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!ok)
                {
                    throw new ArgumentException(
                        $"credentialTypeHex contains non-hex character at position {i}.", nameof(hex));
                }
            }
            result = hex.ToUpperInvariant();
        }
        else
        {
            result = Convert.ToHexString(Encoding.UTF8.GetBytes(plain!));
        }

        if (result.Length == 0 || result.Length > 128)
        {
            throw new ArgumentException(
                "credentialType must encode to 1..128 hex chars (= 1..64 raw bytes).");
        }
        return result;
    }
}
