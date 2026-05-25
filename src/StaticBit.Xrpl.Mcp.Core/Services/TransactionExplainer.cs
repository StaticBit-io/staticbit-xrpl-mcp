using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace StaticBit.Xrpl.Mcp.Core.Services;

/// <summary>
/// Pure function that converts a decoded XRPL transaction (as <see cref="JsonNode"/>)
/// into a one-line human summary suitable for an approval prompt. No network calls,
/// no per-type typed deserialization — works straight off the decoded canonical JSON.
/// </summary>
public static class TransactionExplainer
{
    /// <summary>
    /// Build a single human-readable line describing the transaction's intent.
    /// Returns a generic fallback ("&lt;TransactionType&gt; from &lt;account&gt;")
    /// when the type isn't specifically recognized.
    /// </summary>
    public static string Explain(JsonNode tx)
    {
        if (tx is null) throw new ArgumentNullException(nameof(tx));

        string txType = ReadString(tx, "TransactionType") ?? "<unknown>";
        string account = ToolDisplay.Truncate(ReadString(tx, "Account"));

        StringBuilder sb = new StringBuilder();
        AppendSpecificSummary(sb, tx, txType, account);
        AppendCommonSuffix(sb, tx);
        return sb.ToString();
    }

    private static void AppendSpecificSummary(StringBuilder sb, JsonNode tx, string txType, string account)
    {
        switch (txType)
        {
            case "Payment":
                {
                    string dest = ToolDisplay.Truncate(ReadString(tx, "Destination"));
                    string amount = DescribeAmountNode(tx["Amount"]);
                    string? sendMax = tx["SendMax"] is null ? null : DescribeAmountNode(tx["SendMax"]);
                    string? destTag = ReadUInt(tx, "DestinationTag")?.ToString(CultureInfo.InvariantCulture);
                    sb.Append("Payment from ").Append(account).Append(" to ").Append(dest)
                      .Append(": ").Append(amount);
                    if (sendMax is not null) sb.Append(" (sendMax ").Append(sendMax).Append(')');
                    if (destTag is not null) sb.Append(" [DestTag ").Append(destTag).Append(']');
                    sb.Append('.');
                    break;
                }
            case "TrustSet":
                {
                    JsonNode? limit = tx["LimitAmount"];
                    string desc = DescribeAmountNode(limit);
                    sb.Append("TrustSet from ").Append(account).Append(": limit ").Append(desc).Append('.');
                    break;
                }
            case "OfferCreate":
                {
                    string gets = DescribeAmountNode(tx["TakerGets"]);
                    string pays = DescribeAmountNode(tx["TakerPays"]);
                    sb.Append("OfferCreate from ").Append(account)
                      .Append(": give ").Append(gets).Append(" for ").Append(pays).Append('.');
                    break;
                }
            case "OfferCancel":
                {
                    uint? seq = ReadUInt(tx, "OfferSequence");
                    sb.Append("OfferCancel from ").Append(account)
                      .Append(": remove offer with sequence ").Append(seq?.ToString(CultureInfo.InvariantCulture) ?? "?").Append('.');
                    break;
                }
            case "AMMDeposit":
                {
                    string pool = DescribePool(tx);
                    sb.Append("AMMDeposit from ").Append(account).Append(" into pool ").Append(pool).Append('.');
                    break;
                }
            case "AMMWithdraw":
                {
                    string pool = DescribePool(tx);
                    sb.Append("AMMWithdraw from ").Append(account).Append(" of pool ").Append(pool).Append('.');
                    break;
                }
            case "AMMCreate":
                {
                    string a1 = DescribeAmountNode(tx["Amount"]);
                    string a2 = DescribeAmountNode(tx["Amount2"]);
                    uint? fee = ReadUInt(tx, "TradingFee");
                    sb.Append("AMMCreate by ").Append(account)
                      .Append(": pool ").Append(a1).Append(" / ").Append(a2);
                    if (fee is not null) sb.Append(", tradingFee=").Append(fee).Append(" (1/10 bps)");
                    sb.Append('.');
                    break;
                }
            case "AMMVote":
                {
                    string pool = DescribePool(tx);
                    uint? fee = ReadUInt(tx, "TradingFee");
                    sb.Append("AMMVote by ").Append(account).Append(" on pool ").Append(pool);
                    if (fee is not null) sb.Append(": vote=").Append(fee);
                    sb.Append('.');
                    break;
                }
            case "AMMBid":
                {
                    string pool = DescribePool(tx);
                    sb.Append("AMMBid by ").Append(account).Append(" on pool ").Append(pool).Append('.');
                    break;
                }
            case "AMMDelete":
                {
                    string pool = DescribePool(tx);
                    sb.Append("AMMDelete by ").Append(account).Append(" on pool ").Append(pool).Append('.');
                    break;
                }
            case "NFTokenMint":
                {
                    uint? taxon = ReadUInt(tx, "NFTokenTaxon");
                    string? issuer = ReadString(tx, "Issuer");
                    sb.Append("NFTokenMint by ").Append(account).Append(": taxon=").Append(taxon?.ToString(CultureInfo.InvariantCulture) ?? "?");
                    if (!string.IsNullOrEmpty(issuer)) sb.Append(", on behalf of ").Append(ToolDisplay.Truncate(issuer));
                    sb.Append('.');
                    break;
                }
            case "NFTokenBurn":
                {
                    string nftId = ShortHex(ReadString(tx, "NFTokenID"));
                    string? owner = ReadString(tx, "Owner");
                    sb.Append("NFTokenBurn by ").Append(account).Append(": NFT ").Append(nftId);
                    if (!string.IsNullOrEmpty(owner)) sb.Append(" (held by ").Append(ToolDisplay.Truncate(owner)).Append(')');
                    sb.Append('.');
                    break;
                }
            case "NFTokenCreateOffer":
                {
                    string nftId = ShortHex(ReadString(tx, "NFTokenID"));
                    string amount = DescribeAmountNode(tx["Amount"]);
                    uint? flags = ReadUInt(tx, "Flags");
                    bool isSell = flags.HasValue && (flags.Value & 0x00000001u) != 0;
                    sb.Append("NFTokenCreateOffer (").Append(isSell ? "SELL" : "BUY")
                      .Append(") by ").Append(account).Append(" on ").Append(nftId)
                      .Append(": ").Append(amount).Append('.');
                    break;
                }
            case "NFTokenCancelOffer":
                {
                    int count = tx["NFTokenOffers"] is JsonArray arr ? arr.Count : 0;
                    sb.Append("NFTokenCancelOffer by ").Append(account)
                      .Append(": cancel ").Append(count).Append(" offer(s).");
                    break;
                }
            case "NFTokenAcceptOffer":
                {
                    bool hasSell = tx["NFTokenSellOffer"] is not null;
                    bool hasBuy = tx["NFTokenBuyOffer"] is not null;
                    string mode = hasSell && hasBuy ? "BROKERED" : hasSell ? "DIRECT-SELL" : "DIRECT-BUY";
                    sb.Append("NFTokenAcceptOffer (").Append(mode).Append(") by ").Append(account).Append('.');
                    break;
                }
            case "EscrowCreate":
                {
                    string dest = ToolDisplay.Truncate(ReadString(tx, "Destination"));
                    string amount = DescribeAmountNode(tx["Amount"]);
                    bool conditional = !string.IsNullOrEmpty(ReadString(tx, "Condition"));
                    sb.Append("EscrowCreate (").Append(conditional ? "conditional" : "time-only")
                      .Append(") from ").Append(account).Append(" to ").Append(dest)
                      .Append(": ").Append(amount).Append('.');
                    break;
                }
            case "EscrowFinish":
                {
                    string owner = ToolDisplay.Truncate(ReadString(tx, "Owner"));
                    uint? seq = ReadUInt(tx, "OfferSequence");
                    bool conditional = !string.IsNullOrEmpty(ReadString(tx, "Condition"));
                    sb.Append("EscrowFinish (").Append(conditional ? "conditional" : "time-only")
                      .Append(") by ").Append(account)
                      .Append(": owner=").Append(owner)
                      .Append(", offerSequence=").Append(seq?.ToString(CultureInfo.InvariantCulture) ?? "?").Append('.');
                    break;
                }
            case "EscrowCancel":
                {
                    string owner = ToolDisplay.Truncate(ReadString(tx, "Owner"));
                    uint? seq = ReadUInt(tx, "OfferSequence");
                    sb.Append("EscrowCancel by ").Append(account)
                      .Append(": owner=").Append(owner)
                      .Append(", offerSequence=").Append(seq?.ToString(CultureInfo.InvariantCulture) ?? "?").Append('.');
                    break;
                }
            case "PaymentChannelCreate":
                {
                    string dest = ToolDisplay.Truncate(ReadString(tx, "Destination"));
                    string amount = ReadString(tx, "Amount") ?? "?";
                    uint? delay = ReadUInt(tx, "SettleDelay");
                    sb.Append("PaymentChannelCreate from ").Append(account).Append(" to ").Append(dest)
                      .Append(": ").Append(amount).Append(" drops, settleDelay=")
                      .Append(delay?.ToString(CultureInfo.InvariantCulture) ?? "?").Append("s.");
                    break;
                }
            case "PaymentChannelFund":
                {
                    string ch = ShortHex(ReadString(tx, "Channel"));
                    string amount = ReadString(tx, "Amount") ?? "?";
                    sb.Append("PaymentChannelFund by ").Append(account).Append(" on ").Append(ch)
                      .Append(": +").Append(amount).Append(" drops.");
                    break;
                }
            case "PaymentChannelClaim":
                {
                    string ch = ShortHex(ReadString(tx, "Channel"));
                    uint? flags = ReadUInt(tx, "Flags");
                    bool close = flags.HasValue && (flags.Value & 0x00020000u) != 0;
                    bool renew = flags.HasValue && (flags.Value & 0x00010000u) != 0;
                    string action = close ? "CLOSE" : renew ? "RENEW" : "CLAIM";
                    string? balance = ReadString(tx, "Balance");
                    sb.Append("PaymentChannelClaim (").Append(action).Append(") by ").Append(account)
                      .Append(" on ").Append(ch);
                    if (!string.IsNullOrEmpty(balance)) sb.Append(" → delivered ").Append(balance).Append(" drops");
                    sb.Append('.');
                    break;
                }
            case "CheckCreate":
                {
                    string dest = ToolDisplay.Truncate(ReadString(tx, "Destination"));
                    string sendMax = DescribeAmountNode(tx["SendMax"]);
                    sb.Append("CheckCreate from ").Append(account).Append(" to ").Append(dest)
                      .Append(": sendMax ").Append(sendMax).Append('.');
                    break;
                }
            case "CheckCash":
                {
                    string ch = ShortHex(ReadString(tx, "CheckID"));
                    string descr = tx["Amount"] is not null
                        ? "Amount=" + DescribeAmountNode(tx["Amount"])
                        : tx["DeliverMin"] is not null
                            ? "DeliverMin=" + DescribeAmountNode(tx["DeliverMin"])
                            : "?";
                    sb.Append("CheckCash by ").Append(account).Append(" on check ").Append(ch)
                      .Append(": ").Append(descr).Append('.');
                    break;
                }
            case "CheckCancel":
                {
                    string ch = ShortHex(ReadString(tx, "CheckID"));
                    sb.Append("CheckCancel by ").Append(account).Append(" on check ").Append(ch).Append('.');
                    break;
                }
            case "AccountSet":
                {
                    List<string> parts = new List<string>();
                    uint? setFlag = ReadUInt(tx, "SetFlag");
                    uint? clearFlag = ReadUInt(tx, "ClearFlag");
                    if (setFlag is not null) parts.Add("SetFlag=" + setFlag);
                    if (clearFlag is not null) parts.Add("ClearFlag=" + clearFlag);
                    if (!string.IsNullOrEmpty(ReadString(tx, "Domain"))) parts.Add("Domain");
                    if (ReadUInt(tx, "TransferRate") is uint tr) parts.Add("TransferRate=" + tr);
                    if (ReadUInt(tx, "TickSize") is uint ts) parts.Add("TickSize=" + ts);
                    if (!string.IsNullOrEmpty(ReadString(tx, "NFTokenMinter"))) parts.Add("NFTokenMinter");
                    string changes = parts.Count == 0 ? "no-op" : string.Join(", ", parts);
                    sb.Append("AccountSet on ").Append(account).Append(": ").Append(changes).Append('.');
                    break;
                }
            case "SetRegularKey":
                {
                    string? rk = ReadString(tx, "RegularKey");
                    sb.Append("SetRegularKey on ").Append(account).Append(": ")
                      .Append(string.IsNullOrEmpty(rk) ? "REMOVE existing regular key." : "set to " + ToolDisplay.Truncate(rk) + ".");
                    break;
                }
            case "DepositPreauth":
                {
                    string? auth = ReadString(tx, "Authorize");
                    string? unauth = ReadString(tx, "Unauthorize");
                    string action = !string.IsNullOrEmpty(auth)
                        ? "AUTHORIZE " + ToolDisplay.Truncate(auth)
                        : !string.IsNullOrEmpty(unauth)
                            ? "UNAUTHORIZE " + ToolDisplay.Truncate(unauth)
                            : "?";
                    sb.Append("DepositPreauth on ").Append(account).Append(": ").Append(action).Append('.');
                    break;
                }
            case "SignerListSet":
                {
                    uint? quorum = ReadUInt(tx, "SignerQuorum");
                    int count = tx["SignerEntries"] is JsonArray arr ? arr.Count : 0;
                    sb.Append("SignerListSet on ").Append(account).Append(": ");
                    if (quorum == 0) sb.Append("DELETE signer list.");
                    else sb.Append("quorum=").Append(quorum).Append(" over ").Append(count).Append(" signer(s).");
                    break;
                }
            case "AccountDelete":
                {
                    string dest = ToolDisplay.Truncate(ReadString(tx, "Destination"));
                    uint? destTag = ReadUInt(tx, "DestinationTag");
                    sb.Append("AccountDelete: delete ").Append(account)
                      .Append(", send residual XRP to ").Append(dest);
                    if (destTag is not null) sb.Append(" (DestTag ").Append(destTag).Append(')');
                    sb.Append('.');
                    break;
                }
            case "Clawback":
                {
                    string amount = DescribeAmountNode(tx["Amount"]);
                    string? holder = ReadString(tx, "Holder");
                    string? issuerInAmount = tx["Amount"]?["issuer"]?.GetValue<string>();
                    string from = !string.IsNullOrEmpty(holder) ? ToolDisplay.Truncate(holder)
                        : !string.IsNullOrEmpty(issuerInAmount) ? ToolDisplay.Truncate(issuerInAmount)
                        : "?";
                    sb.Append("Clawback by issuer ").Append(account).Append(" from ").Append(from)
                      .Append(": ").Append(amount).Append('.');
                    break;
                }
            default:
                sb.Append(txType).Append(" from ").Append(account).Append('.');
                break;
        }
    }

