> 🇷🇺 [Прочесть на русском](multi-sign-collection.ru.md)

# Example: Multi-sign signature collection workflow

A Cowork agent that coordinates the process of collecting signatures for a multi-sign transaction from N signers from a pre-configured SignerList. Useful for treasury management, DAO governance, recovery procedures.

Reference: [TestIMultisign.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIMultisign.cs).

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_signer_list_set_prepare`, `xrpl_account_set_prepare` (for DisableMaster), `xrpl_payment_prepare` (or any other write), `xrpl_signer_list_status`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed` |
| **xrpl-signer** | `xrpl_sign_multi` (per signer), `xrpl_sign_combine` (merge) |

## Concept

- **SignerList** — ledger entry on an account that describes the quorum + a list of (signer_account, weight) entries (up to 32). Each signer has its own weight; the tx applies when sum(weights of signers) ≥ quorum.
- **DisableMaster** — the owner optionally disables their master key via `AccountSet asfDisableMaster` after the SignerList is in place. Then **only** the quorum can sign. Without disable, master key works alongside multi-sign.
- **Per-signer signature** — each signer produces their signature independently. All signatures are merged into one tx blob.
- **Order doesn't matter** — `Signer.Multisign` deduplicates and canonical-sorts.

## Architecture

```
Setup (once):
  OWNER ──SignerListSet(quorum=2, SignerEntries=[
    {Account=Signer1, SignerWeight=1},
    {Account=Signer2, SignerWeight=1},
    {Account=Signer3, SignerWeight=1}
  ])──► rippled
                                                              ↓ creates SignerList entry
  OWNER ──AccountSet(SetFlag=asfDisableMaster=4)──► (optional)
                                                              ↓ now ONLY multi-sign works

Signing flow (per tx):
  1. orchestrator → xrpl_payment_prepare(account=OWNER, ...) → blob_unsigned
  2. orchestrator → broadcast blob_unsigned to Signer1, Signer2 (out-of-band)
  3. Signer1 → xrpl_sign_multi(walletName=signer1.addr, blob_unsigned) → blob_partial_1
  4. Signer2 → xrpl_sign_multi(walletName=signer2.addr, blob_unsigned) → blob_partial_2
  5. orchestrator collects [blob_partial_1, blob_partial_2]
  6. orchestrator → xrpl_signer_list_status(OWNER, alreadySignedAccountsCsv=...)
                  → checks collectedWeight ≥ quorum
  7. orchestrator → xrpl_sign_combine([blob_partial_1, blob_partial_2]) → blob_combined
  8. orchestrator → xrpl_tx_submit_signed(blob_combined) → tesSUCCESS
```

## Pre-requisites

- 1 funded account for OWNER ("treasury", "DAO vault", "cold storage").
- ≥ 2 funded accounts for signers (optional — signers may not be accounts; in that case only their XRPL public keys are needed in the SignerList).
- All signers' seeds imported into the keystore via `xrpl_wallet_import_seed`.
- Master key either active (mixed mode) or disabled (multi-sign only).

## Agent prompt

