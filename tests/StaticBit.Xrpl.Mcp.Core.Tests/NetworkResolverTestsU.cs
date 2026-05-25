using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Options;
using StaticBit.Xrpl.Mcp.Core.Services;

namespace StaticBit.Xrpl.Mcp.Core.Tests;

[TestClass]
public class NetworkResolverTestsU
{
    private static NetworkResolver CreateSut(XrplMcpOptions? options = null)
    {
        XrplMcpOptions effective = options ?? new XrplMcpOptions();
        return new NetworkResolver(new TestOptionsMonitor(effective));
    }

    [TestMethod]
    public void TestU_Resolve_KnownName_UsesBuiltinDefault()
    {
        NetworkResolver sut = CreateSut();
        string resolved = sut.Resolve(new NetworkRef("mainnet"));
        Assert.AreEqual("wss://xrplcluster.com", resolved);
    }

    [TestMethod]
    public void TestU_Resolve_ConfiguredNetwork_OverridesBuiltin()
    {
        XrplMcpOptions options = new XrplMcpOptions
        {
            Networks = new Dictionary<string, string>
            {
                ["mainnet"] = "wss://my-private-node.example.com:51234",
            },
        };
        NetworkResolver sut = CreateSut(options);

        string resolved = sut.Resolve(new NetworkRef("mainnet"));

        Assert.AreEqual("wss://my-private-node.example.com:51234", resolved);
    }

    [TestMethod]
    public void TestU_Resolve_DirectUrl_IsPreserved()
    {
        NetworkResolver sut = CreateSut();
        string resolved = sut.Resolve(new NetworkRef("wss://custom.example.org:51233"));
        Assert.AreEqual("wss://custom.example.org:51233", resolved);
    }

    [TestMethod]
    public void TestU_Resolve_HttpsUrl_IsConvertedToWss()
    {
        NetworkResolver sut = CreateSut();
        string resolved = sut.Resolve(new NetworkRef("https://node.example.org"));
        Assert.AreEqual("wss://node.example.org", resolved);
    }

    [TestMethod]
    public void TestU_Resolve_NullNetwork_FallsBackToDefault()
    {
        XrplMcpOptions options = new XrplMcpOptions { DefaultNetwork = "testnet" };
        NetworkResolver sut = CreateSut(options);

        string resolved = sut.Resolve(network: null);

        Assert.AreEqual("wss://s.altnet.rippletest.net:51233", resolved);
    }

    [TestMethod]
    public void TestU_Resolve_UnknownName_Throws()
    {
        NetworkResolver sut = CreateSut();
        Assert.Throws<InvalidOperationException>(() => sut.Resolve(new NetworkRef("does-not-exist")));
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<XrplMcpOptions>
    {
        public TestOptionsMonitor(XrplMcpOptions value) => CurrentValue = value;
        public XrplMcpOptions CurrentValue { get; }
        public XrplMcpOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<XrplMcpOptions, string?> listener) => null;
    }
}
