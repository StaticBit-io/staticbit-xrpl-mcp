> 🇷🇺 [Прочесть на русском](amm-clawback.ru.md)

# Example: AMM Clawback (XLS-37)

A token issuer reclaims its tokens from an AMM pool when a holder violates compliance terms. Used by regulated stablecoins, RWA tokens — the issuer must have a way to recall tokens from anywhere, including liquidity pools.

Reference: [TestIAMMClawback.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIAMMClawback.cs).

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_account_set_prepare` (enable AllowTrustLineClawback), `xrpl_trustset_prepare`, `xrpl_amm_create_prepare`, `xrpl_amm_deposit_prepare`, `xrpl_amm_clawback_prepare`, `xrpl_amm_info`, `xrpl_account_lines`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed` |
| **xrpl-signer** | `xrpl_sign` |

## Concept (XLS-37)

- Regular `Clawback` reclaims tokens only from a holder's **trust line**.
- An AMM pool is a special pseudo-account holding tokens "on behalf of" liquidity providers. Tokens in the pool don't sit on any conventional account's trust line.
- XLS-37 added `AMMClawback` — the issuer specifies `Holder` (LP), `Asset` (the issuer's token), `Asset2` (counterpart), `Amount` (optional). The **issuer's** tokens are pulled out of the pool pro-rata, the holder gets the counterpart asset back.
- **Critical pre-condition**: the issuer must enable `asfAllowTrustLineClawback (16)` BEFORE issuing tokens. If the issuer already issued tokens without that flag, the flag cannot be set — `tecOWNERS` or similar. This is an immutable opt-in.

## Architecture

```
Setup (one-time, BEFORE token issuance):
  ISSUER ──AccountSet(SetFlag=asfAllowTrustLineClawback=16)──► rippled

Token distribution:
  ISSUER ──Payment(Dest=LP, Amount={USD,issuer=ISSUER})──► rippled

LP enters AMM:
  LP ──TrustSet(LimitAmount={USD,issuer=ISSUER, value=1000000})──►
  LP ──AMMCreate (or AMMDeposit) with {USD,...,ISSUER} + XRP──► rippled
                                                                ↓ tokens now in pool, LP holds AMM LPTokens

Compliance event (holder violation):
  ISSUER ──AMMClawback(Holder=LP, Asset={USD,issuer=ISSUER}, Asset2={XRP},
                       Amount?={USD,value=500,issuer=ISSUER})──►
                                                                ↓ pool returns proportional XRP to LP
                                                                ↓ USD tokens from the pool are destroyed (back to issuer)
                                                                ↓ LP's LPTokens burned pro-rata
```

## Pre-requisites

- ISSUER account with `asfAllowTrustLineClawback` already enabled BEFORE the first Payment of issued tokens.
- ≥ 1 LP account with a trust line to ISSUER for the token + a balance of the counterpart asset (XRP or another token).
- AMM pool exists with the pair `{ISSUER's token, counterpart}`.
- XLS-37 amendment is active.

## Agent prompt

```markdown
---
name: amm-clawback-orchestrator
description: Enables AllowTrustLineClawback for an issuer, sets up an AMM pool,
  and performs targeted clawback against a specific LP holder.
tools: xrpl_account_set_prepare, xrpl_trustset_prepare, xrpl_amm_create_prepare,
  xrpl_amm_deposit_prepare, xrpl_amm_clawback_prepare, xrpl_amm_info,
  xrpl_account_lines, xrpl_tx_preflight, xrpl_tx_submit_signed, xrpl_sign
---

Inputs:
- {"step":"enable_clawback","network":"testnet","issuer":"r..."}
- {"step":"setup_pool","network":"...","issuer":"r...","lp":"r...",
   "tokenCurrency":"USD","tokenValue":"100000","counterpartDrops":"50000000"}
- {"step":"clawback","network":"...","issuer":"r...","holder":"r...",
   "tokenCurrency":"USD","amountValue":"<optional>"}

For "enable_clawback":
1. WARN if any tokens of this currency are already issued (xrpl_gateway_balances) —
   flag cannot be set after issuance.
2. xrpl_account_set_prepare(setFlag=16) → preflight → sign → submit.

For "clawback":
1. xrpl_amm_info(asset1={token,issuer},asset2={XRP}) — confirm pool exists
   and LP has shares.
2. xrpl_amm_clawback_prepare(network, account=issuer, holder,
   asset1Currency=token, asset1Issuer=issuer, asset2Currency="XRP",
   asset2Issuer=null, amountValue?)
3. preflight (Asset.issuer == Account, Holder != Account, currency != XRP)
4. sign by issuer → submit.

Return { txHash, engineResult, clawedBackAmount, lpRefundedCounterpart }.
```

## Step-by-step

### 1. Enable clawback (one-time setup)

```text
agent ← {"step":"enable_clawback","issuer":"rIssuerStablecoin..."}

→ pre-check: xrpl_gateway_balances(account=issuer)
   → if any "obligations" != 0, WARN — tokens already issued,
     after this point asfAllowTrustLineClawback cannot be set.

→ xrpl_account_set_prepare(network, account=issuer, setFlag=16)
→ preflight → sign by issuer → submit → tesSUCCESS
```

After this: `xrpl_account_info(issuer).account_flags.allowTrustLineClawback = true`. The flag is immutable — cannot be disabled.

### 2. Setup AMM pool (assumed already done — see existing AMM examples)

The LP must:
- Hold a trust line to the issuer for the token.
- Receive tokens from the issuer via a regular Payment.
- Create the AMM via `xrpl_amm_create_prepare` or deposit into an existing one via `xrpl_amm_deposit_prepare`.

After that:
- `xrpl_amm_info(asset1={USD,issuer},asset2={XRP})` shows the pool with balances.
- `xrpl_account_lines(account=LP)` shows the LPTokens holding.

### 3. Compliance trigger → clawback

Suppose the LP appears on a sanctions list; the issuer is obliged to freeze + retrieve:

```text
agent ← {"step":"clawback","issuer":"rIssuerStablecoin...","holder":"rBadLP...",
         "tokenCurrency":"USD","amountValue":"500"}

Pre-check:
→ xrpl_amm_info(network, asset1={"currency":"USD","issuer":"rIssuerStablecoin..."},
   asset2={"currency":"XRP"})
   → confirm pool exists with LP's shares

→ xrpl_amm_clawback_prepare(
    network="testnet",
    account="rIssuerStablecoin...",
    holder="rBadLP...",
    asset1Currency="USD", asset1Issuer="rIssuerStablecoin...",
    asset2Currency="XRP", asset2Issuer=null,
    amountValue="500"   // limited to 500 USD, omit for max available
  )
→ preflight (Asset.issuer == Account, Holder != Account, Asset != XRP)
   → feasible=true
→ xrpl_sign(walletName=issuer, blob)
→ xrpl_tx_submit_signed(blob_signed, waitForValidation=true)
   → tesSUCCESS
```

### 4. Verify the outcome

```text
agent → xrpl_amm_info(asset1, asset2)
       → pool balances changed: USD reduced, XRP reduced (slightly less than 500 USD's worth,
         due to slippage)

agent → xrpl_account_lines(account=LP)
       → LPTokens balance dropped pro-rata

agent → xrpl_xrp_balance(account=LP)
       → XRP dropped by the counterpart portion (USD was NOT returned to LP — burned
         back to issuer)
```

**Exactly what happens**:
- ISSUER's tokens (`Asset`) destroyed from the pool (reduces obligation balance in `xrpl_gateway_balances`).
- LP's counterpart asset (XRP in our case) **refunded to LP**, pro-rata to the burned LP shares.
- LP's LP-token holding in `xrpl_account_lines` decreases by the burned share.

### 5. Max clawback

Omit `amountValue` → the tool reclaims the maximum the LP can owe (the LP's entire share of the issuer's tokens).

```text
agent ← {"step":"clawback","issuer":"...","holder":"...","tokenCurrency":"USD"
         /* amountValue omitted */}
```

## Verification checklist

- [ ] Pre-Step 1: `xrpl_account_info(issuer).account_flags.allowTrustLineClawback = false`.
- [ ] Step 1: enable flag → `true`.
- [ ] Pre-Step 3: `xrpl_amm_info` shows pool with `{USD, XRP}` and LP holding shares.
- [ ] Step 3: tx hash, `tesSUCCESS`.
- [ ] Step 4: pool USD balance reduced; LP got the XRP portion back; LPTokens shrank.
- [ ] `xrpl_gateway_balances(issuer).obligations.USD` reduced by the clawed-back amount.

## Gotchas

- **`asfAllowTrustLineClawback` cannot be set after issuance**. If at least one Payment with issuer's tokens applied BEFORE the flag was enabled — the flag set fails. This is a design limit protecting holders.
- **XRP cannot be clawed back**. AMMClawback applies only to issued currencies. With a {USD, EUR} pair — the USD issuer can claw USD; the EUR issuer — EUR. Cross-issuer claims fail.
- **Asset.issuer MUST equal Account**. If the tx isn't submitted by the issuer themselves — `tecNO_PERMISSION`.
- **Pool slippage**: burning the issuer's tokens from the pool changes the ratio. LP gets the counterpart at the post-burn rate, not pre-burn. A large clawback may drain the pool down to slippage limits.
- **No double jeopardy**: after AMMClawback for amount=X, re-claiming the same doesn't work (the pool already adjusted).
- **XLS-37 vs MPT clawback**: MPT clawback uses a `Clawback` tx with an MPT-shape amount; AMMClawback applies only to IOU tokens (XLS-37 is a standalone amendment, not MPT).

## Use-cases

- **Regulated stablecoin issuer**: a USDC-equivalent must retrieve tokens after an OFAC SDN designation. Without AMMClawback the issuer cannot pull tokens out of Uniswap-style pools.
- **Real-world asset (RWA) tokens**: the company tokenised securities. On a compliance trigger (revoked accreditation, asset disposal) the issuer reclaims the security tokens from any DeFi protocol.
- **Sanctioned LP**: a regulator notifies the issuer that a specific LP wallet is sanctions-listed. The issuer runs AMMClawback against every pool the LP participates in.
- **Reissuance event**: a token goes through redemption (e.g. bond matures). The issuer claws back from all venues including AMM, then burns the tokens.

## Extensions

- **Batch clawback** (after XLS-56 BatchV1_1): N AMMClawbacks against different LPs in a single atomic tx.
- **Combined freeze + clawback**: first `TrustSet tfSetFreeze` to block the LP-controlled trust line (prevents withdraw from pool), then AMMClawback.
- **Cross-AMM clawback**: if the token sits in several pools (USD/XRP, USD/EUR, USD/BTC) — run AMMClawback against each in sequence.
- **Pre-flight detection**: before clawback the agent can query `xrpl_amm_info` for every pool the LP participates in (via `account_objects type=AMM` + filter).
