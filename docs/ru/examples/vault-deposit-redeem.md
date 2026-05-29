>  🌐 **Язык**: [English](../../examples/vault-deposit-redeem.md) | **Русский**

# Пример: Single-Asset Vault — deposit / redeem (XLS-65, advanced)

Cowork-агент управляет жизненным циклом single-asset vault'а: owner создаёт vault, депозиторы кладут asset → получают share-MPT'ы, потом redeem их обратно. Опционально — issuer clawback из vault'а, или delete пустого vault'а.

Референс: [TestIVault.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIVault.cs).

> ⚠️ **Статус: XLS-65 — DRAFT amendment**, не активирован на стандартном rippletest.net. Рецепт работает только против standalone-узла или devnet'а с включённым amendment'ом. На стандартном testnet ожидаем `temDISABLED`. По мере активации в mainnet/testnet этот рецепт станет drop-in ready.

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_vault_create_prepare`, `xrpl_vault_set_prepare`, `xrpl_vault_delete_prepare`, `xrpl_vault_deposit_prepare`, `xrpl_vault_withdraw_prepare`, `xrpl_vault_clawback_prepare`, `xrpl_account_vaults`, `xrpl_account_mpts` (для проверки share-MPT holdings), `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_tx_lookup` |
| **xrpl-signer** | `xrpl_sign` |

## Концепция (XLS-65)

- **Vault** ≈ pooled-asset DeFi primitive. Owner creates с одним asset (XRP, IOU, или MPT). Получает 64-hex `VaultID`.
- **Share MPT** — auto-issued vault'ом share-токен. Каждый депозитор получает proportional shares. `vault.ShareMPTID` — 48-hex MPTokenIssuanceID этого share-MPT.
- **WithdrawalPolicy** (uint code) — стратегия выплат. Например, FIFO, pro-rata, restricted (amendment-defined).
- **AssetsTotal / AssetsAvailable / LossUnrealized** — балансовые поля, обновляются rippled.
- **PermissionedDomain integration** — если `tfVaultPrivate` set'нут при create, vault принимает депозиты только от accounts с правильными credentials из связанного PermissionedDomain'а.
- **Non-transferable shares** — `tfVaultShareNonTransferable` запрещает transfer share-MPT между accounts.
- **Both flags creation-only**: `tfVaultPrivate` и `tfVaultShareNonTransferable` нельзя изменить после create. Только `Data`, `AssetsMaximum`, `DomainID` можно менять через VaultSet.

## Архитектура

```
Setup:
  OWNER ──VaultCreate(Asset={USD,issuer}, Amount=100, AssetsMaximum=1000000,
                       isPrivate=false, sharesNonTransferable=false)──► rippled
                                                                          ↓ creates LOVault entry
                                                                          ↓ returns VaultID (64-hex)
                                                                          ↓ auto-creates ShareMPT (ShareMPTID 48-hex)
                                                                          ↓ OWNER holds 100 shares pro-rata

Depositors enter:
  LP1 ──VaultDeposit(VaultID, Amount=500 USD)──► rippled
                                                   ↓ LP1 transfers 500 USD → vault pseudo-account
                                                   ↓ LP1 receives ShareMPT pro-rata
  LP2 ──VaultDeposit(VaultID, Amount=200 USD)──► (similar)

Depositors exit:
  LP1 ──VaultWithdraw(VaultID, amountKind="shares", shareMptIssuanceId=<MPTID>,
                       amountValue=100)──► rippled
                                              ↓ burns 100 shares, returns proportional USD
  OR
  LP2 ──VaultWithdraw(VaultID, amountKind="asset", assetCurrency=USD, amountValue=50)──►
                                              ↓ rippled рассчитывает shares-to-burn for 50 USD

Compliance event:
  ISSUER ──VaultClawback(VaultID, Holder=BadLP, amountValue=...?)──►
                                              ↓ issuer reclaims asset from holder's share
                                              ↓ requires asfAllowTrustLineClawback или tfMPTCanClawback

