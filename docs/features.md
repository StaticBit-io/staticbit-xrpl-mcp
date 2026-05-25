# Roadmap: доработки и расширения StaticBit XRPL MCP

Живой backlog предложений по развитию проекта. Не утверждено — обсуждается и приоритизируется по мере планирования релизов.

Текущее покрытие (baseline на момент v0.4.0 marketplace, server `0.1.0`, signer `0.1.1`):

- **21 MCP-tool** в Core: AccountTools, AmmTools, AmmTransactionTools, DexTools, LedgerTools, OfferTools, PaymentTools, TransactionTools.
- **Signer tools**: `xrpl_sign`, `xrpl_sign_multi`, `xrpl_sign_combine`, `xrpl_wallet_{generate,address,export,list,remove,import_seed,import_mnemonic,import_text,import_xumm}`.
- Typed prepare-обёртки: `Payment`, `TrustSet`, `OfferCreate`, `OfferCancel`, `AMMDeposit`, `AMMWithdraw`.
- Всё остальное — через escape hatch `xrpl_tx_prepare_generic`.

---

## 1. Покрытие XRPL-транзакций (typed prepare-обёртки)

Сейчас всё, кроме Payment / TrustSet / OfferCreate / OfferCancel / AMMDeposit / AMMWithdraw, идёт через `xrpl_tx_prepare_generic`. Это работает, но LLM-агенту тяжелее правильно собрать JSON, а юзеру тяжелее ревьюить.

### 1.1. Account-управление

- [x] `xrpl_account_set_prepare` — `AccountSet` со всеми флагами (`DefaultRipple`, `RequireAuth`, `RequireDest`, `DepositAuth`, `AllowTrustLineClawback`, `DisallowIncomingXRP/NFTokenOffer/Check/PayChan/Trustline`), полями `Domain`, `EmailHash`, `MessageKey`, `TickSize`, `TransferRate`, `NFTokenMinter`.
- [x] `xrpl_set_regular_key_prepare` — `SetRegularKey`.
- [x] `xrpl_signer_list_set_prepare` — `SignerListSet` (для multi-sign).
  - [ ] Helper "сколько подписей ещё нужно" поверх `account_objects` — ещё не реализован.
- [x] `xrpl_deposit_preauth_prepare` — `DepositPreauth` (whitelist отправителей). XLS-70 credential-based вариант пока не покрыт.
- [x] `xrpl_account_delete_prepare` — `AccountDelete` (базово). Pre-flight по owned objects и sequence-distance — ещё не реализован.

### 1.2. NFT (XLS-20)

- [x] Write: `NFTokenMint`, `NFTokenBurn`, `NFTokenCreateOffer`, `NFTokenCancelOffer`, `NFTokenAcceptOffer`.
- [x] Read: `account_nfts`, `nft_buy_offers`, `nft_sell_offers`.
  - [ ] `nft_info`, `nft_history` — отсутствуют в SDK как типизированные методы, нужно сделать через GenericRequest.

### 1.3. Escrow

- [x] `EscrowCreate`, `EscrowFinish`, `EscrowCancel` prepare.
- [x] Поддержка crypto-conditions (PREIMAGE-SHA-256) для `Condition`/`Fulfillment`.
- [ ] Read-helper: фильтр `account_objects` по `LedgerEntryType=Escrow` + human-readable summary.

### 1.4. PaymentChannel

- [x] `PaymentChannelCreate`, `PaymentChannelFund`, `PaymentChannelClaim` prepare.
- [ ] Offline-helpers: `channel_authorize` (signer-side) + `channel_verify` (core-side, без сети).

### 1.5. Check

- [x] `CheckCreate`, `CheckCash`, `CheckCancel` prepare.

### 1.6. AMM (XLS-30) — закрыть остаток

- [x] `AMMCreate`, `AMMVote`, `AMMBid`, `AMMDelete`.

### 1.7. Token-issuer flow

- [x] `xrpl_clawback_prepare` — `Clawback`.
- [x] Freeze/Unfreeze helper над `TrustSet` (флаги `tfSetFreeze`/`tfClearFreeze`, `tfSetDeepFreeze`/`tfClearDeepFreeze`).

### 1.8. Multi-sign UX

- [x] `xrpl_sign_multi`, `xrpl_sign_combine` уже есть.
- [ ] Тул "сколько подписей собрано / ещё нужно" (читает `SignerList` + сравнивает с пришедшими `SignerEntries` в blob).

---

## 2. Read API и streaming

