using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StaticBit.Xrpl.Mcp.Signer.Configuration;
using StaticBit.Xrpl.Mcp.Signer.Keystore;
using StaticBit.Xrpl.Mcp.Signer.Tools;

namespace StaticBit.Xrpl.Mcp.Signer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        SignerOptions options = SignerOptions.ResolveFromEnvironment();

        if (string.IsNullOrEmpty(options.Passphrase))
        {
            await Console.Error.WriteLineAsync(
                "FATAL: keystore passphrase is not configured.").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(
                "Set XRPL_SIGNER_PASSPHRASE in the environment, or point ").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(
                "XRPL_SIGNER_PASSPHRASE_FILE at a file (mode 0600) whose first line is the passphrase.").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(
                "The signer is stdio-driven and cannot prompt interactively — stdin is the MCP channel.").ConfigureAwait(false);
            return 2;
        }

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // STDIO is the protocol channel. All log output MUST go to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IKeystore, EncryptedFileKeystore>();
        builder.Services.AddTransient<WalletTools>();
        builder.Services.AddTransient<SignTools>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(WalletTools).Assembly);

        IHost host = builder.Build();

        ILogger startupLog = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("StaticBitXrplMcp.Signer");

        // Touch the keystore once so any I/O / parsing problem is reported up-front,
        // not on the first MCP call. The list is intentionally not logged (would
        // leak addresses to stderr, which may be aggregated by the host).
        try
        {
            IKeystore keystore = host.Services.GetRequiredService<IKeystore>();
            int count = keystore.List().Count;
            startupLog.LogInformation(
                "StaticBitXrplMcp.Signer stdio ready. Keystore at {Path}, wallets={Count}.",
                options.KeystorePath, count);
        }
        catch (Exception ex)
        {
            startupLog.LogError(ex, "Keystore initialization failed.");
            return 3;
        }

        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }
}