Cleanup:
  OWNER ──VaultDelete(VaultID)──► (only when AssetsTotal=0)
```

## Pre-requisites

- 1 funded OWNER account.
- ≥ 1 funded depositor account (LP).
- Если asset = IOU: LP должен иметь trust line к issuer'у на этот token + balance.
- Если asset = MPT: LP должен быть authorized на этот MPT (если RequireAuth).
- XLS-65 amendment активирован (standalone rippled с `--start --quorum=1` и features enabled, или custom devnet).

## Промт агента

```markdown
---
name: vault-orchestrator
description: Manages a single-asset vault lifecycle — create, deposit, withdraw
  (asset or shares mode), clawback, modify, delete.
tools: xrpl_vault_create_prepare, xrpl_vault_set_prepare, xrpl_vault_delete_prepare,
  xrpl_vault_deposit_prepare, xrpl_vault_withdraw_prepare,
  xrpl_vault_clawback_prepare, xrpl_account_vaults, xrpl_account_mpts,
  xrpl_tx_preflight, xrpl_tx_submit_signed, xrpl_tx_lookup, xrpl_sign
---

Inputs:
- {"step":"create","network":"...","owner":"r...","assetCurrency":"USD",
   "assetIssuer":"r...","initialDeposit":"100","assetsMaximum":"1000000",
   "isPrivate":false,"sharesNonTransferable":false,"withdrawalPolicy":1}
- {"step":"deposit","network":"...","lp":"r...","vaultId":"<64-hex>",
   "assetCurrency":"USD","assetIssuer":"r...","amountValue":"500"}
- {"step":"withdraw_asset","network":"...","lp":"r...","vaultId":"<64-hex>",
   "assetCurrency":"USD","assetIssuer":"r...","amountValue":"50",
   "destination":"r... (optional)"}
- {"step":"withdraw_shares","network":"...","lp":"r...","vaultId":"<64-hex>",
   "shareMptIssuanceId":"<48-hex>","amountValue":"100"}
- {"step":"set","network":"...","owner":"r...","vaultId":"<64-hex>",
   "dataHex":"<optional>","assetsMaximum":"<optional>","domainId":"<optional>"}
- {"step":"clawback","network":"...","issuer":"r...","vaultId":"<64-hex>",
   "holder":"r...","amountValue":"<optional>"}
- {"step":"delete","network":"...","owner":"r...","vaultId":"<64-hex>"}
- {"step":"status","network":"...","account":"r..."}

For each step:
1. Build via the matching `*_prepare` tool.
2. preflight → bail if feasible=false.
3. sign + submit.
4. For "create": after success, read xrpl_account_vaults(owner) to extract
   vaultId and shareMptIssuanceId, return both.

For "status": call xrpl_account_vaults(account) and return list.

Return { txHash?, vaultId?, shareMptIssuanceId?, engineResult, ... }
```

## Step-by-step

### 1. Create vault

```text
agent ← {"step":"create","owner":"rOwner...","assetCurrency":"USD",
         "assetIssuer":"rUsdIssuer...","initialDeposit":"100",
         "assetsMaximum":"1000000","isPrivate":false,
         "sharesNonTransferable":false,"withdrawalPolicy":1}

→ xrpl_vault_create_prepare(
    network, account=rOwner,
    assetCurrency="USD", assetIssuer="rUsdIssuer...",
    amountValue="100",
    assetsMaximum="1000000",
    withdrawalPolicy=1,
    isPrivate=false, sharesNonTransferable=false
  )
→ preflight → feasible=true
→ sign by rOwner → submit → tesSUCCESS

→ xrpl_account_vaults(account=rOwner)
   → { "vaults":[{ "vaultId":"<64-hex>","pseudoAccount":"<pseudo>",
                    "shareMptIssuanceId":"<48-hex>","assetsTotal":"100",
                    "assetsAvailable":"100","domainId":null }] }
