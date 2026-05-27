> 🇬🇧 [Read in English](amm-clawback.md)

# Пример: AMM Clawback (XLS-37)

Token issuer возвращает свои tokens из AMM-пула когда holder нарушил compliance terms. Используется regulated стейблкоинами, RWA tokens — issuer обязан иметь способ recall'нуть tokens из любого места, включая liquidity pools.

Референс: [TestIAMMClawback.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIAMMClawback.cs).

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_account_set_prepare` (enable AllowTrustLineClawback), `xrpl_trustset_prepare`, `xrpl_amm_create_prepare`, `xrpl_amm_deposit_prepare`, `xrpl_amm_clawback_prepare`, `xrpl_amm_info`, `xrpl_account_lines`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed` |
| **xrpl-signer** | `xrpl_sign` |

## Концепция (XLS-37)

- Обычный `Clawback` забирает tokens только из **trust lines** holder'а.
- AMM-pool — это специальный pseudo-account holding tokens "от имени" liquidity providers. Tokens в пуле не lie на trust line какого-либо conventional account'а.
- XLS-37 добавил `AMMClawback` — issuer указывает `Holder` (LP), `Asset` (issuer's token), `Asset2` (counterpart), `Amount` (optional). Tokens **issuer'а** забираются из пула pro-rata, holder получает counterpart asset обратно.
- **Critical pre-condition**: issuer должен enable `asfAllowTrustLineClawback (16)` ПЕРЕД issuing tokens. Если issuer уже выпустил tokens без этого флага, flag set'нуть нельзя — `tecOWNERS` или подобное. Это immutable opt-in.

## Архитектура

```
Setup (one-time, ДО выпуска tokens):
  ISSUER ──AccountSet(SetFlag=asfAllowTrustLineClawback=16)──► rippled

Token distribution:
  ISSUER ──Payment(Dest=LP, Amount={USD,issuer=ISSUER})──► rippled

LP enters AMM:
  LP ──TrustSet(LimitAmount={USD,issuer=ISSUER, value=1000000})──►
  LP ──AMMCreate (или AMMDeposit) с {USD,...,ISSUER} + XRP──► rippled
                                                                ↓ tokens теперь в pool, LP holds AMM LPTokens

Compliance event (holder violation):
  ISSUER ──AMMClawback(Holder=LP, Asset={USD,issuer=ISSUER}, Asset2={XRP},
                       Amount?={USD,value=500,issuer=ISSUER})──►
                                                                ↓ pool возвращает proportional XRP к LP
                                                                ↓ USD tokens из pool destroy'ятся (back to issuer)
                                                                ↓ LP's LPTokens burned pro-rata
```

## Pre-requisites

- ISSUER account с уже-enabled `asfAllowTrustLineClawback` ДО первой `Payment` issued tokens.
- ≥ 1 LP account с трастлайном к ISSUER на token + balance в counterpart asset (XRP или другой token).
- AMM pool существует с парой `{ISSUER's token, counterpart}`.
- XLS-37 amendment активирован.

## Промт агента

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
   → if any "obligations" != 0, WARN — токены уже выпущены,
     after this point asfAllowTrustLineClawback cannot be set.

→ xrpl_account_set_prepare(network, account=issuer, setFlag=16)
→ preflight → sign by issuer → submit → tesSUCCESS
```

После этого `xrpl_account_info(issuer).account_flags.allowTrustLineClawback = true`. Flag immutable — нельзя disable.

### 2. Setup AMM pool (assumes already done — see existing AMM examples)

LP должен:
- Иметь trust line к issuer на token.
- Получить tokens от issuer через regular Payment.
- Создать AMM через `xrpl_amm_create_prepare` либо deposit в существующий через `xrpl_amm_deposit_prepare`.

После этого:
- `xrpl_amm_info(asset1={USD,issuer},asset2={XRP})` показывает pool с balances.
- `xrpl_account_lines(account=LP)` показывает LPTokens holding.

### 3. Compliance trigger → clawback

Допустим LP получил указание sanctions list, issuer обязан freeze + retrieve:

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
       → LPTokens balance уменьшился pro-rata

agent → xrpl_xrp_balance(account=LP)
       → XRP уменьшился на counterpart portion (USD weren't returned to LP — burned back
         to issuer)
```

**Что происходит exactly**:
- ISSUER's tokens (`Asset`) destroyed из pool (back to obligation balance reduction in `xrpl_gateway_balances`).
- LP's counterpart asset (XRP в нашем случае) **refunded to LP**, pro-rata к burned LP shares.
- LP's LP token holding `xrpl_account_lines` уменьшается на сожжённую долю.

### 5. Max clawback

Omit `amountValue` → tool забирает максимум того, что LP может потребовать (вся доля LP в issuer's tokens).

```text
agent ← {"step":"clawback","issuer":"...","holder":"...","tokenCurrency":"USD"
         /* amountValue omitted */}
```

## Verification checklist

- [ ] Pre-Step 1: `xrpl_account_info(issuer).account_flags.allowTrustLineClawback = false`.
- [ ] Step 1: enable flag → `true`.
- [ ] Pre-Step 3: `xrpl_amm_info` показывает pool with `{USD, XRP}` and LP holding shares.
- [ ] Step 3: tx hash, `tesSUCCESS`.
- [ ] Step 4: pool balance USD уменьшился; LP получил XRP-portion обратно; LPTokens сократились.
- [ ] `xrpl_gateway_balances(issuer).obligations.USD` уменьшился на claw'd-back amount.

## Подводные камни

- **`asfAllowTrustLineClawback` cannot be set after issuance**. Если хотя бы одна Payment с issuer's tokens была применена ДО enable'а flag'а — flag set fails. Это design limitation для protection holders.
- **XRP cannot be clawed back**. AMMClawback применяется только к issued currencies. Если pair это {USD, EUR} — issuer USD может claw'нуть USD, issuer EUR — EUR. Cross-issuer claims fail.
- **Asset.issuer MUST equal Account**. Если tx submit'ит не сам issuer — `tecNO_PERMISSION`.
- **Pool slippage**: burning issuer's tokens из pool изменяет ratio. LP получит counterpart по post-burn rate, не по pre-burn. Большой clawback может drain pool до slippage limits.
- **No double-jeopardy**: после AMMClawback на amount=X, повторный claim того же не работает (pool уже adjusted).
- **Combined с XLS-37 для MPT**: MPT clawback использует `Clawback` tx с MPT-shape amount, AMMClawback применяется только к IOU tokens (XLS-37 standalone amendment, не MPT).

## Use-cases

- **Regulated stablecoin issuer**: USDC-equivalent должен retrieve tokens после OFAC SDN designation. Без AMMClawback issuer не может вынуть tokens из Uniswap-style pools.
- **Real-world asset (RWA) tokens**: компания токенизировала ценные бумаги. При compliance trigger (revoked accreditation, asset disposal) issuer возвращает security tokens из любого DeFi protocol'а.
- **Sanctioned LP**: regulator notify'ит issuer'а что конкретный wallet LP — sanctions-listed. Issuer делает AMMClawback против всех pool'ов где LP участвует.
- **Reissuance event**: token undergoes redemption (e.g., bond matures). Issuer claws back from all venues including AMM, then burns tokens.

## Расширения

- **Batch clawback** (после активации XLS-56 BatchV1_1): N AMMClawback'ов разных LPs одной atomic-tx.
- **Combined freeze + clawback**: сначала `TrustSet tfSetFreeze` чтобы заблокировать LP-controlled trust line (предотвращает withdraw из pool), потом AMMClawback.
- **Cross-AMM clawback**: если token в нескольких pools (USD/XRP, USD/EUR, USD/BTC) — последовательно run AMMClawback против каждого.
- **Pre-flight detection**: до clawback'а agent может query `xrpl_amm_info` для всех pools где LP участвует (через `account_objects type=AMM` + filter).
