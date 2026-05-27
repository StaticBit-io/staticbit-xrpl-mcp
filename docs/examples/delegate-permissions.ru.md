> 🇬🇧 [Read in English](delegate-permissions.md)

# Пример: Account permission delegation (XLS-75)

Owner делегирует право submit'ить транзакции определённых типов другому account'у, без передачи master key или signer-list-set'апа. Полезно для bots, hot wallets, role-based corporate accounts.

Референс: [TestIDelegateSet.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIDelegateSet.cs).

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_delegate_set_prepare`, `xrpl_account_objects` (тип Delegate), `xrpl_payment_prepare` (или другие tx), `xrpl_tx_preflight`, `xrpl_tx_submit_signed` |
| **xrpl-signer** | `xrpl_sign` |

## Концепция (XLS-75)

- Owner submit'ит `DelegateSet` с массивом permissions = разрешённых tx-types. До 10 типов на одну delegation.
- Создаёт ledger entry `Delegate { Account=owner, Authorize=delegatee, Permissions=[...] }`.
- Delegatee может submit'ить tx указанных типов **от имени owner'а** через специальный `Delegate` field на tx (TX-type код, не account).
- Owner подписывает delegation; delegatee подписывает каждую конкретную tx.
- **Non-delegable** (security-critical): `AccountSet`, `SetRegularKey`, `SignerListSet`, `DelegateSet`. Эти типы blocked rippled'ом — даже если попытаешься delegate, tx fails.
- Пустой `Permissions` массив = clear delegation.

## Архитектура

```
Setup:
  OWNER ──DelegateSet(Authorize=DELEGATEE, Permissions=[Payment,TrustSet,OfferCreate])──►
                                                                                    ↓ creates Delegate entry