| Tool | Статус | Заметки |
|---|---|---|
| `xrpl_ripple_path_find` | ✅ | One-shot pathfinder для cross-currency Payment (HTTP/WebSocket). |
| `xrpl_path_find_create` / `_status` / `_close` | ✅ | WebSocket-only long-running pathfinder; работает только на stdio-deployment. |
| `xrpl_gateway_balances` | ✅ | Issuer obligations, with hotwallet exclusion. |
| `xrpl_server_state` | ✅ | Machine-readable load factors, validation quorum, build. |
| `xrpl_server_definitions` | ✅ | FIELDS/LEDGER_ENTRY_TYPES/TRANSACTION_TYPES + content hash. |
| `xrpl_subscribe` / `xrpl_unsubscribe` | ⚠️ Pass-through | Подписка ставится на shared WebSocket пула; события **не возвращаются** обратно через MCP (см. ниже). |
| `xrpl_account_tx_since` | ✅ | Polling-based alternative to subscribe: stateless, работает на cloud/local/HTTP. |
| `xrpl_manifest` | ⏳ | В SDK нет типизированного метода — нужно делать через `GRequest<>`. Не реализовано. |

### Subscribe/unsubscribe — честные ограничения

MCP — request/response, **не двунаправленный канал**. Cloud-сервер держит ОДНУ WebSocket-сессию на network через `XrplClientPool`, разделяемую между всеми MCP-клиентами. `xrpl_subscribe` ставит подписку, но события приходят на ту общую WebSocket и **никак не доставляются обратно** конкретному MCP-вызову.

Tool оставлен как plumbing для будущих server-side watchers / admin alerts (например, AdminAlerter мог бы их потреблять).

**Для реального мониторинга** — использовать `xrpl_account_tx_since`: passing the highest `ledger_index` seen previously gives stateless, idempotent polling that works в любом deployment-режиме.

- [ ] Полноценный streaming через SSE/long-poll resource-update — отложено до того момента, когда MCP-протокол / Claude Code consumer-side это поддержит.

---

## 3. UX-улучшения подготовки транзакций

- [x] **`xrpl_tx_simulate`** — dry-run симуляция через `xrpl_tx_preflight` + (для Payment) `ripple_path_find` + текущий открытый fee + рекомендуемый LastLedgerSequence.
- [x] **`xrpl_tx_explain`** — превращает decoded tx (blob hex или JSON) в человеческую строку: *"Payment from rA... to rB...: 10000000 drops XRP. [fee=12 drops, seq=42, LLS=1234]"*. Pure local, без сетевых вызовов. Покрывает все основные типы (Payment, TrustSet, Offer*, AMM*, NFToken*, Escrow*, Check*, PaymentChannel*, AccountSet, SetRegularKey, DepositPreauth, SignerListSet, AccountDelete, Clawback) + generic fallback.
- [x] **`xrpl_tx_preflight`** — проверяет `account_info` + `server_state`, считает резервы (base + owner_count × inc), сравнивает с balance, для Payment ещё проверяет `RequireDestinationTag`/`DepositAuth`/`DisallowIncomingXRP` на destination. Возвращает structured report с warnings[].
- [x] **Fee escalation policy** — `LastLedgerSequenceOffset` теперь реально применяется в `TransactionPreparer` (раньше был мёртвой опцией); добавлен `FeeBumpMultiplier` для проактивного over-pay во время open-ledger escalation.
- [x] **`xrpl_tx_decode_blob`** — теперь возвращает осмысленные ошибки (odd length / non-hex / decode exception / null result) вместо пустого `{}`.

---

## 4. Signer — расширение

- [ ] **HD-кошельки (BIP-44)** — сейчас один seed = один аккаунт; добавить derivation indices.
- [ ] **Аппаратные кошельки**: integration с Ledger Nano (через HID или `Ledger.NET`). Опционально.
- [ ] **Audit log**: append-only `~/.staticbit/signer-audit.log` с фактом подписания (tx hash, wallet name, время) — для compliance.
- [ ] **Per-wallet passphrase** — сейчас один master шифрует все записи; добавить режим, где каждая запись имеет свой пароль (тогда `xrpl_sign` запрашивает его в payload).
- [ ] **Импорт из стандартных форматов**: keypair JSON, RippleAdmin, xrpl.js wallet JSON.
- [ ] **OS-биометрика** для разблокировки master-passphrase: macOS Keychain, Windows DPAPI, Linux libsecret. Опционально.

---

## 5. Server-инфраструктура

