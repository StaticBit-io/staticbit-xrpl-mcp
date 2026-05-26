using System;
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
    /// Optional path to an append-only JSON-lines audit log of every successful or
    /// failed signing operation. Empty (default) disables the audit log entirely.
    /// Override with <c>XRPL_SIGNER_AUDIT_LOG</c> env var; the conventional value
    /// is <c>&lt;keystore_dir&gt;/signer-audit.log</c>.
    /// </summary>
    public string AuditLogPath { get; init; } = string.Empty;

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

        // Audit log is opt-in. Empty value = disabled. The conventional place is
        // alongside the keystore so a single backup script catches both.
        string auditLogPath = Environment.GetEnvironmentVariable("XRPL_SIGNER_AUDIT_LOG") ?? string.Empty;

        return new SignerOptions
        {
            KeystorePath = keystorePath,
            Passphrase = passphrase,
            AuditLogPath = auditLogPath,
        };
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