    private static void AppendCommonSuffix(StringBuilder sb, JsonNode tx)
    {
        string? fee = ReadString(tx, "Fee");
        uint? seq = ReadUInt(tx, "Sequence");
        uint? lls = ReadUInt(tx, "LastLedgerSequence");

        if (fee is null && seq is null && lls is null) return;

        sb.Append(" [");
        bool first = true;
        if (fee is not null) { sb.Append("fee=").Append(fee).Append(" drops"); first = false; }
        if (seq is not null) { if (!first) sb.Append(", "); sb.Append("seq=").Append(seq); first = false; }
        if (lls is not null) { if (!first) sb.Append(", "); sb.Append("LLS=").Append(lls); }
        sb.Append(']');
    }

    private static string DescribePool(JsonNode tx)
    {
        return DescribeAssetNode(tx["Asset"]) + " / " + DescribeAssetNode(tx["Asset2"]);
    }

    /// <summary>
    /// Format an Amount-shaped node: drops-string for XRP, or {value,currency,issuer} object.
    /// </summary>
    internal static string DescribeAmountNode(JsonNode? node)
    {
        if (node is null) return "<null>";

        if (node is JsonValue v && v.TryGetValue<string>(out string? s))
        {
            return s + " drops XRP";
        }

        if (node is JsonObject obj)
        {
            string? value = TryString(obj, "value");
            string? currency = TryString(obj, "currency");
            string? issuer = TryString(obj, "issuer");
            string? mpt = TryString(obj, "mpt_issuance_id");
            if (!string.IsNullOrEmpty(mpt))
            {
                return (value ?? "?") + " MPT:" + ShortHex(mpt);
            }
            return (value ?? "?") + " " + (currency ?? "?") + " (" + ToolDisplay.Truncate(issuer) + ")";
        }

        return node.ToJsonString();
    }

    /// <summary>
    /// Format an Asset-shaped node: {currency,issuer?}.
    /// </summary>
    internal static string DescribeAssetNode(JsonNode? node)
    {
        if (node is null) return "<null>";

        if (node is JsonObject obj)
        {
            string? currency = TryString(obj, "currency");
            string? issuer = TryString(obj, "issuer");
            if (string.Equals(currency, "XRP", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(issuer))
            {
                return currency ?? "?";
            }
            return currency + " (" + ToolDisplay.Truncate(issuer) + ")";
        }

        return node.ToJsonString();
    }

    private static string? TryString(JsonObject obj, string key)
    {
        if (obj.TryGetPropertyValue(key, out JsonNode? n) && n is JsonValue v && v.TryGetValue<string>(out string? s))
        {
            return s;
        }
        return null;
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

    private static string ShortHex(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "<null>";
        return id.Length <= 16 ? id : $"{id.AsSpan(0, 8)}...{id.AsSpan(id.Length - 6, 6)}";
    }
}