```

Сохраняем `vaultId` и `shareMptIssuanceId` для дальнейших операций. Owner получил 100 shares (соответствуют 100 USD initial deposit).

### 2. LP deposits asset

```text
agent ← {"step":"deposit","lp":"rLP1...","vaultId":"<...>",
         "assetCurrency":"USD","assetIssuer":"rUsdIssuer...","amountValue":"500"}

→ xrpl_vault_deposit_prepare(
    network, account="rLP1...",
    vaultId, assetCurrency="USD", assetIssuer="rUsdIssuer...",
    amountValue="500"
  )
→ preflight → feasible=true (vault exists, asset matches, LP has trust line)
→ sign by rLP1 → submit → tesSUCCESS
```

После:
- LP1 потерял 500 USD из trust line.
- Vault pseudo-account holds 500 USD больше.
- LP1 получил pro-rata share-MPT: если до этого было 100 shares и 100 USD, после deposit → 600 USD и 600 shares (LP1 имеет 500).

Verify:
```text
agent → xrpl_account_mpts(account="rLP1...")
       → { "holdings":[{ "id":"<48-hex MPTID>","amount":"500","accepted":true }] }
```

### 3. LP withdraws by asset amount (withdraw 50 USD worth)

```text
agent ← {"step":"withdraw_asset","lp":"rLP1...","vaultId":"<...>",
         "assetCurrency":"USD","assetIssuer":"rUsdIssuer...","amountValue":"50"}

→ xrpl_vault_withdraw_prepare(
    network, account="rLP1...",
    vaultId, amountKind="asset", amountValue="50",
    assetCurrency="USD", assetIssuer="rUsdIssuer..."
    /* destination omitted → self */
  )
→ preflight
→ sign by rLP1 → submit → tesSUCCESS
```

Rippled burns proportional shares from LP1 to cover 50 USD (with возможной slippage, depending on WithdrawalPolicy). LP1 receives 50 USD на trust line.

### 4. LP withdraws by exact share count

```text
agent ← {"step":"withdraw_shares","lp":"rLP1...","vaultId":"<...>",
         "shareMptIssuanceId":"<48-hex>","amountValue":"100"}

→ xrpl_vault_withdraw_prepare(
    network, account="rLP1...",
    vaultId, amountKind="shares", amountValue="100",
    shareMptIssuanceId="<48-hex>"
  )
→ sign + submit
```

Rippled burns exactly 100 shares, returns proportional asset to LP1.

### 5. Modify vault (e.g. raise cap)

```text
agent ← {"step":"set","owner":"rOwner...","vaultId":"<...>",
         "assetsMaximum":"5000000"}

→ xrpl_vault_set_prepare(
    network, account="rOwner...", vaultId,
    assetsMaximum="5000000"   /* Data, DomainID — null */
  )
→ sign + submit → tesSUCCESS
```

### 6. Issuer clawback (compliance)

Если LP1 sanctions-listed, issuer (`rUsdIssuer...`) хочет получить tokens обратно:

```text
agent ← {"step":"clawback","issuer":"rUsdIssuer...","vaultId":"<...>",
         "holder":"rLP1...","amountValue":"500"}

→ xrpl_vault_clawback_prepare(
    network, account="rUsdIssuer...", vaultId, holder="rLP1...",
    assetCurrency="USD", assetIssuer="rUsdIssuer...", amountValue="500"
  )
→ preflight (Holder ≠ Account, asset matches vault)
→ sign by issuer → submit → tesSUCCESS
```

После:
- LP1's shares burned.
- 500 USD destroyed back to issuer's obligation balance.
- Vault `AssetsTotal` уменьшился.

Workflow аналогичен AMMClawback (см. [amm-clawback.md](amm-clawback.md)).

### 7. Delete empty vault

```text
agent ← {"step":"delete","owner":"rOwner...","vaultId":"<...>"}

