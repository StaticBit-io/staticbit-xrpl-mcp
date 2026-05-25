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

- [x] `TransactionTools.ExtractBool` / `ExtractUInt` — заменены на `ReadBool`/`ReadUInt` поверх `JsonNode`. Старая реализация re-serialize-then-parse'ила объект каждый вызов; в `SubmitSignedAsync` поллинг-цикле было 3 re-serialize на итерацию, теперь один. `TryGetTxHash` тоже переписан на `JsonNode`.
- [x] Конвенция тестов `*TestsU.cs` + префикс `TestU_` — задокументирована в README.md секция "Конвенция тестов" с пояснением что `U`=Unit, обоснованием фильтра `--filter TestU` и резервированием `*TestsI.cs` / `TestI_*` под будущие интеграционные.
- [x] `Directory.Build.props` — добавлен `<TargetFramework>net10.0</TargetFramework>` как default; убран из Core/Server/Signer/Core.Tests/Server.Tests/Signer.Tests csproj. `Abstractions.csproj` сохранил явный override `netstandard2.1` (это намеренно — для embed'инга в более старые хосты).
- [x] Унифицировано описание `network` и `ledgerIndex` параметров — вынесены в `public const string` в `Services/ToolDescriptions.cs`, заменены в 20 файлах (двух вариаций фразы про ledgerIndex приведены к одной канонической).

---

## 7. Дистрибуция и supply chain

Полная сводка + setup-гайд: [docs/supply-chain.md](supply-chain.md).

- [x] **Автогенерация GitHub release notes** — `release-plugin.sh::group_by_conventional_commit` парсит conventional commits (feat/fix/perf/refactor/docs/test/build/ci/chore) между тегами и группирует под подзаголовки `### Features` / `### Fixes` / ... `### Other`. Старая плоская простыня заменена.
- [x] **SBOM (CycloneDX)** — `dotnet CycloneDX` в release workflow генерит `<plugin>-v<X>.cdx.json` для `xrpl-signer` и `xrpl-local` (для `xrpl-cloud` пропускается — у него нет shipped бинарей). Аттачится к Release.
- [x] **SLSA build provenance attestation** — `actions/attest-build-provenance@v2` через GitHub OIDC. Без секретов. Верифицируется `gh attestation verify`.
- [x] **Per-RID tarballs + SHA-256 sidecars** — каждый `plugins/<name>/bin/<rid>/` бандлится в `<plugin>-v<X>-<rid>.tar.gz` + `.sha256` рядом.
- [x] **Reproducible builds** — `Deterministic=true` всегда, `ContinuousIntegrationBuild=true` при `CI=true`/`GITHUB_ACTIONS=true`. Bit-identity для managed-сборок между запусками одного коммита на одной версии SDK.
- [x] **Notarization** macOS бинарей signer — workflow готов, использует `rcodesign` (pure-Rust, на Linux runner'е без macOS-хоста). Условно skipped если `APPLE_*` секреты не настроены. Required secrets описаны в [docs/supply-chain.md](supply-chain.md).
- [x] **Authenticode** Windows бинарей — workflow готов, использует `osslsigncode` на Linux runner'е. Условно skipped если `WINDOWS_PFX*` не настроен. Required secrets описаны в [docs/supply-chain.md](supply-chain.md).

Хвосты (см. supply-chain.md):

- [ ] Notary stapling для macOS — невозможно для plain Mach-O, только `.app/.dmg/.pkg`.
- [ ] EV code-signing (HSM-токен) для Windows — требует отдельного workflow с DigiCert KeyLocker / Azure Key Vault.
- [ ] Vulnerability scanning в CI (grype / trivy) на основе уже-генерируемого SBOM.
- [ ] Bit-identity для single-file native бинарей — ждёт `SOURCE_DATE_EPOCH` в `dotnet publish` (.NET 11+).

---

## 8. Документация и SDK-парность

- [x] `INSTALL.md` §13 Troubleshooting — добавлены 3 подсекции: macOS Gatekeeper (`xattr -dr com.apple.quarantine`, `spctl --add`), Windows SmartScreen/Defender (`Unblock-File`, `Add-MpPreference -ExclusionPath`), Linux SELinux/AppArmor (`ausearch`, `chcon`/`semanage`, `DOTNET_BUNDLE_EXTRACT_BASE_DIR`).
- [x] **JSON-schema каталог tools** — новый mini-проект [`tools/SchemaGen/`](../tools/SchemaGen/) делает reflection-проход по `[McpServerToolType]`+`[McpServerTool]` в Core и Signer сборках, эмитит [`docs/tools-schema.json`](tools-schema.json) в MCP `tools/list` формате (name + description + JSON-Schema inputSchema, отсортировано по name). 74 tools покрыто. Регенерация: `dotnet run --project tools/SchemaGen -- docs/tools-schema.json`.
- [x] Пример Cowork-агента — [`docs/examples/monitor-balance-telegram.md`](examples/monitor-balance-telegram.md): polling watcher на `xrpl_account_tx_since` + форматирование через `xrpl_tx_explain` + отправка в Telegram. Готовый prompt, обоснование почему polling а не subscribe, варианты расширения и verification-чеклист.
- [x] Глоссарий XRPL-терминов — [`docs/glossary.md`](glossary.md): drops, reserve (base+owner), Sequence, LastLedgerSequence, Ripple epoch, ledger states, trust lines (NoRipple/DefaultRipple/Freeze), engine results (tec/tef/tem/ter/tes), DEX, AMM, Regular Key, Signer List, DepositAuth, Clawback, Escrow, Payment Channel, Check, NFT.

---

## Предлагаемые приоритеты на следующую веху

Не утверждено — обсуждается:

1. **§4 Signer** — HD-кошельки (BIP-44), audit log, hardware-wallet integration, per-wallet passphrase, OS-биометрика для разблокировки. Единственный полностью нетронутый раздел.
2. **§5 хвосты** — OpenTelemetry/metrics + connection-pool health + integration tests.
3. **§1 / §2 хвосты** — signer-list helper, escrow read-helper, AccountDelete pre-flight, `xrpl_manifest`, XLS-70 credential-based DepositPreauth.
4. **§7 хвосты** — vulnerability scanning (grype/trivy на основе уже-генерируемого SBOM).
