>  🌐 **Язык**: [English](../features.md) | **Русский**

# Возможности StaticBit XRPL MCP

Каталог реализованного функционала. Не roadmap — здесь только то, что уже есть в коде и покрыто тестами.

**Текущие цифры**:

- **131 MCP-tools** (полный машинно-читаемый JSON-Schema каталог: [docs/tools-schema.json](../tools-schema.json)).
- **432 unit-теста** (Core: 357, Server: 47, Signer: 28) + **34 integration smoke** против testnet (23 ещё ignore'нуты за draft-amendments Vault / XChain / Loan).
- Все 4 проекта (Abstractions, Core, Server, Signer) + 4 тестовых проекта + утилита `tools/SchemaGen` + плагин-маркетплейс на 3 плагина (`xrpl-cloud`, `xrpl-local`, `xrpl-signer`).

Связанные документы:

- [docs/glossary.ru.md](glossary.md) — словарь XRPL-терминов, встречающихся в описаниях tool'ов.
- [docs/supply-chain.ru.md](supply-chain.md) — что прикладывается к каждому release и как пользователю верифицировать.
- [docs/examples/](examples/) — рецепты Cowork-агентов поверх плагинов.
- [INSTALL.ru.md](../../INSTALL.ru.md), [DEPLOY.ru.md](../../DEPLOY.ru.md), [RELEASE.ru.md](../../RELEASE.ru.md) — установка, развёртывание VPS, релизный процесс.

---

## 1. Покрытие XRPL-транзакций (typed prepare-обёртки)

Каждый из перечисленных tool'ов возвращает **PreparedTransaction** — autofilled unsigned blob + signing data + human-readable summary для approval-flow. Сервер никогда не видит seed; подпись — оффлайн через `xrpl-signer`.

### 1.1. Account-управление

- `xrpl_account_set_prepare` — `AccountSet` со всеми флагами (`DefaultRipple`, `RequireAuth`, `RequireDest`, `DepositAuth`, `AllowTrustLineClawback`, `DisallowIncomingXRP/NFTokenOffer/Check/PayChan/Trustline`), полями `Domain`, `EmailHash`, `MessageKey`, `TickSize`, `TransferRate`, `NFTokenMinter`.
- `xrpl_set_regular_key_prepare` — `SetRegularKey`.
- `xrpl_signer_list_set_prepare` — `SignerListSet` (для multi-sign).
- `xrpl_signer_list_status` — helper поверх `account_objects(type=SignerList)`: возвращает `{quorum, totalAvailableWeight, signers[]}`; принимает `alreadySignedAccountsCsv` и считает `collectedWeight` + `deltaToQuorum` + `quorumReached`.
- `xrpl_deposit_preauth_prepare` — `DepositPreauth` (whitelist отправителей), включая XLS-70 credential-based вариант: `authorizeCredentialsJson` / `unauthorizeCredentialsJson` (массивы `{issuer, credentialType-hex}`, 1-8 entries, mutually exclusive с авторизацией по адресу).
- `xrpl_account_delete_prepare` — `AccountDelete`. Pre-flight расширен в `xrpl_tx_preflight`: проверяет `account_objects(deletion_blockers_only=true)` (если есть блокеры — `feasible=false` + список типов), и правило `currentLedger ≥ account.Sequence + 256` (если не выполнено — сообщает сколько ledger'ов осталось ждать).

### 1.2. NFT (XLS-20)

- Write: `NFTokenMint`, `NFTokenBurn`, `NFTokenCreateOffer`, `NFTokenCancelOffer`, `NFTokenAcceptOffer`.
- Read: `account_nfts`, `nft_buy_offers`, `nft_sell_offers`.

### 1.3. Escrow

- `EscrowCreate`, `EscrowFinish`, `EscrowCancel` prepare.
- Поддержка crypto-conditions (PREIMAGE-SHA-256) для `Condition`/`Fulfillment`.
- Read-helper `xrpl_account_escrows` — `account_objects(type=Escrow)` с split на `sent[]` (account = funder) и `received[]` (account = destination), каждая запись содержит counterparty, amount + human-readable, finishAfter/cancelAfter (UTC), condition, source/destination tags, previousTxnId.

### 1.4. PaymentChannel

- `PaymentChannelCreate`, `PaymentChannelFund`, `PaymentChannelClaim` prepare.

### 1.5. Check

- `CheckCreate`, `CheckCash`, `CheckCancel` prepare.

### 1.6. AMM (XLS-30)

- Полное покрытие: `AMMCreate`, `AMMDeposit`, `AMMWithdraw`, `AMMVote`, `AMMBid`, `AMMDelete`.

### 1.7. Token-issuer flow

- `xrpl_clawback_prepare` — `Clawback`.
- Freeze/Unfreeze helper над `TrustSet` (флаги `tfSetFreeze`/`tfClearFreeze`, `tfSetDeepFreeze`/`tfClearDeepFreeze`).

### 1.8. Multi-sign UX

- `xrpl_sign_multi`, `xrpl_sign_combine` — собрать частичную подпись и склеить несколько в одну.
- `xrpl_signer_list_status` (см. §1.1) — показывает quorum, текущий собранный вес, дельту до quorum, флаг unknownSigners (если в csv пришли адреса, которых нет в on-chain SignerList).

### 1.9. Multi-Purpose Tokens (XLS-33)

Не-trustline fungible-токены: issuer создаёт `MPTokenIssuance`, держатели — per-account `MPToken`. Все 4 prepare-tool'а возвращают autofilled UNSIGNED transaction; on-chain `MPTokenIssuanceID` (48-hex) выводится из `Issuer + Sequence` после validation.

- `xrpl_mpt_issuance_create_prepare` — `MPTokenIssuanceCreate`. Принимает `assetScale` (0..10), `maximumAmount` (uint64 str), `transferFee` (0..50000 = 0..50% в 1/10 bps), `metadataHex` (≤1024 raw bytes ≤2048 hex chars), плюс флаги: либо `flagsBitmask` (uint), либо boolean'ы `canLock`/`requireAuth`/`canEscrow`/`canTrade`/`canTransfer`/`canClawback` (mutually exclusive с bitmask). Внутренняя валидация: `transferFee>0` требует `tfMPTCanTransfer`, неизвестные биты в bitmask отклоняются.
- `xrpl_mpt_issuance_destroy_prepare` — `MPTokenIssuanceDestroy`. Доступен только при `OutstandingAmount=0`.
- `xrpl_mpt_issuance_set_prepare` — `MPTokenIssuanceSet`. Global lock/unlock (issuance целиком) либо per-holder lock/unlock через optional `holder`. Параметр `lockBalance` — `true` → tfMPTLock, `false` → tfMPTUnlock, `null` → no-op (для future fields).
- `xrpl_mpt_authorize_prepare` — `MPTokenAuthorize`. Двойная роль: holder opt-in (account=holder, без `holder`-param) или issuer authorizes specific holder (account=issuer, holder=target). `unauthorize=true` → tfMPTUnauthorize (revoke).
- Read-helpers (см. §2): `xrpl_account_mpt_issuances` (issuances, которыми владеет account как issuer), `xrpl_account_mpts` (MPT-holdings account'а от других issuer'ов).
- `xrpl_tx_preflight` (§3) расширен: AssetScale > 10, TransferFee > 50000, TransferFee>0 без tfMPTCanTransfer, MaximumAmount overflow, metadata > 2048 hex chars, MPTokenIssuanceSet с одновременным lock+unlock, Authorize с Holder=Account — все попадают в warnings + feasible=false.
- `xrpl_tx_explain` (§3) распознаёт все 4 MPT-tx и декодирует флаги в человеческую строку (`[CanLock|RequireAuth|CanTransfer]`).

### 1.10. Batch (XLS-56)

Атомарное выполнение до 8 inner-tx под единым outer Batch. Outer batch account платит fee и подаёт транзакцию; inner tx могут принадлежать любому Account, но требуют BatchSigners для multi-account flow.

- `xrpl_batch_prepare` — принимает `mode` ∈ {`AllOrNothing`, `OnlyOne`, `UntilFailure`, `Independent`} и `innerTransactionsJson` (JSON array of 1..8 tx-объектов). Tool автоматически: OR'ит `tfInnerBatchTxn` (0x40000000) к каждому inner Flags, форсит `Fee="0"` и `SigningPubKey=""`, удаляет `TxnSignature`/`Signers`, отклоняет nested Batch, требует `Sequence` или `TicketSequence` для каждого inner (autofill через границы account'ов невозможен). Опциональный `batchSignersJson` — массив `{account, signingPubKey?, txnSignature?, signers?}` для multi-account-batches.
- `xrpl_tx_preflight` (§3) расширен Batch-warnings: ровно один mode flag, ≤8 inner tx, отсутствие nested Batch, `tfInnerBatchTxn` присутствует на каждом inner, `Fee="0"`, `SigningPubKey=""`, отсутствие `TxnSignature`/`Signers`.
- `xrpl_tx_explain` (§3) разворачивает Batch: выводит mode, число inner tx и рекурсивно объясняет каждую inner-tx в одной строке.

### 1.11. Точечные amendments (quick wins)

Пять отдельных prepare-tool'ов, закрывающие отставание SDK по mainnet-активным amendment'ам:

- `xrpl_ticket_create_prepare` — `TicketCreate`. Резервирует 1..250 sequence-номеров как Tickets (по +1 owner-object и ~2 XRP reserve каждый). Потом любая транзакция может использовать `TicketSequence` вместо `Sequence`. Preflight проверяет диапазон 1..250.
- `xrpl_nft_modify_prepare` — XLS-46 `NFTokenModify`. Меняет URI у NFT с флагом `tfMutable=16`. Принимает `uriHex` / `uriPlain` (UTF-8 → hex авто) / `clearUri=true` (mutually exclusive). Опциональный `owner` если submitter — issuer / authorized minter. Preflight проверяет 64-char hex NFTokenID и чётность длины URI hex.
- `xrpl_oracle_set_prepare` / `xrpl_oracle_delete_prepare` — XLS-47 price oracles. OracleSet: `oracleDocumentId` (uint32), `lastUpdateTimeUnix` (Unix seconds, ±300s от ledger close), `priceDataSeriesJson` (1..10 entries: `{baseAsset, quoteAsset, assetPrice?, scale?}`; `assetPrice`/`scale` парные, scale 0..10, base/quote — 3-char или 40-char hex), опциональные ASCII `provider`/`uri`/`assetClass` авто-кодируются в hex. OracleDelete: только `oracleDocumentId`.
- `xrpl_delegate_set_prepare` — XLS-75 `DelegateSet`. Делегирует subset транзакций другому account'у через `permissionsCsv` (1..10 tx-type-имён, no duplicates). Запрещённые типы (AccountSet, SetRegularKey, SignerListSet, DelegateSet) отбиваются. Пустой CSV → clear delegation.
- `xrpl_tx_explain` (§3) распознаёт все 5 типов; `xrpl_tx_preflight` (§3) — type-specific warnings (TicketCount range, OracleSet PriceDataSeries count, OracleSet LastUpdateTime, DelegateSet duplicates / non-delegable / Authorize=Account, NFTokenModify NFTokenID format / URI hex parity).

### 1.12. Compliance / Identity (XLS-37 / 40 / 70 / 80)

Связанный bundle для controlled-issuance и on-chain identity. Стыкуется с уже реализованными `xrpl_deposit_preauth_prepare` (XLS-70 credential-based authorize) и MPT `tfMPTRequireAuth` (§1.9).

**Credentials standalone (XLS-70)** — `xrpl_credential_create_prepare` (issuer → subject, provisional), `xrpl_credential_accept_prepare` (subject ratifies, reserve переезжает на subject), `xrpl_credential_delete_prepare` (issuer revoke / subject un-accept / любой после expiration). Принимают `credentialType` / `uri` как hex **или** plain text (mutually exclusive). Опциональный `expirationUtc`. Лимиты: CredentialType ≤ 128 hex chars, URI ≤ 512 hex chars (= 256 bytes).

**PermissionedDomains (XLS-80)** — `xrpl_permissioned_domain_set_prepare` (create / modify через optional 64-hex `domainId`) с `acceptedCredentialsJson` (JSON array `{issuer, credentialType-hex}`, 1..10, dedup по (issuer, type-uppercase)). `xrpl_permissioned_domain_delete_prepare`. AcceptedCredentials на modify **полностью заменяет** прежний список.

**DID (XLS-40)** — `xrpl_did_set_prepare` принимает 3 поля (`data` / `didDocument` / `uri`) каждое в hex или plain (mutually exclusive внутри пары), как минимум одно поле обязательно, лимит каждого 512 hex chars (= 256 bytes). `xrpl_did_delete_prepare` — без полей. У account'а максимум один DID.

**AMMClawback (XLS-37)** — `xrpl_amm_clawback_prepare`. Issuer (`account`) возвращает свои tokens, депонированные `holder`'ом в AMM-пул. `asset1` — issued token (issuer обязан совпадать с `account`), `asset2` — counterpart (может быть XRP). Опциональный `amountValue` (decimal string) лимитирует claw-back. Требует `asfAllowTrustLineClawback`.

**Read-helpers (см. §2)** — `xrpl_account_credentials` (split на `issued` / `held`, флаг `accepted=lsfAccepted`, UTF-8 декод hex полей), `xrpl_account_did` (один объект или null), `xrpl_account_permissioned_domains` (с распаковкой AcceptedCredentials).

**`xrpl_tx_explain` (§3)** распознаёт все 8 tx-типов; **`xrpl_tx_preflight` (§3)** — type-specific warnings (CredentialType hex parity + length, Subject/Issuer ≠ Account, CredentialDelete требует хотя бы один из Subject/Issuer, PermissionedDomainSet AcceptedCredentials 1..10 + dedup, DIDSet хотя бы одно поле + размеры, AMMClawback Holder ≠ Account + Asset.issuer == Account).

### 1.13. Single-Asset Vault (XLS-65)

DeFi-vault примитив: owner создаёт vault с одним asset (XRP / IOU / MPT), депозиторы получают shares-MPT (auto-issued от pseudo-account), withdrawals подчиняются `WithdrawalPolicy` (uint код). Опционально приватный (требует credentialed access из PermissionedDomain) и/или с non-transferable shares.

- `xrpl_vault_create_prepare` — создание. Параметры: `assetCurrency`/`assetIssuer`, `amountValue` (initial deposit), опциональные `assetsMaximum` (STNumber), `metadataHex` (для share-MPT, ≤2048 hex), `dataHex` (≤512 hex), `domainId`, `withdrawalPolicy`, `scale` (0..18), `isPrivate` / `sharesNonTransferable` (creation-time только). Композитные флаги: `tfVaultPrivate=0x10000` + `tfVaultShareNonTransferable=0x20000`.
- `xrpl_vault_set_prepare` — modify (Data / AssetsMaximum / DomainID). Минимум одно поле. Пустой `domainId=""` очищает.
- `xrpl_vault_delete_prepare` — только пустой vault (AssetsTotal=0).
- `xrpl_vault_deposit_prepare` — депозит asset, получение shares-MPT.
- `xrpl_vault_withdraw_prepare` — двух-режимный: `amountKind='asset'` (точная сумма asset, shares burned по rate) либо `'shares'` (redeem N shares за asset). Для shares-режима требует `shareMptIssuanceId` (48-hex, из `vault.ShareMPTID`). Опциональный `destination` + `destinationTag`.
- `xrpl_vault_clawback_prepare` — issuer clawback из vault'а. Omit amount → max available. XRP не clawback'ится.
- **Read-helper** `xrpl_account_vaults` (см. §2) — отдаёт `vaultId`, `pseudoAccount`, `asset`, `assetsTotal/Available/Maximum`, `lossUnrealized`, `shareMptIssuanceId`, `withdrawalPolicy`, `scale`, `dataHex` + `dataUtf8`, `domainId`.

### 1.14. XChain bridges (XLS-38)

Cross-chain bridge flow между locking и issuing chains через door-accounts. Все 8 prepare-tool'ов используют общий 4-tuple `bridgeJson`: `{LockingChainDoor, LockingChainIssue, IssuingChainDoor, IssuingChainIssue}`. Поддерживает XRP-XRP и IOU-IOU bridges.

**Lifecycle:**
1. `xrpl_xchain_create_bridge_prepare` — door account на каждой цепи зеркалит bridge (`signatureRewardDrops` обязателен; `minAccountCreateDrops` опционально для XRP-XRP — включает AccountCreate flow).
2. `xrpl_xchain_modify_bridge_prepare` — изменение `SignatureReward` / `MinAccountCreateAmount`. `clearMinAccountCreate=true` устанавливает `tfClearAccountCreateAmount=0x10000` (взаимоисключающе с `minAccountCreateDrops`).
3. `xrpl_xchain_create_claim_id_prepare` — recipient на destination chain резервирует claim-id, фиксирует `otherChainSource` (адрес-отправитель на source chain).
4. `xrpl_xchain_commit_prepare` — sender на source chain коммитит средства с claim-id'ом. Опциональный `otherChainDestination` (omit → требует явный XChainClaim).
5. `xrpl_xchain_claim_prepare` — явный claim на destination chain (когда commit не указал destination).
6. `xrpl_xchain_account_create_commit_prepare` — XRP-XRP only, создаёт новый аккаунт на destination chain через bridge.
7. `xrpl_xchain_add_claim_attestation_prepare` — witness server submits ECDSA attestation для claim flow. Поля: `attestationRewardAccount`, `attestationSignerAccount`, `otherChainSource`, `publicKeyHex`, `signatureHex`, `wasLockingChainSend` (0/1).
8. `xrpl_xchain_add_account_create_attestation_prepare` — то же для account-create flow, дополнительно `xchainAccountCreateCount`.

- **Read-helper** `xrpl_account_bridges` (см. §2) — отдаёт `xchainBridge` 4-tuple, `signatureReward`, `minAccountCreateAmount`, текущие счётчики `xchainClaimId` / `xchainAccountCreateCount` / `xchainAccountClaimCount`.
- **`xrpl_tx_explain` (§3)** распознаёт все 14 tx-типов (Vault + XChain). **`xrpl_tx_preflight` (§3)** — type-specific warnings: VaultID 64-hex, Scale 0..18, Data parity + size, Holder ≠ Account для clawback; bridge {LockingChainDoor, IssuingChainDoor} различаются, Issues объектные, требуемые поля attestation flows, `WasLockingChainSend ∈ {0,1}`.

### 1.15. Lending (XLS-66 LoanBroker / Loan)

Bleeding-edge DeFi lending: LoanBroker сидит поверх Vault и управляет пулом займов с cover capital (first-loss). Все 9 prepare-tool'ов опираются на 64-hex hash IDs (LoanBrokerID, LoanID).

**LoanBroker write-flow** ([LoanBrokerTools.cs](../../src/StaticBit.Xrpl.Mcp.Core/Tools/LoanBrokerTools.cs)):

- `xrpl_loan_broker_set_prepare` — create (omit `loanBrokerId`) / modify. Параметры: `vaultId`, `coverRateMinimum` / `coverRateLiquidation` (1/100th bp, 0..100000; liquidation ≤ minimum), `managementFeeRate` (1/10th bp, 0..10000), `debtMaximum` (STNumber), `dataHex` (≤512 hex).
- `xrpl_loan_broker_delete_prepare` — только при пустом брокере.
- `xrpl_loan_broker_cover_deposit_prepare` / `_cover_withdraw_prepare` — first-loss capital deposit/withdraw, asset должен совпадать с vault'ом.
- `xrpl_loan_broker_cover_clawback_prepare` — issuer claw-back. Минимум один из `loanBrokerId` или `(amountValue + assetCurrency)`. XRP не clawback'ится.

**Loan write-flow** ([LoanTools.cs](../../src/StaticBit.Xrpl.Mcp.Core/Tools/LoanTools.cs)):

- `xrpl_loan_set_prepare` — оригинация. Lender ≠ borrower. Все 5 rate-полей (interest/late/close/overpayment/overpayment-fee) проверяются ≤100000. Optional fee fields (STNumber): origination/service/late/close. `paymentTotal` / `paymentInterval` / `gracePeriod` (defaults 1/60/60 в SDK). `allowOverpayment=true` → tfLoanOverpayment.
- `xrpl_loan_manage_prepare` — `action` ∈ {`default`, `impair`, `unimpair`} (mutually exclusive flags `tfLoanDefault=0x10000` / `tfLoanImpair=0x20000` / `tfLoanUnimpair=0x40000`).
- `xrpl_loan_pay_prepare` — borrower payment. `paymentKind` ∈ {`scheduled` (default, no flag), `overpayment`, `full`, `late`} — последние три mutually exclusive.
- `xrpl_loan_delete_prepare` — только закрытый/полностью погашенный loan.

**Read-helpers (см. §2):**

- `xrpl_account_loan_brokers` — `loanBrokerId`, `pseudoAccount`, `vaultId`, debt counters (`debtTotal`/`debtMaximum`), `coverAvailable`, cover-rates, `managementFeeRate`, `activeLoanCount`, `dataUtf8`.
- `xrpl_account_loans` — `loanId`, `borrower`, `loanBrokerId`, все interest/fee rates, principal counters (`principalOutstanding`/`principalRequested`/`totalValueOutstanding`), `periodicPayment`, `managementFeeOutstanding`, schedule (`startDate`/`previousPaymentDueDate`/`nextPaymentDueDate` UTC ISO-8601, `paymentInterval`/`gracePeriod`/`paymentRemaining`), `loanScale`.

**`xrpl_tx_explain` (§3)** распознаёт все 9 типов. **`xrpl_tx_preflight` (§3)** — type-specific warnings: LoanBrokerID/LoanID/VaultID 64-hex, CoverRates ≤100000 + liquidation ≤ minimum, ManagementFeeRate ≤10000, LoanSet rate-fields ≤100000, Counterparty ≠ Account, LoanManage exactly-one action flag, LoanPay payment-kind mutual exclusivity, LoanBrokerCoverClawback требует хотя бы один из ID/Amount.

> Замечание: XLS-66 amendment ещё в draft статусе. SDK содержит типы, но активация на mainnet не подтверждена — preflight ловит SDK-уровневую валидацию, но network-side rules (lender authorization, cover-rate post-tx check, и т.д.) проверяются только на onchain submit.

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
| `xrpl_manifest` | ✅ | Validator manifest по public_key. SDK не имеет типизированного метода — используется `GRequest<JsonNode, ManifestRequest>` с inline-описанием envelope'а. |

### Subscribe/unsubscribe — честные ограничения

MCP — request/response, **не двунаправленный канал**. Cloud-сервер держит ОДНУ WebSocket-сессию на network через `XrplClientPool`, разделяемую между всеми MCP-клиентами. `xrpl_subscribe` ставит подписку, но события приходят на ту общую WebSocket и **никак не доставляются обратно** конкретному MCP-вызову.

Tool оставлен как plumbing для будущих server-side watchers / admin alerts (например, AdminAlerter мог бы их потреблять).

**Для реального мониторинга** — использовать `xrpl_account_tx_since`: passing the highest `ledger_index` seen previously gives stateless, idempotent polling that works в любом deployment-режиме.

---

## 3. UX-улучшения подготовки транзакций

- **`xrpl_tx_simulate`** — dry-run симуляция через `xrpl_tx_preflight` + (для Payment) `ripple_path_find` + текущий открытый fee + рекомендуемый LastLedgerSequence.
- **`xrpl_tx_explain`** — превращает decoded tx (blob hex или JSON) в человеческую строку: *"Payment from rA... to rB...: 10000000 drops XRP. [fee=12 drops, seq=42, LLS=1234]"*. Pure local, без сетевых вызовов. Покрывает все основные типы (Payment, TrustSet, Offer\*, AMM\*, NFToken\*, Escrow\*, Check\*, PaymentChannel\*, AccountSet, SetRegularKey, DepositPreauth, SignerListSet, AccountDelete, Clawback) + generic fallback.
- **`xrpl_tx_preflight`** — проверяет `account_info` + `server_state`, считает резервы (base + owner_count × inc), сравнивает с balance, для Payment ещё проверяет `RequireDestinationTag`/`DepositAuth`/`DisallowIncomingXRP` на destination. Возвращает structured report с warnings[].
- **Fee escalation policy** — `LastLedgerSequenceOffset` реально применяется в `TransactionPreparer`; `FeeBumpMultiplier` для проактивного over-pay во время open-ledger escalation.
- **`xrpl_tx_decode_blob`** — возвращает осмысленные ошибки (odd length / non-hex / decode exception / null result) вместо пустого `{}`.

---

## 4. Signer

- **HD-кошельки (BIP-44)** — `KeystoreEntry` поддерживает опциональные поля (`Kind="seed"|"mnemonic"`, отдельно зашифрованная мнемоника + BIP-39 passphrase + `DerivationPathTemplate`). Старые seed-записи (без `Kind`) грузятся как `Kind="seed"` без изменения формата; первая HD-запись поднимает file version 1 → 2. Default для HD = secp256k1 (XRPL BIP-44 convention из xrpl.js).
  - Tools: `xrpl_wallet_generate_mnemonic` (генерит BIP-39 12/15/18/21/24 слов, возвращает мнемонику один раз для бэкапа), `xrpl_wallet_derive_address` (выводит `{address, publicKey}` для произвольного index без секретов), `xrpl_wallet_export_index` (даёт XRPL family-seed для конкретного индекса HD — для импорта в другие тулзы).
  - Расширены: `xrpl_wallet_import_mnemonic` (default `storeAsHd=true`, fallback на legacy seed-режим через `storeAsHd=false`), `xrpl_wallet_export` (для mnemonic-kind возвращает мнемонику + bip39Passphrase + template + algorithm), `xrpl_sign`/`xrpl_sign_multi` (параметр `index=0`, для seed-kind строго 0).
- **Audit log** — `IAuditLogger` интерфейс + `FileAuditLogger` (JSONL формат `{ts,event,wallet,result,...}`) + `NullAuditLogger`. Opt-in через `XRPL_SIGNER_AUDIT_LOG` env (default — выключен). Логирует успешные подписи (`event="sign"`), неуспешные расшифровки (`event="decrypt_fail"` для compliance — failed passphrase attempts) и tx-level ошибки (`event="sign", result="error"`). Write-ошибки → warning в stderr, signer не падает. Per-file perms 0600 на Unix.
- **Encrypted keystore (исходный baseline, сохраняется)** — PBKDF2-SHA256 600k итераций + AES-256-GCM, per-record salt/IV. Атомарная запись (temp + rename). Master passphrase через `XRPL_SIGNER_PASSPHRASE` или `XRPL_SIGNER_PASSPHRASE_FILE`.

---

## 5. Server-инфраструктура

- **Тесты Server**: 47 unit-тестов — BearerAuthMiddleware (health bypass, 404 для scanners, HTTPS-enforcement, X-Forwarded-Proto, empty tokens, missing/wrong/valid bearer, label assignment, X-Forwarded-For first hop); AdminAlerter (disabled, event-disabled, dedup window + после окна, разные tags не схлопываются, rate cap + reset после минуты + 0=отключено, queue capacity DropOldest); RequestLoggingMiddleware (disabled, health-skip, query-string opt-in/opt-out, label, status code, logs-on-exception); ServerOptions defaults; ResolveRateLimitPartitionKey (ip/token/both/unknown, case insensitive).
- **Structured request logging** с redaction — `RequestLoggingMiddleware` логирует Method, Path, Status, Duration, IP, label. Bodies НЕ логируются (могут содержать r-addresses/amounts). Query string opt-in через `RequestLogging.IncludeQueryString`.
- **CORS** — `ServerOptions.Cors` (`Enabled`, `AllowedOrigins[]`, `AllowedHeaders[]`, `AllowedMethods[]`, `AllowCredentials`). Поддерживает wildcard `["*"]` для AllowedOrigins. Подключается ДО bearer-auth чтобы OPTIONS preflights не требовали токен.
- **Graceful shutdown** для long-poll `wait_for_validation` — суженный catch в `TransactionTools.SubmitSignedAsync` (только `Exception` без `OperationCanceledException`). Cancellation пробрасывается немедленно.
- **Rate-limit per token** — `RateLimitOptions.PartitionBy={ip|token|both}` (default `ip` для совместимости). При `token`/`both` partition key включает bearer label. AdminAlert при превышении включает и label, и ip.
- **OpenTelemetry / metrics** — `XrplMcpMetrics` Meter в Core: `xrpl_mcp_pool_connections` gauge, `xrpl_mcp_pool_reconnects_total{network,reason}` counter с reasons cold/dropped/ttl/error, `xrpl_mcp_pool_connect_duration_seconds` histogram, `xrpl_mcp_tool_calls_total{tool,status,label}` + `xrpl_mcp_tool_duration_seconds` для HTTP MCP-вызовов. Server опционально включает Prometheus scrape endpoint через `ServerOptions.Metrics.Enabled`, `Path` (default `/metrics`); auth bypassed, защита через reverse proxy.
- **Connection pool health** — `XrplMcpOptions.ConnectionTtlMinutes` (default 0 = off). Reactive eviction на следующем `GetAsync`: возраст > TTL → reconnect с reason="ttl" в metric. Существующий dropped-socket reconnect инкрементит counter с reason="dropped". `PoolEntry` хранит `CreatedAt`. Без отдельного background-timer'а.
- **Integration-тесты** — проект `tests/StaticBit.Xrpl.Mcp.Integration.Tests/` с `[TestCategory("Integration")]`, конвенция `*TestsI.cs` / `TestI_*`. **34 active + 23 [Ignore]** smoke tests против testnet (≈18s полный прогон):
  - **Ledger** (5): `server_info`, `server_state`, `fee`, `account_info` funded address, `ledger validated`.
  - **Read-helpers** (9): `account_mpt_issuances`, `account_mpts`, `account_credentials`, `account_did`, `account_permissioned_domains`, `account_vaults`, `account_bridges`, `account_loan_brokers`, `account_loans` — проверяют корректную JSON-форму даже при пустом результате.
  - **Prepare-smoke** (20 active): MPT (4), Batch, Oracle (2), Ticket, Delegate (2), NFTokenModify (2), Credentials (3), PermissionedDomains (2), DID (2), AMMClawback. Каждый round-trip'ит autofill + binary encoding + summary без подписи/submit. Общий helper `PrepareSmokeAssert.Standard` проверяет `TxBlobUnsigned` непустой, `Sequence`/`Fee`/`LastLedgerSequence` autofilled, `TransactionType` совпадает.
  - **[Ignore]'нуты** (23): Vault (XLS-65, 6 tests), XChain (XLS-38, 8 tests, sidechain-only), LoanBroker / Loan (XLS-66, 9 tests) — все три amendments в draft статусе и не активированы на стандартном rippletest.net. Снять `[Ignore]` при тестировании против узла с активными amendment'ами.
  - **`[DoNotParallelize]`** на каждом классе — SDK `RequestManager` имеет ID-race при concurrent-запросах через один WebSocket; разные классы (со своими pool'ами) идут параллельно, внутри класса — последовательно.
  - Workflow [.github/workflows/integration-tests.yml](../../.github/workflows/integration-tests.yml) — daily cron + workflow_dispatch с override URL. Переопределение endpoint через `XRPL_TESTNET_WS`.

---

## 6. Качество кода

- `TransactionTools.ExtractBool` / `ExtractUInt` — заменены на `ReadBool`/`ReadUInt` поверх `JsonNode`. Старая реализация re-serialize-then-parse'ила объект каждый вызов; в `SubmitSignedAsync` поллинг-цикле было 3 re-serialize на итерацию, теперь один. `TryGetTxHash` тоже переписан на `JsonNode`.
- Конвенция тестов `*TestsU.cs` + префикс `TestU_` — задокументирована в README.md (секция "Конвенция тестов"). `U`=Unit, фильтр `--filter TestU`. Зарезервировано `*TestsI.cs` / `TestI_*` под интеграционные.
- `Directory.Build.props` — `<TargetFramework>net10.0</TargetFramework>` как default; убран из всех csproj кроме `Abstractions.csproj` (тот сохранил явный override `netstandard2.1` для embed'инга в более старые хосты).
- Унифицировано описание `network` и `ledgerIndex` параметров — вынесены в `public const string` в `Services/ToolDescriptions.cs`, заменены в 20 файлах (две вариации фразы про ledgerIndex приведены к одной канонической).

---

## 7. Дистрибуция и supply chain

Полная сводка + setup-гайд: [docs/supply-chain.ru.md](supply-chain.md).

- **Автогенерация GitHub release notes** — `release-plugin.sh::group_by_conventional_commit` парсит conventional commits (feat/fix/perf/refactor/docs/test/build/ci/chore) между тегами и группирует под подзаголовки `### Features` / `### Fixes` / ... `### Other`.
- **SBOM (CycloneDX)** — `dotnet CycloneDX` в release workflow генерит `<plugin>-v<X>.cdx.json` для `xrpl-signer` и `xrpl-local` (для `xrpl-cloud` пропускается — у него нет shipped бинарей). Аттачится к Release.
- **SLSA build provenance attestation** — `actions/attest-build-provenance@v2` через GitHub OIDC. Без секретов. Верифицируется `gh attestation verify`.
- **Per-RID tarballs + SHA-256 sidecars** — каждый `plugins/<name>/bin/<rid>/` бандлится в `<plugin>-v<X>-<rid>.tar.gz` + `.sha256` рядом.
- **Reproducible builds** — `Deterministic=true` всегда, `ContinuousIntegrationBuild=true` при `CI=true`/`GITHUB_ACTIONS=true`. Bit-identity для managed-сборок между запусками одного коммита на одной версии SDK.
- **Notarization macOS бинарей signer** — workflow использует `rcodesign` (pure-Rust, на Linux runner'е без macOS-хоста). Условно skipped если `APPLE_*` секреты не настроены. Required secrets описаны в [docs/supply-chain.ru.md](supply-chain.md).
- **Authenticode Windows бинарей** — workflow использует `osslsigncode` на Linux runner'е. Условно skipped если `WINDOWS_PFX*` не настроен. Required secrets описаны в [docs/supply-chain.ru.md](supply-chain.md).

---

## 8. Документация

- [INSTALL.ru.md](../../INSTALL.ru.md) §13 Troubleshooting — 3 подсекции для unsigned бинарей: macOS Gatekeeper (`xattr -dr com.apple.quarantine`, `spctl --add`), Windows SmartScreen/Defender (`Unblock-File`, `Add-MpPreference -ExclusionPath`), Linux SELinux/AppArmor (`ausearch`, `chcon`/`semanage`, `DOTNET_BUNDLE_EXTRACT_BASE_DIR`).
- **JSON-schema каталог tools** — mini-проект [`tools/SchemaGen/`](../../tools/SchemaGen/) делает reflection-проход по `[McpServerToolType]`+`[McpServerTool]` в Core и Signer сборках, эмитит [`docs/tools-schema.json`](../tools-schema.json) в MCP `tools/list` формате (name + description + JSON-Schema inputSchema, отсортировано по name). 130 tools покрыто. Регенерация: `dotnet run --project tools/SchemaGen -- docs/tools-schema.json`.
- **Cowork-агент рецепты** — [`docs/examples/`](examples/) содержит **12 готовых workflow'ов**, каждый перекрёстно сверен с integration-тестом upstream-проекта [XrplCSharp](https://github.com/StaticBit-io/XrplCSharp/tree/release/Tests/Xrpl.Tests/Integration/transactions). Индекс — [`docs/examples/README.ru.md`](examples/README.md). Покрытые сценарии:
  - **Read + polling**: monitor-balance-telegram.
  - **MPT (XLS-33)**: controlled-mpt-issuance.
  - **Batch (XLS-56)**: atomic-batch-payment (pending BatchV1_1 — amendment removed in rippled v3.1.1).
  - **Compliance / identity**: kyc-credentials-flow (XLS-70), permissioned-domain-and-did (XLS-40+80).
  - **Oracle (XLS-47)**: oracle-price-feed (periodic publisher).
  - **Tickets**: tickets-parallel-submit (head-of-line-blocking free batch).
  - **NFT (XLS-46)**: nft-mutable-uri.
  - **Multi-sign**: multi-sign-collection (signature collection + combine workflow).
  - **AMM (XLS-37)**: amm-clawback (issuer reclaims tokens из пула).
  - **DelegateSet (XLS-75)**: delegate-permissions (per-tx-type delegation, bot acts on owner's behalf).
  - **Vault (XLS-65, draft)**: vault-deposit-redeem.
  Все рецепты следуют единой структуре (use-case → tools → arch → pre-req → промт → step-by-step → verification → gotchas → use-cases → extensions). Обнаружены 2 feature-gap'а в текущем MCP API (`credentialIdsJson` в `xrpl_payment_prepare` и `xrpl_hash_credential` tool) — документированы в `examples/README.md` для следующих спринтов.
- Глоссарий XRPL-терминов — [`docs/glossary.ru.md`](glossary.md): drops, reserve (base+owner), Sequence, LastLedgerSequence, Ripple epoch, ledger states, trust lines (NoRipple/DefaultRipple/Freeze), engine results (tec/tef/tem/ter/tes), DEX, AMM, Regular Key, Signer List, DepositAuth, Clawback, Escrow, Payment Channel, Check, NFT.
- [docs/supply-chain.ru.md](supply-chain.md) — что прикладывается к каждому release, как пользователю верифицировать, какие secrets нужно настроить для опциональной macOS notarization / Windows Authenticode.
