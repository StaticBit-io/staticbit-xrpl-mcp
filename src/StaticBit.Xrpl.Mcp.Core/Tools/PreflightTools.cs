using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;

namespace StaticBit.Xrpl.Mcp.Core.Tools;

/// <summary>
/// Pre-flight + dry-run MCP tools. Inspect a transaction's feasibility BEFORE it touches
/// the ledger so callers can give the user a clean, specific reason if it will fail.
/// </summary>
[McpServerToolType]
public sealed class PreflightTools
{
    private readonly XrplClientPool _pool;

    public PreflightTools(XrplClientPool pool)
    {
        _pool = pool;
    }

    [McpServerTool(Name = "xrpl_tx_preflight")]
    [Description("Read-only pre-flight check. Inspects the sender (account_info + server_state) and, for Payment, the destination, to surface common gotchas BEFORE signing/submitting: insufficient balance, insufficient reserve after Amount+Fee, RequireDestinationTag without DestinationTag, DepositAuth without preauth, DisallowIncomingXRP. Pass the prepared transaction as txJson (from a *_prepare tool). Returns a JSON report — { feasible, balanceXrp, reserveXrp, freeXrp, requiredXrp, sourceFlags, destinationFlags, warnings[] }. NOT a guarantee — does not check path liquidity or AMM state.")]
    public async Task<string> PreflightAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Transaction JSON string (e.g. the TxJson field from a *_prepare result).")] string txJson,
        CancellationToken cancellationToken = default)
    {
        JsonNode tx = ParseTxJson(txJson);
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        PreflightReport report = await BuildReportAsync(client, tx, cancellationToken).ConfigureAwait(false);
        return report.ToJson().ToJsonString();
    }

    [McpServerTool(Name = "xrpl_tx_simulate")]
    [Description("Dry-run a transaction without submitting. Runs xrpl_tx_preflight plus type-specific checks: for Payment, calls ripple_path_find to confirm a path exists for cross-currency / token deliveries and includes the first alternative's source_amount + paths_computed. Returns { preflight, suggestedPathfind, recommendedFee, recommendedLastLedgerSequence, warnings[] }. Useful for showing the user a realistic 'what will happen' before they approve a signature.")]
    public async Task<string> SimulateAsync(
        [Description(ToolDescriptions.Network)] string network,
        [Description("Transaction JSON string.")] string txJson,
        CancellationToken cancellationToken = default)
    {
        JsonNode tx = ParseTxJson(txJson);
        IXrplClient client = await _pool.GetAsync(new NetworkRef(network), cancellationToken).ConfigureAwait(false);

        PreflightReport preflight = await BuildReportAsync(client, tx, cancellationToken).ConfigureAwait(false);

        JsonNode? suggestedPath = null;
        string txType = ReadString(tx, "TransactionType") ?? "";
        if (string.Equals(txType, "Payment", StringComparison.Ordinal))
        {
            JsonNode? amountNode = tx["Amount"];
            JsonObject? amountAsObject = amountNode as JsonObject;
            bool isCrossCurrency = amountAsObject is not null;

            if (isCrossCurrency)
            {
                try
                {
                    Currency parsedAmount = ParseCurrencyFromNode(amountNode!);
                    string? sourceAccount = ReadString(tx, "Account");
                    string? destination = ReadString(tx, "Destination");
                    if (!string.IsNullOrEmpty(sourceAccount) && !string.IsNullOrEmpty(destination))
                    {
                        RipplePathFindRequest req = new RipplePathFindRequest(sourceAccount, destination, parsedAmount);
                        RipplePathFindResponse resp = await client.RipplePathFind(req, cancellationToken).ConfigureAwait(false);
                        suggestedPath = JsonNode.Parse(XrplJson.Serialize(resp));
                        if (resp.Alternatives is null || resp.Alternatives.Count == 0)
                        {
                            preflight.Warnings.Add("ripple_path_find returned no alternatives — no liquidity path found for this Payment.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    preflight.Warnings.Add("ripple_path_find failed: " + ex.Message);
                }
            }
        }

        Fee feeResp;
        try
        {
            feeResp = await client.Fee(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            preflight.Warnings.Add("fee command failed: " + ex.Message);
            feeResp = null!;
        }

        string? recommendedFee = ReadFeeDrops(feeResp);

        uint currentValidatedLedger = 0;
        try
        {
            currentValidatedLedger = await client.GetLedgerIndex(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            preflight.Warnings.Add("ledger index lookup failed: " + ex.Message);
        }

        JsonObject result = new JsonObject
        {
            ["preflight"] = preflight.ToJson(),
            ["recommendedFee"] = recommendedFee,
            ["recommendedLastLedgerSequence"] = currentValidatedLedger == 0 ? null : (JsonNode)(currentValidatedLedger + 20u),
            ["suggestedPathfind"] = suggestedPath,
        };
        return result.ToJsonString();
    }

    private async Task<PreflightReport> BuildReportAsync(IXrplClient client, JsonNode tx, CancellationToken cancellationToken)
    {
        PreflightReport report = new PreflightReport();

        string? account = ReadString(tx, "Account");
        if (string.IsNullOrEmpty(account))
        {
            report.Warnings.Add("tx is missing Account.");
            report.Feasible = false;
            return report;
        }

        AccountInfo sourceInfo;
        try
        {
            sourceInfo = await client
                .AccountInfo(new AccountInfoRequest(account), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            report.Warnings.Add("account_info(source) failed: " + ex.Message);
            report.Feasible = false;
            return report;
        }

        report.SourceAccount = account;
        report.BalanceDrops = TryParseDrops(sourceInfo.AccountData?.Balance?.Value);
        report.OwnerCount = sourceInfo.AccountData?.OwnerCount ?? 0;
        report.CurrentSequence = sourceInfo.AccountData?.Sequence ?? 0;
        report.SourceFlags = ExtractSourceFlags(sourceInfo.AccountFlags);

        // Reserves come from the network — they shift across amendments, so always fetch.
        try
        {
            ServerState serverState = await client
                .ServerState(new ServerStateRequest(), cancellationToken)
                .ConfigureAwait(false);
            report.ReserveBaseXrp = serverState.State?.ValidatedLedger?.ReserveBase ?? 0u;
            report.ReserveIncXrp = serverState.State?.ValidatedLedger?.ReserveInc ?? 0u;
        }
        catch (Exception ex)
        {
            report.Warnings.Add("server_state failed (reserves unknown): " + ex.Message);
        }

        long reserveDrops = ((long)report.ReserveBaseXrp + (long)report.OwnerCount * report.ReserveIncXrp) * 1_000_000L;
        report.ReserveDrops = reserveDrops;
        report.FreeDrops = Math.Max(0L, report.BalanceDrops - reserveDrops);

        long feeDrops = TryParseDrops(ReadString(tx, "Fee"));
        long amountDrops = 0L;
        if (tx["Amount"] is JsonValue av && av.TryGetValue<string>(out string? amountStr))
        {
            amountDrops = TryParseDrops(amountStr);
        }
        report.RequiredDrops = feeDrops + amountDrops + reserveDrops;

        if (report.BalanceDrops < feeDrops + amountDrops + reserveDrops)
        {
            report.Warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"Balance ({report.BalanceDrops} drops) is below required ({report.RequiredDrops} drops = amount {amountDrops} + fee {feeDrops} + reserve {reserveDrops})."));
            report.Feasible = false;
        }

        // Sequence sanity — Sequence in the tx should match the account's next valid sequence.
        uint? txSeq = ReadUInt(tx, "Sequence");
        if (txSeq.HasValue && report.CurrentSequence > 0 && txSeq.Value != report.CurrentSequence)
        {
            report.Warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"Tx Sequence={txSeq.Value} does not match account's current Sequence={report.CurrentSequence}; rebuild via *_prepare to autofill."));
        }

        // Payment-specific destination checks.
        string txType = ReadString(tx, "TransactionType") ?? "";
        if (string.Equals(txType, "Payment", StringComparison.Ordinal))
        {
            string? destination = ReadString(tx, "Destination");
            if (!string.IsNullOrEmpty(destination))
            {
                report.Destination = destination;
                AccountInfo? destInfo = null;
                try
                {
                    destInfo = await client
                        .AccountInfo(new AccountInfoRequest(destination), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    report.Warnings.Add("account_info(destination) failed: " + ex.Message);
                }

                if (destInfo is not null)
                {
                    Dictionary<string, bool> destFlags = ExtractSourceFlags(destInfo.AccountFlags);
                    report.DestinationFlags = destFlags;

                    bool requireDestTag = destInfo.AccountFlags?.RequireDestinationTag ?? false;
                    if (requireDestTag && ReadUInt(tx, "DestinationTag") is null)
                    {
                        report.Warnings.Add("Destination has RequireDestinationTag enabled but DestinationTag is missing.");
                        report.Feasible = false;
                    }

                    if (destFlags.TryGetValue("depositAuth", out bool da) && da)
                    {
                        report.Warnings.Add("Destination has DepositAuth enabled; without a matching DepositPreauth this Payment will fail with tecNO_PERMISSION.");
                    }

                    if (destFlags.TryGetValue("disallowIncomingXRP", out bool dx) && dx
                        && tx["Amount"] is JsonValue)
                    {
                        report.Warnings.Add("Destination has DisallowIncomingXRP set (advisory; not enforced by ledger but discouraged).");
                    }
                }
            }
        }

        return report;
    }

    private static Dictionary<string, bool> ExtractSourceFlags(AccountInfoAccountFlags? flags)
    {
        Dictionary<string, bool> result = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (flags is null) return result;
        result["defaultRipple"] = flags.DefaultRipple;
        result["depositAuth"] = flags.DepositAuth;
        result["disableMasterKey"] = flags.DisableMasterKey;
        if (flags.DisallowIncomingCheck.HasValue) result["disallowIncomingCheck"] = flags.DisallowIncomingCheck.Value;
        if (flags.DisallowIncomingNFTokenOffer.HasValue) result["disallowIncomingNFTokenOffer"] = flags.DisallowIncomingNFTokenOffer.Value;
        if (flags.DisallowIncomingPayChan.HasValue) result["disallowIncomingPayChan"] = flags.DisallowIncomingPayChan.Value;
        if (flags.DisallowIncomingTrustline.HasValue) result["disallowIncomingTrustline"] = flags.DisallowIncomingTrustline.Value;
        result["disallowIncomingXRP"] = flags.DisallowIncomingXRP;
        result["globalFreeze"] = flags.GlobalFreeze;
        result["noFreeze"] = flags.NoFreeze;
        result["passwordSpent"] = flags.PasswordSpent;
        result["requireAuthorization"] = flags.RequireAuthorization;
        result["requireDestinationTag"] = flags.RequireDestinationTag;
        return result;
    }

    private static Currency ParseCurrencyFromNode(JsonNode node)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out string? s))
        {
            return CurrencyParser.Parse(s);
        }
        if (node is JsonObject)
        {
            return CurrencyParser.Parse(node.ToJsonString());
        }
        throw new ArgumentException("Unsupported Amount node shape.");
    }

    private static string? ReadFeeDrops(Fee? fee)
    {
        if (fee is null) return null;
        // Fee response has Drops.OpenLedger (string). The exact shape lives in Xrpl SDK;
        // serialize the whole thing and let the caller introspect — keep it forward-compatible.
        string serialized = XrplJson.Serialize(fee);
        try
        {
            JsonNode? root = JsonNode.Parse(serialized);
            if (root is null) return null;
            string? open = root["drops"]?["open_ledger_fee"]?.GetValue<string>()
                           ?? root["Drops"]?["OpenLedgerFee"]?.GetValue<string>();
            return open;
        }
        catch
        {
            return null;
        }
    }

    private static JsonNode ParseTxJson(string txJson)
    {
        if (string.IsNullOrWhiteSpace(txJson))
        {
            throw new ArgumentException("txJson is required.", nameof(txJson));
        }
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(txJson);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("txJson is not valid JSON: " + ex.Message, nameof(txJson), ex);
        }
        if (parsed is not JsonObject)
        {
            throw new ArgumentException("txJson must be a JSON object.", nameof(txJson));
        }
        return parsed;
    }

    private static long TryParseDrops(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0L;
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0L;
    }

    private static string? ReadString(JsonNode tx, string key)
    {
        if (tx[key] is JsonValue v && v.TryGetValue<string>(out string? s)) return s;
        return null;
    }

    private static uint? ReadUInt(JsonNode tx, string key)
    {
        JsonNode? n = tx[key];
        if (n is null) return null;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<uint>(out uint u)) return u;
            if (v.TryGetValue<long>(out long l) && l >= 0 && l <= uint.MaxValue) return (uint)l;
            if (v.TryGetValue<string>(out string? s) && uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint p))
            {
                return p;
            }
        }
        return null;
    }
}

