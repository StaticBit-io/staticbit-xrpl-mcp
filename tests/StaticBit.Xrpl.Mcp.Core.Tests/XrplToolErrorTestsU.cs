using System;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using ModelContextProtocol;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client.Exceptions;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class XrplToolErrorTestsU
{
    private static JsonElement Envelope(Exception ex)
        => JsonDocument.Parse(XrplToolError.SerializeError(ex)).RootElement;

    private static string Category(Exception ex) => Envelope(ex).GetProperty("category").GetString()!;

    // The rippled error string (e.g. "noPermission") is surfaced in the "rawError" field.
    private static string? RawError(Exception ex)
        => Envelope(ex).TryGetProperty("rawError", out JsonElement v) ? v.GetString() : null;

    // --- C. Network / transport failures must be typed, not collapsed to "Unknown". ---

    [TestMethod]
    public void TestU_Serialize_WebSocketException_IsTemporaryServerProblem()
    {
        Assert.AreEqual("TemporaryServerProblem", Category(new WebSocketException("The remote party closed the WebSocket connection.")));
    }

    [TestMethod]
    public void TestU_Serialize_SocketException_IsTemporaryServerProblem()
    {
        Assert.AreEqual("TemporaryServerProblem", Category(new SocketException(10054)));
    }

    [TestMethod]
    public void TestU_Serialize_IOException_IsTemporaryServerProblem()
    {
        Assert.AreEqual("TemporaryServerProblem", Category(new IOException("Unable to read data from the transport connection.")));
    }

    // --- B. Input-validation failures must name the offending field. ---

    [TestMethod]
    public void TestU_Serialize_ArgumentException_IsInvalidInput_WithFieldName()
    {
        JsonElement env = Envelope(new ArgumentException("network is required.", "network"));

        Assert.AreEqual("InvalidInput", env.GetProperty("category").GetString());
        Assert.AreEqual("network", env.GetProperty("fieldName").GetString());
        StringAssert.Contains(env.GetProperty("message").GetString(), "network is required");
    }

    // --- D. Machine-readable surface: ThrowMcp routes a real cause to a visible McpException. ---

    [TestMethod]
    public void TestU_ThrowMcp_WrapsArgumentException_AsMcpExceptionCarryingEnvelope()
    {
        McpException ex = Assert.ThrowsExactly<McpException>(
            () => XrplToolError.ThrowMcp(new ArgumentException("issuer is required.", "issuer")));

        // The McpException message IS the JSON envelope (SDK 1.3.0 surfaces it to the client
        // as "An error occurred invoking '<tool>': <message>").
        JsonElement env = JsonDocument.Parse(ex.Message).RootElement;
        Assert.AreEqual("InvalidInput", env.GetProperty("category").GetString());
        Assert.AreEqual("issuer", env.GetProperty("fieldName").GetString());
    }

    [TestMethod]
    public void TestU_ThrowMcp_DoesNotDoubleWrapExistingMcpException()
    {
        McpException original = new McpException("{\"category\":\"InvalidInput\"}");

        McpException thrown = Assert.ThrowsExactly<McpException>(() => XrplToolError.ThrowMcp(original));

        Assert.AreSame(original, thrown);
    }

    // --- A. rippled error codes: recover error_code from the "<code> - <message>" wire format
    //        even when the SDK throws an untyped XrplException the classifier leaves as Unknown. ---

    [TestMethod]
    public void TestU_Serialize_RippledNoPermission_ExtractsCode_AndTypesUnsupported()
    {
        XrplException ex = new XrplException("noPermission - You don't have permission for this command.");

        Assert.AreEqual("noPermission", RawError(ex));
        Assert.AreEqual("UnsupportedRequest", Category(ex));
        StringAssert.Contains(Envelope(ex).GetProperty("message").GetString(), "noPermission");
    }

    [TestMethod]
    public void TestU_Serialize_RippledUnknownCmd_ExtractsCode_AndTypesUnsupported()
    {
        XrplException ex = new XrplException("unknownCmd - Unknown method.");

        Assert.AreEqual("unknownCmd", RawError(ex));
        Assert.AreEqual("UnsupportedRequest", Category(ex));
    }

    [TestMethod]
    public void TestU_Serialize_RippledOtherCode_StillExtractsCode_LeavesCategoryUnknown()
    {
        // We only confidently type the "command unavailable" bucket; other codes keep a neutral
        // category but MUST still surface the machine-readable code (criterion A).
        XrplException ex = new XrplException("actNotFound - Account not found.");

        Assert.AreEqual("actNotFound", RawError(ex));
        Assert.AreEqual("Unknown", Category(ex));
    }
}
