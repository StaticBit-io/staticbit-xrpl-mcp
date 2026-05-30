>  🌐 **Язык**: [English](../../examples/payment-workflows.md) | **Русский**

# Пример: Payment-workflows — XRP, cross-currency и credential-gated

Фундаментальный примитив XRPL: отправка ценности от A к B. Три сценария в одном документе, покрывают основную массу реальных Payment-кейсов: обычный перевод XRP в drops, cross-currency delivery с pathfinding и credential-gated Payment получателю, сидящему за PermissionedDomain. Все сценарии — только для testnet; все суммы/адреса — placeholder'ы.

Референс: [TestIPayment.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIPayment.cs), [TestIPathFind.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIPathFind.cs), [TestICredential.TestCredential_EndToEnd_DepositPreauthAndPayment](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestICredential.cs#L312).

## Use case

- **Сценарий 1 — простая XRP-оплата**: alice отправляет bob'у N XRP на testnet. Главная цель — зафиксировать конвертацию drops ↔ XRP так, чтобы агент случайно не переплатил/недоплатил на 6 порядков.
- **Сценарий 2 — cross-currency payment с pathfinding'ом**: alice держит XRP, bob ожидает USD, выпущенные `rIssuer...`. XRPL DEX/AMM ребалансит на лету через поле `paths_computed` из `ripple_path_find`, защищено через `SendMax` и `DeliverMin`.
- **Сценарий 3 — credential-gated payment**: bob включает DepositAuth и PermissionedDomain, привязанный к KYC-credential'у. Первая попытка без credential'а отвергается ledger'ом (`tecNO_PERMISSION`); после `CredentialAccept` ретрай проходит.

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_xrp_balance`, `xrpl_account_lines`, `xrpl_fee`, `xrpl_payment_prepare`, `xrpl_ripple_path_find`, `xrpl_path_find_create`, `xrpl_path_find_status`, `xrpl_path_find_close`, `xrpl_trustset_prepare`, `xrpl_account_set_prepare`, `xrpl_deposit_preauth_prepare`, `xrpl_permissioned_domain_set_prepare`, `xrpl_credential_accept_prepare`, `xrpl_hash_credential`, `xrpl_tx_prepare_generic`, `xrpl_tx_preflight`, `xrpl_tx_simulate`, `xrpl_tx_submit_signed`, `xrpl_tx_lookup` |
| **xrpl-signer** | `xrpl_sign` |

## Архитектура

```
Scenario 1: simple XRP payment
  ALICE ──Payment(Destination=BOB, Amount="1500000" /* drops */)──► rippled
                                                                     ↓ tesSUCCESS, meta.delivered_amount="1500000"

Scenario 2: cross-currency with pathfinding
  ISSUER ──TrustSet(...)/Payment(USD)──► holders
  ALICE ──ripple_path_find(srcAcc=ALICE, dstAcc=BOB,
                           destAmount={USD, issuer=ISSUER, value="10"})──►
                                          ↓ alternatives[0].paths_computed + source_amount
  ALICE ──Payment(Destination=BOB, Amount={USD, issuer=ISSUER, value="10"},
                  SendMax="<drops>", DeliverMin={...}, Paths=[...],
                  Flags=tfPartialPayment)──► rippled
                                              ↓ meta.delivered_amount = actually settled

Scenario 3: credential-gated payment via PermissionedDomain
  Setup (one-time):
    ISSUER ──CredentialCreate(Subject=ALICE, Type="KYC-Tier1")──►
    BOB    ──AccountSet(setFlag=asfDepositAuth=9)──►
    BOB    ──PermissionedDomainSet(AcceptedCredentials=[{ISSUER,KYC-Tier1}])──►
    BOB    ──DepositPreauth(AuthorizeCredentials=[{ISSUER,KYC-Tier1}])──►

  Negative path (Alice has NOT yet accepted):
    ALICE ──Payment(Destination=BOB, CredentialIDs=[hash])──►
                                                              ↓ tecNO_PERMISSION

  Positive path (after CredentialAccept):
    ALICE ──CredentialAccept(Issuer=ISSUER, Type="KYC-Tier1")──►
    ALICE ──Payment(Destination=BOB, CredentialIDs=[hash])──►
                                                              ↓ tesSUCCESS
```

## Prerequisites

- Два funded testnet-аккаунта ALICE и BOB через [testnet faucet](https://xrpl.org/xrp-testnet-faucet.html) (для сценария 3 — три аккаунта, добавить ISSUER).
- Кошельки импортированы в keystore `xrpl-signer` под именами `alice`, `bob`, `issuer`.
- Для сценария 2 — у BOB должна быть trustline на ISSUER для cross-currency target'а.
- Для сценария 3 — XLS-70 (Credentials) и XLS-80 (PermissionedDomains) должны быть активны на выбранной сети (обе — на стандартном testnet'е; на mainnet проверить через `xrpl_server_state`).
- Default `network` — `testnet`. Никогда не гонять сценарии 2/3 против mainnet без отключения шортката `tfPartialPayment` и повторного `xrpl_tx_simulate` против реальной ликвидности.

## Промт агента

```markdown
---
name: payment-workflows
description: Orchestrates three XRPL Payment scenarios — simple XRP drops transfer,
  cross-currency payment with pathfinding (SendMax + DeliverMin + tfPartialPayment),
  and credential-gated Payment via DepositAuth + PermissionedDomain + DepositPreauth.
tools: xrpl_xrp_balance, xrpl_account_lines, xrpl_fee, xrpl_payment_prepare,
  xrpl_ripple_path_find, xrpl_path_find_create, xrpl_path_find_status,
  xrpl_path_find_close, xrpl_trustset_prepare, xrpl_account_set_prepare,
  xrpl_deposit_preauth_prepare, xrpl_permissioned_domain_set_prepare,
  xrpl_credential_accept_prepare, xrpl_hash_credential, xrpl_tx_prepare_generic,
  xrpl_tx_preflight, xrpl_tx_simulate, xrpl_tx_submit_signed, xrpl_tx_lookup,
  xrpl_sign
---

Inputs (one per call):
- {"step":"pay_xrp","network":"testnet","from":"r...","to":"r...","xrp":"1.5"}
- {"step":"pay_cross","network":"...","from":"r...","to":"r...",
   "destCurrency":"USD","destIssuer":"r...","destValue":"10","sendMaxDrops":"15000000"}
- {"step":"pay_gated","network":"...","from":"r...","to":"r...","amountDrops":"1000000",
   "issuer":"r...","credentialTypePlain":"KYC-Tier1"}

For "pay_xrp":
1. CONVERT xrp → drops as a decimal-string operation:
   drops = round(xrp * 1_000_000) — keep as STRING, never float64.
   Refuse if drops has a decimal point or non-digit chars.
2. xrpl_payment_prepare(network, account=from, destination=to, amount=drops).
3. xrpl_tx_preflight → bail on feasible=false.
4. xrpl_sign(walletName=from, txBlobUnsigned) → xrpl_tx_submit_signed(..., waitForValidation=true).
5. xrpl_tx_lookup(network, txHash, binary=false) → assert validated=true and
   meta.TransactionResult="tesSUCCESS"; surface meta.delivered_amount.

For "pay_cross":
1. xrpl_ripple_path_find(network, sourceAccount=from, destinationAccount=to,
     destinationAmount=JSON({value=destValue,currency=destCurrency,issuer=destIssuer}),
     sendMax=sendMaxDrops).
2. If alternatives[] is empty → return {error:"no_path"}.
3. Build the cross-currency Payment via xrpl_tx_prepare_generic with txJson:
     {"TransactionType":"Payment","Account":from,"Destination":to,
      "Amount":{"currency":destCurrency,"issuer":destIssuer,"value":destValue},
      "SendMax":alternatives[0].source_amount,
      "DeliverMin":{"currency":destCurrency,"issuer":destIssuer,
                    "value":<99% of destValue>},
      "Paths":alternatives[0].paths_computed,
      "Flags":131072 /* tfPartialPayment */}
4. xrpl_tx_simulate(network, txJson) — confirms feasibility against current liquidity.
5. xrpl_sign → xrpl_tx_submit_signed → xrpl_tx_lookup.
6. Surface meta.delivered_amount — this is the AUTHORITATIVE amount the destination got.

For "pay_gated":
1. credHash = xrpl_hash_credential(subject=from, issuer=issuer, credentialTypePlain).
2. (Optional negative-path probe) xrpl_payment_prepare(... credentialIdsJson=["<credHash>"])
     → sign → submit → expect tecNO_PERMISSION if Subject hasn't accepted yet.
3. xrpl_credential_accept_prepare(network, account=from, issuer, credentialTypePlain)
     → sign by 'from' → submit → tesSUCCESS.
4. Retry the Payment from step 2 → tesSUCCESS.

Return {txHash, engineResult, deliveredAmount?}.
```

## Step-by-step

### Сценарий 1 — простая XRP-оплата (drops accounting)

```text
agent ← {"step":"pay_xrp","network":"testnet","from":"rAlice...","to":"rBob...","xrp":"1.5"}

→ drops conversion:
   "1.5" XRP × 1_000_000 → "1500000" drops  // string, NOT 1.5 — see Gotchas
→ xrpl_xrp_balance(network="testnet", account="rAlice...")
   → "1000" /* free XRP, must be ≥ 1.5 + fee + reserve delta */
→ xrpl_fee(network="testnet")
   → { openLedgerFee:"12", ... } /* drops */
→ xrpl_payment_prepare(
    network="testnet",
    account="rAlice...",
    destination="rBob...",
    amount="1500000"
  )
   → { txBlobUnsigned, txJson, signingData }
→ xrpl_tx_preflight(network="testnet", txJson)
   → { feasible:true, balanceXrp:"1000", requiredXrp:"1.5000012",
       warnings:[] }
→ xrpl_sign(walletName="alice", txBlobUnsigned)
   → { txBlobSigned, hash:"<64-hex>" }
→ xrpl_tx_submit_signed(network="testnet", txBlobSigned, waitForValidation=true)
   → { engineResult:"tesSUCCESS", txHash:"<...>", validated:true }
→ xrpl_tx_lookup(network="testnet", txHash, binary=false)
   → { validated:true, meta:{ TransactionResult:"tesSUCCESS",
       delivered_amount:"1500000" } }
```

Post-условия:
- `xrpl_xrp_balance(rBob)` увеличивается ровно на 1.5 XRP (fee платит alice, не bob).
- `xrpl_xrp_balance(rAlice)` уменьшается на `1.5 + fee/1_000_000` XRP.

### Сценарий 2 — cross-currency payment с pathfinding'ом

Setup (one-time): у BOB есть trustline на ISSUER для `USD`. ISSUER уже выпустил USD через прямой Payment маркет-мейкерским аккаунтам (иначе нет ликвидности → пустой `alternatives[]`).

```text
agent ← {"step":"pay_cross","network":"testnet","from":"rAlice...","to":"rBob...",
         "destCurrency":"USD","destIssuer":"rIssuer...","destValue":"10",
         "sendMaxDrops":"15000000" /* up to 15 XRP */}

Step 2a: discover a path
→ xrpl_ripple_path_find(
    network="testnet",
    sourceAccount="rAlice...",
    destinationAccount="rBob...",
    destinationAmount='{"value":"10","currency":"USD","issuer":"rIssuer..."}',
    sendMax="15000000"
  )
   → { alternatives:[
        { source_amount:"12345678" /* drops */,
          paths_computed:[[{"currency":"USD","issuer":"rIssuer..."}], ...] },
        ...
       ] }

Step 2b: build the Payment via the generic preparer
→ xrpl_tx_prepare_generic(
    network="testnet",
    txJson='{
      "TransactionType":"Payment",
      "Account":"rAlice...",
      "Destination":"rBob...",
      "Amount":{"currency":"USD","issuer":"rIssuer...","value":"10"},
      "SendMax":"12345678",
      "DeliverMin":{"currency":"USD","issuer":"rIssuer...","value":"9.9"},
      "Paths":[[{"currency":"USD","issuer":"rIssuer..."}]],
      "Flags":131072
    }',
    humanSummary="Alice → Bob: deliver 10 USD (min 9.9), up to 15 XRP spent"
  )
   → { txBlobUnsigned, txJson:<autofilled with Fee, Sequence, LastLedgerSequence>, ... }

