>  🌐 **Язык**: [English](../../examples/oracle-price-feed.md) | **Русский**

# Пример: Oracle price feed publisher (XLS-47)

Cowork-агент периодически (раз в N минут) pull'ит цену с внешнего HTTP API (CoinGecko, Binance, Chainlink off-chain), обновляет on-chain Oracle ledger entry через `OracleSet`. Используется для AMM-based DeFi, lending protocols, options pricing — любого on-chain контракта которому нужны real-world rates.

Референс: [TestIOracle.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIOracle.cs).

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_oracle_set_prepare`, `xrpl_oracle_delete_prepare`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_ledger` (для close-time), `xrpl_account_objects` (для проверки существования Oracle) |
| **xrpl-signer** | `xrpl_sign` |
| **HTTP fetch** | внешний (через generic tool или curl wrapper — зависит от deployment'а) |
| **scheduler** | `/loop` или `/schedule` (slash-commands harness) для periodic invocation |

## Концепция (XLS-47)

- Oracle identified by `(Account, OracleDocumentID)` — `OracleDocumentID` это uint32, аккаунт-локальный.
- `PriceDataSeries` — массив 1..10 entries формата `{BaseAsset, QuoteAsset, AssetPrice?, Scale?}`.
- `AssetPrice` это **scaled uint** — реальное значение = `AssetPrice / 10^Scale`. Например, AssetPrice=155000, Scale=6 → реальная цена = 0.155 (если цена XRP/USD ≈ $0.155).
- `LastUpdateTime` — Unix timestamp (seconds), должен быть в пределах **300 секунд** от ledger close time. Иначе `tecINVALID_UPDATE_TIME`.
- Первые 5 entries в `PriceDataSeries` помещаются в один owner-reserve slot; entries 6-10 требуют второй slot.
- На update'е omit `Provider` и `AssetClass` — они required только при первом создании.

## Архитектура polling-publisher'а

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

- 1 funded аккаунт для Oracle owner ($1+ XRP остатка).
- Outside-MCP HTTP fetch capability (через MCP-host's `fetch` или generic tool — зависит от вашего setup'а).
- XLS-47 amendment активирован (на стандартном testnet — да).

## Промт агента

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

Цена `0.155 USD за 1 XRP`.

### 2. Scale to uint

```text
price = 0.155
scale = 6
assetPrice = round(price * 10^scale) = 155000
```

Выбор scale = trade-off precision vs uint64 capacity. Для всех major fiat pairs scale=6 даёт sub-millisatoshi precision и вообще не приближается к uint64 limit.

### 3. Get ledger close time

```text
agent → xrpl_ledger(network="testnet", ledgerIndex="validated")
       → { "ledger": { "close_time": 1737000000 /* unix seconds */, ... } }
```

LastUpdateTime должно быть в окне `[close_time-300, close_time+300]` — иначе tx fails. Используем сразу `close_time` — он в окне по определению.

### 4. Check if Oracle exists (creation vs update)

```text
agent → xrpl_account_objects(network, account=oracleOwner, type="Oracle")
       → { "AccountObjects": [{ "OracleDocumentID": 42, ... }] }
```

Если есть `OracleDocumentID=42` — update path. Если нет — first-time creation.

### 5. Prepare + sign + submit

**Creation path** (требует provider + assetClass):

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

**Update path** (provider/assetClass можно опустить, scale можно изменить — entries полностью заменяются):

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

Reserve возвращается. Down-stream contracts использующие этот Oracle, начинают получать "Oracle not found" — нужно их обновить заранее.

## Periodicity / scheduling

**Через `/loop`** (само-pacing):
```bash
/loop oracle-feed
```
Agent сам решает когда снова запуститься через `ScheduleWakeup(delaySeconds=600)`.

**Через `/schedule`** (внешний cron):
```bash
/schedule "every 10 minutes" oracle-feed --params '{"oracleOwner":"...","oracleDocumentId":42, ...}'
```

Cron creates a routine that fires the agent on the given crontab.

## Multi-feed (до 10 pairs)

Один Oracle может содержать до 10 PriceData entries. На каждом update — отправляется новый full массив (rippled replaces, not patches). Полезно для bundling нескольких pairs в одно update'е:

```json
[
  {"baseAsset":"XRP","quoteAsset":"USD","assetPrice":"155000","scale":6},
  {"baseAsset":"BTC","quoteAsset":"USD","assetPrice":"6750000","scale":2},
  {"baseAsset":"ETH","quoteAsset":"USD","assetPrice":"3450000","scale":3}
]
```

Все три обновятся atomic-ally одной транзакцией. Один fee.

## Verification

- `engine_result == "tesSUCCESS"`.
- `xrpl_account_objects(account=oracleOwner, type="Oracle")` показывает entry с обновлённым `LastUpdateTime` и новыми `PriceDataSeries`.
- Down-stream contracts (читающие Oracle через rippled APIs) видят свежую цену.

## Гарантии и подводные камни

- **Stale data**: rippled НЕ enforce'ит периодичность updates. Если agent падает, цена остаётся "застывшей". Consumers должны проверять `LastUpdateTime` относительно current ledger time.
- **Source manipulation**: oracle полагается на trust в single account (Oracle owner). Для DeFi-grade reliability используют multiple independent oracles + on-chain medianization (например, агент A read'ит несколько Oracle entries разных owners и computes median).
- **TWAP / VWAP** для anti-manipulation: оракл хранит spot price, time-weighted average computes consumer. Не делайте averaging в agent'е — это снижает observability.
- **LastUpdateTime drift**: если агент работает в loop'е с slow polling, `lastUpdateTimeUnix` может оказаться > 300s в прошлое к моменту autofill'а → `tecINVALID_UPDATE_TIME`. Используйте свежий `xrpl_ledger` close_time **прямо перед** prepare'ом.

## Use-cases

- **Lending protocol price oracle**: liquidation engine читает price.
- **Synthetic asset issuer**: synthetic XRP/USD pair, mint/burn at on-chain rate.
- **AMM with off-chain price reference**: бот следит за divergence между AMM pool ratio и Oracle, делает arbitrage trades.
- **Options pricing**: settlement price для on-chain options contracts.

## Расширения

- **Multi-source aggregation**: внутри одного OracleSet'а entries от разных providers; consumer вычисляет median. Но on-chain entries identified by `(BaseAsset, QuoteAsset)` — нельзя иметь дубликаты. Workaround — несколько Oracle owners.
- **Heartbeat monitoring**: отдельный watcher-agent следит за `LastUpdateTime`; если > N минут — alert через Telegram (см. monitor-balance-telegram.md).
- **Fallback chain**: если CoinGecko падает — agent fall'ит на Binance API → Chainlink → cached value.