Usage (delegatee submits on owner's behalf):
  DELEGATEE ──Payment{ Account=OWNER, Delegate=<DELEGATEE>, Destination=..., Amount=... }──►
                                                                                    ↓ rippled validates:
                                                                                    ↓   1. Delegate entry exists for (OWNER, DELEGATEE)
                                                                                    ↓   2. tx.TransactionType in Permissions
                                                                                    ↓   3. tx signed by DELEGATEE
                                                                                    ↓ applies tx as if OWNER submitted

Revocation:
  OWNER ──DelegateSet(Authorize=DELEGATEE, Permissions=[])──► clears delegation
   OR
  OWNER ──DelegateSet(Authorize=DELEGATEE, Permissions=[ReducedSet])──► updates permissions
```

## Pre-requisites

- 1 funded OWNER account.
- 1 funded DELEGATEE account.
- Их seeds оба в keystore.
- XLS-75 amendment активирован.

## Промт агента

```markdown
---
name: delegation-orchestrator
description: Sets up account permission delegation, monitors active delegations,
  performs delegated transactions on behalf of the owner.
tools: xrpl_delegate_set_prepare, xrpl_payment_prepare, xrpl_tx_prepare_generic,
  xrpl_account_objects, xrpl_tx_preflight, xrpl_tx_submit_signed, xrpl_sign
---

Inputs:
- {"step":"grant","network":"testnet","owner":"r...","delegatee":"r...",
   "permissionsCsv":"Payment,TrustSet,OfferCreate,OfferCancel"}
- {"step":"revoke","network":"...","owner":"r...","delegatee":"r..."}
- {"step":"delegated_tx","network":"...","owner":"r...","delegatee":"r...",
   "txJson":"<tx with Delegate field, Account=owner>"}
- {"step":"status","network":"...","owner":"r..."}

For "grant" / "revoke":
1. xrpl_delegate_set_prepare(network, account=owner, delegateAccount=delegatee,
   permissionsCsv)
2. preflight (max 10 entries, no duplicates, none in non-delegable list,
   no AccountSet/SetRegularKey/SignerListSet/DelegateSet)
3. sign by owner → submit.

For "delegated_tx":
1. Construct txJson with: Account=owner, Delegate=delegatee_pub_key OR
   delegatee address (rippled accepts both). For now, set Delegate = delegatee.
2. xrpl_tx_prepare_generic(network, account=owner, txJson)
   → blob_unsigned (autofilled using owner's Sequence)
3. preflight
4. sign by **delegatee** — KEY POINT — delegatee signs, not owner.
   xrpl_sign(walletName=delegatee, blob_unsigned)
5. submit.

For "status":
xrpl_account_objects(network, account=owner, type="Delegate")
→ list of all delegations granted by owner
→ each entry shows Authorize and Permissions
```

## Step-by-step

### 1. Grant delegation

```text
agent ← {"step":"grant","owner":"rAlice...","delegatee":"rTradeBot...",
         "permissionsCsv":"Payment,TrustSet,OfferCreate,OfferCancel"}

→ xrpl_delegate_set_prepare(
    network="testnet",
    account="rAlice...",
    delegateAccount="rTradeBot...",
    permissionsCsv="Payment,TrustSet,OfferCreate,OfferCancel"
  )

Tool internally maps:
  Payment → 0
  TrustSet → 20
  OfferCreate → 7
  OfferCancel → 8
Permissions becomes [{Permission:{PermissionValue:0}}, ..., {PermissionValue:8}]

→ preflight (no duplicates, no non-delegable types, ≤10 entries) → feasible=true
→ xrpl_sign(walletName=rAlice, blob) — OWNER signs the grant
→ xrpl_tx_submit_signed → tesSUCCESS
```

Tool rejects до отправки если попытаешься включить запрещённый тип:

```text
permissionsCsv="Payment,AccountSet"
  → ArgumentException: "Transaction type 'AccountSet' cannot be delegated"
```

### 2. Verify delegation

```text
agent → xrpl_account_objects(network, account="rAlice...", type="Delegate")
       → {
           "AccountObjects": [
             {
               "LedgerEntryType": "Delegate",
               "Account": "rAlice...",
               "Authorize": "rTradeBot...",
               "Permissions": [
                 {"Permission":{"PermissionValue":0}},   // Payment
                 {"Permission":{"PermissionValue":20}},  // TrustSet
                 {"Permission":{"PermissionValue":7}},   // OfferCreate
                 {"Permission":{"PermissionValue":8}}    // OfferCancel
               ],
               ...
             }
           ]
         }
```

### 3. Delegatee submits delegated transaction

DELEGATEE (например, trading bot) хочет от имени Alice разместить order:

```text
agent ← {"step":"delegated_tx","owner":"rAlice...","delegatee":"rTradeBot...",
         "txJson":{
           "TransactionType":"OfferCreate",
           "Account":"rAlice...",
           "Delegate":"rTradeBot...",
           "TakerPays":{"value":"100","currency":"USD","issuer":"rIssuer..."},
           "TakerGets":"30000000"
         }}

→ xrpl_tx_prepare_generic(network, account="rAlice...", txJson)
   → blob_unsigned (autofill takes Sequence from rAlice's account_info)
→ preflight → feasible=true
→ xrpl_sign(walletName="rTradeBot...", blob_unsigned)
   IMPORTANT: signing by DELEGATEE, не by OWNER
→ xrpl_tx_submit_signed → tesSUCCESS
```

rippled валидирует:
- Delegate entry для `(rAlice, rTradeBot)` exists?
- `OfferCreate` (code 7) ∈ `Permissions`?
- TxnSignature валиден для signing public key, который соответствует `Delegate` account?

Если всё ok — tx применяется **как будто rAlice submit'ила сама**. Sequence/Fee/reserves count'ятся для Alice'ы.

### 4. Negative case (delegatee tries unauthorized type)

```text
TX with TransactionType="AccountSet" (non-delegable):
  → fails at prepare-stage in our agent if permissionsCsv blocked it
  → or fails at rippled with tecNO_DELEGATE_PERMISSION if user bypassed
```

### 5. Revoke delegation

```text
agent ← {"step":"revoke","owner":"rAlice...","delegatee":"rTradeBot..."}

→ xrpl_delegate_set_prepare(
    network, account="rAlice...", delegateAccount="rTradeBot...",
    permissionsCsv=""    // empty → clear
  )
→ sign by rAlice → submit → tesSUCCESS
```

После этого Delegate entry удаляется. Любой следующий delegated-tx fails с `tecNO_DELEGATE_PERMISSION`.

### 6. Modify permissions

Не нужен явный modify — `DelegateSet` с теми же `(account, delegateAccount)` но новым `permissionsCsv` **полностью заменяет** Permissions массив:

```text
agent ← {"step":"grant","owner":"...","delegatee":"...",
         "permissionsCsv":"Payment"}    // reduced from 4 types to 1
```

Старый Delegate entry overwritten.

## Verification checklist

- [ ] Step 1: `xrpl_account_objects(owner, type="Delegate")` показывает new entry.
- [ ] Step 1: Permissions массив с правильными PermissionValue codes.
- [ ] Step 3: Tx applied with `tx.Account=owner`, but signed by delegatee — `xrpl_tx_lookup` showed `Delegate` field set.
- [ ] Step 5: после revoke, `xrpl_account_objects` больше не возвращает запись.
- [ ] Negative: delegated tx с typer вне allowlist → `tecNO_DELEGATE_PERMISSION` или `tecNO_PERMISSION`.

## Подводные камни

- **Non-delegable types are blocked**: AccountSet, SetRegularKey, SignerListSet, DelegateSet — нельзя delegate. Tool отказывает до submit'а.
- **Per-delegatee, not per-account-wide**: каждая (owner, delegatee) пара — отдельная Delegate entry. Owner может иметь N независимых delegatees с разными permission sets.
- **Sequence & Fee belong to OWNER**: даже когда delegatee submits, Sequence increment'ится у owner'а, fee списывается у owner'а, reserves считаются у owner'а. Delegatee just signs.
- **Reserve cost**: Delegate entry — owner-object на owner'е (+2 XRP reserve). N delegations = N reserves.
- **Reuse of master/regular key by owner remains valid**: даже после grant'а, owner может submit'ить тот же tx type сам без `Delegate` field. Delegation — additive permission, not replacement.

## Use-cases

- **Trading bot**: owner grants `Payment,OfferCreate,OfferCancel` к bot account'у. Bot управляет orders, не имеет доступа к owner's seed.
- **Custodial-style hot wallet**: custodian's account имеет `Payment` permission на cold storage account — может paying clients без cold storage key access.
- **Role-based corporate accounts**: separate accounts для "Accounting" (allowed `TrustSet`), "Trading" (allowed `OfferCreate`), "Payments" (allowed `Payment`). Каждый delegatee имеет minimum-required permission.
- **Time-bounded delegation**: combine с external scheduler — agent revoke'ит delegation через час после grant'а.

## Расширения

- **Auto-revoke after timeout**: combine с `/loop` или `/schedule` — agent monitors `xrpl_account_objects` и автоматически revoke'ит delegations старше N часов.
- **Multi-delegatee orchestration**: один owner делегирует разным bots разные permissions, agent tracks all and provides single dashboard через `xrpl_account_objects`.
- **Conditional revocation**: agent monitors delegatee's behavior через `xrpl_account_tx_since` и revoke'ит при suspicious patterns.
- **Combined with multi-sign**: SignerList controls who can submit DelegateSet (treasury committee), delegations go to operational bots.
