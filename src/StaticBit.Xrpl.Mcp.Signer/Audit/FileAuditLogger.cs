using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace StaticBit.Xrpl.Mcp.Signer.Audit;

/// <summary>
/// File-backed JSONL audit log. Each event is one JSON object on its own line,
/// written with <c>File.AppendAllText</c> + Unix 0600 perms after first create.
/// Write errors are surfaced via <c>ILogger</c> (stderr) but never bubble out —
/// the audit log must not break signing.
/// </summary>
public sealed class FileAuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _path;
    private readonly ILogger<FileAuditLogger> _logger;
    private readonly object _writeLock = new object();
    private bool _permsApplied;

    public FileAuditLogger(string path, ILogger<FileAuditLogger> logger)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Audit log path is empty.", nameof(path));
        }
        _path = path;
        _logger = logger;

        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit log directory '{Dir}' could not be created — audit will be silently dropped.", dir);
            }
        }
    }

    public void LogSign(string wallet, int? index, string signMode, string? txHash, string? txType)
    {
        JsonObject e = NewBase("sign", wallet, result: "ok");
        e["signMode"] = signMode;
        if (index.HasValue) e["index"] = index.Value;
        if (!string.IsNullOrEmpty(txHash)) e["txHash"] = txHash;
        if (!string.IsNullOrEmpty(txType)) e["txType"] = txType;
        Write(e);
    }

    public void LogDecryptFail(string wallet, string reason)
    {
        JsonObject e = NewBase("decrypt_fail", wallet, result: "decrypt_failed");
        e["reason"] = reason;
        Write(e);
    }

    public void LogSignError(string wallet, string reason)
    {
        JsonObject e = NewBase("sign", wallet, result: "error");
        e["reason"] = reason;
        Write(e);
    }

    private static JsonObject NewBase(string @event, string wallet, string result)
    {
        return new JsonObject
        {
            ["ts"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            ["event"] = @event,
            ["wallet"] = wallet,
            ["result"] = result,
        };
    }

    private void Write(JsonObject envelope)
    {
        string line = envelope.ToJsonString(JsonOptions) + "\n";
        try
        {
            lock (_writeLock)
            {
                File.AppendAllText(_path, line, Encoding.UTF8);
                if (!_permsApplied)
                {
                    TryRestrictFilePermissions(_path);
                    _permsApplied = true;
                }
            }
        }
        catch (Exception ex)
        {
            // Audit log failures are deliberately non-fatal — a sign call must
            // complete even if disk is full / permission denied. Surface to
            // stderr so the operator notices.
            _logger.LogWarning(ex, "Audit log write to '{Path}' failed — event dropped.", _path);
        }
    }

    private static void TryRestrictFilePermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
        }
    }
}