```markdown
---
name: multi-sign-orchestrator
description: Coordinates a multi-sign transaction: builds the unsigned blob,
  monitors signer-list status, collects partial signatures, combines, submits.
tools: xrpl_signer_list_set_prepare, xrpl_account_set_prepare, xrpl_payment_prepare,
  xrpl_tx_prepare_generic, xrpl_signer_list_status, xrpl_sign_multi,
  xrpl_sign_combine, xrpl_tx_preflight, xrpl_tx_submit_signed
---

Inputs:
- {"step":"setup","network":"testnet","owner":"r...",
   "signerQuorum":2,
   "signerEntries":[{"account":"r...","weight":1}, ...],
   "disableMaster":true}
- {"step":"prepare","network":"...","owner":"r...","txJson":"<full Payment / other JSON>"}
- {"step":"collect_sigs","network":"...","owner":"r...",
   "blobUnsigned":"<hex>","signerWallets":["r...","r..."]}
- {"step":"submit","network":"...","blobCombined":"<hex>"}
- {"step":"status","network":"...","owner":"r...","alreadySignedAccountsCsv":"r1,r2"}

For "setup":
1. xrpl_signer_list_set_prepare(network, account=owner, signerQuorum, signerEntriesJson)
   → sign by owner (master key) → submit.
2. If disableMaster=true:
   xrpl_account_set_prepare(network, account=owner, setFlag=4 /*asfDisableMaster*/)
   → sign by owner (master key, last time it works) → submit.

For "prepare":
1. xrpl_tx_prepare_generic(network, account=owner, txJson)
   → returns autofilled txBlobUnsigned.
2. Return blob for distribution to signers.

For "collect_sigs":
1. For each signer in signerWallets:
   blob_i = xrpl_sign_multi(walletName=signer, txBlobUnsigned=blob)
2. After collecting all (or quorum-enough): xrpl_sign_combine([blob_1, ..., blob_n])
   → blob_combined.
3. Return blob_combined.

For "status":
xrpl_signer_list_status(account=owner, alreadySignedAccountsCsv)
→ returns { quorum, totalAvailableWeight, collectedWeight,
            deltaToQuorum, quorumReached, signers[], unknownSignersIgnored[] }

For "submit":
xrpl_tx_preflight(blobCombined.txJson) → bail if feasible=false
xrpl_tx_submit_signed(blobCombined, waitForValidation=true)
```

## Step-by-step

### 1. Setup SignerList

```text
agent ← {"step":"setup","owner":"rTreasury...","signerQuorum":2,
         "signerEntries":[
           {"account":"rSigner1...","weight":1},
           {"account":"rSigner2...","weight":1},
           {"account":"rSigner3...","weight":1}
         ],
         "disableMaster":true}

Step 1a: SignerListSet
→ xrpl_signer_list_set_prepare(
    network, account="rTreasury...", signerQuorum=2,
    signerEntriesJson=[{"account":"rSigner1...","weight":1},...]
  )
→ sign by rTreasury (master key) → submit → tesSUCCESS

Step 1b: DisableMaster
→ xrpl_account_set_prepare(network, account=rTreasury, setFlag=4)
→ sign by rTreasury (master key — last time) → submit → tesSUCCESS
```

After 1b: `xrpl_account_info(rTreasury) → AccountFlags.DisableMasterKey=true`. Master key no longer works. Multi-sign only.

### 2. Prepare unsigned tx

Suppose OWNER wants to send 10 XRP to rRecipient:

```text
agent ← {"step":"prepare","owner":"rTreasury...","txJson":{
  "TransactionType":"Payment","Account":"rTreasury...",
  "Destination":"rRecipient...","Amount":"10000000"
}}

→ xrpl_tx_prepare_generic(network, account="rTreasury...", txJson)
   → blob_unsigned (autofilled: Sequence, Fee, LastLedgerSequence)
```

**Important**: multi-sign Fee = base_fee × (1 + N_signers). For 2 signers this is 3× base. SDK autofill computes this correctly when given `signersCount` — currently MCP uses default (1). You can override manually with an explicit Fee in another `xrpl_tx_prepare_generic`.

### 3. Check collection progress

Before or during signature collection:

```text
agent ← {"step":"status","owner":"rTreasury...","alreadySignedAccountsCsv":""}

→ xrpl_signer_list_status(account="rTreasury...", alreadySignedAccountsCsv="")
   → {
       "quorum": 2,
       "totalAvailableWeight": 3,
       "collectedWeight": 0,
       "deltaToQuorum": 2,
       "quorumReached": false,
       "signers": [
         {"account":"rSigner1...","weight":1,"hasSigned":false},
         {"account":"rSigner2...","weight":1,"hasSigned":false},
         {"account":"rSigner3...","weight":1,"hasSigned":false}
       ]
     }
```

Useful for a progress UI in Telegram/Slack.

### 4. Collect partial signatures

Each signer (separately, on their own machine / in their own workflow) does:

```text
agent ← {"step":"collect_sigs","signerWallets":["rSigner1..."],
         "blobUnsigned":"<...>"}

→ xrpl_sign_multi(walletName="rSigner1...", txBlobUnsigned="<...>")
   → blob_partial_1 (contains SigningPubKey, TxnSignature, Signers[1])
```