- [x] **Тесты Server**: 47 unit-тестов — BearerAuthMiddleware (health bypass, 404 для scanners, HTTPS-enforcement, X-Forwarded-Proto, empty tokens, missing/wrong/valid bearer, label assignment, X-Forwarded-For first hop); AdminAlerter (disabled, event-disabled, dedup window + после окна, разные tags не схлопываются, rate cap + reset после минуты + 0=отключено, queue capacity DropOldest); RequestLoggingMiddleware (disabled, health-skip, query-string opt-in/opt-out, label, status code, logs-on-exception); ServerOptions defaults; ResolveRateLimitPartitionKey (ip/token/both/unknown, case insensitive).
- [x] **Structured request logging** с redaction — `RequestLoggingMiddleware` логирует Method, Path, Status, Duration, IP, label. Bodies НЕ логируются (могут содержать r-addresses/amounts). Query string opt-in через `RequestLogging.IncludeQueryString`.
- [x] **CORS** — `ServerOptions.Cors` (`Enabled`, `AllowedOrigins[]`, `AllowedHeaders[]`, `AllowedMethods[]`, `AllowCredentials`). Поддерживает wildcard `["*"]` для AllowedOrigins. Подключается ДО bearer-auth чтобы OPTIONS preflights не требовали токен.
- [x] **Graceful shutdown** для long-poll `wait_for_validation` — суженный catch в `TransactionTools.SubmitSignedAsync` (только `Exception` без `OperationCanceledException`). Cancellation теперь пробрасывается немедленно.
- [x] **Rate-limit per token** — `RateLimitOptions.PartitionBy={ip|token|both}` (default `ip` для совместимости). При `token`/`both` partition key включает bearer label. AdminAlert при превышении теперь включает и label, и ip.
- [ ] **OpenTelemetry / metrics**: счётчики MCP-вызовов по tool, latency, network errors к XRPL, размер connection pool. Prometheus-endpoint на `/metrics`. — отложено.
- [ ] **Connection pool health**: `XrplClientPool` — TTL соединений, ping/pong, авто-реконнект при разрыве WS. — отложено.
- [ ] **Integration-тесты** против rippled testnet/devnet — отдельная категория `[TestCategory("Integration")]`, в CI запускать по schedule, не на каждый PR. — отложено.

---

## 6. Качество кода — мелкие чистки

- [ ] `TransactionTools.ExtractBool` / `ExtractUInt` делают полный re-serialize объекта чтобы прочесть одно поле — заменить на `JsonNode` без промежуточной строки.
- [ ] Конвенция имён тестов: `*TestsU.cs` суффикс `U` нестандартен — либо документировать в `CLAUDE.md`/`README`, либо привести к `*Tests.cs`.
- [ ] `Directory.Build.props` — централизовать также `TargetFramework` (сейчас, видимо, в каждом csproj).
- [ ] Унифицировать имена network-параметров: `'mainnet' | 'testnet' | 'devnet' | wss://...` — вынести в общую константу + единое описание `[Description]`.

---

## 7. Дистрибуция и supply chain

- [ ] **Автогенерация GitHub release notes** из commit-history между тегами плагинов.
- [ ] **Notarization** macOS-бинарей signer (иначе Gatekeeper блокирует у юзеров).
- [ ] **Authenticode-подпись** Windows-бинарей.
- [ ] **SBOM (CycloneDX)** + SLSA attestation для бинарей в плагинах. Особенно важно для signer (он держит ключи).
- [ ] **Reproducible builds** для `build-signer-binaries.sh` — чтобы юзер мог верифицировать что в плагине именно тот код, что в репо.

---

## 8. Документация и SDK-парность

- [ ] `INSTALL.md` — раздел troubleshooting per-OS (Linux SELinux/AppArmor для signer, macOS Gatekeeper, Windows SmartScreen).
- [ ] **OpenAPI / JSON-schema** для всех MCP-tool'ов — генерировать из `[McpServerTool]` reflection-проходом, выкладывать в репо для third-party агентов.
- [ ] Пример Cowork-агента: "monitor my balance and Telegram me on incoming Payment".
- [ ] Глоссарий XRPL-терминов в `docs/` (drops, reserve, NoRipple, DefaultRipple, ledger states).

---

## Предлагаемые приоритеты на следующую веху

Не утверждено — обсуждается:

1. **NFT + Escrow + AccountSet typed wrappers** (§1.1, §1.2, §1.3) — закрывает 80% реальных юзкейсов и убирает зависимость от хрупкого `tx_prepare_generic`.
2. **`xrpl_tx_explain`** (§3) — критично для UX подписания, независимо от остального.
3. **Тесты на Server** (§5) — пустой проект на CI это явный долг.
4. **`xrpl_ripple_path_find`** (§2) — без него cross-currency payments через агента фактически не работают.
