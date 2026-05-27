> 🇬🇧 [Read in English](tickets-parallel-submit.md)

# Пример: Tickets для параллельной отправки tx

Resilient batch agent: резервирует пул Tickets, потом submit'ит N независимых транзакций параллельно (не блокируясь на ordered Sequence). Полезно для market-making, payout-distribution, mass-airdrop.

Референс: `ticketCreate.cs` (lower-case file) и весь pattern использования `TicketSequence` вместо `Sequence` в SDK integration-тестах.

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_account_info`, `xrpl_ticket_create_prepare`, `xrpl_tx_prepare_generic` (для tx с TicketSequence), `xrpl_payment_prepare` (если payment), `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_account_objects` (тип `Ticket`) |
| **xrpl-signer** | `xrpl_sign` |

## Зачем нужны Tickets

Обычный XRPL flow требует **последовательного** `Sequence` на каждой tx от одного account'а. Если ты хочешь submit'ить 10 параллельных Payment'ов и хотя бы один тормозит — все остальные ждут.

Tickets — это **зарезервированные сlot'ы для будущих tx**. Аккаунт делает `TicketCreate(TicketCount=N)` — Sequence увеличивается на N, и эти N номеров становятся свободно используемыми **в любом порядке**.

Каждая дальнейшая tx помечается `TicketSequence=<ticket_num>` (без `Sequence` поля). Можно отправлять все 10 параллельно — порядок применения rippled-ом не важен.

Лимит: одновременно у account'а ≤ 250 Tickets.

## Архитектура

```
Account state: Sequence=42, OwnerCount=0

Step 1: TicketCreate(TicketCount=10)
  ─────────────────────────────────►  rippled
                                       ↓ tesSUCCESS
                                       Account: Sequence=53, OwnerCount=10
                                       Created Ticket entries with
                                       TicketSequence = 43, 44, 45, ..., 52

Step 2: Build 10 Payment txs in parallel
  payment[0] uses TicketSequence=43
  payment[1] uses TicketSequence=44
  ...
  payment[9] uses TicketSequence=52
   each has Sequence = 0 (omitted), TicketSequence set

Step 3: Submit all 10 concurrently
  ─────────────────────────────────►  rippled
                                       ↓ each independently lands
                                       (any order; failed tickets stay)
```

После каждого успешного submit'а — ticket "burned", соответствующий `Ticket` ledger entry уничтожается, OwnerCount уменьшается.

## Pre-requisites

- 1 funded аккаунт.
- Reserve должен покрывать **base reserve + (current OwnerCount + N) × increment** на время существования tickets. Т.е. если планируешь 10 tickets, нужно держать ≥ 10 × 2 XRP = 20 XRP сверху для owner reserve.

## Промт агента

```markdown
---
name: tickets-parallel
description: Reserves a pool of Tickets, then issues N independent transactions
  in parallel using TicketSequence (no head-of-line blocking).
tools: xrpl_account_info, xrpl_ticket_create_prepare, xrpl_tx_prepare_generic,
  xrpl_payment_prepare, xrpl_tx_preflight, xrpl_tx_submit_signed,
  xrpl_account_objects, xrpl_sign
---

Input:
{
  "network": "testnet",
  "account": "r...",
  "ticketCount": 10,
  "transactions": [
    {"to":"rUser1...","amountDrops":"1000000"},
    {"to":"rUser2...","amountDrops":"2000000"},
    ...
  ]
}

Workflow:
1. Validate len(transactions) <= ticketCount (else fail-fast).
2. Call xrpl_account_info(account) → get current Sequence (call it S).
3. Call xrpl_ticket_create_prepare(network, account, ticketCount).
4. preflight + sign + submit_signed (waitForValidation=true).
5. On success, reserved Tickets are TicketSequence = S+1 .. S+ticketCount.
6. For each transaction in transactions array (use i-th ticket):
   a. Build txJson with TicketSequence=S+i+1 and no Sequence:
      {"TransactionType":"Payment","Account":account,"Destination":...,
       "Amount":"<drops>","TicketSequence":<S+i+1>}
   b. Call xrpl_tx_prepare_generic(network, account, txJson).
   c. preflight (skip if you trust the build).
   d. sign + submit_signed (DO NOT wait for validation on each — fire and
      forget; track txHashes for later validation if needed).
7. Optionally poll each txHash via xrpl_tx_lookup until validated.

Return:
{
  "ticketsCreated": 10,
  "submitted": [{ "txHash":"...","ticketSequence":44,"destination":"...", }, ...],
  "ticketsRemaining": <see xrpl_account_objects type=Ticket>
}
```

## Step-by-step

### 1. Check current state

```text
agent → xrpl_account_info(network="testnet", account="rAlice...")
       → { "AccountData": { "Sequence": 42, "OwnerCount": 0 } }
```

Current Sequence = 42. После TicketCreate(10) станет 53.

### 2. Reserve 10 tickets

```text
agent → xrpl_ticket_create_prepare(network, account="rAlice...", ticketCount=10)
       → tx_blob_unsigned (TransactionType=TicketCreate, Sequence=42, TicketCount=10)
