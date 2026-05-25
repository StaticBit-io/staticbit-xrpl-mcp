using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Options;
using StaticBit.Xrpl.Mcp.Core.Services;
using StaticBit.Xrpl.Mcp.Core.Tools;
using Xrpl.Client;

namespace StaticBit.Xrpl.Mcp.Core.DependencyInjection;

/// <summary>
/// DI integration for StaticBit XRPL MCP services. Designed to be called from any
/// host (the StaticBit.Xrpl.Mcp.Server CLI, the Staticbit Wallet, or a test fixture).
/// </summary>
public static class XrplMcpServiceCollectionExtensions
{
    public static IServiceCollection AddStaticBitXrplMcp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        services
            .AddOptions<XrplMcpOptions>()
            .Bind(configuration.GetSection(XrplMcpOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<NetworkResolver>();
        services.AddSingleton<XrplClientPool>();
        services.AddSingleton<IXrplClientPool<IXrplClient>>(sp => sp.GetRequiredService<XrplClientPool>());
        services.AddSingleton<TransactionPreparer>();

        // Tool classes — instance-resolved by the MCP runtime via IServiceProvider.
        services.AddTransient<LedgerTools>();
        services.AddTransient<AccountTools>();
        services.AddTransient<DexTools>();
        services.AddTransient<AmmTools>();
        services.AddTransient<PaymentTools>();
        services.AddTransient<OfferTools>();
        services.AddTransient<AmmTransactionTools>();
        services.AddTransient<TransactionTools>();
        services.AddTransient<AccountManagementTools>();
        services.AddTransient<NftTools>();
        services.AddTransient<EscrowTools>();
        services.AddTransient<PaymentChannelTools>();
        services.AddTransient<CheckTools>();
        services.AddTransient<AmmManagementTools>();
        services.AddTransient<IssuerTools>();

        return services;
    }
}
