> 🇷🇺 [Прочесть на русском](delegate-permissions.ru.md)

# Example: Account permission delegation (XLS-75)

An owner delegates the right to submit transactions of specific types to another account, without handing over the master key or setting up a signer list. Useful for bots, hot wallets, role-based corporate accounts.

Reference: [TestIDelegateSet.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIDelegateSet.cs).

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_delegate_set_prepare`, `xrpl_account_objects` (type Delegate), `xrpl_payment_prepare` (or other tx), `xrpl_tx_preflight`, `xrpl_tx_submit_signed` |
| **xrpl-signer** | `xrpl_sign` |

## Concept (XLS-75)

- The owner submits `DelegateSet` with a permissions array = the allowed tx types. Up to 10 types per delegation.
- Creates a ledger entry `Delegate { Account=owner, Authorize=delegatee, Permissions=[...] }`.
- The delegatee can submit tx of the listed types **on behalf of the owner** via a special `Delegate` field on the tx (TX-type code, not account).
- The owner signs the delegation; the delegatee signs every concrete tx.
- **Non-delegable** (security-critical): `AccountSet`, `SetRegularKey`, `SignerListSet`, `DelegateSet`. These types are blocked by rippled — even if you try to delegate them, the tx fails.
- An empty `Permissions` array = clear delegation.

## Architecture

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
- Both seeds in the keystore.
- XLS-75 amendment is active.

## Agent prompt

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

The tool rejects before submit if you try to include a forbidden type:

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

### 3. Delegatee submits a delegated transaction

DELEGATEE (e.g. a trading bot) wants to place an order on Alice's behalf:

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
   IMPORTANT: signing by DELEGATEE, not by OWNER
→ xrpl_tx_submit_signed → tesSUCCESS
```

rippled validates:
- Delegate entry for `(rAlice, rTradeBot)` exists?
- `OfferCreate` (code 7) ∈ `Permissions`?
- TxnSignature valid for the signing public key matching the `Delegate` account?

If all checks pass — the tx applies **as if rAlice submitted it**. Sequence/Fee/reserves are accounted to Alice.

### 4. Negative case (delegatee tries an unauthorized type)

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

After this the Delegate entry is removed. Any subsequent delegated tx fails with `tecNO_DELEGATE_PERMISSION`.

### 6. Modify permissions

No explicit modify needed — `DelegateSet` with the same `(account, delegateAccount)` but a new `permissionsCsv` **fully replaces** the Permissions array:

```text
agent ← {"step":"grant","owner":"...","delegatee":"...",
         "permissionsCsv":"Payment"}    // reduced from 4 types to 1
```

The old Delegate entry is overwritten.

## Verification checklist

- [ ] Step 1: `xrpl_account_objects(owner, type="Delegate")` shows the new entry.
- [ ] Step 1: Permissions array with correct PermissionValue codes.
- [ ] Step 3: tx applied with `tx.Account=owner` but signed by delegatee — `xrpl_tx_lookup` shows the `Delegate` field set.
- [ ] Step 5: after revoke, `xrpl_account_objects` no longer returns the entry.
- [ ] Negative: delegated tx with type outside the allowlist → `tecNO_DELEGATE_PERMISSION` or `tecNO_PERMISSION`.

## Gotchas

- **Non-delegable types are blocked**: AccountSet, SetRegularKey, SignerListSet, DelegateSet — cannot be delegated. The tool refuses before submit.
- **Per-delegatee, not per-account-wide**: each (owner, delegatee) pair is its own Delegate entry. The owner may have N independent delegatees with different permission sets.
- **Sequence & Fee belong to OWNER**: even when the delegatee submits, Sequence increments at the owner, fee comes from the owner, reserves are computed at the owner. The delegatee just signs.
- **Reserve cost**: a Delegate entry is an owner-object at the owner (+2 XRP reserve). N delegations = N reserves.
- **Reuse of master/regular key by owner remains valid**: even after the grant, the owner can submit the same tx type themselves without a `Delegate` field. Delegation is an additive permission, not a replacement.

## Use-cases

- **Trading bot**: owner grants `Payment,OfferCreate,OfferCancel` to a bot account. The bot manages orders without owning the seed.
- **Custodial-style hot wallet**: a custodian's account has `Payment` permission on a cold-storage account — can pay clients without cold-storage key access.
- **Role-based corporate accounts**: separate accounts for "Accounting" (allowed `TrustSet`), "Trading" (allowed `OfferCreate`), "Payments" (allowed `Payment`). Each delegatee has the minimum-required permission.
- **Time-bounded delegation**: combine with an external scheduler — the agent revokes the delegation an hour after the grant.

## Extensions

- **Auto-revoke after timeout**: combine with `/loop` or `/schedule` — the agent monitors `xrpl_account_objects` and auto-revokes delegations older than N hours.
- **Multi-delegatee orchestration**: a single owner delegates different permissions to several bots; the agent tracks all and provides a single dashboard via `xrpl_account_objects`.
- **Conditional revocation**: the agent monitors the delegatee's behaviour via `xrpl_account_tx_since` and revokes on suspicious patterns.
- **Combined with multi-sign**: SignerList controls who can submit DelegateSet (a treasury committee); delegations then flow to operational bots.
