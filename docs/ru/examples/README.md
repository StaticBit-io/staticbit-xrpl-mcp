>  🌐 **Язык**: [English](../../examples/README.md) | **Русский**

# Cowork-агент рецепты для StaticBit XRPL MCP

Каждый рецепт — готовый workflow для Cowork-агента, который покрывает один use-case через сочетание MCP tool'ов из плагинов `xrpl-cloud` / `xrpl-local` / `xrpl-signer`. Все sequences перекрёстно сверены с integration-тестами upstream-проекта [XrplCSharp](https://github.com/StaticBit-io/XrplCSharp/tree/release/Tests/Xrpl.Tests/Integration/transactions) — каждый шаг рецепта соответствует реальной on-chain последовательности, проверенной testnet/devnet прогоном.

## Каталог

| Файл | Use-case | Amendment(s) | Статус на mainnet/testnet |
|---|---|---|---|
| [monitor-balance-telegram.md](monitor-balance-telegram.md) | Polling watcher для tx на адрес + Telegram уведомления | базовое read API + polling | ✅ работает on standard testnet |
| [controlled-mpt-issuance.md](controlled-mpt-issuance.md) | MPT lifecycle с RequireAuth + CanClawback (regulated issuance) | XLS-33 | ✅ активирован |
| [atomic-batch-payment.md](atomic-batch-payment.md) | Atomic 3-way swap через Batch | XLS-56 | ⚠️ pending BatchV1_1 (removed v3.1.1) |
| [kyc-credentials-flow.md](kyc-credentials-flow.md) | End-to-end KYC через XLS-70 Credentials + DepositPreauth | XLS-70 | ✅ активирован, drop-in |
| [permissioned-domain-and-did.md](permissioned-domain-and-did.md) | DID identity + PermissionedDomain gating | XLS-40 + XLS-80 | ✅ активированы |
| [oracle-price-feed.md](oracle-price-feed.md) | Periodic off-chain price publisher (CoinGecko → OracleSet) | XLS-47 | ✅ активирован |
| [tickets-parallel-submit.md](tickets-parallel-submit.md) | Tickets для параллельной отправки tx без head-of-line blocking | TicketCreate (давно активирован) | ✅ |
| [nft-mutable-uri.md](nft-mutable-uri.md) | Dynamic NFT с обновляемой URI (gaming, badges, RWA) | XLS-46 | ✅ активирован |
| [multi-sign-collection.md](multi-sign-collection.md) | Multi-sign signature collection workflow (treasury, DAO) | базовый SignerListSet (XRPL core) | ✅ |
| [amm-clawback.md](amm-clawback.md) | Issuer возвращает tokens из AMM-пула (compliance) | XLS-37 | ✅ активирован |
| [delegate-permissions.md](delegate-permissions.md) | Per-tx-type delegation (bot acts on owner's behalf) | XLS-75 | ✅ активирован |
| [vault-deposit-redeem.md](vault-deposit-redeem.md) | Single-asset vault lifecycle (deposit/withdraw/clawback) | XLS-65 | ⚠️ draft, на стандартном testnet `temDISABLED` |
| [payment-workflows.md](payment-workflows.md) | Три сценария Payment — XRP в drops, cross-currency с pathfinding'ом, credential-gated через PermissionedDomain | базовый Payment + XLS-70 + XLS-80 | ✅ активированы |

## Структура каждого рецепта

Все рецепты следуют унифицированной структуре:

1. **Use-case** — бизнес-задача, на которую отвечает рецепт.
2. **Что используется** — таблица плагин ↔ tool'ы.
3. **Концепция / Архитектура** — диаграмма потока + ключевые ledger entries.
4. **Pre-requisites** — что должно быть готово.
5. **Промт агента** — готовый markdown-файл агента для `~/.claude/agents/<name>.md`.
6. **Step-by-step** — последовательность вызовов tool'ов с примерами.
7. **Verification checklist** — как проверить успешность.
8. **Подводные камни** — known gotchas.
9. **Use-cases** — где это применимо в проде.
10. **Расширения** — куда можно развить идею.

## Закрытые feature-gaps

Обнаруженные при написании рецептов и **закрытые в текущей версии**:

- ✅ **`feature-gap-001`** — `xrpl_payment_prepare` принимает optional `credentialIdsJson` (массив 1..8 64-hex Hash256 строк), нормализует к uppercase, проверяет dedup + hex format.
- ✅ **`feature-gap-002`** — `xrpl_hash_credential(subject, issuer, credentialTypeHex|credentialTypePlain)` tool. Pure-local SHA-512/2 через SDK `Xrpl.Utils.Hashes.HashCredential` — не делает network вызовов, используется офлайн для построения `Payment.CredentialIDs`.

## Соответствие XrplCSharp integration-тестам

Каждый рецепт перекрёстно проверен с integration-тестом из upstream-репозитория:

| Рецепт | Соответствующий integration-тест |
|---|---|
| controlled-mpt-issuance | `TestIMPTokenCreate`, `TestIMPTokenAuthorize`, `TestIMPTokenManage` |
| atomic-batch-payment | `TestIBatch` (marked `[Ignore]` after v3.1.1) |
| kyc-credentials-flow | `TestICredential.TestCredential_EndToEnd_DepositPreauthAndPayment` |
| permissioned-domain-and-did | `TestIPermissionedDomain`, `TestIDID` |
| oracle-price-feed | `TestIOracle.TestOracleSet_CreateOracle_*` |
| tickets-parallel-submit | `ticketCreate.cs` (lowercase в SDK testset) |
| nft-mutable-uri | (NFTokenModify покрывается через NFT base) |
| multi-sign-collection | `TestIMultisign` (Payment_SubmitMulti / ManualSigning / CombineBlobs) |
| amm-clawback | `TestIAMMClawback` |
| delegate-permissions | `TestIDelegateSet` |
| vault-deposit-redeem | `TestIVault`, `TestIVaultBase` |
| payment-workflows | `TestIPayment`, `TestIPathFind`, `TestICredential.TestCredential_EndToEnd_DepositPreauthAndPayment` |

## Как использовать рецепты

### Вариант 1 — однократный manual run

1. Установи требуемые плагины из манифеста plugin.json'а конкретного рецепта.
2. Скопируй блок "Промт агента" в `~/.claude/agents/<name>.md` либо в `.claude/agents/<name>.md` твоего проекта.
3. Запусти агента через `claude agent <name>` или через invocation из главного claude session.

### Вариант 2 — embed в существующий workflow

Промт агента — это готовая модульная компонента. Можно использовать как sub-agent через `Agent` tool из главного оркестратора:

```markdown
Agent({
  description: "Run KYC credentials flow for Alice and Merchant",
  subagent_type: "kyc-credentials",
  prompt: "{step:'create', issuer:'rIssuer...', subject:'rAlice...', credentialTypePlain:'KYC-Tier1'}"
})
```

### Вариант 3 — recurring runs

Combine с `/loop` или `/schedule`:

```bash
/schedule "every 10 minutes" oracle-feed --params '{"oracleOwner":"...","oracleDocumentId":42,...}'
```

## Контрибуция

Если найдёте баг в рецепте или хотите добавить новый use-case:

1. Проверьте upstream integration-тест в [XrplCSharp/Tests/Xrpl.Tests/Integration/transactions/](https://github.com/StaticBit-io/XrplCSharp/tree/release/Tests/Xrpl.Tests/Integration/transactions).
2. Если sequence работает в integration-тесте — копируйте его в рецепт.
3. Запустите наш smoke-test против testnet: `dotnet test --filter "TestCategory=Integration"`.
4. Submit PR с новым английским `.md` файлом (+ зеркалом `docs/ru/examples/<name>.md`) и обновлением этого index'а.
