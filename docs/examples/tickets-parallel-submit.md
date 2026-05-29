>  🌐 **Language**: **English** | [Русский](../ru/examples/tickets-parallel-submit.md)

# Example: Tickets for parallel tx submission

A resilient batch agent: reserves a pool of Tickets, then submits N independent transactions in parallel (without blocking on the ordered Sequence). Useful for market-making, payout distribution, mass airdrop.

Reference: `ticketCreate.cs` (lowercase file) and the entire pattern of using `TicketSequence` instead of `Sequence` across SDK integration tests.

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_account_info`, `xrpl_ticket_create_prepare`, `xrpl_tx_prepare_generic` (for tx with TicketSequence), `xrpl_payment_prepare` (if a payment), `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_account_objects` (type `Ticket`) |
| **xrpl-signer** | `xrpl_sign` |

## Why Tickets

The normal XRPL flow requires **sequential** `Sequence` on every tx from one account. If you want to submit 10 parallel Payments and any one of them stalls — all the others wait.

Tickets are **pre-reserved slots for future tx**. The account submits `TicketCreate(TicketCount=N)` — Sequence advances by N, and those N numbers become freely usable **in any order**.

Each later tx is tagged `TicketSequence=<ticket_num>` (no `Sequence` field). All 10 may be sent in parallel — application order doesn't matter to rippled.

Limit: an account holds ≤ 250 Tickets simultaneously.

## Architecture

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

After every successful submit, the ticket is "burned" — its `Ticket` ledger entry is destroyed and OwnerCount decreases.

## Pre-requisites

- 1 funded account.
- Reserve must cover **base reserve + (current OwnerCount + N) × increment** while the tickets exist. So for 10 tickets you need ≥ 10 × 2 XRP = 20 XRP of additional owner reserve.

## Agent prompt

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

Current Sequence = 42. After TicketCreate(10) it becomes 53.

### 2. Reserve 10 tickets

```text
agent → xrpl_ticket_create_prepare(network, account="rAlice...", ticketCount=10)
       → tx_blob_unsigned (TransactionType=TicketCreate, Sequence=42, TicketCount=10)
agent → xrpl_tx_preflight → feasible=true (1..250 range)
agent → xrpl_sign(walletName=rAlice, blob)
agent → xrpl_tx_submit_signed(blob_signed, waitForValidation=true)
       → tesSUCCESS, validated, ledger=12345
```

After validation: `rAlice.Sequence = 53`, OwnerCount = 10. Tickets with `TicketSequence = 43, 44, ..., 52` exist as ledger entries.

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

All 10 submits run in parallel — application order in the ledger may be anything.

### 5. Poll for finality

```text
for txHash in submitted:
  agent → xrpl_tx_lookup(network, txHash)
         → { validated:true, meta:{TransactionResult:"tesSUCCESS"}, ledger_index:... }
```

If any tx isn't validated after ~LLS=20 ledgers — resubmit with the same blob (rippled is idempotent on TxBlob).

### 6. Verify tickets consumed

```text
agent → xrpl_account_objects(account="rAlice...", type="Ticket")
       → { "AccountObjects": [] }
```

All 10 tickets "burned", OwnerCount dropped by 10.

## Variations

### Use only some of the reserved tickets

If you reserved 10 but only used 7 — the remaining 3 tickets stay as ledger entries and hold reserve. To release them: an `xrpl_tx_prepare_generic` with a minimal `AccountSet{ Account, TicketSequence=<unused> }`. A no-op tx that "burns" the ticket.

### Parallel limit

In practice, don't submit all 10 simultaneously — rippled may temporarily reject with `terQUEUED`. Recommended pattern — batches of 3–5 with a small delay (250ms).

### Mix tx types

Tickets aren't bound to a tx type. One ticket may be used for `OfferCreate`, another for `Payment`, a third for `TrustSet` — all from the same account.

## Use-cases

- **Market-maker order placement**: place N orders at once via `OfferCreate` × N with TicketSequences.
- **Mass airdrop**: 250 payouts → create 250 tickets → parallel submit. If one payout fails (e.g. recipient has DisallowIncomingXRP), the rest continue.
- **Account recovery prep**: an account pre-creates several tickets, then "slices" its state (`AccountDelete` + `SetRegularKey` + `SignerListSet`) through them — operations normally blocked by sequence ordering.
- **Resilient batch jobs**: critical tx (smart-contract-like operation chain) — each uses its own ticket; a single failure does not block the others.

## Verification checklist

- [ ] After Step 2: `Sequence` increased by `ticketCount`, `OwnerCount += ticketCount`.
- [ ] Step 3: `xrpl_account_objects` shows `ticketCount` ticket entries with correct TicketSequence numbers.
- [ ] Step 4: every submit has `TicketSequence` set, `Sequence` absent/0.
- [ ] After all submits validated: `OwnerCount` dropped by the number of successful txs.
- [ ] All tx hashes appear in `xrpl_account_tx(account)`.

## Gotchas

- **Ticket limit 250** — if you try `ticketCount=251` or the account already holds 250 tickets and asks for more — `temINVALID_COUNT`. Preflight catches this.
- **Reserve** — `(current OwnerCount + ticketCount) × inc + base` must be < balance − 1 XRP buffer. Otherwise TicketCreate fails with `tecINSUFFICIENT_RESERVE`.
- **Ticket survival across rippled restarts** — tickets live in the ledger, not in memory. A node restart doesn't affect them.
- **TicketSequence reuse** — after a tx applies successfully, the ticket "burns" in the same ledger. Reusing the same TicketSequence — `tefNO_TICKET`.
