>  🌐 **Язык**: [English](../../examples/atomic-batch-payment.md) | **Русский**

# Пример: Atomic Batch payment (XLS-56)

Cowork-агент собирает несколько Payment'ов от разных аккаунтов и упаковывает их в один `Batch` так, что либо все проходят, либо ни один (классический атомарный 3-way swap, оплата с rebate, payroll-распределение).

Референс: [TestIBatch.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIBatch.cs).

> ⚠️ **Внимание про статус Batch на mainnet/testnet**: amendment был временно удалён в rippled v3.1.1 из-за бага и будет возвращён как `BatchV1_1`. Рецепт работает против standalone-узла с включённым amendment или после релиза BatchV1_1. На стандартном testnet рецепт сейчас вернёт `temDISABLED` — это **ожидаемое** поведение.

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_payment_prepare` ×N (для inner-tx), `xrpl_batch_prepare`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_account_info` (для получения Sequence) |
| **xrpl-signer** | `xrpl_sign` для outer Batch, `xrpl_sign_multi` для multi-account BatchSigners |

## Режимы Batch

Установлены константы flag'ов в `xrpl_batch_prepare`:

| mode | flag | поведение |
|---|---|---|
| `AllOrNothing` | 0x00010000 | Все inner успешны → outer success; иначе все откатываются. Классический атомарный swap. |
| `OnlyOne` | 0x00020000 | Первый прошедший — фиксируется, остальные не пытаются. Polling-like best-of-N. |
| `UntilFailure` | 0x00040000 | Применяет по порядку, останавливается на первой ошибке (предыдущие зафиксированы). Цепочка зависимых шагов. |
| `Independent` | 0x00080000 | Каждый inner оценивается независимо, результаты не зависят друг от друга. Параллельные несвязанные tx. |

## Архитектура 3-way swap

```
Alice owes USD to Bob, Bob owes EUR to Carol, Carol owes XRP to Alice.
Все три долга закрываются одной Batch'ой (mode=AllOrNothing).

  ┌─────────────────── outer Batch (account=Alice, mode=AllOrNothing) ────────────────┐
  │                                                                                    │
  │  RawTransactions:                                                                 │
  │    [0] Payment{ Account=Alice, Dest=Bob,   Amount={USD,...,issuer=Alice} }        │
  │    [1] Payment{ Account=Bob,   Dest=Carol, Amount={EUR,...,issuer=Bob}   }        │
  │    [2] Payment{ Account=Carol, Dest=Alice, Amount="3000000" /*drops XRP*/}        │
  │                                                                                    │
  │  BatchSigners:                                                                    │
  │    [{ Account=Bob,   SigningPubKey, TxnSignature }]                                │
  │    [{ Account=Carol, SigningPubKey, TxnSignature }]                                │
  │  // Alice (outer Account) signs the outer envelope, no separate BatchSigner needed. │
  └────────────────────────────────────────────────────────────────────────────────────┘
```

