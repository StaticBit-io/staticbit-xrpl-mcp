>  🌐 **Язык**: [English](../../examples/multi-sign-collection.md) | **Русский**

# Пример: Multi-sign signature collection workflow

Cowork-агент координирует процесс сбора подписей для multi-sign транзакции от N signer'ов из заранее настроенного SignerList. Полезно для treasury management, DAO governance, recovery procedures.

Референс: [TestIMultisign.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIMultisign.cs).

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_signer_list_set_prepare`, `xrpl_account_set_prepare` (для DisableMaster), `xrpl_payment_prepare` (или любой другой write), `xrpl_signer_list_status`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed` |
| **xrpl-signer** | `xrpl_sign_multi` (per signer), `xrpl_sign_combine` (склейка) |

## Концепция

- **SignerList** — ledger entry на account, описывает quorum + список (signer_account, weight) entries (до 32). Каждый signer имеет свой weight; tx применяется когда sum(weights подписавших) ≥ quorum.
- **DisableMaster** — owner может опционально disable свой master key через `AccountSet asfDisableMaster` после установки SignerList. Тогда **только** через quorum можно подписывать. Без disable master может бок-о-бок c multi-sign.
- **Per-signer signature** — каждый signer создаёт свою signature независимо. Затем все signatures merge'атся в один tx blob.
- **Order не важен** — `Signer.Multisign` дедуплицирует и canonical-sort'ит.

## Архитектура

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
                  → проверяет collectedWeight ≥ quorum
  7. orchestrator → xrpl_sign_combine([blob_partial_1, blob_partial_2]) → blob_combined
  8. orchestrator → xrpl_tx_submit_signed(blob_combined) → tesSUCCESS
```

## Pre-requisites

- 1 funded account для OWNER ("treasury", "DAO vault", "cold storage").
- ≥ 2 funded accounts для signers (не обязательно — signers могут не быть accounts; в этом случае нужны только их XRPL public keys в SignerList).
- Все signers' seeds импортированы в keystore через `xrpl_wallet_import_seed`.
- Master key либо активен (mixed-mode), либо disabled (multi-sign only).

## Промт агента

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
→ sign by rTreasury (master key — последний раз) → submit → tesSUCCESS
```

После 1b: `xrpl_account_info(rTreasury) → AccountFlags.DisableMasterKey=true`. Master key больше не работает. Только multi-sign.

### 2. Prepare unsigned tx

Допустим OWNER хочет послать 10 XRP на rRecipient:

```text
agent ← {"step":"prepare","owner":"rTreasury...","txJson":{
  "TransactionType":"Payment","Account":"rTreasury...",
  "Destination":"rRecipient...","Amount":"10000000"
}}

→ xrpl_tx_prepare_generic(network, account="rTreasury...", txJson)
   → blob_unsigned (autofilled: Sequence, Fee, LastLedgerSequence)
```

**Important**: Fee для multi-sign = base_fee × (1 + N_signers). При 2 signers это 3× base. SDK autofill это считает корректно если передать `signersCount` — но через MCP пока используется default (1). Можно вручную подкрутить через дополнительный `xrpl_tx_prepare_generic` с явным Fee.

### 3. Check collection progress

Перед сбором подписей или в процессе:

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

Полезно для UI / progress reporting в Telegram/Slack.

### 4. Collect partial signatures

Каждый signer (раздельно, на своей машине / в своём workflow) делает:

```text
agent ← {"step":"collect_sigs","signerWallets":["rSigner1..."],
         "blobUnsigned":"<...>"}

→ xrpl_sign_multi(walletName="rSigner1...", txBlobUnsigned="<...>")
   → blob_partial_1 (содержит SigningPubKey, TxnSignature, Signers[1])
```

Затем второй signer:

```text
→ xrpl_sign_multi(walletName="rSigner2...", txBlobUnsigned="<...>")
   → blob_partial_2
```

### 5. Combine signatures

Когда у orchestrator'а есть ≥ quorum partial blobs:

```text
agent → xrpl_sign_combine([blob_partial_1, blob_partial_2])
       → blob_combined (один blob с обоими signers' entries)
```

`sign_combine` дедуплицирует и canonical-sort'ит. Можно передать 5 partial blobs если нужно — лишние просто игнорируются.

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

`quorumReached=true` → можно submit'ить.

### 7. Submit

```text
agent → xrpl_tx_preflight(blob_combined.txJson) → feasible=true
agent → xrpl_tx_submit_signed(blob_combined, waitForValidation=true)
       → { engineResult: "tesSUCCESS", validated:true, ledgerIndex:... }
```

## Verification checklist

- [ ] Step 1a: `xrpl_signer_list_status(owner)` показывает signerList с правильным quorum/weights.
- [ ] Step 1b: `xrpl_account_info(owner).account_flags.disableMasterKey = true`.
- [ ] Step 4: каждый partial blob можно декодировать через `xrpl_tx_decode_blob` и видеть один `Signers` entry.
- [ ] Step 6: `quorumReached=true`, `unknownSignersIgnored=[]`.
- [ ] Step 7: tx применилась, balance OWNER уменьшился.

## Workflow variations

### Async signature collection через Telegram bot

Orchestrator (наш agent) рассылает blob через Telegram каждому signer'у:

```
agent → mcp__telegram_*__send_message(to=rSigner1_chatId,
        text="Pending multi-sign: ... reply with /sign <blob>")
```

Signer Bot принимает /sign command, calls `xrpl_sign_multi` локально, returns blob обратно. Orchestrator аккумулирует ответы.

### Time-bounded collection

LastLedgerSequence ограничивает окно — если signatures не собрались за ~80s (20 ledgers × 4s), tx fails с `tefPAST_SEQ` или подобным. Для долгих collection processes:
- bump LastLedgerSequence через autofill с custom offset; OR
- использовать `TicketSequence` вместо `Sequence` — tickets живут вечно. Signatures можно собирать днями.

### Mixed weights / quorum scenarios

Например, "CEO weight=2, two Board members weight=1 each, quorum=3":
- CEO один не может (weight 2 < 3).
- Любой board member один не может (1 < 3).
- CEO + 1 board member = 3 (passes).
- Два board members = 2 (fails).
Configure через `signerEntriesJson` с `weight` на каждом entry.

## Use-cases

- **Corporate treasury** — все outflow tx требуют 2-of-3 board approval.
- **DAO vault** — 4-of-7 council multi-sig.
- **Hot/cold storage** — cold storage с 3-of-5 hardware-wallet signers.
- **Recovery procedure** — main account loses access → multi-sign team может submit `SetRegularKey` для recovery key, или AccountDelete to recipient.
- **Compliance workflow** — payment > $X требует доп-подпись от compliance officer.

## Подводные камни

- **Disable master irreversibly without quorum**: после `asfDisableMaster=4`, master key больше не работает. Если SignerList не setup или quorum unreachable — account becomes locked. Используй mixed-mode (без disable) пока не уверен в setup.
- **Fee escalation**: на load-spike rippled может требовать более высокий fee. Multi-sign fee уже × N — pre-emptively apply FeeBumpMultiplier (см. `XrplMcpOptions.FeeBumpMultiplier`).
- **Sequence vs Ticket**: при долгом сборе подписей Sequence может проскочить (другая tx от same account). Используй TicketSequence для immune signature collection.
- **Quorum can be unreachable**: если из 5 signers quorum=3, и 3 signers потеряли ключи — account залочен. Setup → `xrpl_signer_list_set_prepare` с новым list (через quorum surviving signers, если возможно).
