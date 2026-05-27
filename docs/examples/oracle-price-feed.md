> 🇷🇺 [Прочесть на русском](oracle-price-feed.ru.md)

# Example: Oracle price feed publisher (XLS-47)

A Cowork agent that periodically (every N minutes) pulls a price from an external HTTP API (CoinGecko, Binance, off-chain Chainlink), then updates an on-chain Oracle ledger entry via `OracleSet`. Used by AMM-based DeFi, lending protocols, options pricing — any on-chain contract that needs real-world rates.

Reference: [TestIOracle.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIOracle.cs).

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_oracle_set_prepare`, `xrpl_oracle_delete_prepare`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_ledger` (for close-time), `xrpl_account_objects` (to verify Oracle existence) |
| **xrpl-signer** | `xrpl_sign` |
| **HTTP fetch** | external (via a generic tool or curl wrapper — depends on the deployment) |
| **scheduler** | `/loop` or `/schedule` (slash-commands harness) for periodic invocation |

## Concept (XLS-47)

- An Oracle is identified by `(Account, OracleDocumentID)` — `OracleDocumentID` is uint32, account-scoped.
- `PriceDataSeries` — array of 1..10 entries shaped `{BaseAsset, QuoteAsset, AssetPrice?, Scale?}`.
- `AssetPrice` is a **scaled uint** — real value = `AssetPrice / 10^Scale`. E.g. AssetPrice=155000, Scale=6 → real price = 0.155 (when XRP/USD ≈ $0.155).
- `LastUpdateTime` — Unix timestamp (seconds); must be within **300 seconds** of ledger close time. Otherwise `tecINVALID_UPDATE_TIME`.
- The first 5 PriceData entries fit in one owner-reserve slot; entries 6–10 require a second slot.
- On update you may omit `Provider` and `AssetClass` — they're only required at first creation.

## Architecture of the polling publisher

```
┌─────────────────┐  every 10 min   ┌─────────────────┐  GET /price/XRP-USD  ┌─────────────┐
│ /loop scheduler │ ──────────────► │ oracle-feed     │ ───────────────────► │  CoinGecko  │
│                 │                 │  agent          │ ◄─────── { price }── │  / Binance  │
└─────────────────┘                 └────────┬────────┘                       └─────────────┘
                                             │
                                             │ format price → scaled uint
                                             │ fetch ledger close_time
                                             ▼
                                  ┌─────────────────────────┐
                                  │ xrpl_oracle_set_prepare │
                                  │ → preflight             │
                                  │ → xrpl_sign             │
                                  │ → submit                │
                                  └─────────────────────────┘
                                             │
                                             ▼
                                          rippled
```

## Pre-requisites

