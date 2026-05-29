>  🌐 **Language**: **English** | [Русский](../ru/examples/vault-deposit-redeem.md)

# Example: Single-Asset Vault — deposit / redeem (XLS-65, advanced)

A Cowork agent that runs the lifecycle of a single-asset vault: the owner creates a vault, depositors put in the asset → receive share-MPTs, then redeem them back. Optionally — issuer clawback from the vault, or delete an empty vault.

Reference: [TestIVault.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIVault.cs).

> ⚠️ **Status: XLS-65 is a DRAFT amendment**, not active on standard rippletest.net. The recipe works only against a standalone node or a devnet with the amendment enabled. On standard testnet expect `temDISABLED`. Once the amendment activates on mainnet/testnet, this recipe becomes drop-in ready.

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_vault_create_prepare`, `xrpl_vault_set_prepare`, `xrpl_vault_delete_prepare`, `xrpl_vault_deposit_prepare`, `xrpl_vault_withdraw_prepare`, `xrpl_vault_clawback_prepare`, `xrpl_account_vaults`, `xrpl_account_mpts` (to verify share-MPT holdings), `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_tx_lookup` |
| **xrpl-signer** | `xrpl_sign` |

## Concept (XLS-65)

- **Vault** ≈ pooled-asset DeFi primitive. The owner creates one with a single asset (XRP, IOU, or MPT). Receives a 64-hex `VaultID`.
- **Share MPT** — share token auto-issued by the vault. Every depositor receives shares proportionally. `vault.ShareMPTID` — 48-hex MPTokenIssuanceID of that share-MPT.
- **WithdrawalPolicy** (uint code) — distribution strategy. E.g. FIFO, pro-rata, restricted (amendment-defined).
- **AssetsTotal / AssetsAvailable / LossUnrealized** — balance fields, updated by rippled.
- **PermissionedDomain integration** — when `tfVaultPrivate` is set at create, the vault accepts deposits only from accounts with the right credentials from the linked PermissionedDomain.
- **Non-transferable shares** — `tfVaultShareNonTransferable` blocks transfers of the share-MPT between accounts.
- **Both flags creation-only**: `tfVaultPrivate` and `tfVaultShareNonTransferable` cannot be changed after create. Only `Data`, `AssetsMaximum`, `DomainID` are mutable via VaultSet.

## Architecture

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
                                              ↓ rippled computes shares-to-burn for 50 USD

Compliance event:
  ISSUER ──VaultClawback(VaultID, Holder=BadLP, amountValue=...?)──►
                                              ↓ issuer reclaims asset from holder's share
                                              ↓ requires asfAllowTrustLineClawback or tfMPTCanClawback

Cleanup:
  OWNER ──VaultDelete(VaultID)──► (only when AssetsTotal=0)
```

## Pre-requisites

- 1 funded OWNER account.
- ≥ 1 funded depositor account (LP).
- If asset = IOU: the LP needs a trust line to the issuer for that token + balance.
- If asset = MPT: the LP must be authorised on that MPT (when RequireAuth).
- XLS-65 amendment is active (standalone rippled with `--start --quorum=1` and features enabled, or a custom devnet).

## Agent prompt

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

Save `vaultId` and `shareMptIssuanceId` for later operations. The owner now holds 100 shares (corresponding to the 100 USD initial deposit).

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

After:
- LP1 lost 500 USD from the trust line.
- The vault pseudo-account holds 500 more USD.
- LP1 received a pro-rata share-MPT: if the pool had 100 shares and 100 USD before, after the deposit → 600 USD and 600 shares (LP1 has 500).

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

Rippled burns proportional shares from LP1 to cover 50 USD (with possible slippage, depending on WithdrawalPolicy). LP1 receives 50 USD on the trust line.

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

Rippled burns exactly 100 shares and returns proportional asset to LP1.

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

If LP1 is sanctions-listed, the issuer (`rUsdIssuer...`) wants tokens back:

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

After:
- LP1's shares burned.
- 500 USD destroyed back to the issuer's obligation balance.
- Vault `AssetsTotal` decreased.

Workflow is analogous to AMMClawback (see [amm-clawback.md](amm-clawback.md)).