Step 2c: simulate against live liquidity
→ xrpl_tx_simulate(network="testnet", txJson)
   → { preflight:{feasible:true,...},
       suggestedPathfind:{ alternatives:[{ source_amount:"12345678", ... }] },
       recommendedFee:"12",
       warnings:[] }

Step 2d: sign + submit + verify
→ xrpl_sign(walletName="alice", txBlobUnsigned)
→ xrpl_tx_submit_signed(network="testnet", txBlobSigned, waitForValidation=true)
   → { engineResult:"tesSUCCESS", txHash, validated:true }
→ xrpl_tx_lookup(network="testnet", txHash, binary=false)
   → { meta:{ TransactionResult:"tesSUCCESS",
       delivered_amount:{"currency":"USD","issuer":"rIssuer...","value":"9.97"} } }
```

`delivered_amount` в `meta` — **авторитетная** реально-зачисленная сумма. Благодаря `tfPartialPayment + DeliverMin` ledger гарантирует:
- `value(delivered_amount) ≥ value(DeliverMin)` (здесь 9.97 ≥ 9.9 — принимаем).
- `drops_spent ≤ SendMax` (здесь ≤ 12 345 678 drops — принимаем).

Если читать оригинальный `Amount` (`10 USD`) вместо `meta.delivered_amount`, бухгалтерия зачислит 10 USD, а на счёт пришло только 9.97 USD — классический баг на стороне биржи.

Для долгоиграющих интерактивных UI (стакан) предпочитайте стрим-версию:

```text
→ handle = xrpl_path_find_create(network, sourceAccount=from, destinationAccount=to,
   destinationAmount=..., sendMax=...)