- 1 funded Oracle-owner account ($1+ XRP balance).
- Out-of-MCP HTTP fetch capability (via the MCP host's `fetch` or a generic tool — depends on your setup).
- XLS-47 amendment is active (yes on standard testnet).

## Agent prompt

```markdown
---
name: oracle-feed-publisher
description: Periodically fetches an off-chain price and publishes it on-chain
  via OracleSet. Designed to run on /loop or /schedule.
tools: xrpl_oracle_set_prepare, xrpl_oracle_delete_prepare, xrpl_ledger,
  xrpl_account_objects, xrpl_tx_preflight, xrpl_tx_submit_signed, xrpl_sign,
  WebFetch
---

Input:
{
  "network":"testnet",
  "oracleOwner":"r...",
  "oracleDocumentId": 42,
  "provider": "CoinGecko",    // only required on first call (creation)
  "assetClass": "currency",   // only required on first call
  "feeds": [
    { "baseAsset":"XRP","quoteAsset":"USD","sourceUrl":"https://api.coingecko.com/.../xrp-usd","scale":6 },
    { "baseAsset":"BTC","quoteAsset":"USD","sourceUrl":"https://api.coingecko.com/.../btc-usd","scale":2 }
  ]
}

For each invocation:
1. For each feed:
   a. WebFetch(sourceUrl) → parse JSON → extract numeric price.
   b. assetPrice = round(price * 10^scale). Validate fits in uint64.
2. Get current ledger close time: xrpl_ledger(network, ledgerIndex="validated")
   → extract `close_time` (Unix seconds). Use as `lastUpdateTimeUnix`.
   (Must be within 300s of submit time — close enough.)
3. Determine if Oracle exists:
   xrpl_account_objects(network, account=oracleOwner, type="Oracle")
   → check if entry with matching OracleDocumentID exists.
   If NOT exists → first-time creation, must include provider+assetClass.
   If exists → update, can omit provider/assetClass.
4. Build priceDataSeriesJson:
   [{"baseAsset":"XRP","quoteAsset":"USD","assetPrice":"155000","scale":6}, ...]
5. xrpl_oracle_set_prepare(network, account=oracleOwner, oracleDocumentId,
   lastUpdateTimeUnix, priceDataSeriesJson, provider, uri, assetClass)
6. xrpl_tx_preflight — must satisfy feasible=true (PriceDataSeries 1..10,
   LastUpdateTime non-zero, etc).
7. xrpl_sign(walletName=oracleOwner, blob).
8. xrpl_tx_submit_signed(blob, waitForValidation=true).

Return:
{
  "txHash":"...","engineResult":"tesSUCCESS",
  "updates":[{ "baseAsset":"XRP","quoteAsset":"USD","price":0.155,
               "assetPrice":155000,"scale":6 }, ...]
}
```

## Step-by-step (single feed)

### 1. Fetch external price

```text
agent → WebFetch("https://api.coingecko.com/api/v3/simple/price?ids=ripple&vs_currencies=usd")
       → { "ripple": { "usd": 0.155 } }
```

Price `0.155 USD per 1 XRP`.

### 2. Scale to uint

```text
price = 0.155
scale = 6
assetPrice = round(price * 10^scale) = 155000
```

Choice of scale = trade-off precision vs uint64 capacity. For all major fiat pairs scale=6 gives sub-millisatoshi precision and doesn't come close to the uint64 limit.

### 3. Get ledger close time

```text
agent → xrpl_ledger(network="testnet", ledgerIndex="validated")
       → { "ledger": { "close_time": 1737000000 /* unix seconds */, ... } }
```

LastUpdateTime must sit in the window `[close_time-300, close_time+300]` — otherwise tx fails. Use the `close_time` directly — it sits in the window by definition.

### 4. Check if Oracle exists (creation vs update)

```text
agent → xrpl_account_objects(network, account=oracleOwner, type="Oracle")
       → { "AccountObjects": [{ "OracleDocumentID": 42, ... }] }
```

If `OracleDocumentID=42` exists → update path. Otherwise — first-time creation.

### 5. Prepare + sign + submit

**Creation path** (requires provider + assetClass):

```text
agent → xrpl_oracle_set_prepare(
  network="testnet", account=oracleOwner,
  oracleDocumentId=42, lastUpdateTimeUnix=1737000000,
  priceDataSeriesJson=
    '[{"baseAsset":"XRP","quoteAsset":"USD","assetPrice":"155000","scale":6}]',
  provider="CoinGecko",
  uri="https://api.coingecko.com/api/v3",
  assetClass="currency"
)
→ xrpl_tx_preflight → feasible=true
→ xrpl_sign → blob_signed
→ xrpl_tx_submit_signed(waitForValidation=true) → tesSUCCESS
```

**Update path** (provider/assetClass can be omitted; scale is changeable — entries fully replaced):

```text
agent → xrpl_oracle_set_prepare(
  ... oracleDocumentId=42, lastUpdateTimeUnix=..., priceDataSeriesJson=...
  /* provider, assetClass — null */
)
→ ... submit → tesSUCCESS
```

### 6. Delete (cleanup at end of life)

```text
agent → xrpl_oracle_delete_prepare(network, account=oracleOwner, oracleDocumentId=42)
→ sign + submit → tesSUCCESS
```

Reserve is released. Downstream contracts that reference this Oracle start getting "Oracle not found" — update them in advance.

## Periodicity / scheduling

**Via `/loop`** (self-pacing):
```bash
/loop oracle-feed
```
The agent decides when to wake up next via `ScheduleWakeup(delaySeconds=600)`.

**Via `/schedule`** (external cron):
```bash
/schedule "every 10 minutes" oracle-feed --params '{"oracleOwner":"...","oracleDocumentId":42, ...}'
```

Cron creates a routine that fires the agent on the given crontab.

## Multi-feed (up to 10 pairs)

A single Oracle may contain up to 10 PriceData entries. On every update a new full array is sent (rippled replaces, not patches). Useful for bundling multiple pairs into one update:

```json
[
  {"baseAsset":"XRP","quoteAsset":"USD","assetPrice":"155000","scale":6},
  {"baseAsset":"BTC","quoteAsset":"USD","assetPrice":"6750000","scale":2},
  {"baseAsset":"ETH","quoteAsset":"USD","assetPrice":"3450000","scale":3}
]
```

All three update atomically in a single transaction. One fee.

## Verification

- `engine_result == "tesSUCCESS"`.
- `xrpl_account_objects(account=oracleOwner, type="Oracle")` shows an entry with updated `LastUpdateTime` and the new `PriceDataSeries`.
- Downstream contracts (reading the Oracle via rippled APIs) see the fresh price.

## Guarantees and gotchas

- **Stale data**: rippled does NOT enforce update cadence. If the agent crashes, the price "freezes". Consumers must check `LastUpdateTime` against current ledger time.
- **Source manipulation**: the oracle trusts a single account (Oracle owner). For DeFi-grade reliability use multiple independent oracles + on-chain medianization (e.g. agent A reads several Oracle entries from different owners and computes the median).
- **TWAP / VWAP** for anti-manipulation: the oracle stores spot price; the consumer computes the time-weighted average. Don't average inside the agent — it reduces observability.
- **LastUpdateTime drift**: if the agent runs in a loop with slow polling, `lastUpdateTimeUnix` may end up > 300s in the past by autofill time → `tecINVALID_UPDATE_TIME`. Fetch a fresh `xrpl_ledger` close_time **right before** the prepare.

## Use-cases

- **Lending protocol price oracle**: liquidation engine reads the price.
- **Synthetic asset issuer**: synthetic XRP/USD pair, mint/burn at the on-chain rate.
- **AMM with off-chain price reference**: a bot watches the divergence between AMM pool ratio and the Oracle, runs arbitrage trades.
- **Options pricing**: settlement price for on-chain options contracts.

## Extensions

- **Multi-source aggregation**: inside one OracleSet, entries from different providers; the consumer computes the median. But on-chain entries are identified by `(BaseAsset, QuoteAsset)` — duplicates aren't allowed. Workaround — multiple Oracle owners.
- **Heartbeat monitoring**: a separate watcher agent tracks `LastUpdateTime`; if > N minutes — alert via Telegram (see monitor-balance-telegram.md).
- **Fallback chain**: if CoinGecko fails — the agent falls back to Binance API → Chainlink → cached value.