→ xrpl_vault_delete_prepare(network, account="rOwner...", vaultId)
→ preflight (AssetsTotal must be 0 — иначе tecHAS_OBLIGATIONS)
→ sign by rOwner → submit → tesSUCCESS
```

Полный flow для delete:
1. Все depositors withdraw their full shares.
2. Owner withdraws его остаток.
3. `AssetsTotal == 0` → можно delete.

## Verification checklist

- [ ] Step 1: `xrpl_account_vaults(owner)` shows new vault, `assetsTotal=100`, `shareMptIssuanceId` valid.
- [ ] Step 2: LP1's `xrpl_account_mpts` shows share holding, vault `assetsTotal` increased.
- [ ] Step 3: LP1's USD trust line balance increased by 50, vault `assetsTotal` decreased.
- [ ] Step 4: LP1's share MPT amount decreased by 100.
- [ ] Step 5: `xrpl_account_vaults` shows new `assetsMaximum`.
- [ ] Step 6: clawback reflected in `assetsTotal` + `xrpl_gateway_balances(issuer)`.
- [ ] Step 7: vault removed from `xrpl_account_vaults` list.

## Подводные камни

- **Amendment activation** — XLS-65 ещё draft. Стандартный testnet → `temDISABLED`. Используй standalone rippled.
- **Asset matches** — VaultDeposit/Withdraw asset spec MUST exactly match Vault's asset (currency + issuer). Иначе `tecPATH_DRY` или подобное.
- **Private vault gating** — если `tfVaultPrivate`, депозитор должен иметь credential из vault'а PermissionedDomain. Без credential → `tecNO_PERMISSION`.
- **Withdrawal policy gating** — `WithdrawalPolicy` определяет когда / сколько можно withdraw. Например, time-locked policy блокирует raw withdrawals. Используй `xrpl_account_vaults` чтобы прочитать policy и понять constraints.
- **Slippage on asset-mode withdraw**: при `amountKind=asset`, точное количество asset'а гарантировано, но shares-to-burn рассчитываются rippled-ом — могут отличаться от naive `value × total_shares / total_assets`.
- **Share-MPT non-transferable**: если `sharesNonTransferable=true`, нельзя trade share-MPT на DEX, нельзя deposit в AMM. Holders могут только withdraw обратно через vault.

## Use-cases

- **Tokenized treasury bonds**: issuer deposits bonds в vault, retail purchase shares pro-rata.
- **Yield farming protocol** — vault auto-allocates deposits to highest-yield strategy. WithdrawalPolicy enforces lock-up period.
- **Private RWA fund**: tfVaultPrivate + KYC PermDomain. Только accredited investors могут deposit.
- **DAO treasury delegation**: DAO owns vault, multi-sig submits VaultDeposit / VaultWithdraw, members hold shares представляющие governance + claims.
- **Insurance fund** — vault holds reserves; claim event triggers VaultClawback от issuer'а (если applicable) for outflow.

## Расширения

- **Vault + Loan integration** (XLS-66): vault — backing для LoanBroker (`LoanBrokerSet.VaultID = <vault>`). Vault deposits → underwriting capital для loans.
- **Multi-vault portfolio**: agent manages N vaults across various assets. Auto-rebalance: monitor `assetsTotal / assetsMaximum` ratio per vault; deposit to under-allocated, withdraw from over-allocated.
- **Cross-vault swaps**: redeem from vault A, deposit into vault B atomic-ally через Batch (после BatchV1_1 activation).
- **Yield reporting**: agent compares `assetsTotal` across time, computes effective APY, posts to Telegram dashboard.

## Текущие integration-тесты

Наши smoke-тесты в [tests/StaticBit.Xrpl.Mcp.Integration.Tests/MptBatchVaultOracleTestsI.cs](../../../tests/StaticBit.Xrpl.Mcp.Integration.Tests/MptBatchVaultOracleTestsI.cs) для Vault'а `[Ignore]`'нуты с пометкой "XLS-65 Vault amendment is draft and not active on standard testnet". Когда amendment активируется — снять `[Ignore]` и тесты заработают.
