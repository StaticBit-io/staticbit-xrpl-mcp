---
name: xrpl-cloud-operations
description: Use this skill when the user wants to query XRPL state (balances, transaction history, ledger, order book, AMM pools) or prepare/submit XRPL transactions through the StaticBit cloud server. Recognizes phrases like "check XRPL balance", "send 10 XRP", "create trustline", "what's on the XRPL DEX", "look up transaction by hash", "show me the order book", "AMM pool info", "сколько XRP у адреса", "отправь XRP", "создай trustline". Routes calls through the `xrpl-cloud` HTTP MCP (https://xrpl-mcp.staticbit.io). For signing, expects the `xrpl-signer` plugin to be installed alongside.
---

# XRPL operations via cloud MCP

The `xrpl-cloud` plugin exposes the same <!-- toolcount:xrpl -->116<!-- /toolcount:xrpl --> tools as `xrpl-local`, but routes them through the StaticBit-hosted HTTP server at `xrpl-mcp.staticbit.io`. Tool prefix: `mcp__plugin_xrpl-cloud_xrpl-cloud__xrpl_*`.

## Read-only operations (no signing needed)

Direct calls — no flow, no keystore involvement.

| User asks | Tool |
|---|---|
| Check XRP balance | `xrpl_xrp_balance` |
| Account info / sequence / flags / reserves | `xrpl_account_info` |
| Trust lines of an account | `xrpl_account_lines` |
| Transaction history | `xrpl_account_tx` |
| Active DEX offers of an account | `xrpl_account_offers` |
| All ledger objects owned by account | `xrpl_account_objects` |
| Look up a single tx by hash | `xrpl_tx_lookup` |
| Ledger header / state at a sequence | `xrpl_ledger` |
| Order book for a currency pair | `xrpl_book_offers` |
| AMM pool info | `xrpl_amm_info` |
| Current network fee | `xrpl_fee` |
| Node status | `xrpl_server_info` |

Always pass `network` explicitly — mainnet for production addresses, testnet for `xrpl.org` faucet addresses. Don't guess.

## Write operations — two-phase flow with the signer

For any transaction that changes the ledger (Payment, TrustSet, OfferCreate, OfferCancel, AMMDeposit, AMMWithdraw, generic) follow this strict sequence:

1. **Prepare** — `xrpl_<action>_prepare`. The cloud server returns:
   - `txJson` — autofilled transaction (Sequence, Fee, LastLedgerSequence populated)
   - `txBlobUnsigned` — canonical hex blob
   - `signingData` — pre-image suitable for hardware-wallet signing
   - `humanSummary` — one-line description
   - `requiresUserApproval: true`

2. **Show the `humanSummary` to the user. Ask explicit confirmation.** Do not skip — this is the user's last chance to catch a wrong address, a fee surprise, or drops-vs-XRP confusion.

3. **Sign** with `mcp__plugin_xrpl-signer_xrpl-signer__xrpl_sign`:
   - `name` — wallet alias from the keystore
   - `transaction` — either the `txJson` (passed as a JSON string) or the `txBlobUnsigned`
   - Returns `{txBlob, hash}` where `txBlob` is the signed hex blob.

4. **Submit signed** — `xrpl_tx_submit_signed`:
   - `txBlobSigned` from step 3
   - Optional `waitForValidation=true` to confirm inclusion in a validated ledger
   - Returns `{engineResult, txHash, validated, ledgerIndex}`

Surface `engineResult` to the user verbatim — rippled errors like `tecUNFUNDED_PAYMENT`, `tecPATH_DRY`, `tefMAX_LEDGER` are the actual diagnostic.

## Critical rules

- **Never** ask the user to paste a seed, mnemonic, secret numbers, or passphrase into the chat. Those belong in the signer plugin's keystore (passphrase in ENV `XRPL_SIGNER_PASSPHRASE`).
- **Never** call write tools without showing `humanSummary` first.
- **Never** retry a stale prepare blob — `LastLedgerSequence` expires fast. Re-run `*_prepare` for a fresh window.
- If `xrpl-signer` is not installed, tell the user to install it (`/plugin install xrpl-signer@staticbit-xrpl-mcp`) before attempting write flow.

## When this skill is NOT for you

- The user explicitly says "use local XRPL" or "without going to the server" — use `xrpl-local-operations` skill instead.
- The user is only managing wallets (no read of ledger, no sending) — `xrpl-wallet-operations` skill in the signer plugin handles that.

## Example

> User: "Send 10 XRP from main wallet to rN7n7otQDd6FczFgLdhmKfNVrPBcA... on mainnet."
>
> 1. `mcp__plugin_xrpl-cloud_xrpl-cloud__xrpl_payment_prepare(network=mainnet, account=<main's address>, destination=rN7n..., amount=10000000)`
> 2. Show: "Payment: 10 XRP (10 000 000 drops) from <main short...> to rN7n.... Fee 12 drops. Expires at ledger 104XXXXXX. Confirm?"
> 3. Wait for explicit "yes".
> 4. `mcp__plugin_xrpl-signer_xrpl-signer__xrpl_sign(name=main, transaction=<the txJson>)`
> 5. `mcp__plugin_xrpl-cloud_xrpl-cloud__xrpl_tx_submit_signed(txBlobSigned=<txBlob>, waitForValidation=true)`
> 6. Report engineResult and tx hash.