**Critical**: каждый inner-tx должен иметь:
- свой `Sequence` (или `TicketSequence`) — текущий для inner-Account'а;
- `Fee = "0"` (форсится prepare-tool'ом);
- `SigningPubKey = ""` (форсится);
- НЕТ `TxnSignature`, НЕТ `Signers` (форсится — удаляются);
- флаг `tfInnerBatchTxn = 0x40000000` в Flags (форсится OR'ом).

## Pre-requisites

- 3 funded аккаунта (Alice, Bob, Carol) импортированных в keystore.
- Trust lines: Bob → Alice (USD), Carol → Bob (EUR). XRP не требует trust lines.
- Для multi-account batch: каждый non-outer Account должен подписать свою часть отдельно.

## Промт агента

```markdown
---
name: batch-orchestrator
description: Builds and submits an atomic XLS-56 Batch transaction containing
  Payments from multiple accounts. Co-signs from each non-outer Account.
tools: xrpl_account_info, xrpl_payment_prepare, xrpl_batch_prepare,
  xrpl_tx_preflight, xrpl_tx_submit_signed, xrpl_sign, xrpl_sign_multi
---

Input:
{
  "network": "testnet",
  "mode": "AllOrNothing" | "OnlyOne" | "UntilFailure" | "Independent",
  "outerAccount": "rAlice...",
  "innerPayments": [
    { "account":"rAlice...", "destination":"rBob...", "amount":"<...>" },
    { "account":"rBob...",   "destination":"rCarol...", "amount":"<...>" },
    { "account":"rCarol...", "destination":"rAlice...", "amount":"3000000" }
  ]
}

Algorithm:
1. For each inner: call `xrpl_account_info(account=inner.account)` to fetch
   current Sequence. Store seq+i (i = 0,1,2,... within the same Account)
   so concurrent inner-tx from one account use distinct sequence numbers.
2. For each inner: call `xrpl_payment_prepare(network, account, destination,
   amount, sequence=<computed>)` — BUT this gives a fully-prepared standalone
   tx which is too much. Instead, build the inner JSON manually:
     {"TransactionType":"Payment","Account":...,"Destination":...,
      "Amount":...,"Sequence":<i>}
   (no Fee/SigningPubKey/Flags — `xrpl_batch_prepare` forces them).
3. Call `xrpl_batch_prepare(network, account=outerAccount, mode,
   innerTransactionsJson=<JSON-array>, batchSignersJson=<optional>)`.
4. For multi-account batches, collect `BatchSigners`:
   - For each unique non-outer Account in inner list: ask the signer to
     sign that inner's "batch signing data" (inner's blob + outer Account
     concat). Use `xrpl_sign_multi(walletName=accountAddr, txBlobUnsigned=outerBlob)`.
   - Aggregate the resulting BatchSigner entries and pass via batchSignersJson.
5. `xrpl_tx_preflight` outer.
6. `xrpl_sign(walletName=outerAccount, ...)` finalizes the outer signature.
7. `xrpl_tx_submit_signed(..., waitForValidation=true)`.

If `engine_result_message` includes `temDISABLED` — Batch amendment not active
on this network; report and stop.
```

## Step-by-step (упрощённый single-account case)

Самый простой Batch — несколько Payment'ов от **одного** Account'а. Без BatchSigners.

### 1. Fetch current sequence

```text
agent → xrpl_account_info(network="testnet", account="rAlice...")
       → { "AccountData": { "Sequence": 42, ... } }
```

### 2. Build inner JSONs

```json
[
  {"TransactionType":"Payment","Account":"rAlice...","Destination":"rBob...",
   "Amount":"1000000","Sequence":42},
  {"TransactionType":"Payment","Account":"rAlice...","Destination":"rCarol...",
   "Amount":"2000000","Sequence":43}
]
```

Передаём как строку в `innerTransactionsJson`.

### 3. Prepare outer Batch

```text
agent → xrpl_batch_prepare(
  network="testnet",
  account="rAlice...",
  mode="AllOrNothing",
  innerTransactionsJson="[ ... ]"
)
```

Tool:
- OR'ит `tfInnerBatchTxn` в Flags каждого inner;
- форсит Fee="0", SigningPubKey="";
- проверяет ≤8 inner;
- проверяет валидные Sequence на каждом inner;
- Autofill outer'а (заполняет outer.Sequence — Sequence Alice'а ПОСЛЕ inner'ов, т.е. 44).

### 4. Preflight + sign + submit

```text
agent → xrpl_tx_preflight(txJson) → feasible=true
agent → xrpl_sign(walletName="rAlice...", txBlobUnsigned)
agent → xrpl_tx_submit_signed(txBlobSigned, waitForValidation=true)
       → { engineResult: "tesSUCCESS", txHash, ledgerIndex, validated:true }
```

## Multi-account case (3-way swap)

Если в inner'ах разные Accounts — нужна `batchSignersJson` с подписями от каждого non-outer Account'а.

### Получение BatchSigner подписей

XLS-56 определяет: каждый non-outer Account подписывает **specific signing data**, состоящую из outer tx blob (без BatchSigners) + canonical prefix. SDK предоставляет это через `Wallet.Sign(tx, multisign:true)` сейчас, но для Batch нужен специальный `wallet.SignAsBatchSigner(outerTx)`.

В нашем MCP это пока не выделено в отдельный tool — workaround:

1. Сначала собирается outer Batch без `batchSignersJson` через `xrpl_batch_prepare`.
2. Полученный `txBlobUnsigned` отдаётся каждому non-outer Account'у для подписи через `xrpl_sign_multi(walletName=accountAddr, txBlobUnsigned)`.
3. Из ответа извлекаются `SigningPubKey` и `TxnSignature` (формируются в JSON-объект).
4. Собирается `batchSignersJson` массив, и `xrpl_batch_prepare` вызывается **повторно** с этим массивом.
5. Outer Account подписывает финальный blob через `xrpl_sign`.

> ⚠️ **Gap**: текущий `xrpl_sign_multi` не выдаёт BatchSigner-specific подпись (он рассчитан на SignerList multi-sign, не на XLS-56). Потенциальное расширение — `xrpl_sign_batch_signer` tool. Для standalone-узла можно работать только с single-account Batch'ами.

## Verification

- `engine_result == "tesSUCCESS"` — все inner'ы успешно применены (в режиме AllOrNothing).
- В metadata будет один `AffectedNodes` массив суммирующий изменения всех inner'ов.
- Для `OnlyOne` mode — meta покажет какой именно inner успешно применён.
- Для `UntilFailure` — meta покажет до какого inner'а дошло.
- `xrpl_account_tx(account=outerAccount)` покажет одну запись с `tx.TransactionType=Batch`.

## Use-cases где это реально нужно

- **Atomic swap** через ledger (вместо PathFinding/cross-currency Payment).
- **Payroll batching** — outer Account платит N сотрудникам разными суммами/токенами. Если у одного destination'а DepositAuth/DisallowIncoming — вся пачка откатывается.
- **DEX rebalance** — отменить старый Offer + создать новый одним atomic-set'ом.
- **MPT distribution** — issuer переводит свежесозданный MPT N holder'ам, авторизованным заранее.
