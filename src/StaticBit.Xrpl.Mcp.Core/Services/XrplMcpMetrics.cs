using System;
using System.Diagnostics.Metrics;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Shared <see cref="System.Diagnostics.Metrics.Meter"/> for the XRPL MCP stack.
/// Exposes counters / histograms / observable gauges that the hosting process
/// (cloud server or local stdio) can wire up to OpenTelemetry / Prometheus —
/// the meter name is stable across hosts.
///
/// Metric reference (Prometheus naming after exporter normalisation):
/// <list type="bullet">
/// <item><c>xrpl_mcp_pool_connections{network}</c> — gauge: current open WebSocket count per network.</item>
/// <item><c>xrpl_mcp_pool_reconnects_total{network,reason}</c> — counter: how many times we re-established a connection.</item>
/// <item><c>xrpl_mcp_pool_connect_duration_seconds{network}</c> — histogram: time to connect, including TLS+WS handshake.</item>
/// <item><c>xrpl_mcp_tool_calls_total{tool,status}</c> — counter: how many invocations of each MCP tool. Wired by hosts that intercept tool dispatch.</item>
/// <item><c>xrpl_mcp_tool_duration_seconds{tool}</c> — histogram: tool execution time. Same.</item>
/// </list>
/// </summary>
public sealed class XrplMcpMetrics
{
    /// <summary>
    /// Meter name — fixed so Prometheus exporter / OTel pipelines can target it without hardcoding versions.
    /// </summary>
    public const string MeterName = "StaticBitXrplMcp";

    public Meter Meter { get; }

    public Counter<long> PoolReconnects { get; }
    public Histogram<double> PoolConnectDurationSeconds { get; }
    public Counter<long> ToolCalls { get; }
    public Histogram<double> ToolDurationSeconds { get; }

    public XrplMcpMetrics()
    {
        Meter = new Meter(MeterName, version: "1.0.0");

        PoolReconnects = Meter.CreateCounter<long>(
            name: "xrpl_mcp_pool_reconnects_total",
            unit: "1",
            description: "Number of times a pooled XRPL WebSocket was (re)established. Tagged with reason: cold | dropped | ttl | error.");

        PoolConnectDurationSeconds = Meter.CreateHistogram<double>(
            name: "xrpl_mcp_pool_connect_duration_seconds",
            unit: "s",
            description: "Time spent establishing a new XRPL WebSocket, including TLS + handshake.");

        ToolCalls = Meter.CreateCounter<long>(
            name: "xrpl_mcp_tool_calls_total",
            unit: "1",
            description: "Number of MCP-tool invocations, tagged by tool name and status (ok | error).");

        ToolDurationSeconds = Meter.CreateHistogram<double>(
            name: "xrpl_mcp_tool_duration_seconds",
            unit: "s",
            description: "Wall-clock duration of MCP-tool invocations.");
    }
}
