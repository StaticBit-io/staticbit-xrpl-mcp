using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace StaticBit.Xrpl.Mcp.Signer.Configuration;

/// <summary>
/// Configuration for the offline signer. Resolved from environment variables
/// (no JSON config file — the signer is stdio-driven and avoids any filesystem
/// configuration aside from the encrypted keystore itself).
/// </summary>
public sealed class SignerOptions
{
    /// <summary>
    /// Absolute path to the keystore file. Defaults to
    /// <c>%USERPROFILE%\.staticbit-xrpl-signer\keystore.json</c> on Windows and
    /// <c>~/.staticbit-xrpl-signer/keystore.json</c> on Unix.
    /// Override with <c>XRPL_SIGNER_KEYSTORE_PATH</c>.
    /// </summary>
    public string KeystorePath { get; init; } = string.Empty;

    /// <summary>
    /// Passphrase used to unlock the keystore. Cached in memory for the lifetime
    /// of the signer process — the value is never written to disk. Resolution order:
    /// <list type="number">
    /// <item><c>XRPL_SIGNER_PASSPHRASE</c> environment variable</item>
    /// <item>file pointed to by <c>XRPL_SIGNER_PASSPHRASE_FILE</c> (first line)</item>
    /// </list>
    /// If neither is set the signer refuses to start — there is no interactive prompt
    /// because stdin is occupied by the MCP protocol channel.
    /// </summary>
    public string Passphrase { get; init; } = string.Empty;

    /// <summary>
    /// Path to an append-only JSON-lines audit log of every successful or failed signing
    /// operation. Empty disables the audit log. When <c>XRPL_SIGNER_AUDIT_LOG</c> is unset it
    /// defaults to <c>&lt;keystore_dir&gt;/signer-audit.log</c>; set the variable to a path to move
    /// it, or to <c>off</c> to disable it.
    /// </summary>
    public string AuditLogPath { get; init; } = string.Empty;

    /// <summary>
    /// High-risk transaction kinds (see <see cref="Policy.SigningPolicy.HighRiskKinds"/>) the
    /// operator explicitly allows the signer to sign. Empty (default) refuses all of them.
    /// Comma- or semicolon-separated list in <c>XRPL_SIGNER_ALLOW_TX_TYPES</c>.
    /// </summary>
    public IReadOnlyList<string> AllowedHighRiskKinds { get; init; } = Array.Empty<string>();

    public static SignerOptions ResolveFromEnvironment()
    {
        string passphrase = Environment.GetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE") ?? string.Empty;
        if (string.IsNullOrEmpty(passphrase))
        {
            string? passphraseFile = Environment.GetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE_FILE");
            if (!string.IsNullOrWhiteSpace(passphraseFile) && File.Exists(passphraseFile))
            {
                passphrase = ReadFirstLine(passphraseFile);
            }
        }

        string keystorePath = Environment.GetEnvironmentVariable("XRPL_SIGNER_KEYSTORE_PATH")
            ?? GetDefaultKeystorePath();

        return new SignerOptions
        {
            KeystorePath = keystorePath,
            Passphrase = passphrase,
            AuditLogPath = ResolveAuditLogPath(
                Environment.GetEnvironmentVariable("XRPL_SIGNER_AUDIT_LOG"), keystorePath),
            AllowedHighRiskKinds = ParseList(Environment.GetEnvironmentVariable("XRPL_SIGNER_ALLOW_TX_TYPES")),
        };
    }

    /// <summary>
    /// Audit log is on by default, next to the keystore so a single backup catches both.
    /// Unset or empty → default path; <c>off</c> / <c>none</c> / <c>false</c> → disabled.
    /// </summary>
    public static string ResolveAuditLogPath(string? configured, string keystorePath)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            string directory = Path.GetDirectoryName(keystorePath) ?? string.Empty;
            return Path.Combine(directory, "signer-audit.log");
        }

        string value = configured.Trim();
        return value.Equals("off", StringComparison.OrdinalIgnoreCase)
            || value.Equals("none", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value;
    }

    public static IReadOnlyList<string> ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string GetDefaultKeystorePath()
    {
        string home = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(home, ".staticbit-xrpl-signer", "keystore.json");
    }

    private static string ReadFirstLine(string path)
    {
        using StreamReader reader = new StreamReader(path);
        string? line = reader.ReadLine();
        return line?.Trim() ?? string.Empty;
    }
}