### 7. Delete an empty vault

```text
agent ← {"step":"delete","owner":"rOwner...","vaultId":"<...>"}

→ xrpl_vault_delete_prepare(network, account="rOwner...", vaultId)
→ preflight (AssetsTotal must be 0 — otherwise tecHAS_OBLIGATIONS)
→ sign by rOwner → submit → tesSUCCESS
```

Full delete flow:
1. All depositors withdraw their full shares.
2. The owner withdraws their remainder.
3. `AssetsTotal == 0` → ready to delete.

## Verification checklist

- [ ] Step 1: `xrpl_account_vaults(owner)` shows new vault, `assetsTotal=100`, `shareMptIssuanceId` valid.
- [ ] Step 2: LP1's `xrpl_account_mpts` shows share holding, vault `assetsTotal` increased.
- [ ] Step 3: LP1's USD trust line balance increased by 50, vault `assetsTotal` decreased.
- [ ] Step 4: LP1's share MPT amount decreased by 100.
- [ ] Step 5: `xrpl_account_vaults` shows new `assetsMaximum`.
- [ ] Step 6: clawback reflected in `assetsTotal` + `xrpl_gateway_balances(issuer)`.
- [ ] Step 7: vault removed from `xrpl_account_vaults` list.

## Gotchas

- **Amendment activation** — XLS-65 is draft. Standard testnet → `temDISABLED`. Use standalone rippled.
- **Asset matches** — VaultDeposit/Withdraw asset spec MUST exactly match the vault's asset (currency + issuer). Otherwise `tecPATH_DRY` or similar.
- **Private vault gating** — when `tfVaultPrivate`, the depositor must hold a credential from the vault's PermissionedDomain. Without it → `tecNO_PERMISSION`.
- **Withdrawal policy gating** — `WithdrawalPolicy` defines when / how much you can withdraw. E.g. a time-locked policy blocks raw withdrawals. Use `xrpl_account_vaults` to read the policy and understand the constraints.
- **Slippage on asset-mode withdraw**: with `amountKind=asset`, the exact asset amount is guaranteed, but shares-to-burn are computed by rippled — they may differ from a naive `value × total_shares / total_assets`.
- **Share-MPT non-transferable**: when `sharesNonTransferable=true`, you cannot trade the share-MPT on DEX or deposit into AMM. Holders can only withdraw back through the vault.

## Use-cases

- **Tokenized treasury bonds**: issuer deposits bonds into a vault, retail buys shares pro-rata.
- **Yield farming protocol** — vault auto-allocates deposits to the highest-yield strategy. WithdrawalPolicy enforces a lock-up period.
- **Private RWA fund**: tfVaultPrivate + KYC PermDomain. Only accredited investors may deposit.
- **DAO treasury delegation**: the DAO owns the vault, multi-sig submits VaultDeposit / VaultWithdraw, members hold shares representing governance + claims.
- **Insurance fund** — vault holds reserves; a claim event triggers VaultClawback from the issuer (where applicable) for outflow.

## Extensions

- **Vault + Loan integration** (XLS-66): the vault backs a LoanBroker (`LoanBrokerSet.VaultID = <vault>`). Vault deposits → underwriting capital for loans.
- **Multi-vault portfolio**: an agent manages N vaults across various assets. Auto-rebalance: monitor `assetsTotal / assetsMaximum` per vault; deposit to under-allocated, withdraw from over-allocated.
- **Cross-vault swaps**: redeem from vault A, deposit into vault B atomically via Batch (after BatchV1_1 activation).
- **Yield reporting**: the agent compares `assetsTotal` over time, computes effective APY, posts to a Telegram dashboard.

## Current integration tests

Our smoke tests in [tests/StaticBit.Xrpl.Mcp.Integration.Tests/MptBatchVaultOracleTestsI.cs](../../tests/StaticBit.Xrpl.Mcp.Integration.Tests/MptBatchVaultOracleTestsI.cs) for Vault are `[Ignore]`'d with the note "XLS-65 Vault amendment is draft and not active on standard testnet". Once the amendment activates — remove `[Ignore]` and the tests start working.
