>  🌐 **Language**: **English** | [Русский](../ru/examples/atomic-batch-payment.md)

# Example: Atomic Batch payment (XLS-56)

A Cowork agent that bundles several Payments from different accounts into a single `Batch` so that either all succeed or none do (classic atomic 3-way swap, payment with rebate, payroll distribution).

Reference: [TestIBatch.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIBatch.cs).

> ⚠️ **About Batch status on mainnet/testnet**: the amendment was temporarily removed in rippled v3.1.1 due to a bug and will return as `BatchV1_1`. The recipe works against a standalone node with the amendment enabled or after the BatchV1_1 release. On standard testnet the recipe currently returns `temDISABLED` — **expected** behaviour.

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_payment_prepare` ×N (for inner-tx), `xrpl_batch_prepare`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_account_info` (to fetch Sequence) |
| **xrpl-signer** | `xrpl_sign` for outer Batch, `xrpl_sign_multi` for multi-account BatchSigners |

## Batch modes

Flag constants set in `xrpl_batch_prepare`:

| mode | flag | behaviour |
|---|---|---|
| `AllOrNothing` | 0x00010000 | All inner succeed → outer success; otherwise all roll back. Classic atomic swap. |
| `OnlyOne` | 0x00020000 | First to succeed wins, the rest aren't attempted. Polling-like best-of-N. |
| `UntilFailure` | 0x00040000 | Applies in order, stops on the first error (previous ones stay applied). Chain of dependent steps. |
| `Independent` | 0x00080000 | Each inner is evaluated independently, outcomes don't depend on one another. Parallel unrelated tx. |

## Architecture of a 3-way swap

```
Alice owes USD to Bob, Bob owes EUR to Carol, Carol owes XRP to Alice.
All three debts close in one Batch (mode=AllOrNothing).

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

**Critical**: every inner-tx must have:
- its own `Sequence` (or `TicketSequence`) — current for the inner-Account;
- `Fee = "0"` (forced by the prepare tool);
- `SigningPubKey = ""` (forced);
- NO `TxnSignature`, NO `Signers` (forced — stripped);
- the `tfInnerBatchTxn = 0x40000000` flag in Flags (forced via OR).

## Pre-requisites

- 3 funded accounts (Alice, Bob, Carol) imported into the keystore.
- Trust lines: Bob → Alice (USD), Carol → Bob (EUR). XRP does not need a trust line.
- For a multi-account batch: every non-outer Account must sign its part separately.

## Agent prompt

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

## Step-by-step (simplified single-account case)

The simplest Batch — several Payments from a **single** Account. No BatchSigners required.

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

Pass as a string into `innerTransactionsJson`.

### 3. Prepare the outer Batch

```text
agent → xrpl_batch_prepare(
  network="testnet",
  account="rAlice...",
  mode="AllOrNothing",
  innerTransactionsJson="[ ... ]"
)
```

The tool:
- ORs `tfInnerBatchTxn` into each inner's Flags;
- forces Fee="0", SigningPubKey="";
- checks ≤8 inner;
- validates Sequence on every inner;
- Autofills the outer (fills outer.Sequence — Alice's Sequence AFTER the inner ones, i.e. 44).

### 4. Preflight + sign + submit

```text
agent → xrpl_tx_preflight(txJson) → feasible=true
agent → xrpl_sign(walletName="rAlice...", txBlobUnsigned)
agent → xrpl_tx_submit_signed(txBlobSigned, waitForValidation=true)
       → { engineResult: "tesSUCCESS", txHash, ledgerIndex, validated:true }
```

## Multi-account case (3-way swap)

When inner Accounts differ, `batchSignersJson` is required with signatures from each non-outer Account.

### Collecting BatchSigner signatures

XLS-56 specifies: every non-outer Account signs **specific signing data** consisting of the outer tx blob (without BatchSigners) + a canonical prefix. The SDK provides this via `Wallet.Sign(tx, multisign:true)` today, but Batch needs a dedicated `wallet.SignAsBatchSigner(outerTx)`.

This is not yet a separate tool in MCP — workaround:

1. First build the outer Batch without `batchSignersJson` via `xrpl_batch_prepare`.
2. Hand the resulting `txBlobUnsigned` to each non-outer Account for signing via `xrpl_sign_multi(walletName=accountAddr, txBlobUnsigned)`.
3. Extract `SigningPubKey` and `TxnSignature` from the response into JSON.
4. Assemble the `batchSignersJson` array and call `xrpl_batch_prepare` **again** with it.
5. The outer Account signs the final blob via `xrpl_sign`.

> ⚠️ **Gap**: the current `xrpl_sign_multi` does not produce a BatchSigner-specific signature (it targets SignerList multi-sign, not XLS-56). Potential extension — an `xrpl_sign_batch_signer` tool. On a standalone node only single-account Batches are workable.

## Verification

- `engine_result == "tesSUCCESS"` — all inner applied (in AllOrNothing).
- The metadata has one `AffectedNodes` array summarising changes from all inner.
- For `OnlyOne` — meta shows which inner applied.
- For `UntilFailure` — meta shows how far it went.
- `xrpl_account_tx(account=outerAccount)` shows a single record with `tx.TransactionType=Batch`.

## Real-world use-cases

- **Atomic swap** through the ledger (instead of PathFinding/cross-currency Payment).
- **Payroll batching** — outer Account pays N employees different amounts/tokens. If any destination has DepositAuth/DisallowIncoming — the whole batch rolls back.
- **DEX rebalance** — cancel an old Offer + create a new one as one atomic set.
- **MPT distribution** — issuer sends a freshly-minted MPT to N pre-authorised holders.