Then the second signer:

```text
→ xrpl_sign_multi(walletName="rSigner2...", txBlobUnsigned="<...>")
   → blob_partial_2
```

### 5. Combine signatures

When the orchestrator has ≥ quorum partial blobs:

```text
agent → xrpl_sign_combine([blob_partial_1, blob_partial_2])
       → blob_combined (one blob with both signer entries)
```

`sign_combine` deduplicates and canonical-sorts. You may pass 5 partial blobs if needed — extras are simply ignored.

### 6. Final status check

```text
agent → xrpl_signer_list_status(account=rTreasury,
        alreadySignedAccountsCsv="rSigner1...,rSigner2...")
       → {
           "quorum": 2, "collectedWeight": 2, "deltaToQuorum": 0,
           "quorumReached": true,
           "unknownSignersIgnored": []
         }
```

`quorumReached=true` → ready to submit.

### 7. Submit

```text
agent → xrpl_tx_preflight(blob_combined.txJson) → feasible=true
agent → xrpl_tx_submit_signed(blob_combined, waitForValidation=true)
       → { engineResult: "tesSUCCESS", validated:true, ledgerIndex:... }
```

## Verification checklist

- [ ] Step 1a: `xrpl_signer_list_status(owner)` shows the signerList with the right quorum/weights.
- [ ] Step 1b: `xrpl_account_info(owner).account_flags.disableMasterKey = true`.
- [ ] Step 4: each partial blob decodes via `xrpl_tx_decode_blob` and shows one `Signers` entry.
- [ ] Step 6: `quorumReached=true`, `unknownSignersIgnored=[]`.
- [ ] Step 7: tx applied, OWNER's balance dropped.

## Workflow variations

### Async signature collection via Telegram bot

The orchestrator (our agent) broadcasts the blob via Telegram to every signer:

```
agent → mcp__telegram_*__send_message(to=rSigner1_chatId,
        text="Pending multi-sign: ... reply with /sign <blob>")
```

The signer bot accepts the /sign command, calls `xrpl_sign_multi` locally, returns the blob. The orchestrator aggregates responses.

### Time-bounded collection

LastLedgerSequence limits the window — if signatures don't arrive within ~80s (20 ledgers × 4s), tx fails with `tefPAST_SEQ` or similar. For long collection processes:
- bump LastLedgerSequence via autofill with a custom offset; OR
- use `TicketSequence` instead of `Sequence` — tickets live forever. Signatures can be collected for days.

### Mixed weights / quorum scenarios

E.g. "CEO weight=2, two Board members weight=1 each, quorum=3":
- CEO alone cannot sign (weight 2 < 3).
- Any single board member cannot (1 < 3).
- CEO + 1 board member = 3 (passes).
- Two board members = 2 (fails).
Configure via `signerEntriesJson` with a `weight` per entry.

## Use-cases

- **Corporate treasury** — every outflow tx requires 2-of-3 board approval.
- **DAO vault** — 4-of-7 council multi-sig.
- **Hot/cold storage** — cold storage with 3-of-5 hardware-wallet signers.
- **Recovery procedure** — main account loses access → multi-sign team submits `SetRegularKey` for the recovery key, or AccountDelete to a recipient.
- **Compliance workflow** — payment > $X requires an additional signature from a compliance officer.

## Gotchas

- **Disable master irreversibly without quorum**: after `asfDisableMaster=4`, the master key no longer works. If the SignerList isn't set up or quorum is unreachable — the account is locked. Use mixed mode (no disable) until you're confident in the setup.
- **Fee escalation**: on load spikes rippled may require a higher fee. Multi-sign fee is already × N — pre-emptively apply FeeBumpMultiplier (see `XrplMcpOptions.FeeBumpMultiplier`).
- **Sequence vs Ticket**: during a long signature collection Sequence may advance (another tx from the same account). Use TicketSequence for an immune signature collection.
- **Quorum can become unreachable**: if of 5 signers quorum=3, and 3 signers lose their keys — the account is locked. Setup → `xrpl_signer_list_set_prepare` with a new list (through the surviving quorum, if possible).
