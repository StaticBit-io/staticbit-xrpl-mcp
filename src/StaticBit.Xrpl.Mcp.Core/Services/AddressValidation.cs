using System;
using Xrpl.AddressCodec;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Centralised XRPL address validation. Tools should call <see cref="AssertValid"/>
/// before passing an address into SDK calls, otherwise the SDK throws
/// <c>EncodingFormatException</c> at decode time with a parameter-less message
/// ("Checksum does not validate"), which surfaces to the MCP client as a generic
/// "An error occurred invoking ..." with no hint at which input was malformed.
/// </summary>
public static class AddressValidation
{
    /// <summary>
    /// Returns true when <paramref name="address"/> is a syntactically valid
    /// XRPL classic address (starts with 'r', passes base58check).
    /// </summary>
    public static bool IsValid(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        try
        {
            return XrplCodec.IsValidClassicAddress(address);
        }
        catch
        {
            // SDK occasionally throws on malformed input rather than returning
            // false. Treat any exception as "invalid".
            return false;
        }
    }

    /// <summary>
    /// Throws an MCP-visible error (via <see cref="XrplToolError"/>) when
    /// <paramref name="address"/> is missing or fails base58check. Use at
    /// the top of every tool method that accepts an address before
    /// forwarding into the SDK. The thrown McpException's message carries
    /// the classified JSON envelope that AI agents can introspect.
    /// </summary>
    public static void AssertValid(string? address, string paramName)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            XrplToolError.ThrowInvalidInput(
                $"{paramName} is required (XRPL classic address starting with 'r').",
                paramName);
        }

        if (!IsValid(address))
        {
            XrplToolError.ThrowInvalidInput(
                $"{paramName} is not a valid XRPL classic address (base58check failed): '{address}'.",
                paramName);
        }
    }
}