/// <summary>
/// Structured pre-flight result. Serializable to JSON via <see cref="ToJson"/>.
/// </summary>
internal sealed class PreflightReport
{
    public bool Feasible { get; set; } = true;
    public string? SourceAccount { get; set; }
    public string? Destination { get; set; }
    public long BalanceDrops { get; set; }
    public uint OwnerCount { get; set; }
    public uint CurrentSequence { get; set; }
    public uint ReserveBaseXrp { get; set; }
    public uint ReserveIncXrp { get; set; }
    public long ReserveDrops { get; set; }
    public long FreeDrops { get; set; }
    public long RequiredDrops { get; set; }
    public Dictionary<string, bool> SourceFlags { get; set; } = new Dictionary<string, bool>(StringComparer.Ordinal);
    public Dictionary<string, bool> DestinationFlags { get; set; } = new Dictionary<string, bool>(StringComparer.Ordinal);
    public List<string> Warnings { get; set; } = new List<string>();

    public JsonObject ToJson()
    {
        JsonObject obj = new JsonObject
        {
            ["feasible"] = Feasible,
            ["sourceAccount"] = SourceAccount,
            ["destination"] = Destination,
            ["balanceDrops"] = BalanceDrops,
            ["balanceXrp"] = DropsToXrp(BalanceDrops),
            ["ownerCount"] = OwnerCount,
            ["currentSequence"] = CurrentSequence,
            ["reserveBaseXrp"] = ReserveBaseXrp,
            ["reserveIncXrp"] = ReserveIncXrp,
            ["reserveDrops"] = ReserveDrops,
            ["reserveXrp"] = DropsToXrp(ReserveDrops),
            ["freeDrops"] = FreeDrops,
            ["freeXrp"] = DropsToXrp(FreeDrops),
            ["requiredDrops"] = RequiredDrops,
            ["requiredXrp"] = DropsToXrp(RequiredDrops),
        };

        JsonObject sourceFlags = new JsonObject();
        foreach (KeyValuePair<string, bool> kvp in SourceFlags) sourceFlags[kvp.Key] = kvp.Value;
        obj["sourceFlags"] = sourceFlags;

        JsonObject destFlags = new JsonObject();
        foreach (KeyValuePair<string, bool> kvp in DestinationFlags) destFlags[kvp.Key] = kvp.Value;
        obj["destinationFlags"] = destFlags;

        JsonArray warnings = new JsonArray();
        foreach (string w in Warnings) warnings.Add(w);
        obj["warnings"] = warnings;

        return obj;
    }

    private static string DropsToXrp(long drops) =>
        (drops / 1_000_000m).ToString("0.######", CultureInfo.InvariantCulture);
}
