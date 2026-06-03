using Mcp.Auth.ResourceServer;
using Microsoft.Extensions.Configuration;
using StaticBit.Xrpl.Mcp.Core.Options;
using StaticBit.Xrpl.Mcp.Server.Configuration;

namespace StaticBit.Xrpl.Mcp.Server.Tests;

/// <summary>
/// Regression guard for environment-variable → strongly-typed options binding in the
/// XRPL cloud server. <c>WebApplication.CreateBuilder(args)</c> wires <c>appsettings.json</c>,
/// per-environment overrides, command-line args, and <c>AddEnvironmentVariables()</c>
/// (no prefix). Asserts every documented env-var form actually reaches its <c>TOptions</c>
/// property.
///
/// History: the cross-repo doc audit (2026-05-30) found that <c>x-mcp</c>'s pipeline used
/// <c>AddEnvironmentVariables(prefix:"XMCP_")</c> and recommended <c>XMCP_ENCRYPTION_KEY</c>
/// in docs — but after the prefix strip the var became the root key <c>ENCRYPTION_KEY</c>,
/// leaving <c>X:EncryptionKey</c> empty. Users got opaque "X:EncryptionKey is empty" at the
/// first OAuth request, having done exactly what the README said. This test class makes
/// the equivalent failure mode unrepresentable here: every documented form is exercised,
/// and a raw form pinned as not binding.
/// </summary>
[TestClass]
[DoNotParallelize] // Process-global env vars; parallel methods would tangle.
public class EnvBindingTestsU
{
    /// <summary>Recreate the env-var contribution of <c>WebApplication.CreateBuilder</c>.</summary>
    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder().AddEnvironmentVariables().Build();

    private static T BindSection<T>(IConfiguration cfg, string section) where T : new() =>
        cfg.GetSection(section).Get<T>() ?? new T();

    // ---------------- Server section ----------------

    [TestMethod]
    public void TestU_Server__Transport_Binds()
    {
        using EnvVarScope _ = EnvVarScope.Set("Server__Transport", "http");

        Assert.AreEqual("http", BindSection<ServerOptions>(BuildConfig(), ServerOptions.SectionName).Transport);
    }

    [TestMethod]
    public void TestU_Server__HttpPort_Binds_AsInt()
    {
        using EnvVarScope _ = EnvVarScope.Set("Server__HttpPort", "5500");

        Assert.AreEqual(5500, BindSection<ServerOptions>(BuildConfig(), ServerOptions.SectionName).HttpPort);
    }

    // ---------------- StaticBitXrplMcp section ----------------

    [TestMethod]
    public void TestU_StaticBitXrplMcp__DefaultNetwork_Binds()
    {
        using EnvVarScope _ = EnvVarScope.Set("StaticBitXrplMcp__DefaultNetwork", "testnet");

        Assert.AreEqual("testnet", BindSection<XrplMcpOptions>(BuildConfig(), XrplMcpOptions.SectionName).DefaultNetwork);
    }

    [TestMethod]
    public void TestU_StaticBitXrplMcp__Networks_DictionaryBinds()
    {
        using EnvVarScope _1 = EnvVarScope.Set("StaticBitXrplMcp__Networks__custom", "wss://my-rippled.example.com");
        using EnvVarScope _2 = EnvVarScope.Set("StaticBitXrplMcp__Networks__testnet", "wss://other.test.example.com");

        XrplMcpOptions opts = BindSection<XrplMcpOptions>(BuildConfig(), XrplMcpOptions.SectionName);
        Assert.AreEqual("wss://my-rippled.example.com", opts.Networks["custom"]);
        Assert.AreEqual("wss://other.test.example.com", opts.Networks["testnet"]);
    }

    [TestMethod]
    public void TestU_StaticBitXrplMcp__RequestTimeoutSeconds_BindsAsInt()
    {
        using EnvVarScope _ = EnvVarScope.Set("StaticBitXrplMcp__RequestTimeoutSeconds", "60");

        Assert.AreEqual(60, BindSection<XrplMcpOptions>(BuildConfig(), XrplMcpOptions.SectionName).RequestTimeoutSeconds);
    }

    [TestMethod]
    public void TestU_StaticBitXrplMcp__FeeBumpMultiplier_BindsAsDecimal()
    {
        using EnvVarScope _ = EnvVarScope.Set("StaticBitXrplMcp__FeeBumpMultiplier", "1.5");

        Assert.AreEqual(1.5m, BindSection<XrplMcpOptions>(BuildConfig(), XrplMcpOptions.SectionName).FeeBumpMultiplier);
    }

    // ---------------- OAuth section (Mcp.Auth.ResourceServer SDK) ----------------

    [DataTestMethod]
    [DataRow("OAuth__Issuer",            "https://auth.mcp.staticbit.ai",       "Issuer")]
    [DataRow("OAuth__Resource",          "https://xrpl.mcp.staticbit.ai/mcp",   "Resource")]
    [DataRow("OAuth__RequiredScope",     "xrpl",                                "RequiredScope")]
    [DataRow("OAuth__VaultBaseUrl",      "http://staticbit-mcp-auth:8080",      "VaultBaseUrl")]
    [DataRow("OAuth__VaultServiceToken", "internal-shared-secret-with-as",      "VaultServiceToken")]
    public void TestU_OAuth__Fields_Bind(string envVar, string expected, string property)
    {
        using EnvVarScope _ = EnvVarScope.Set(envVar, expected);

        McpResourceServerOptions opts = BindSection<McpResourceServerOptions>(
            BuildConfig(), McpResourceServerOptions.SectionName);
        string? actual = property switch
        {
            "Issuer"            => opts.Issuer,
            "Resource"          => opts.Resource,
            "RequiredScope"     => opts.RequiredScope,
            "VaultBaseUrl"      => opts.VaultBaseUrl,
            "VaultServiceToken" => opts.VaultServiceToken,
            _ => throw new InvalidOperationException($"Untested property: {property}")
        };
        Assert.AreEqual(expected, actual);
    }

    // ---------------- Negative regression guard ----------------

    /// <summary>
    /// The pipeline has no prefix-stripping provider — so <c>XRPL_MCP_DEFAULT_NETWORK</c>
    /// (shout-case raw form) MUST NOT bind to <c>StaticBitXrplMcp:DefaultNetwork</c>.
    /// If this starts failing the pipeline gained an extra mapping that needs intentional review.
    /// </summary>
    [TestMethod]
    public void TestU_RawShoutCaseForm_DoesNotBind()
    {
        using EnvVarScope _ = EnvVarScope.Set("XRPL_MCP_DEFAULT_NETWORK", "must-not-leak-into-XrplMcpOptions");

        // DefaultNetwork should keep its property default ("mainnet").
        Assert.AreEqual("mainnet", BindSection<XrplMcpOptions>(BuildConfig(), XrplMcpOptions.SectionName).DefaultNetwork);
    }

    /// <summary>
    /// Disposable RAII helper: sets an env-var on enter, restores the prior value on
    /// dispose. MSTest runs tests in parallel within an assembly when configured, and
    /// process-global env-vars would tangle without restoration.
    /// </summary>
    private sealed class EnvVarScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _prior;

        private EnvVarScope(string name, string? prior) { _name = name; _prior = prior; }

        public static EnvVarScope Set(string name, string? value)
        {
            string? prior = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
            return new EnvVarScope(name, prior);
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _prior);
    }
}