agent → xrpl_tx_preflight → feasible=true (1..250 range)
agent → xrpl_sign(walletName=rAlice, blob)
agent → xrpl_tx_submit_signed(blob_signed, waitForValidation=true)
       → tesSUCCESS, validated, ledger=12345
```

После validation: `rAlice.Sequence = 53`, OwnerCount = 10. Tickets с `TicketSequence = 43, 44, ..., 52` существуют как ledger entries.

### 3. Verify tickets exist

```text
agent → xrpl_account_objects(network, account="rAlice...", type="Ticket")
       → { "AccountObjects": [
            { "TicketSequence": 43, ... },
            { "TicketSequence": 44, ... },
            ...
            { "TicketSequence": 52, ... }
          ] }
```

10 entries.

### 4. Parallel submit using TicketSequence

```text
for i, payment in enumerate(transactions):
  ticket = 43 + i  # use tickets in any order — doesn't matter
  txJson = {
    "TransactionType": "Payment",
    "Account": "rAlice...",
    "Destination": payment.to,
    "Amount": payment.amountDrops,
    "TicketSequence": ticket
    # NO "Sequence" field
  }
  agent → xrpl_tx_prepare_generic(network, account, txJson) → blob_unsigned
  agent → xrpl_sign(walletName=rAlice, blob_unsigned) → blob_signed
  agent → xrpl_tx_submit_signed(blob_signed, waitForValidation=false)
         → { txHash, engineResult: "tesSUCCESS" /* preliminary */ }
```

Все 10 submit'ов идут параллельно — порядок применения в ledger'е может быть любой.

### 5. Poll for finality

```text
for txHash in submitted:
  agent → xrpl_tx_lookup(network, txHash)
         → { validated:true, meta:{TransactionResult:"tesSUCCESS"}, ledger_index:... }
```

Если кто-то не validated после ~LLS=20 ledgers — повторить submit с тем же blob'ом (rippled idempotent на TxBlob).

### 6. Verify tickets consumed

```text
agent → xrpl_account_objects(account="rAlice...", type="Ticket")
       → { "AccountObjects": [] }
```

Все 10 tickets "сожжены", OwnerCount уменьшился на 10.

## Variations

### Use only some of the reserved tickets

Если зарезервировал 10, а понадобилось только 7 — оставшиеся 3 tickets лежат как ledger entries, занимают reserve. Чтобы освободить — `xrpl_tx_prepare_generic` с минимальным `AccountSet{ Account, TicketSequence=<unused> }`. Это no-op tx, но "сжигает" ticket.

### Parallel limit

В реальности не submit'ите все 10 одновременно — rippled может временно reject'ить с `terQUEUED`. Рекомендуемый pattern — batch'и по 3-5 с small delay (250ms).

### Mix tx types

Tickets — не привязаны к tx type. Можно один ticket использовать для `OfferCreate`, другой для `Payment`, третий для `TrustSet` — все из того же account'а.

## Use-cases

- **Market-maker order placement**: разместить N orders одновременно через `OfferCreate` × N с TicketSequences.
- **Mass airdrop**: 250 payouts → создать 250 tickets → parallel submit. Если один payout fails (например, recipient has DisallowIncomingXRP), остальные продолжают.
- **Account recovery preparation**: account создаёт несколько tickets ЗАРАНЕЕ, затем "разрезает" account state (`AccountDelete` + `SetRegularKey` + `SignerListSet`) через них — обычно sequence-blocked.
- **Resilient batch jobs**: критичные tx (smart-contract-like operation chain) — каждая использует свой ticket, при сбое одной остальные не блокируются.

## Verification checklist

- [ ] After Step 2: `Sequence` increased by `ticketCount`, `OwnerCount += ticketCount`.
- [ ] Step 3: `xrpl_account_objects` показывает `ticketCount` ticket entries с правильными TicketSequence numbers.
- [ ] Step 4 каждый submit имеет `TicketSequence` set, `Sequence` отсутствует/0.
- [ ] After all submits validated: `OwnerCount` уменьшился на число successful txs.
- [ ] Все tx hashes доступны через `xrpl_account_tx(account)`.

## Подводные камни

- **Ticket limit 250** — если попытаешься `ticketCount=251` или у account'а уже 250 tickets и хочешь ещё — `temINVALID_COUNT`. Preflight это поймает.
- **Reserve** — `(current OwnerCount + ticketCount) × inc + base` должно быть < balance - 1 XRP buffer. Иначе TicketCreate fails с `tecINSUFFICIENT_RESERVE`.
- **Ticket survival across rippled restarts** — tickets хранятся в ledger, не в memory. Перезапуск ноды на них не влияет.
- **TicketSequence reuse** — после успешного применения tx, ticket "сжигается" в той же ledger'е. Попытка использовать тот же TicketSequence снова — `tefNO_TICKET`.