→ poll xrpl_path_find_status(network, handle) every few seconds until results stabilise
→ xrpl_path_find_close(network, handle) when done
```

`xrpl_ripple_path_find` — one-shot эквивалент — подходит для пакетного потока агента.

### Сценарий 3 — credential-gated payment через PermissionedDomain

Setup credentials, DepositAuth и PermissionedDomain **уже описан** в:
- [kyc-credentials-flow.md](kyc-credentials-flow.md) — полный XLS-70 lifecycle (CredentialCreate → CredentialAccept → DepositPreauth + AuthorizeCredentials).
- [permissioned-domain-and-did.md](permissioned-domain-and-did.md) — XLS-80 PermissionedDomain ledger entry и форма `acceptedCredentialsJson`.

Этот рецепт фокусируется на **Payment-стороне**: failure path до CredentialAccept, потом success path после.

Pre-state (уже на месте, см. два рецепта выше):
- ISSUER выполнил `xrpl_credential_create_prepare(account=ISSUER, subject=ALICE, credentialTypePlain="KYC-Tier1")` → tesSUCCESS. Credential в состоянии `provisional`.
- BOB выполнил `xrpl_account_set_prepare(setFlag=9 /* asfDepositAuth */)` → tesSUCCESS.
- BOB выполнил `xrpl_permissioned_domain_set_prepare(acceptedCredentialsJson='[{"issuer":"rIssuer...","credentialType":"4B59432D5469657231"}]')` → tesSUCCESS.
- BOB выполнил `xrpl_deposit_preauth_prepare(authorizeCredentialsJson='[{"issuer":"rIssuer...","credentialType":"4B59432D5469657231"}]')` → tesSUCCESS.
- ALICE **ещё не** запустила `CredentialAccept` — credential ещё `provisional`.

```text
agent ← {"step":"pay_gated","network":"testnet","from":"rAlice...","to":"rBob...",
         "amountDrops":"1000000","issuer":"rIssuer...",
         "credentialTypePlain":"KYC-Tier1"}

