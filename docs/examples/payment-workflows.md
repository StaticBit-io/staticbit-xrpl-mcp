>  🌐 **Language**: **English** | [Русский](../ru/examples/payment-workflows.md)

# Example: Payment workflows — XRP, cross-currency and credential-gated

The foundational XRPL primitive: sending value from A to B. Three scenarios in one document, covering the bulk of real-world Payment usage: a plain XRP drops transfer, a cross-currency delivery with pathfinding, and a credential-gated Payment into a recipient sitting behind a PermissionedDomain. All scenarios are testnet-only; all amounts/addresses are placeholders.

Reference: [TestIPayment.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIPayment.cs), [TestIPathFind.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIPathFind.cs), [TestICredential.TestCredential_EndToEnd_DepositPreauthAndPayment](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestICredential.cs#L312).

## Use case

- **Scenario 1 — simple XRP payment**: alice sends bob N XRP on testnet. The whole point is to nail down the drops ↔ XRP conversion so the agent doesn't accidentally over- or under-pay by 6 orders of magnitude.
- **Scenario 2 — cross-currency payment with pathfinding**: alice holds XRP, bob expects USD issued by `rIssuer...`. The XRPL DEX/AMM rebalances on the fly through the `paths_computed` field of `ripple_path_find`, gated by `SendMax` and `DeliverMin` for safety.
- **Scenario 3 — credential-gated payment**: bob enables DepositAuth and a PermissionedDomain backed by a KYC credential. A first attempt without the credential is rejected by the ledger (`tecNO_PERMISSION`); after `CredentialAccept`, the retry succeeds.

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_xrp_balance`, `xrpl_account_lines`, `xrpl_fee`, `xrpl_payment_prepare`, `xrpl_ripple_path_find`, `xrpl_path_find_create`, `xrpl_path_find_status`, `xrpl_path_find_close`, `xrpl_trustset_prepare`, `xrpl_account_set_prepare`, `xrpl_deposit_preauth_prepare`, `xrpl_permissioned_domain_set_prepare`, `xrpl_credential_accept_prepare`, `xrpl_hash_credential`, `xrpl_tx_prepare_generic`, `xrpl_tx_preflight`, `xrpl_tx_simulate`, `xrpl_tx_submit_signed`, `xrpl_tx_lookup` |
| **xrpl-signer** | `xrpl_sign` |

## Architecture

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

- Two funded testnet accounts ALICE and BOB via the [testnet faucet](https://xrpl.org/xrp-testnet-faucet.html) (3 accounts for scenario 3 — add ISSUER).
- Wallets imported into the `xrpl-signer` keystore under names `alice`, `bob`, `issuer`.
- For scenario 2 — BOB must hold a trustline to ISSUER for the cross-currency target.
- For scenario 3 — XLS-70 (Credentials) and XLS-80 (PermissionedDomains) must be active on the chosen network (both on standard testnet; verify on mainnet with `xrpl_server_state` if used there).
- Default `network` is `testnet`. Never run scenarios 2/3 against mainnet without disabling the `tfPartialPayment` shortcut and re-running `xrpl_tx_simulate` against real liquidity.

## Agent prompt

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

### Scenario 1 — simple XRP payment (drops accounting)

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

Post-conditions:
- `xrpl_xrp_balance(rBob)` increases by exactly 1.5 XRP (the fee is paid by alice, not bob).
- `xrpl_xrp_balance(rAlice)` drops by `1.5 + fee/1_000_000` XRP.

### Scenario 2 — cross-currency payment with pathfinding

Setup (one-time): BOB holds a trustline to ISSUER for `USD`. ISSUER already issued USD via direct Payment to some market-maker accounts (otherwise no liquidity → empty `alternatives[]`).

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

The `delivered_amount` in `meta` is the **authoritative** settled amount. Because of `tfPartialPayment + DeliverMin`, the ledger guarantees:
- `value(delivered_amount) ≥ value(DeliverMin)` (here 9.97 ≥ 9.9 — accept).
- `drops_spent ≤ SendMax` (here ≤ 12 345 678 drops — accept).

If you read the original `Amount` (`10 USD`) instead of `meta.delivered_amount`, you may credit your accounting system with 10 USD while only 9.97 USD actually arrived — a classic exchange-side bug.

For long-running interactive UIs (an order book) prefer the streaming version:

```text
→ handle = xrpl_path_find_create(network, sourceAccount=from, destinationAccount=to,
   destinationAmount=..., sendMax=...)
→ poll xrpl_path_find_status(network, handle) every few seconds until results stabilise
→ xrpl_path_find_close(network, handle) when done
```

`xrpl_ripple_path_find` is the one-shot equivalent — fine for the agent's batch flow.

### Scenario 3 — credential-gated payment via PermissionedDomain

The setup of credentials, DepositAuth and PermissionedDomain is **already covered** in:
- [kyc-credentials-flow.md](kyc-credentials-flow.md) — full XLS-70 lifecycle (CredentialCreate → CredentialAccept → DepositPreauth + AuthorizeCredentials).
- [permissioned-domain-and-did.md](permissioned-domain-and-did.md) — XLS-80 PermissionedDomain ledger entry and the `acceptedCredentialsJson` shape.

This recipe focuses on the **Payment side**: the failure path before CredentialAccept, then the success path after.

Pre-state (already in place, see the two recipes above):
- ISSUER has run `xrpl_credential_create_prepare(account=ISSUER, subject=ALICE, credentialTypePlain="KYC-Tier1")` → tesSUCCESS. Credential is in `provisional` state.
- BOB has run `xrpl_account_set_prepare(setFlag=9 /* asfDepositAuth */)` → tesSUCCESS.
- BOB has run `xrpl_permissioned_domain_set_prepare(acceptedCredentialsJson='[{"issuer":"rIssuer...","credentialType":"4B59432D5469657231"}]')` → tesSUCCESS.
- BOB has run `xrpl_deposit_preauth_prepare(authorizeCredentialsJson='[{"issuer":"rIssuer...","credentialType":"4B59432D5469657231"}]')` → tesSUCCESS.
- ALICE has **NOT** yet run `CredentialAccept` — the credential is still `provisional`.

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

The ledger evaluates the credential: it exists, it matches BOB's `AuthorizeCredentials` set, but its `lsfAccepted` flag is `false` — therefore the recipient gate rejects the deposit. **The fee is still consumed** (`tec*` codes are claimed-fee failures). This is a feature: it deters spammers.

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

Scenario 1:
- [ ] Pre-pay: `xrpl_xrp_balance(rAlice)` and `xrpl_xrp_balance(rBob)` recorded.
- [ ] Post-pay: `xrpl_tx_lookup(txHash).validated == true`.
- [ ] `meta.TransactionResult == "tesSUCCESS"`.
- [ ] `meta.delivered_amount == "1500000"` (exact string match — never compare against a float-converted XRP value).
- [ ] `xrpl_xrp_balance(rBob) - pre = "1.5"` XRP.

Scenario 2:
- [ ] `xrpl_account_lines(rBob)` shows a trustline to `rIssuer...` for `USD` with `limit ≥ 10`.
- [ ] `xrpl_ripple_path_find` returns a non-empty `alternatives[]`.
- [ ] `xrpl_tx_simulate.preflight.feasible == true`.
- [ ] `xrpl_tx_lookup(txHash).meta.delivered_amount.value ≥ "9.9"` (the `DeliverMin` floor).
- [ ] `xrpl_account_lines(rBob)` USD balance increased by `meta.delivered_amount.value`.

Scenario 3:
- [ ] Pre-accept: `xrpl_tx_lookup(<txHash from step 3b>).meta.TransactionResult == "tecNO_PERMISSION"` (or `"tecNO_PERMISSION_RECIPIENT"`).
- [ ] After CredentialAccept: `xrpl_account_credentials(rAlice).held[?]` has `accepted == true`.
- [ ] Retry: `xrpl_tx_lookup(<txHash from step 3d>).meta.TransactionResult == "tesSUCCESS"`.
- [ ] `xrpl_xrp_balance(rBob)` increased by 1 XRP.

## Gotchas

- **Drops vs XRP — the #1 footgun**. `Amount` for an XRP Payment is a **string of drops**, not a decimal XRP number. Passing `"1.5"` to `xrpl_payment_prepare(amount=...)` will be **interpreted as 1.5 drops** = 0.0000015 XRP, not 1.5 XRP. Conversion: `drops = round(xrp * 1_000_000)`. Done as a string operation to avoid float64 rounding (e.g. `0.1 + 0.2 = 0.30000000000000004`).
- **`meta.delivered_amount` ≠ `Amount` for token payments**. With `tfPartialPayment`, the ledger may settle less than `Amount`. Always credit downstream accounting from `meta.delivered_amount`, not from the original `Amount` field. For XRP payments without partial-payment, they match — but write the code as if they don't.
- **`tfPartialPayment` is mandatory for token-↔-token deliveries that may route through multiple hops** — without it `tecPATH_PARTIAL` can hit if any single hop has insufficient liquidity to satisfy the exact `Amount`. The `DeliverMin` field is your safety floor: the tx fails as `tecPATH_PARTIAL` if it cannot deliver at least that.
- **`SendMax` is a maximum spend, not the actual spend**. For cross-currency Payments the ledger spends `min(SendMax, source_amount_of_chosen_path)`. Funds beyond what was actually consumed stay in the source account.
- **Path staleness**. `xrpl_ripple_path_find` returns a path computed against a specific ledger; by the time you submit, the order book may have moved. `xrpl_tx_simulate` re-checks; in heavy markets prefer `xrpl_path_find_create` + a short poll loop ending in submit, all within ~10 ledgers.
- **`tec*` codes consume the fee**. `tecNO_PERMISSION` in scenario 3 step 3b is still applied to the ledger — alice loses the fee even though no funds moved. This is by design and must be reflected in any "failed payment" UI.
- **`tecNO_PERMISSION` vs `tecNO_PERMISSION_RECIPIENT`**. Older rippled returned `tecNO_PERMISSION`; newer (post-XLS-70 amendments) refined it to `tecNO_PERMISSION_RECIPIENT` for the deposit-side failure. Agents should treat both as the same recipient-gate rejection.
- **DepositAuth without preauth blocks ALL payments**. If BOB enables `asfDepositAuth` but forgets either the explicit `DepositPreauth(authorize=...)` or the credential-based `DepositPreauth(authorizeCredentialsJson=...)`, every incoming Payment fails. Verify the recipient's preauth state before sending.
- **Validated vs current**. `xrpl_tx_lookup` with `validated=true` is the only authoritative source of truth — never trust the immediate submit response as final.
- **Trustlines for the destination of cross-currency Payments**. BOB MUST have a trustline to ISSUER for USD before scenario 2 can deliver USD to him. Without it: `tecNO_LINE` or `tecPATH_DRY`. Use `xrpl_trustset_prepare` first.

## Production use-cases

- **Wallet UI sends**: scenario 1, plus a UX layer translating user-typed `1.5` XRP into `"1500000"` drops at the boundary. Always read back `meta.delivered_amount` for the receipt.
- **Cross-currency on-ramp**: alice deposits XRP, the on-ramp routes through the DEX/AMM to deliver USD-stablecoin to bob's wallet. `SendMax` is the upper bound, `DeliverMin` the floor — both shown to the user as the worst- and best-case quote.
- **Permissioned merchant deposit**: bob is a regulated merchant; only KYC-verified payers (per the issuer's credential allow-list) may deposit. The merchant rejects everyone else at the protocol layer — no off-chain filtering.
- **B2B treasury payments**: combine scenario 2 (cross-currency) with the existing [multi-sign-collection.md](multi-sign-collection.md) recipe — the treasury wallet pays out USD invoices funded from XRP, signed by the multi-sig quorum.
- **Whitelisted airdrop**: scenario 3 + a script that iterates a list of credential holders, sending a small drops Payment to each. Anyone without an accepted credential is filtered out by the ledger itself.

## Extensions

- **Batch send to N recipients** (XLS-56 BatchV1_1, see [atomic-batch-payment.md](atomic-batch-payment.md)) — one atomic transaction containing N inner `Payment` ops with mixed XRP/token amounts.
- **DestinationTag enforcement**: many exchange deposits require a tag — `xrpl_tx_preflight` flags missing tags as warnings when the destination has `RequireDestinationTag`. Make this a hard error before signing.
- **Path-find with a credential constraint** (XLS-80 permissioned DEX): pathfind only through orderbooks restricted by the same `DomainID` BOB uses for deposits.
- **Auto-DeliverMin tuning**: instead of a static 99 % floor, compute `DeliverMin` from the current spread between the path's quote and the on-chain reference oracle (see [oracle-price-feed.md](oracle-price-feed.md)).
