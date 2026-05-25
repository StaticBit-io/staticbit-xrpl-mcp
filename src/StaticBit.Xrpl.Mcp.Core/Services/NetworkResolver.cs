using System;
using Microsoft.Extensions.Options;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Options;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Resolves a <see cref="NetworkRef"/> into a concrete WebSocket URL using configuration
/// and built-in defaults.
/// </summary>
public sealed class NetworkResolver
{
    private readonly IOptionsMonitor<XrplMcpOptions> _options;

    public NetworkResolver(IOptionsMonitor<XrplMcpOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Resolves the given <see cref="NetworkRef"/>. When <paramref name="network"/> is
    /// <c>null</c> the configured default network is used.
    /// </summary>
    public string Resolve(NetworkRef? network)
    {
        XrplMcpOptions options = _options.CurrentValue;
        NetworkRef effective = network ?? new NetworkRef(options.DefaultNetwork);

        if (effective.IsUrl)
        {
            return NormalizeUrl(effective.Value);
        }

        string key = effective.Value.ToLowerInvariant();

        if (options.Networks.TryGetValue(key, out string? configured) && !string.IsNullOrWhiteSpace(configured))
        {
            return NormalizeUrl(configured);
        }

        if (XrplMcpOptions.DefaultNetworks.TryGetValue(key, out string? builtin))
        {
            return NormalizeUrl(builtin);
        }

        throw new InvalidOperationException(
            $"Unknown XRPL network '{effective.Value}'. Provide a WebSocket URL or configure it in {XrplMcpOptions.SectionName}:Networks.");
    }

    private static string NormalizeUrl(string url)
    {
        string trimmed = url.Trim().TrimEnd('/');

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat("ws://", trimmed.AsSpan(7));
        }

        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat("wss://", trimmed.AsSpan(8));
        }

        return trimmed;
    }
}
