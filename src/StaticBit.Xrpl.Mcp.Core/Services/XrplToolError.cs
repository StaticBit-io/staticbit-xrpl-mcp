using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol;
using Xrpl.Client.Exceptions;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Bridge between the upstream <see cref="XrplErrorClassifier"/> and the MCP
/// transport. Wraps any thrown exception (SDK <c>RippledException</c>, local
/// <c>ArgumentException</c>, anything else) into a structured JSON payload
/// and re-throws it as <see cref="McpException"/>, which the MCP SDK propagates
/// to the client as the visible tool-call error message (per SDK contract).
///
/// AI agents see a normalised, machine-readable error envelope:
/// <code>
/// {
///   "category": "InvalidInput",
///   "subject":  "Account",
///   "title":    "Incorrect address",
///   "message":  "subject is not a valid XRPL classic address...",
///   "fieldName":  "subject",
///   "fieldValue": "rfBKzgT2...",
///   "command":   null,
///   "isRetryable":  false,
///   "isUserFixable": true,
///   "rawError":     "ArgumentException",
///   "rawErrorCode": null,
///   "warnings":     []
/// }
/// </code>
/// </summary>
public static class XrplToolError
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        // Skip nulls so the payload is compact for the LLM context window.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    // rippled reports failures on the wire as "<code> - <human message>" (e.g.
    // "noPermission - You don't have permission for this command."). Some SDK paths throw an
    // untyped XrplException instead of a typed RippledException, so the upstream classifier
    // can't structurally recognise them — this pattern recovers the machine-readable code.
    private static readonly Regex RippledCodePattern = new Regex(
        @"^(?<code>[A-Za-z][A-Za-z0-9_]*) - .+",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Codes that mean "this request is not available on this node" — the one rippled bucket we
    // can map to a precise category with confidence. Everything else keeps a neutral category
    // but still gets its code surfaced.
    private static readonly HashSet<string> UnavailableCommandCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "noPermission", "unknownCmd", "notSupported", "notEnabled", "unimplemented", "amendmentBlocked",
    };

    /// <summary>
    /// Classifies <paramref name="exception"/> via <see cref="XrplErrorClassifier"/>
    /// and returns a JSON-serialised error envelope. Local-side exceptions that
    /// the upstream classifier would otherwise mark as <c>Unknown / internal
    /// error</c> are re-mapped to user-facing categories when their type is
    /// known (e.g. ArgumentException → InvalidInput).
    /// </summary>
    public static string SerializeError(Exception exception)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        XrplErrorInfo info = exception.Classify();
        info = Refine(info, exception);

        return JsonSerializer.Serialize(new
        {
            category = info.Category.ToString(),
            subject = info.Subject.ToString(),
            title = info.Title,
            message = info.UserMessage,
            fieldName = info.FieldName,
            fieldValue = info.FieldValue,
            command = info.Command,
            isRetryable = info.IsRetryable,
            isUserFixable = info.IsUserFixable,
            rawError = info.RawError,
            rawErrorCode = info.RawErrorCode,
            warnings = info.Warnings,
        }, JsonOptions);
    }

    private static XrplErrorInfo Refine(XrplErrorInfo info, Exception exception)
    {
        // The upstream classifier defaults non-RippledException paths to
        // Category=Unknown/Subject=Unknown/"internal error". Override that for
        // exception types whose semantics we know — gives AI agents a stable
        // category signal even for client-side validation failures.
        if (info.Category != XrplErrorCategory.Unknown)
        {
            return info;
        }

        if (exception is ArgumentException argEx)
        {
            return new XrplErrorInfo
            {
                RawError = info.RawError,
                RawErrorCode = info.RawErrorCode,
                RawErrorMessage = info.RawErrorMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Request,
                Title = "Invalid input",
                UserMessage = argEx.Message,
                IsRetryable = false,
                IsUserFixable = true,
                Command = info.Command,
                FieldName = string.IsNullOrEmpty(argEx.ParamName) ? info.FieldName : argEx.ParamName,
                FieldValue = info.FieldValue,
                Warnings = info.Warnings,
            };
        }

        if (exception is System.TimeoutException or OperationCanceledException)
        {
            return new XrplErrorInfo
            {
                RawError = info.RawError,
                RawErrorCode = info.RawErrorCode,
                RawErrorMessage = info.RawErrorMessage,
                Category = XrplErrorCategory.TemporaryServerProblem,
                Subject = XrplErrorSubject.Server,
                Title = "Operation timed out",
                UserMessage = exception.Message,
                IsRetryable = true,
                IsUserFixable = false,
                Command = info.Command,
                Warnings = info.Warnings,
            };
        }

        // Transport-level failures — the WebSocket/JSON-RPC link to the rippled node
        // dropped, refused, or reset. The upstream classifier has no concept of these
        // .NET socket types, so without this branch they collapse to Unknown/"internal
        // error" and the agent can't tell a network blip from a real bug.
        if (exception is WebSocketException or SocketException or IOException or HttpRequestException)
        {
            return new XrplErrorInfo
            {
                RawError = info.RawError,
                RawErrorCode = info.RawErrorCode,
                RawErrorMessage = info.RawErrorMessage,
                Category = XrplErrorCategory.TemporaryServerProblem,
                Subject = XrplErrorSubject.Server,
                Title = "Connection problem",
                UserMessage = exception.Message,
                IsRetryable = true,
                IsUserFixable = false,
                Command = info.Command,
                Warnings = info.Warnings,
            };
        }

        // Untyped rippled failure (e.g. XrplException from ripple_path_find): recover the wire
        // error code so the agent gets a structured error_code instead of "internal error" prose.
        if (exception.GetType().Namespace is { } ns && ns.StartsWith("Xrpl", StringComparison.Ordinal))
        {
            string message = string.IsNullOrEmpty(info.UserMessage) ? (exception.Message ?? string.Empty) : info.UserMessage;
            Match match = RippledCodePattern.Match(message);
            if (match.Success)
            {
                string code = match.Groups["code"].Value;
                bool unavailable = UnavailableCommandCodes.Contains(code);
                return new XrplErrorInfo
                {
                    // RawError carries the rippled error string (e.g. "noPermission"); the numeric
                    // RawErrorCode is left as the classifier found it (usually absent here).
                    RawError = code,
                    RawErrorCode = info.RawErrorCode,
                    RawErrorMessage = info.RawErrorMessage,
                    Category = unavailable ? XrplErrorCategory.UnsupportedRequest : XrplErrorCategory.Unknown,
                    Subject = XrplErrorSubject.Server,
                    Title = unavailable ? "Request not available on this node" : "Rippled error",
                    UserMessage = message,
                    IsRetryable = false,
                    IsUserFixable = unavailable,
                    Command = info.Command,
                    FieldName = info.FieldName,
                    FieldValue = info.FieldValue,
                    Warnings = info.Warnings,
                };
            }
        }

        return info;
    }

    /// <summary>
    /// Throws <see cref="McpException"/> whose <c>Message</c> carries the
    /// classified, JSON-encoded error payload. Catch unhandled exceptions
    /// from a tool body and call this to produce a clean client-facing
    /// error response. If <paramref name="exception"/> is already an
    /// <see cref="McpException"/> (e.g. it bubbled up from an inner
    /// validator that already produced the envelope) it is rethrown
    /// unchanged — no double-wrapping.
    /// </summary>
    public static void ThrowMcp(Exception exception)
    {
        if (exception is McpException)
        {
            throw exception;
        }

        string payload = SerializeError(exception);
        throw new McpException(payload, exception);
    }

    /// <summary>
    /// Throws an MCP-visible "invalid input" error with a custom message —
    /// shortcut for argument validation that should reach the client.
    /// </summary>
    public static void ThrowInvalidInput(string message, string? paramName = null)
    {
        ArgumentException argEx = paramName != null
            ? new ArgumentException(message, paramName)
            : new ArgumentException(message);
        ThrowMcp(argEx);
    }
}