Step 3a: compute the credential hash (pure local)
→ xrpl_hash_credential(
    subject="rAlice...",
    issuer="rIssuer...",
    credentialTypePlain="KYC-Tier1"
  ) → "<64-hex>"  /* call it CREDHASH */

Step 3b: negative path — payment without an accepted credential
→ xrpl_payment_prepare(
    network="testnet",
    account="rAlice...",
    destination="rBob...",
    amount="1000000",
    credentialIdsJson='["<CREDHASH>"]'
  )
→ xrpl_sign(walletName="alice", txBlobUnsigned)
→ xrpl_tx_submit_signed(network="testnet", txBlobSigned, waitForValidation=true)
   → { engineResult:"tecNO_PERMISSION" /* sometimes tecNO_PERMISSION_RECIPIENT
                                          depending on amendment version */,
       validated:true }
```

Ledger оценивает credential: он существует, совпадает с `AuthorizeCredentials` BOB'а, но его флаг `lsfAccepted` = `false` — поэтому ворота получателя отвергают депозит. **Fee всё равно расходуется** (коды `tec*` — это claimed-fee failures). Это by design: отсекает спамеров.

```text
Step 3c: ALICE accepts the credential
→ xrpl_credential_accept_prepare(
    network="testnet",
    account="rAlice...",
    issuer="rIssuer...",
    credentialTypePlain="KYC-Tier1"
  )
