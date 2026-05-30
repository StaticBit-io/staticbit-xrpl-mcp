using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Mcp.Auth.ResourceServer;
using ModelContextProtocol.Server;
using StaticBit.Xrpl.Mcp.Abstractions;
using StaticBit.Xrpl.Mcp.Core.Services;
using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
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
        return UntrustedContent.Wrap(report.ToJson().ToJsonString(), $"xrpl:tx_preflight:{network}");
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
        return UntrustedContent.Wrap(result.ToJsonString(), $"xrpl:tx_simulate:{network}");
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
        else if (txType.StartsWith("MPToken", StringComparison.Ordinal))
        {
            ApplyMptWarnings(report, tx, txType);
        }
        else if (string.Equals(txType, "Batch", StringComparison.Ordinal))
        {
            ApplyBatchWarnings(report, tx);
        }
        else if (string.Equals(txType, "TicketCreate", StringComparison.Ordinal))
        {
            uint? count = ReadUInt(tx, "TicketCount");
            if (count is null || count.Value < 1 || count.Value > 250)
            {
                report.Warnings.Add($"TicketCreate: TicketCount must be 1..250 (got {count?.ToString(CultureInfo.InvariantCulture) ?? "missing"}).");
                report.Feasible = false;
            }
            // The 'must not exceed 250 total Tickets' rule needs account_objects(Ticket); for now we
            // only check the per-tx upper bound — the network will reject the overflow case.
        }
        else if (string.Equals(txType, "OracleSet", StringComparison.Ordinal))
        {
            JsonArray? series = tx["PriceDataSeries"] as JsonArray;
            int count = series?.Count ?? 0;
            if (count < 1 || count > 10)
            {
                report.Warnings.Add($"OracleSet: PriceDataSeries must contain 1..10 entries (got {count}).");
                report.Feasible = false;
            }
            uint? lastUpdate = ReadUInt(tx, "LastUpdateTime");
            if (lastUpdate is null || lastUpdate.Value == 0)
            {
                report.Warnings.Add("OracleSet: LastUpdateTime is missing or zero.");
                report.Feasible = false;
            }
        }
        else if (string.Equals(txType, "DelegateSet", StringComparison.Ordinal))
        {
            JsonArray? perms = tx["Permissions"] as JsonArray;
            int count = perms?.Count ?? 0;
            if (count > 10)
            {
                report.Warnings.Add($"DelegateSet: Permissions has {count} entries (max 10).");
                report.Feasible = false;
            }
            string? authorize = ReadString(tx, "Authorize");
            string? src = ReadString(tx, "Account");
            if (!string.IsNullOrEmpty(authorize) && string.Equals(authorize, src, StringComparison.Ordinal))
            {
                report.Warnings.Add("DelegateSet: Authorize must differ from Account.");
                report.Feasible = false;
            }
            if (perms is not null)
            {
                HashSet<string> nonDelegable = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "AccountSet", "SetRegularKey", "SignerListSet", "DelegateSet",
                };
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int idx = 0;
                foreach (JsonNode? entry in perms)
                {
                    string? value = entry?["Permission"]?["PermissionValue"] is JsonValue pv && pv.TryGetValue<string>(out string? s) ? s : null;
                    if (string.IsNullOrEmpty(value))
                    {
                        report.Warnings.Add($"DelegateSet: Permissions[{idx}].PermissionValue is empty.");
                        report.Feasible = false;
                    }
                    else if (nonDelegable.Contains(value))
                    {
                        report.Warnings.Add($"DelegateSet: '{value}' is non-delegable.");
                        report.Feasible = false;
                    }
                    else if (!seen.Add(value))
                    {
                        report.Warnings.Add($"DelegateSet: duplicate PermissionValue '{value}'.");
                        report.Feasible = false;
                    }
                    idx++;
                }
            }
        }
        else if (txType.StartsWith("Credential", StringComparison.Ordinal))
        {
            ApplyCredentialWarnings(report, tx, txType);
        }
        else if (txType.StartsWith("PermissionedDomain", StringComparison.Ordinal))
        {
            ApplyPermissionedDomainWarnings(report, tx, txType);
        }
        else if (string.Equals(txType, "DIDSet", StringComparison.Ordinal))
        {
            string? data = ReadString(tx, "Data");
            string? doc = ReadString(tx, "DIDDocument");
            string? uri = ReadString(tx, "URI");
            if (string.IsNullOrEmpty(data) && string.IsNullOrEmpty(doc) && string.IsNullOrEmpty(uri))
            {
                report.Warnings.Add("DIDSet: must include at least one of Data / DIDDocument / URI.");
                report.Feasible = false;
            }
            foreach ((string name, string? val) in new[] { ("Data", data), ("DIDDocument", doc), ("URI", uri) })
            {
                if (string.IsNullOrEmpty(val)) continue;
                if (val.Length > 512)
                {
                    report.Warnings.Add($"DIDSet: {name} is {val.Length} hex chars (max 512 = 256 raw bytes).");
                    report.Feasible = false;
                }
                if ((val.Length & 1) != 0)
                {
                    report.Warnings.Add($"DIDSet: {name} must have an even number of hex chars.");
                    report.Feasible = false;
                }
            }
        }
        else if (txType.StartsWith("Vault", StringComparison.Ordinal))
        {
            ApplyVaultWarnings(report, tx, txType);
        }
        else if (txType.StartsWith("LoanBroker", StringComparison.Ordinal))
        {
            ApplyLoanBrokerWarnings(report, tx, txType);
        }
        else if (txType.StartsWith("Loan", StringComparison.Ordinal))
        {
            ApplyLoanWarnings(report, tx, txType);
        }
        else if (txType.StartsWith("XChain", StringComparison.Ordinal))
        {
            ApplyXChainWarnings(report, tx, txType);
        }
        else if (string.Equals(txType, "AMMClawback", StringComparison.Ordinal))
        {
            string? holder = ReadString(tx, "Holder");
            string? src = ReadString(tx, "Account");
            if (string.IsNullOrEmpty(holder))
            {
                report.Warnings.Add("AMMClawback: Holder is required.");
                report.Feasible = false;
            }
            else if (string.Equals(holder, src, StringComparison.Ordinal))
            {
                report.Warnings.Add("AMMClawback: Holder must differ from Account (the issuer).");
                report.Feasible = false;
            }

            // Asset.issuer must equal Account — only an issuer can claw back its own tokens.
            string? assetIssuer = tx["Asset"]?["issuer"] is JsonValue iv && iv.TryGetValue<string>(out string? iStr) ? iStr : null;
            if (!string.IsNullOrEmpty(src) && !string.IsNullOrEmpty(assetIssuer)
                && !string.Equals(assetIssuer, src, StringComparison.Ordinal))
            {
                report.Warnings.Add("AMMClawback: Asset.issuer must equal Account — only the issuer can claw back its own tokens.");
                report.Feasible = false;
            }
        }
        else if (string.Equals(txType, "NFTokenModify", StringComparison.Ordinal))
        {
            string? nftId = ReadString(tx, "NFTokenID");
            if (string.IsNullOrEmpty(nftId) || nftId.Length != 64)
            {
                report.Warnings.Add("NFTokenModify: NFTokenID must be a 64-char hex string.");
                report.Feasible = false;
            }
            string? uriHex = ReadString(tx, "URI");
            if (!string.IsNullOrEmpty(uriHex))
            {
                if ((uriHex.Length & 1) != 0)
                {
                    report.Warnings.Add("NFTokenModify: URI must have an even number of hex chars.");
                    report.Feasible = false;
                }
            }
            // Mutability (tfMutable=16) is checked by reading the NFT's flags off-chain — non-trivial here.
        }
        else if (string.Equals(txType, "AccountDelete", StringComparison.Ordinal))
        {
            // AccountDelete has two unique pre-flight constraints (rippled rules):
            // 1. The account must own NO deletion-blocker objects (some types like
            //    Escrow, PaymentChannel, NFTokenPage with NFTs cannot be deleted).
            // 2. Sequence + 256 must be ≤ current validated ledger sequence — protects
            //    against deleting an account whose recent tx history is still in flight.
            try
            {
                AccountObjectsRequest objReq = new AccountObjectsRequest(account)
                {
                    DeletionBlockersOnly = true,
                    LedgerIndex = new LedgerIndex(LedgerIndexType.Validated),
                };
                AccountObjects objResp = await client.AccountObjects(objReq, cancellationToken).ConfigureAwait(false);
                if (objResp.AccountObjectList is not null && objResp.AccountObjectList.Count > 0)
                {
                    List<string> blockerTypes = new List<string>();
                    foreach (BaseLedgerEntry entry in objResp.AccountObjectList)
                    {
                        blockerTypes.Add(entry.LedgerEntryType.ToString());
                    }
                    report.Warnings.Add(
                        $"AccountDelete will fail: account owns {objResp.AccountObjectList.Count} deletion-blocker object(s): " +
                        string.Join(", ", blockerTypes) +
                        ". Resolve each (cancel escrows, close payment channels, burn NFTs, etc.) and retry.");
                    report.Feasible = false;
                }
            }
            catch (Exception ex)
            {
                report.Warnings.Add("account_objects(deletion_blockers_only=true) failed: " + ex.Message);
            }

            try
            {
                uint currentLedger = await client.GetLedgerIndex(cancellationToken).ConfigureAwait(false);
                if (currentLedger > 0 && report.CurrentSequence > 0
                    && currentLedger < report.CurrentSequence + 256u)
                {
                    long ledgersToWait = (long)(report.CurrentSequence + 256u) - currentLedger;
                    report.Warnings.Add(string.Create(CultureInfo.InvariantCulture,
                        $"AccountDelete needs current ledger ≥ account.Sequence + 256. Current={currentLedger}, " +
                        $"Sequence={report.CurrentSequence}, must wait ~{ledgersToWait} more ledgers (~{ledgersToWait * 4}s)."));
                    report.Feasible = false;
                }
            }
            catch (Exception ex)
            {
                report.Warnings.Add("ledger index lookup failed (cannot verify Sequence+256 rule): " + ex.Message);
            }
        }

        return report;
    }

    private static void ApplyMptWarnings(PreflightReport report, JsonNode tx, string txType)
    {
        switch (txType)
        {
            case "MPTokenIssuanceCreate":
                {
                    uint? scale = ReadUInt(tx, "AssetScale");
                    if (scale is not null && scale.Value > 10)
                    {
                        report.Warnings.Add($"MPTokenIssuanceCreate: AssetScale={scale.Value} > 10 (XLS-33 max).");
                        report.Feasible = false;
                    }

                    uint? transferFee = ReadUInt(tx, "TransferFee");
                    if (transferFee is not null && transferFee.Value > 50000)
                    {
                        report.Warnings.Add($"MPTokenIssuanceCreate: TransferFee={transferFee.Value} > 50000 (= 50%).");
                        report.Feasible = false;
                    }

                    uint? flags = ReadUInt(tx, "Flags");
                    if (transferFee is not null && transferFee.Value > 0
                        && (flags is null || (flags.Value & 32u) == 0))
                    {
                        report.Warnings.Add("MPTokenIssuanceCreate: TransferFee>0 requires tfMPTCanTransfer (32) in Flags.");
                        report.Feasible = false;
                    }

                    string? maxAmount = ReadString(tx, "MaximumAmount");
                    if (!string.IsNullOrEmpty(maxAmount))
                    {
                        if (!ulong.TryParse(maxAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed)
                            || parsed > 9223372036854775807UL)
                        {
                            report.Warnings.Add("MPTokenIssuanceCreate: MaximumAmount must be a uint64 string ≤ 9223372036854775807.");
                            report.Feasible = false;
                        }
                    }

                    string? metadata = ReadString(tx, "MPTokenMetadata");
                    if (!string.IsNullOrEmpty(metadata) && metadata.Length > 2048)
                    {
                        report.Warnings.Add($"MPTokenIssuanceCreate: MPTokenMetadata is {metadata.Length} hex chars; max 2048 (= 1024 raw bytes).");
                        report.Feasible = false;
                    }
                    break;
                }
            case "MPTokenIssuanceSet":
                {
                    uint? flags = ReadUInt(tx, "Flags");
                    if (flags is not null)
                    {
                        bool lockBit = (flags.Value & 0x1u) != 0;
                        bool unlockBit = (flags.Value & 0x2u) != 0;
                        if (lockBit && unlockBit)
                        {
                            report.Warnings.Add("MPTokenIssuanceSet: cannot set both tfMPTLock (1) and tfMPTUnlock (2) at once.");
                            report.Feasible = false;
                        }
                    }
                    if (string.IsNullOrEmpty(ReadString(tx, "MPTokenIssuanceID")))
                    {
                        report.Warnings.Add("MPTokenIssuanceSet: MPTokenIssuanceID is required.");
                        report.Feasible = false;
                    }
                    break;
                }
            case "MPTokenIssuanceDestroy":
                if (string.IsNullOrEmpty(ReadString(tx, "MPTokenIssuanceID")))
                {
                    report.Warnings.Add("MPTokenIssuanceDestroy: MPTokenIssuanceID is required.");
                    report.Feasible = false;
                }
                break;
            case "MPTokenAuthorize":
                {
                    if (string.IsNullOrEmpty(ReadString(tx, "MPTokenIssuanceID")))
                    {
                        report.Warnings.Add("MPTokenAuthorize: MPTokenIssuanceID is required.");
                        report.Feasible = false;
                    }
                    string? holder = ReadString(tx, "Holder");
                    string? account = ReadString(tx, "Account");
                    if (!string.IsNullOrEmpty(holder) && string.Equals(holder, account, StringComparison.Ordinal))
                    {
                        report.Warnings.Add("MPTokenAuthorize: Holder must differ from Account; omit Holder for self opt-in.");
                        report.Feasible = false;
                    }
                    break;
                }
        }
    }

    private static void ApplyLoanBrokerWarnings(PreflightReport report, JsonNode tx, string txType)
    {
        switch (txType)
        {
            case "LoanBrokerSet":
                {
                    string? vid = ReadString(tx, "VaultID");
                    if (string.IsNullOrEmpty(vid) || vid.Length != 64)
                    {
                        report.Warnings.Add("LoanBrokerSet: VaultID is required and must be 64-hex.");
                        report.Feasible = false;
                    }
                    string? bid = ReadString(tx, "LoanBrokerID");
                    if (!string.IsNullOrEmpty(bid) && bid.Length != 64)
                    {
                        report.Warnings.Add($"LoanBrokerSet: LoanBrokerID must be 64-hex (got {bid.Length}).");
                        report.Feasible = false;
                    }

                    uint? min = ReadUInt(tx, "CoverRateMinimum");
                    uint? liq = ReadUInt(tx, "CoverRateLiquidation");
                    if (min.HasValue && min.Value > 100000)
                    {
                        report.Warnings.Add($"LoanBrokerSet: CoverRateMinimum={min.Value} > 100000 (1/100th bp max).");
                        report.Feasible = false;
                    }
                    if (liq.HasValue && liq.Value > 100000)
                    {
                        report.Warnings.Add($"LoanBrokerSet: CoverRateLiquidation={liq.Value} > 100000 (1/100th bp max).");
                        report.Feasible = false;
                    }
                    if (min.HasValue && liq.HasValue && liq.Value > min.Value)
                    {
                        report.Warnings.Add("LoanBrokerSet: CoverRateLiquidation must be ≤ CoverRateMinimum.");
                        report.Feasible = false;
                    }
                    uint? fee = ReadUInt(tx, "ManagementFeeRate");
                    if (fee.HasValue && fee.Value > 10000)
                    {
                        report.Warnings.Add($"LoanBrokerSet: ManagementFeeRate={fee.Value} > 10000 (1/10th bp max).");
                        report.Feasible = false;
                    }
                    break;
                }
            case "LoanBrokerDelete":
            case "LoanBrokerCoverDeposit":
            case "LoanBrokerCoverWithdraw":
                {
                    string? bid = ReadString(tx, "LoanBrokerID");
                    if (string.IsNullOrEmpty(bid) || bid.Length != 64)
                    {
                        report.Warnings.Add($"{txType}: LoanBrokerID is required and must be 64-hex.");
                        report.Feasible = false;
                    }
                    if (txType != "LoanBrokerDelete" && tx["Amount"] is null)
                    {
                        report.Warnings.Add($"{txType}: Amount is required.");
                        report.Feasible = false;
                    }
                    break;
                }
            case "LoanBrokerCoverClawback":
                {
                    string? bid = ReadString(tx, "LoanBrokerID");
                    bool hasAmount = tx["Amount"] is not null;
                    if (string.IsNullOrEmpty(bid) && !hasAmount)
                    {
                        report.Warnings.Add("LoanBrokerCoverClawback: at least one of LoanBrokerID or Amount must be present.");
                        report.Feasible = false;
                    }
                    if (!string.IsNullOrEmpty(bid) && bid.Length != 64)
                    {
                        report.Warnings.Add($"LoanBrokerCoverClawback: LoanBrokerID must be 64-hex (got {bid.Length}).");
                        report.Feasible = false;
                    }
                    break;
                }
        }
    }

    private static void ApplyLoanWarnings(PreflightReport report, JsonNode tx, string txType)
    {
        // Disambiguate from LoanBroker* (already routed above by StartsWith order).
        switch (txType)
        {
            case "LoanSet":
                {
                    string? bid = ReadString(tx, "LoanBrokerID");
                    if (string.IsNullOrEmpty(bid) || bid.Length != 64)
                    {
                        report.Warnings.Add("LoanSet: LoanBrokerID is required and must be 64-hex.");
                        report.Feasible = false;
                    }
                    string? cp = ReadString(tx, "Counterparty");
                    string? src = ReadString(tx, "Account");
                    if (string.IsNullOrEmpty(cp))
                    {
                        report.Warnings.Add("LoanSet: Counterparty is required.");
                        report.Feasible = false;
                    }
                    else if (string.Equals(cp, src, StringComparison.Ordinal))
                    {
                        report.Warnings.Add("LoanSet: Counterparty must differ from Account (lender ≠ borrower).");
                        report.Feasible = false;
                    }
                    if (string.IsNullOrEmpty(ReadString(tx, "PrincipalRequested")))
                    {
                        report.Warnings.Add("LoanSet: PrincipalRequested is required.");
                        report.Feasible = false;
                    }
                    foreach (string field in new[] { "InterestRate", "LateInterestRate", "CloseInterestRate",
                        "OverpaymentInterestRate", "OverpaymentFee" })
                    {
                        uint? v = ReadUInt(tx, field);
                        if (v.HasValue && v.Value > 100000)
                        {
                            report.Warnings.Add($"LoanSet: {field}={v.Value} > 100000 (1/100th bp max).");
                            report.Feasible = false;
                        }
                    }
                    break;
                }
            case "LoanManage":
                {
                    string? lid = ReadString(tx, "LoanID");
                    if (string.IsNullOrEmpty(lid) || lid.Length != 64)
                    {
                        report.Warnings.Add("LoanManage: LoanID is required and must be 64-hex.");
                        report.Feasible = false;
                    }
                    uint? flags = ReadUInt(tx, "Flags");
                    const uint mask = 0x00010000u | 0x00020000u | 0x00040000u;
                    uint actionBits = flags.HasValue ? (flags.Value & mask) : 0u;
                    int count = System.Numerics.BitOperations.PopCount(actionBits);
                    if (count != 1)
                    {
                        report.Warnings.Add($"LoanManage: exactly one of tfLoanDefault/Impair/Unimpair must be set (got {count}).");
                        report.Feasible = false;
                    }
                    break;
                }
            case "LoanPay":
                {
                    string? lid = ReadString(tx, "LoanID");
                    if (string.IsNullOrEmpty(lid) || lid.Length != 64)
                    {
                        report.Warnings.Add("LoanPay: LoanID is required and must be 64-hex.");
                        report.Feasible = false;
                    }
                    if (tx["Amount"] is null)
                    {
                        report.Warnings.Add("LoanPay: Amount is required.");
                        report.Feasible = false;
                    }
                    uint? flags = ReadUInt(tx, "Flags");
                    if (flags.HasValue)
                    {
                        const uint mask = 0x00010000u | 0x00020000u | 0x00040000u;
                        uint kindBits = flags.Value & mask;
                        if (kindBits != 0 && (kindBits & (kindBits - 1)) != 0)
                        {
                            report.Warnings.Add("LoanPay: only one of tfLoanOverpayment/FullPayment/LatePayment may be set.");
                            report.Feasible = false;
                        }
                    }
                    break;
                }
            case "LoanDelete":
                {
                    string? lid = ReadString(tx, "LoanID");
                    if (string.IsNullOrEmpty(lid) || lid.Length != 64)
                    {
                        report.Warnings.Add("LoanDelete: LoanID is required and must be 64-hex.");
                        report.Feasible = false;
                    }
                    break;
                }
        }
    }

    private static void ApplyVaultWarnings(PreflightReport report, JsonNode tx, string txType)
    {
        // VaultCreate has no VaultID; the rest must have a 64-hex VaultID.
        if (txType != "VaultCreate")
        {
            string? vaultId = ReadString(tx, "VaultID");
            if (string.IsNullOrEmpty(vaultId))
            {
                report.Warnings.Add($"{txType}: VaultID is required.");
                report.Feasible = false;
            }
            else if (vaultId.Length != 64)
            {
                report.Warnings.Add($"{txType}: VaultID must be 64 hex chars (got {vaultId.Length}).");
                report.Feasible = false;
            }
        }

        switch (txType)
        {
            case "VaultCreate":
                {
                    if (tx["Asset"] is null)
                    {
                        report.Warnings.Add("VaultCreate: Asset is required.");
                        report.Feasible = false;
                    }
                    if (tx["Amount"] is null)
                    {
                        report.Warnings.Add("VaultCreate: Amount (initial deposit) is required.");
                        report.Feasible = false;
                    }
                    uint? scale = ReadUInt(tx, "Scale");
                    if (scale is not null && scale.Value > 18)
                    {
                        report.Warnings.Add($"VaultCreate: Scale must be 0..18 (got {scale.Value}).");
                        report.Feasible = false;
                    }
                    string? data = ReadString(tx, "Data");
                    if (!string.IsNullOrEmpty(data) && (data.Length > 512 || (data.Length & 1) != 0))
                    {
                        report.Warnings.Add($"VaultCreate: Data must be even-length hex ≤512 chars (got {data.Length}).");
                        report.Feasible = false;
                    }
                    break;
                }
            case "VaultDeposit":
            case "VaultWithdraw":
                if (tx["Amount"] is null)
                {
                    report.Warnings.Add($"{txType}: Amount is required.");
                    report.Feasible = false;
                }
                break;
            case "VaultClawback":
                {
                    string? holder = ReadString(tx, "Holder");
                    string? src = ReadString(tx, "Account");
                    if (string.IsNullOrEmpty(holder))
                    {
                        report.Warnings.Add("VaultClawback: Holder is required.");
                        report.Feasible = false;
                    }
                    else if (string.Equals(holder, src, StringComparison.Ordinal))
                    {
                        report.Warnings.Add("VaultClawback: Holder must differ from Account.");
                        report.Feasible = false;
                    }
                    break;
                }
        }
    }

    private static void ApplyXChainWarnings(PreflightReport report, JsonNode tx, string txType)
    {
        if (tx["XChainBridge"] is not JsonObject bridge)
        {
            report.Warnings.Add($"{txType}: XChainBridge is required (object).");
            report.Feasible = false;
            return;
        }
        foreach (string field in new[] { "LockingChainDoor", "IssuingChainDoor" })
        {
            string? v = bridge[field] is JsonValue jv && jv.TryGetValue<string>(out string? s) ? s : null;
            if (string.IsNullOrEmpty(v))
            {
                report.Warnings.Add($"{txType}: XChainBridge.{field} is required.");
                report.Feasible = false;
            }
        }
        foreach (string field in new[] { "LockingChainIssue", "IssuingChainIssue" })
        {
            if (bridge[field] is not JsonObject)
            {
                report.Warnings.Add($"{txType}: XChainBridge.{field} must be an object {{currency, issuer?}}.");
                report.Feasible = false;
            }
        }
        string? lockDoor = bridge["LockingChainDoor"] is JsonValue lv && lv.TryGetValue<string>(out string? l) ? l : null;
        string? issDoor = bridge["IssuingChainDoor"] is JsonValue iv && iv.TryGetValue<string>(out string? i) ? i : null;
        if (!string.IsNullOrEmpty(lockDoor) && !string.IsNullOrEmpty(issDoor)
            && string.Equals(lockDoor, issDoor, StringComparison.Ordinal))
        {
            report.Warnings.Add($"{txType}: LockingChainDoor and IssuingChainDoor must differ.");
            report.Feasible = false;
        }

        switch (txType)
        {
            case "XChainCreateBridge":
            case "XChainCreateClaimID":
                if (tx["SignatureReward"] is null)
                {
                    report.Warnings.Add($"{txType}: SignatureReward is required.");
                    report.Feasible = false;
                }
                break;
            case "XChainCommit":
            case "XChainClaim":
                if (string.IsNullOrEmpty(ReadString(tx, "XChainClaimID")))
                {
                    report.Warnings.Add($"{txType}: XChainClaimID is required.");
                    report.Feasible = false;
                }
                if (tx["Amount"] is null)
                {
                    report.Warnings.Add($"{txType}: Amount is required.");
                    report.Feasible = false;
                }
                if (txType == "XChainClaim" && string.IsNullOrEmpty(ReadString(tx, "Destination")))
                {
                    report.Warnings.Add("XChainClaim: Destination is required.");
                    report.Feasible = false;
                }
                break;
            case "XChainAccountCreateCommit":
                if (tx["Amount"] is null || tx["SignatureReward"] is null
                    || string.IsNullOrEmpty(ReadString(tx, "Destination")))
                {
                    report.Warnings.Add("XChainAccountCreateCommit: Amount, SignatureReward and Destination are all required.");
                    report.Feasible = false;
                }
                break;
            case "XChainAddClaimAttestation":
            case "XChainAddAccountCreateAttestation":
                {
                    foreach (string req in new[] { "AttestationRewardAccount", "AttestationSignerAccount",
                        "OtherChainSource", "PublicKey", "Signature", "Destination" })
                    {
                        if (string.IsNullOrEmpty(ReadString(tx, req)))
                        {
                            report.Warnings.Add($"{txType}: {req} is required.");
                            report.Feasible = false;
                        }
                    }
                    uint? wasLocking = ReadUInt(tx, "WasLockingChainSend");
                    if (wasLocking is null || wasLocking.Value > 1)
                    {
                        report.Warnings.Add($"{txType}: WasLockingChainSend must be 0 or 1.");
                        report.Feasible = false;
                    }
                    if (txType == "XChainAddAccountCreateAttestation"
                        && string.IsNullOrEmpty(ReadString(tx, "XChainAccountCreateCount")))
                    {
                        report.Warnings.Add("XChainAddAccountCreateAttestation: XChainAccountCreateCount is required.");
                        report.Feasible = false;
                    }
                    if (txType == "XChainAddClaimAttestation"
                        && string.IsNullOrEmpty(ReadString(tx, "XChainClaimID")))
                    {
                        report.Warnings.Add("XChainAddClaimAttestation: XChainClaimID is required.");
                        report.Feasible = false;
                    }
                    break;
                }
        }
    }

    private static void ApplyCredentialWarnings(PreflightReport report, JsonNode tx, string txType)
    {
        string? credType = ReadString(tx, "CredentialType");
        if (string.IsNullOrEmpty(credType) && txType != "CredentialDelete")
        {
            report.Warnings.Add($"{txType}: CredentialType is required.");
            report.Feasible = false;
        }
        if (!string.IsNullOrEmpty(credType))
        {
            if ((credType.Length & 1) != 0)
            {
                report.Warnings.Add($"{txType}: CredentialType must have an even number of hex chars.");
                report.Feasible = false;
            }
            if (credType.Length > 128)
            {
                report.Warnings.Add($"{txType}: CredentialType is {credType.Length} hex chars (max 128 = 64 raw bytes).");
                report.Feasible = false;
            }
        }

        string? account = ReadString(tx, "Account");
        switch (txType)
        {
            case "CredentialCreate":
                {
                    string? subject = ReadString(tx, "Subject");
                    if (string.IsNullOrEmpty(subject))
                    {
                        report.Warnings.Add("CredentialCreate: Subject is required.");
                        report.Feasible = false;
                    }
                    else if (string.Equals(subject, account, StringComparison.Ordinal))
                    {
                        report.Warnings.Add("CredentialCreate: Subject must differ from Account (the issuer).");
                        report.Feasible = false;
                    }

                    string? uriHex = ReadString(tx, "URI");
                    if (!string.IsNullOrEmpty(uriHex))
                    {
                        if ((uriHex.Length & 1) != 0)
                        {
                            report.Warnings.Add("CredentialCreate: URI must have an even number of hex chars.");
                            report.Feasible = false;
                        }
                        if (uriHex.Length > 512)
                        {
                            report.Warnings.Add($"CredentialCreate: URI is {uriHex.Length} hex chars (max 512 = 256 raw bytes).");
                            report.Feasible = false;
                        }
                    }
                    break;
                }
            case "CredentialAccept":
                {
                    string? issuer = ReadString(tx, "Issuer");
                    if (string.IsNullOrEmpty(issuer))
                    {
                        report.Warnings.Add("CredentialAccept: Issuer is required.");
                        report.Feasible = false;
                    }
                    else if (string.Equals(issuer, account, StringComparison.Ordinal))
                    {
                        report.Warnings.Add("CredentialAccept: Issuer must differ from Account (the subject).");
                        report.Feasible = false;
                    }
                    break;
                }
            case "CredentialDelete":
                {
                    string? subject = ReadString(tx, "Subject");
                    string? issuer = ReadString(tx, "Issuer");
                    if (string.IsNullOrEmpty(subject) && string.IsNullOrEmpty(issuer))
                    {
                        report.Warnings.Add("CredentialDelete: at least one of Subject or Issuer must be present.");
                        report.Feasible = false;
                    }
                    if (!string.IsNullOrEmpty(subject) && string.Equals(subject, account, StringComparison.Ordinal))
                    {
                        report.Warnings.Add("CredentialDelete: Subject must differ from Account (omit when self).");
                        report.Feasible = false;
                    }
                    if (!string.IsNullOrEmpty(issuer) && string.Equals(issuer, account, StringComparison.Ordinal))
                    {
                        report.Warnings.Add("CredentialDelete: Issuer must differ from Account (omit when self).");
                        report.Feasible = false;
                    }
                    break;
                }
        }
    }

    private static void ApplyPermissionedDomainWarnings(PreflightReport report, JsonNode tx, string txType)
    {
        string? domainId = ReadString(tx, "DomainID");

        if (txType == "PermissionedDomainDelete")
        {
            if (string.IsNullOrEmpty(domainId))
            {
                report.Warnings.Add("PermissionedDomainDelete: DomainID is required.");
                report.Feasible = false;
            }
            else if (domainId.Length != 64)
            {
                report.Warnings.Add($"PermissionedDomainDelete: DomainID must be 64 hex chars (got {domainId.Length}).");
                report.Feasible = false;
            }
            return;
        }

        // PermissionedDomainSet
        if (!string.IsNullOrEmpty(domainId) && domainId.Length != 64)
        {
            report.Warnings.Add($"PermissionedDomainSet: DomainID (when modifying) must be 64 hex chars (got {domainId.Length}).");
            report.Feasible = false;
        }

        JsonArray? credentials = tx["AcceptedCredentials"] as JsonArray;
        int count = credentials?.Count ?? 0;
        if (count < 1 || count > 10)
        {
            report.Warnings.Add($"PermissionedDomainSet: AcceptedCredentials must contain 1..10 entries (got {count}).");
            report.Feasible = false;
        }

        if (credentials is not null)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < credentials.Count; i++)
            {
                JsonNode? wrapper = credentials[i];
                JsonNode? cred = wrapper?["Credential"];
                if (cred is null)
                {
                    report.Warnings.Add($"PermissionedDomainSet: AcceptedCredentials[{i}].Credential is missing.");
                    report.Feasible = false;
                    continue;
                }
                string? issuer = cred["Issuer"] is JsonValue iv && iv.TryGetValue<string>(out string? iStr) ? iStr : null;
                string? credType = cred["CredentialType"] is JsonValue cv && cv.TryGetValue<string>(out string? cStr) ? cStr : null;
                if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(credType))
                {
                    report.Warnings.Add($"PermissionedDomainSet: AcceptedCredentials[{i}] is missing Issuer or CredentialType.");
                    report.Feasible = false;
                    continue;
                }
                if (credType.Length > 128)
                {
                    report.Warnings.Add($"PermissionedDomainSet: AcceptedCredentials[{i}].CredentialType > 128 hex chars.");
                    report.Feasible = false;
                }
                string key = issuer + ":" + credType.ToUpperInvariant();
                if (!seen.Add(key))
                {
                    report.Warnings.Add($"PermissionedDomainSet: AcceptedCredentials contains duplicate ({issuer}, {credType}).");
                    report.Feasible = false;
                }
            }
        }
    }

    private static void ApplyBatchWarnings(PreflightReport report, JsonNode tx)
    {
        uint? flags = ReadUInt(tx, "Flags");
        const uint modeMask = 0x00010000u | 0x00020000u | 0x00040000u | 0x00080000u;
        uint modeBits = flags.HasValue ? (flags.Value & modeMask) : 0u;
        int modeCount = System.Numerics.BitOperations.PopCount(modeBits);
        if (modeCount != 1)
        {
            report.Warnings.Add($"Batch: exactly one mode flag must be set (AllOrNothing|OnlyOne|UntilFailure|Independent); got {modeCount}.");
            report.Feasible = false;
        }

        JsonArray? inners = tx["RawTransactions"] as JsonArray;
        int innerCount = inners?.Count ?? 0;
        if (innerCount == 0)
        {
            report.Warnings.Add("Batch: RawTransactions is empty.");
            report.Feasible = false;
        }
        else if (innerCount > 8)
        {
            report.Warnings.Add($"Batch: {innerCount} inner tx exceeds XLS-56 limit of 8.");
            report.Feasible = false;
        }

        if (inners is not null)
        {
            for (int i = 0; i < inners.Count; i++)
            {
                JsonNode? wrapper = inners[i];
                JsonNode? inner = wrapper?["RawTransaction"];
                if (inner is null)
                {
                    report.Warnings.Add($"Batch: RawTransactions[{i}].RawTransaction is missing.");
                    report.Feasible = false;
                    continue;
                }
                string innerType = ReadString(inner, "TransactionType") ?? "";
                if (string.Equals(innerType, "Batch", StringComparison.OrdinalIgnoreCase))
                {
                    report.Warnings.Add($"Batch: RawTransactions[{i}] cannot itself be Batch (nesting forbidden).");
                    report.Feasible = false;
                }
                uint? innerFlags = ReadUInt(inner, "Flags");
                if (innerFlags is null || (innerFlags.Value & 0x40000000u) == 0)
                {
                    report.Warnings.Add($"Batch: RawTransactions[{i}] missing tfInnerBatchTxn (0x40000000) flag.");
                    report.Feasible = false;
                }
                string? innerFee = ReadString(inner, "Fee");
                if (!string.IsNullOrEmpty(innerFee) && innerFee != "0")
                {
                    report.Warnings.Add($"Batch: RawTransactions[{i}].Fee must be \"0\" (got '{innerFee}').");
                    report.Feasible = false;
                }
                string? spk = ReadString(inner, "SigningPubKey");
                if (spk is null || spk.Length > 0)
                {
                    report.Warnings.Add($"Batch: RawTransactions[{i}].SigningPubKey must be empty string.");
                    report.Feasible = false;
                }
                if (inner["TxnSignature"] is not null)
                {
                    report.Warnings.Add($"Batch: RawTransactions[{i}].TxnSignature must not be present inside a Batch.");
                    report.Feasible = false;
                }
                if (inner["Signers"] is not null)
                {
                    report.Warnings.Add($"Batch: RawTransactions[{i}].Signers must not be present inside a Batch.");
                    report.Feasible = false;
                }
            }
        }
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
