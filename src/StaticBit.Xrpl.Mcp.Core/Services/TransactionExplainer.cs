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
            case "MPTokenIssuanceCreate":
                {
                    uint? scale = ReadUInt(tx, "AssetScale");
                    string? maxAmount = ReadString(tx, "MaximumAmount");
                    uint? fee = ReadUInt(tx, "TransferFee");
                    uint? flags = ReadUInt(tx, "Flags");
                    sb.Append("MPTokenIssuanceCreate by ").Append(account)
                      .Append(": assetScale=").Append(scale?.ToString(CultureInfo.InvariantCulture) ?? "0")
                      .Append(", maxAmount=").Append(string.IsNullOrEmpty(maxAmount) ? "uncapped" : maxAmount)
                      .Append(", transferFee=").Append(fee?.ToString(CultureInfo.InvariantCulture) ?? "0")
                      .Append(", flags=").Append(DescribeMptIssuanceCreateFlags(flags)).Append('.');
                    break;
                }
            case "MPTokenIssuanceDestroy":
                {
                    string id = ShortHex(ReadString(tx, "MPTokenIssuanceID"));
                    sb.Append("MPTokenIssuanceDestroy by ").Append(account).Append(": id=").Append(id).Append('.');
                    break;
                }
            case "MPTokenIssuanceSet":
                {
                    string id = ShortHex(ReadString(tx, "MPTokenIssuanceID"));
                    string? holder = ReadString(tx, "Holder");
                    uint? flags = ReadUInt(tx, "Flags");
                    string action = flags switch
                    {
                        not null when (flags.Value & 0x1u) != 0 => "LOCK",
                        not null when (flags.Value & 0x2u) != 0 => "UNLOCK",
                        _ => "NO-OP",
                    };
                    string scope = string.IsNullOrEmpty(holder) ? "global" : "holder=" + ToolDisplay.Truncate(holder);
                    sb.Append("MPTokenIssuanceSet by ").Append(account)
                      .Append(": id=").Append(id).Append(", scope=").Append(scope)
                      .Append(", action=").Append(action).Append('.');
                    break;
                }
            case "MPTokenAuthorize":
                {
                    string id = ShortHex(ReadString(tx, "MPTokenIssuanceID"));
                    string? holder = ReadString(tx, "Holder");
                    uint? flags = ReadUInt(tx, "Flags");
                    bool unauth = flags.HasValue && (flags.Value & 0x1u) != 0;
                    string role = string.IsNullOrEmpty(holder)
                        ? (unauth ? "holder OPT-OUT" : "holder OPT-IN")
                        : (unauth ? "issuer REVOKE for " + ToolDisplay.Truncate(holder) : "issuer AUTHORIZE " + ToolDisplay.Truncate(holder));
                    sb.Append("MPTokenAuthorize by ").Append(account).Append(": id=").Append(id)
                      .Append(", ").Append(role).Append('.');
                    break;
                }
            case "LoanBrokerSet":
                {
                    string vid = ShortHex(ReadString(tx, "VaultID"));
                    string? bid = ReadString(tx, "LoanBrokerID");
                    string mode = string.IsNullOrEmpty(bid) ? "CREATE" : "MODIFY " + ShortHex(bid);
                    List<string> parts = new List<string>();
                    if (ReadUInt(tx, "CoverRateMinimum") is uint cm) parts.Add("coverMin=" + cm);
                    if (ReadUInt(tx, "CoverRateLiquidation") is uint cl) parts.Add("coverLiq=" + cl);
                    if (ReadUInt(tx, "ManagementFeeRate") is uint mf) parts.Add("feeRate=" + mf);
                    if (!string.IsNullOrEmpty(ReadString(tx, "DebtMaximum"))) parts.Add("debtMax=" + ReadString(tx, "DebtMaximum"));
                    string detail = parts.Count == 0 ? "" : ", " + string.Join(", ", parts);
                    sb.Append("LoanBrokerSet by ").Append(account).Append(": ").Append(mode)
                      .Append(" on vault ").Append(vid).Append(detail).Append('.');
                    break;
                }
            case "LoanBrokerDelete":
                {
                    string bid = ShortHex(ReadString(tx, "LoanBrokerID"));
                    sb.Append("LoanBrokerDelete by ").Append(account).Append(": ").Append(bid).Append('.');
                    break;
                }
            case "LoanBrokerCoverDeposit":
                {
                    string bid = ShortHex(ReadString(tx, "LoanBrokerID"));
                    string amount = DescribeAmountNode(tx["Amount"]);
                    sb.Append("LoanBrokerCoverDeposit by ").Append(account).Append(" into ")
                      .Append(bid).Append(": ").Append(amount).Append('.');
                    break;
                }
            case "LoanBrokerCoverWithdraw":
                {
                    string bid = ShortHex(ReadString(tx, "LoanBrokerID"));
                    string amount = DescribeAmountNode(tx["Amount"]);
                    string? dest = ReadString(tx, "Destination");
                    string destPart = string.IsNullOrEmpty(dest) || string.Equals(dest, ReadString(tx, "Account"), StringComparison.Ordinal)
                        ? "self"
                        : ToolDisplay.Truncate(dest);
                    sb.Append("LoanBrokerCoverWithdraw by ").Append(account).Append(" from ").Append(bid)
                      .Append(": ").Append(amount).Append(" → ").Append(destPart).Append('.');
                    break;
                }
            case "LoanBrokerCoverClawback":
                {
                    string? bid = ReadString(tx, "LoanBrokerID");
                    string brokerPart = string.IsNullOrEmpty(bid) ? "any-broker" : ShortHex(bid);
                    string amount = tx["Amount"] is null ? "max available" : DescribeAmountNode(tx["Amount"]);
                    sb.Append("LoanBrokerCoverClawback by issuer ").Append(account)
                      .Append(" on ").Append(brokerPart).Append(": amount=").Append(amount).Append('.');
                    break;
                }
            case "LoanSet":
                {
                    string bid = ShortHex(ReadString(tx, "LoanBrokerID"));
                    string? cp = ReadString(tx, "Counterparty");
                    string? principal = ReadString(tx, "PrincipalRequested");
                    uint? total = ReadUInt(tx, "PaymentTotal");
                    uint? flags = ReadUInt(tx, "Flags");
                    string opt = flags.HasValue && (flags.Value & 0x00010000u) != 0 ? ", overpayment-allowed" : "";
                    sb.Append("LoanSet by lender ").Append(account)
                      .Append(" → borrower ").Append(ToolDisplay.Truncate(cp))
                      .Append(" on broker ").Append(bid)
                      .Append(": principal=").Append(principal ?? "?");
                    if (total.HasValue) sb.Append(", ").Append(total.Value).Append(" payment(s)");
                    sb.Append(opt).Append('.');
                    break;
                }
            case "LoanManage":
                {
                    string lid = ShortHex(ReadString(tx, "LoanID"));
                    uint? flags = ReadUInt(tx, "Flags");
                    string action = flags switch
                    {
                        not null when (flags.Value & 0x00010000u) != 0 => "DEFAULT",
                        not null when (flags.Value & 0x00020000u) != 0 => "IMPAIR",
                        not null when (flags.Value & 0x00040000u) != 0 => "UNIMPAIR",
                        _ => "?",
                    };
                    sb.Append("LoanManage by ").Append(account).Append(": ").Append(action)
                      .Append(" loan ").Append(lid).Append('.');
                    break;
                }
            case "LoanPay":
                {
                    string lid = ShortHex(ReadString(tx, "LoanID"));
                    string amount = DescribeAmountNode(tx["Amount"]);
                    uint? flags = ReadUInt(tx, "Flags");
                    string kind = flags switch
                    {
                        not null when (flags.Value & 0x00010000u) != 0 => "overpayment",
                        not null when (flags.Value & 0x00020000u) != 0 => "full",
                        not null when (flags.Value & 0x00040000u) != 0 => "late",
                        _ => "scheduled",
                    };
                    sb.Append("LoanPay by borrower ").Append(account).Append(" on loan ").Append(lid)
                      .Append(": ").Append(amount).Append(" (").Append(kind).Append(").");
                    break;
                }
            case "LoanDelete":
                {
                    string lid = ShortHex(ReadString(tx, "LoanID"));
                    sb.Append("LoanDelete by ").Append(account).Append(": ").Append(lid).Append('.');
                    break;
                }
            case "VaultCreate":
                {
                    string asset = DescribeAssetNode(tx["Asset"]);
                    string amount = DescribeAmountNode(tx["Amount"]);
                    string? max = ReadString(tx, "AssetsMaximum");
                    uint? flags = ReadUInt(tx, "Flags");
                    List<string> flagParts = new List<string>();
                    if (flags.HasValue && (flags.Value & 0x00010000u) != 0) flagParts.Add("private");
                    if (flags.HasValue && (flags.Value & 0x00020000u) != 0) flagParts.Add("non-transferable");
                    sb.Append("VaultCreate by ").Append(account).Append(": asset=").Append(asset)
                      .Append(", initial=").Append(amount);
                    if (!string.IsNullOrEmpty(max)) sb.Append(", max=").Append(max);
                    if (flagParts.Count > 0) sb.Append(", flags=").Append(string.Join("+", flagParts));
                    sb.Append('.');
                    break;
                }
            case "VaultSet":
                {
                    string vid = ShortHex(ReadString(tx, "VaultID"));
                    List<string> parts = new List<string>();
                    string? data = ReadString(tx, "Data");
                    string? max = ReadString(tx, "AssetsMaximum");
                    string? domain = ReadString(tx, "DomainID");
                    if (!string.IsNullOrEmpty(data)) parts.Add($"Data({data.Length / 2}b)");
                    if (!string.IsNullOrEmpty(max)) parts.Add($"AssetsMaximum={max}");
                    if (domain is not null)
                    {
                        parts.Add(domain.Length == 0 ? "DomainID=CLEAR" : "DomainID=" + ShortHex(domain));
                    }
                    string changes = parts.Count == 0 ? "no-op" : string.Join(", ", parts);
                    sb.Append("VaultSet by ").Append(account).Append(" on ").Append(vid).Append(": ").Append(changes).Append('.');
                    break;
                }
            case "VaultDelete":
                {
                    string vid = ShortHex(ReadString(tx, "VaultID"));
                    sb.Append("VaultDelete by ").Append(account).Append(": ").Append(vid).Append('.');
                    break;
                }
            case "VaultDeposit":
                {
                    string vid = ShortHex(ReadString(tx, "VaultID"));
                    string amount = DescribeAmountNode(tx["Amount"]);
                    sb.Append("VaultDeposit by ").Append(account).Append(" into ").Append(vid).Append(": ").Append(amount).Append('.');
                    break;
                }
            case "VaultWithdraw":
                {
                    string vid = ShortHex(ReadString(tx, "VaultID"));
                    string amount = DescribeAmountNode(tx["Amount"]);
                    string? dest = ReadString(tx, "Destination");
                    string destPart = string.IsNullOrEmpty(dest) || string.Equals(dest, ReadString(tx, "Account"), StringComparison.Ordinal)
                        ? "self"
                        : ToolDisplay.Truncate(dest);
                    sb.Append("VaultWithdraw by ").Append(account).Append(" from ").Append(vid)
                      .Append(": ").Append(amount).Append(" → ").Append(destPart).Append('.');
                    break;
                }
            case "VaultClawback":
                {
                    string vid = ShortHex(ReadString(tx, "VaultID"));
                    string? holder = ReadString(tx, "Holder");
                    string amount = tx["Amount"] is null ? "max available" : DescribeAmountNode(tx["Amount"]);
                    sb.Append("VaultClawback by issuer ").Append(account).Append(" on ").Append(vid)
                      .Append(": from ").Append(ToolDisplay.Truncate(holder))
                      .Append(", amount=").Append(amount).Append('.');
                    break;
                }
            case "XChainCreateBridge":
                {
                    string bridge = DescribeBridgeNode(tx["XChainBridge"]);
                    string reward = DescribeAmountNode(tx["SignatureReward"]);
                    sb.Append("XChainCreateBridge by ").Append(account)
                      .Append(": ").Append(bridge).Append(", reward=").Append(reward);
                    if (tx["MinAccountCreateAmount"] is not null)
                    {
                        sb.Append(", minCreate=").Append(DescribeAmountNode(tx["MinAccountCreateAmount"]));
                    }
                    sb.Append('.');
                    break;
                }
            case "XChainModifyBridge":
                {
                    string bridge = DescribeBridgeNode(tx["XChainBridge"]);
                    List<string> parts = new List<string>();
                    if (tx["SignatureReward"] is not null) parts.Add("reward=" + DescribeAmountNode(tx["SignatureReward"]));
                    if (tx["MinAccountCreateAmount"] is not null) parts.Add("minCreate=" + DescribeAmountNode(tx["MinAccountCreateAmount"]));
                    uint? flags = ReadUInt(tx, "Flags");
                    if (flags.HasValue && (flags.Value & 0x00010000u) != 0) parts.Add("CLEAR minCreate");
                    string changes = parts.Count == 0 ? "no-op" : string.Join(", ", parts);
                    sb.Append("XChainModifyBridge by ").Append(account).Append(": ").Append(bridge)
                      .Append(" — ").Append(changes).Append('.');
                    break;
                }
            case "XChainCreateClaimID":
                {
                    string bridge = DescribeBridgeNode(tx["XChainBridge"]);
                    string reward = DescribeAmountNode(tx["SignatureReward"]);
                    string? source = ReadString(tx, "OtherChainSource");
                    sb.Append("XChainCreateClaimID by ").Append(account).Append(": ").Append(bridge)
                      .Append(", otherChainSource=").Append(ToolDisplay.Truncate(source))
                      .Append(", reward=").Append(reward).Append('.');
                    break;
                }
            case "XChainCommit":
                {
                    string? claimId = ReadString(tx, "XChainClaimID");
                    string amount = DescribeAmountNode(tx["Amount"]);
                    string? dest = ReadString(tx, "OtherChainDestination");
                    string destPart = string.IsNullOrEmpty(dest) ? "explicit-claim" : ToolDisplay.Truncate(dest);
                    sb.Append("XChainCommit by ").Append(account)
                      .Append(": claimId=").Append(claimId ?? "?")
                      .Append(", amount=").Append(amount).Append(" → ").Append(destPart).Append('.');
                    break;
                }
            case "XChainClaim":
                {
                    string? claimId = ReadString(tx, "XChainClaimID");
                    string amount = DescribeAmountNode(tx["Amount"]);
                    string? dest = ReadString(tx, "Destination");
                    sb.Append("XChainClaim by ").Append(account)
                      .Append(": claimId=").Append(claimId ?? "?")
                      .Append(", amount=").Append(amount).Append(" → ").Append(ToolDisplay.Truncate(dest)).Append('.');
                    break;
                }
            case "XChainAccountCreateCommit":
                {
                    string amount = DescribeAmountNode(tx["Amount"]);
                    string reward = DescribeAmountNode(tx["SignatureReward"]);
                    string? dest = ReadString(tx, "Destination");
                    sb.Append("XChainAccountCreateCommit by ").Append(account)
                      .Append(": create ").Append(ToolDisplay.Truncate(dest))
                      .Append(" with ").Append(amount).Append(" (reward ").Append(reward).Append(").");
                    break;
                }
            case "XChainAddClaimAttestation":
                {
                    string? claimId = ReadString(tx, "XChainClaimID");
                    string amount = DescribeAmountNode(tx["Amount"]);
                    string? signer = ReadString(tx, "AttestationSignerAccount");
                    uint? wasLocking = ReadUInt(tx, "WasLockingChainSend");
                    sb.Append("XChainAddClaimAttestation by witness ").Append(account)
                      .Append(": claimId=").Append(claimId ?? "?")
                      .Append(", amount=").Append(amount)
                      .Append(", signer=").Append(ToolDisplay.Truncate(signer))
                      .Append(", wasLocking=").Append(wasLocking == 1 ? "yes" : "no").Append('.');
                    break;
                }
            case "XChainAddAccountCreateAttestation":
                {
                    string? count = ReadString(tx, "XChainAccountCreateCount");
                    string amount = DescribeAmountNode(tx["Amount"]);
                    string? dest = ReadString(tx, "Destination");
                    sb.Append("XChainAddAccountCreateAttestation by witness ").Append(account)
                      .Append(": count=").Append(count ?? "?")
                      .Append(", amount=").Append(amount)
                      .Append(", create ").Append(ToolDisplay.Truncate(dest)).Append('.');
                    break;
                }
            case "CredentialCreate":
                {
                    string? subject = ReadString(tx, "Subject");
                    string credType = ShortHex(ReadString(tx, "CredentialType"));
                    sb.Append("CredentialCreate by issuer ").Append(account)
                      .Append(" for subject ").Append(ToolDisplay.Truncate(subject))
                      .Append(": type=").Append(credType).Append('.');
                    break;
                }
            case "CredentialAccept":
                {
                    string? issuer = ReadString(tx, "Issuer");
                    string credType = ShortHex(ReadString(tx, "CredentialType"));
                    sb.Append("CredentialAccept by subject ").Append(account)
                      .Append(" of credential from ").Append(ToolDisplay.Truncate(issuer))
                      .Append(": type=").Append(credType).Append('.');
                    break;
                }
            case "CredentialDelete":
                {
                    string? subject = ReadString(tx, "Subject");
                    string? issuer = ReadString(tx, "Issuer");
                    string credType = ShortHex(ReadString(tx, "CredentialType"));
                    string role = !string.IsNullOrEmpty(subject) && !string.IsNullOrEmpty(issuer)
                        ? "expiry sweep"
                        : !string.IsNullOrEmpty(subject)
                            ? "issuer revoke for " + ToolDisplay.Truncate(subject)
                            : "subject un-accept of " + ToolDisplay.Truncate(issuer);
                    sb.Append("CredentialDelete by ").Append(account).Append(": ").Append(role)
                      .Append(", type=").Append(credType).Append('.');
                    break;
                }
            case "PermissionedDomainSet":
                {
                    string? domainId = ReadString(tx, "DomainID");
                    int count = tx["AcceptedCredentials"] is JsonArray arr ? arr.Count : 0;
                    string mode = string.IsNullOrEmpty(domainId) ? "CREATE" : "MODIFY " + ShortHex(domainId);
                    sb.Append("PermissionedDomainSet by ").Append(account).Append(": ").Append(mode)
                      .Append(", ").Append(count).Append(" accepted credential(s).");
                    break;
                }
            case "PermissionedDomainDelete":
                {
                    string domainId = ShortHex(ReadString(tx, "DomainID"));
                    sb.Append("PermissionedDomainDelete by ").Append(account).Append(": ").Append(domainId).Append('.');
                    break;
                }
            case "DIDSet":
                {
                    List<string> fields = new List<string>();
                    string? data = ReadString(tx, "Data");
                    string? doc = ReadString(tx, "DIDDocument");
                    string? uri = ReadString(tx, "URI");
                    if (!string.IsNullOrEmpty(data)) fields.Add("Data(" + data.Length / 2 + "b)");
                    if (!string.IsNullOrEmpty(doc)) fields.Add("DIDDocument(" + doc.Length / 2 + "b)");
                    if (!string.IsNullOrEmpty(uri)) fields.Add("URI(" + uri.Length / 2 + "b)");
                    string changes = fields.Count == 0 ? "no-op" : string.Join(", ", fields);
                    sb.Append("DIDSet on ").Append(account).Append(": ").Append(changes).Append('.');
                    break;
                }
            case "DIDDelete":
                sb.Append("DIDDelete on ").Append(account).Append('.');
                break;
            case "AMMClawback":
                {
                    string? holder = ReadString(tx, "Holder");
                    string asset1 = DescribeAssetNode(tx["Asset"]);
                    string asset2 = DescribeAssetNode(tx["Asset2"]);
                    string amount = tx["Amount"] is null ? "max available" : DescribeAmountNode(tx["Amount"]);
                    sb.Append("AMMClawback by issuer ").Append(account).Append(" from holder ").Append(ToolDisplay.Truncate(holder))
                      .Append(": pool ").Append(asset1).Append(" / ").Append(asset2)
                      .Append(", amount=").Append(amount).Append('.');
                    break;
                }
            case "TicketCreate":
                {
                    uint? count = ReadUInt(tx, "TicketCount");
                    sb.Append("TicketCreate by ").Append(account)
                      .Append(": reserve ").Append(count?.ToString(CultureInfo.InvariantCulture) ?? "?")
                      .Append(" Ticket(s).");
                    break;
                }
            case "NFTokenModify":
                {
                    string nftId = ShortHex(ReadString(tx, "NFTokenID"));
                    string? owner = ReadString(tx, "Owner");
                    string? newUri = ReadString(tx, "URI");
                    sb.Append("NFTokenModify by ").Append(account).Append(": NFT ").Append(nftId);
                    if (!string.IsNullOrEmpty(owner)) sb.Append(" (held by ").Append(ToolDisplay.Truncate(owner)).Append(')');
                    sb.Append(string.IsNullOrEmpty(newUri) ? " → CLEAR URI" : $" → new URI ({newUri.Length / 2} bytes)");
                    sb.Append('.');
                    break;
                }
            case "OracleSet":
                {
                    uint? id = ReadUInt(tx, "OracleDocumentID");
                    int seriesCount = tx["PriceDataSeries"] is JsonArray arr ? arr.Count : 0;
                    uint? lastUpdate = ReadUInt(tx, "LastUpdateTime");
                    sb.Append("OracleSet by ").Append(account).Append(": id=")
                      .Append(id?.ToString(CultureInfo.InvariantCulture) ?? "?")
                      .Append(", ").Append(seriesCount).Append(" price entr").Append(seriesCount == 1 ? "y" : "ies");
                    if (lastUpdate is not null) sb.Append(", lastUpdate=").Append(lastUpdate);
                    sb.Append('.');
                    break;
                }
            case "OracleDelete":
                {
                    uint? id = ReadUInt(tx, "OracleDocumentID");
                    sb.Append("OracleDelete by ").Append(account).Append(": id=")
                      .Append(id?.ToString(CultureInfo.InvariantCulture) ?? "?").Append('.');
                    break;
                }
            case "DelegateSet":
                {
                    string? authorize = ReadString(tx, "Authorize");
                    JsonArray? perms = tx["Permissions"] as JsonArray;
                    int count = perms?.Count ?? 0;
                    sb.Append("DelegateSet by ").Append(account).Append(": ");
                    if (count == 0)
                    {
                        sb.Append("CLEAR delegation");
                        if (!string.IsNullOrEmpty(authorize)) sb.Append(" to ").Append(ToolDisplay.Truncate(authorize));
                    }
                    else
                    {
                        sb.Append("delegate to ").Append(ToolDisplay.Truncate(authorize)).Append(" for ").Append(count).Append(" tx-type(s)");
                        List<string> typeNames = new List<string>();
                        if (perms is not null)
                        {
                            foreach (JsonNode? entry in perms)
                            {
                                JsonNode? perm = entry?["Permission"];
                                if (perm?["PermissionValue"] is JsonValue pv && pv.TryGetValue<string>(out string? pvStr))
                                {
                                    typeNames.Add(pvStr);
                                }
                            }
                        }
                        if (typeNames.Count > 0) sb.Append(" [").Append(string.Join(",", typeNames)).Append(']');
                    }
                    sb.Append('.');
                    break;
                }
            case "Batch":
                {
                    uint? flags = ReadUInt(tx, "Flags");
                    string mode = flags switch
                    {
                        not null when (flags.Value & 0x00010000u) != 0 => "AllOrNothing",
                        not null when (flags.Value & 0x00020000u) != 0 => "OnlyOne",
                        not null when (flags.Value & 0x00040000u) != 0 => "UntilFailure",
                        not null when (flags.Value & 0x00080000u) != 0 => "Independent",
                        _ => "?",
                    };
                    int innerCount = tx["RawTransactions"] is JsonArray arr ? arr.Count : 0;
                    int signerCount = tx["BatchSigners"] is JsonArray sarr ? sarr.Count : 0;
                    sb.Append("Batch by ").Append(account).Append(": mode=").Append(mode)
                      .Append(", ").Append(innerCount).Append(" inner tx");
                    if (signerCount > 0) sb.Append(", ").Append(signerCount).Append(" batchSigner(s)");
                    if (innerCount > 0 && tx["RawTransactions"] is JsonArray arr2)
                    {
                        sb.Append(" [");
                        for (int i = 0; i < arr2.Count; i++)
                        {
                            if (i > 0) sb.Append("; ");
                            JsonNode? inner = arr2[i]?["RawTransaction"];
                            sb.Append(inner is null ? "?" : Explain(inner));
                        }
                        sb.Append(']');
                    }
                    sb.Append('.');
                    break;
                }
            default:
                sb.Append(txType).Append(" from ").Append(account).Append('.');
                break;
        }
    }

    private static string DescribeMptIssuanceCreateFlags(uint? flags)
    {
        if (!flags.HasValue || flags.Value == 0) return "[none]";
        List<string> parts = new List<string>();
        if ((flags.Value & 2u) != 0) parts.Add("CanLock");
        if ((flags.Value & 4u) != 0) parts.Add("RequireAuth");
        if ((flags.Value & 8u) != 0) parts.Add("CanEscrow");
        if ((flags.Value & 16u) != 0) parts.Add("CanTrade");
        if ((flags.Value & 32u) != 0) parts.Add("CanTransfer");
        if ((flags.Value & 64u) != 0) parts.Add("CanClawback");
        return parts.Count == 0 ? "[0x" + flags.Value.ToString("X", CultureInfo.InvariantCulture) + "]" : "[" + string.Join("|", parts) + "]";
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

    private static string DescribeBridgeNode(JsonNode? node)
    {
        if (node is null) return "<null>";
        string ld = node["LockingChainDoor"] is JsonValue lv && lv.TryGetValue<string>(out string? ldStr) ? ToolDisplay.Truncate(ldStr) : "?";
        string id = node["IssuingChainDoor"] is JsonValue iv && iv.TryGetValue<string>(out string? idStr) ? ToolDisplay.Truncate(idStr) : "?";
        string li = DescribeAssetNode(node["LockingChainIssue"]);
        string ii = DescribeAssetNode(node["IssuingChainIssue"]);
        return $"{ld} [{li}] ↔ {id} [{ii}]";
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