→ xrpl_sign(walletName="alice", txBlobUnsigned)
→ xrpl_tx_submit_signed → tesSUCCESS

Step 3d: retry the same Payment
→ xrpl_payment_prepare(... same args as Step 3b ...)
→ xrpl_sign(walletName="alice", txBlobUnsigned)
→ xrpl_tx_submit_signed(network="testnet", txBlobSigned, waitForValidation=true)
   → { engineResult:"tesSUCCESS", txHash, validated:true }
→ xrpl_tx_lookup(network="testnet", txHash, binary=false)
   → { meta:{ TransactionResult:"tesSUCCESS",
              delivered_amount:"1000000" } }
```

## Verification

Сценарий 1:
- [ ] До оплаты: записаны `xrpl_xrp_balance(rAlice)` и `xrpl_xrp_balance(rBob)`.
- [ ] После оплаты: `xrpl_tx_lookup(txHash).validated == true`.
- [ ] `meta.TransactionResult == "tesSUCCESS"`.
- [ ] `meta.delivered_amount == "1500000"` (строгое строковое сравнение — никогда не против float-конвертированного XRP-значения).
- [ ] `xrpl_xrp_balance(rBob) - pre = "1.5"` XRP.

Сценарий 2:
- [ ] `xrpl_account_lines(rBob)` показывает trustline на `rIssuer...` для `USD` с `limit ≥ 10`.
- [ ] `xrpl_ripple_path_find` возвращает непустой `alternatives[]`.
- [ ] `xrpl_tx_simulate.preflight.feasible == true`.
- [ ] `xrpl_tx_lookup(txHash).meta.delivered_amount.value ≥ "9.9"` (пол `DeliverMin`).
- [ ] USD-баланс `xrpl_account_lines(rBob)` вырос на `meta.delivered_amount.value`.

Сценарий 3:
- [ ] До accept: `xrpl_tx_lookup(<txHash from step 3b>).meta.TransactionResult == "tecNO_PERMISSION"` (или `"tecNO_PERMISSION_RECIPIENT"`).
- [ ] После CredentialAccept: `xrpl_account_credentials(rAlice).held[?]` имеет `accepted == true`.
- [ ] Retry: `xrpl_tx_lookup(<txHash from step 3d>).meta.TransactionResult == "tesSUCCESS"`.
- [ ] `xrpl_xrp_balance(rBob)` увеличился на 1 XRP.

## Gotchas

- **Drops vs XRP — главный footgun**. `Amount` в XRP-Payment'е — **строка drops'ов**, не десятичное XRP-число. Передача `"1.5"` в `xrpl_payment_prepare(amount=...)` будет **интерпретирована как 1.5 drops** = 0.0000015 XRP, не 1.5 XRP. Конверсия: `drops = round(xrp * 1_000_000)`. Делается как строковая операция, чтобы избежать float64-округлений (например, `0.1 + 0.2 = 0.30000000000000004`).
- **`meta.delivered_amount` ≠ `Amount` для token-payments**. С `tfPartialPayment` ledger может зачислить меньше, чем `Amount`. Всегда кредитуйте downstream-бухгалтерию из `meta.delivered_amount`, не из оригинального поля `Amount`. Для XRP-payments без partial-payment они совпадают — но писать код надо так, как будто нет.
- **`tfPartialPayment` обязателен для token-↔-token доставки, которая может маршрутизироваться через несколько хопов** — без него `tecPATH_PARTIAL` ударит, если на каком-то одном хопе не хватит ликвидности на точный `Amount`. Поле `DeliverMin` — ваш safety floor: tx падает как `tecPATH_PARTIAL`, если не может доставить хотя бы это.
- **`SendMax` — максимум spend'а, не реальный spend**. Для cross-currency Payment'ов ledger тратит `min(SendMax, source_amount_of_chosen_path)`. Средства сверх реально потреблённого остаются в source-аккаунте.
- **Path staleness**. `xrpl_ripple_path_find` возвращает path, посчитанный против конкретного ledger'а; к моменту submit'а order book мог сдвинуться. `xrpl_tx_simulate` перепроверяет; на тяжёлых маркетах предпочитайте `xrpl_path_find_create` + короткий poll-loop, заканчивающийся submit'ом, всё в пределах ~10 ledger'ов.
- **Коды `tec*` потребляют fee**. `tecNO_PERMISSION` в сценарии 3 шаг 3b всё равно применяется к ledger'у — alice теряет fee, хотя никакие средства не двинулись. Это by design и должно отражаться в любом UI "failed payment".
- **`tecNO_PERMISSION` vs `tecNO_PERMISSION_RECIPIENT`**. Старый rippled возвращал `tecNO_PERMISSION`; новые (после XLS-70 amendments) уточнили это до `tecNO_PERMISSION_RECIPIENT` для deposit-side отказа. Агенты должны трактовать оба как один и тот же recipient-gate reject.
- **DepositAuth без preauth блокирует ВСЕ payment'ы**. Если BOB включил `asfDepositAuth`, но забыл либо явный `DepositPreauth(authorize=...)`, либо credential-based `DepositPreauth(authorizeCredentialsJson=...)`, каждый входящий Payment падает. Проверяйте preauth-state получателя перед отправкой.
- **Validated vs current**. `xrpl_tx_lookup` с `validated=true` — единственный авторитетный источник истины — никогда не доверяйте immediate submit response как финальному.
- **Trustlines у получателя cross-currency Payment'ов**. BOB ДОЛЖЕН иметь trustline на ISSUER для USD до того, как сценарий 2 сможет доставить ему USD. Без неё: `tecNO_LINE` или `tecPATH_DRY`. Сначала используйте `xrpl_trustset_prepare`.

## Production use-cases

- **Wallet UI sends**: сценарий 1 плюс UX-слой, транслирующий введённые пользователем `1.5` XRP в `"1500000"` drops на границе. Всегда читать `meta.delivered_amount` для чека.
- **Cross-currency on-ramp**: alice вносит XRP, on-ramp маршрутизирует через DEX/AMM, чтобы доставить USD-stablecoin на кошелёк bob'а. `SendMax` — верхняя граница, `DeliverMin` — пол — обе показываются пользователю как worst- и best-case quote.
- **Permissioned merchant deposit**: bob — регулируемый merchant; только KYC-верифицированные плательщики (по credential-allow-list'у issuer'а) могут депонировать. Merchant отвергает всех остальных на уровне протокола — без off-chain фильтрации.
- **B2B treasury payments**: комбинировать сценарий 2 (cross-currency) с существующим рецептом [multi-sign-collection.md](multi-sign-collection.md) — treasury-кошелёк платит USD-инвойсы, фондированные из XRP, подписанные кворумом multi-sig.
- **Whitelisted airdrop**: сценарий 3 + скрипт, итерирующий список credential-holders, отправляющий каждому маленький drops-Payment. Все без accepted-credential'а отфильтровываются самим ledger'ом.

## Расширения

- **Batch send to N recipients** (XLS-56 BatchV1_1, см. [atomic-batch-payment.md](atomic-batch-payment.md)) — одна атомарная транзакция, содержащая N inner `Payment`-операций со смешанными XRP/token amounts.
- **DestinationTag enforcement**: многие exchange deposits требуют tag — `xrpl_tx_preflight` помечает отсутствующие tags как warnings, когда у destination'а `RequireDestinationTag`. Сделайте это hard error до подписи.
- **Path-find с credential constraint** (XLS-80 permissioned DEX): pathfind только через ордербуки, ограниченные тем же `DomainID`, который BOB использует для депозитов.
- **Auto-DeliverMin tuning**: вместо статичного 99 %-пола вычислять `DeliverMin` из текущего spread'а между котировкой path'а и on-chain reference oracle (см. [oracle-price-feed.md](oracle-price-feed.md)).
