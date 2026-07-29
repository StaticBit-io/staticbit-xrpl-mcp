>  🌐 **Language**: **English** | [Русский](README.ru.md)

# xrpl-cloud plugin

A lightweight HTTP MCP client for our cloud XRPL server at `xrpl.mcp.staticbit.ai`. No binaries, no local processes — Claude Code authenticates to the server via **OAuth 2.1** (run `/mcp` once to sign in).

## When to choose this plugin

- **Cowork / 24/7 routines** — a server-side agent must run without depending on your machine.
- **Mobile / lightweight** — you don't want to download a ~100 MB local server.
- **Multi-device** — sign in via OAuth on each device; a single XRPL configuration lives on the server.

If you are privacy-sensitive (you don't want the cloud server admin to see traffic to XRPL nodes), look at `xrpl-local` instead.

## Installation

```
/plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
/plugin install xrpl-cloud@staticbit-xrpl-mcp
```

### Authentication (OAuth 2.1)

The server is protected by OAuth; only accounts on the **allow-list** can sign in — ask the `xrpl.mcp.staticbit.ai` admin to add your account. No bearer/ENV needs to be set. Then, in Claude Code:

```
/mcp
```

Complete the browser sign-in to `auth.mcp.staticbit.ai` — Claude Code will perform dynamic client registration, store the token, and refresh it automatically.

## Verification

```
/mcp
```
```
xrpl-cloud  https://xrpl.mcp.staticbit.ai/mcp (HTTP)  ✓ Connected
```

All <!-- toolcount:xrpl -->116<!-- /toolcount:xrpl --> tools are available as `mcp__plugin_xrpl-cloud_xrpl-cloud__*`. Full parameter-level reference: [`docs/TOOLS.generated.md`](../../docs/TOOLS.generated.md). Categories at a glance (with representative tools):
- **read** (server / account / ledger): `server_info`, `server_state`, `server_definitions`, `manifest`, `fee`, `ledger`, `tx_lookup`, `account_{info,lines,tx,tx_since,offers,objects,nfts,mpts,mpt_issuances,vaults,credentials,permissioned_domains,escrows,bridges,did,loans,loan_brokers}`, `xrp_balance`, `book_offers`, `gateway_balances`, `ripple_path_find`
- **read** (XLS-aware): `amm_info`, `nft_{buy,sell}_offers`, `signer_list_status`, `tx_simulate`, `path_find_*`, `hash_credential`
- **prepare** (DEX / payments / accounts): `payment_prepare`, `trustset_prepare`, `offer_create_prepare`, `offer_cancel_prepare`, `account_set_prepare`, `set_regular_key_prepare`, `signer_list_set_prepare`, `deposit_preauth_prepare`, `ticket_create_prepare`, `account_delete_prepare`, `clawback_prepare`, `trustline_freeze_prepare`, `delegate_set_prepare`, `batch_prepare`, `tx_prepare_generic`
- **prepare** (AMM): `amm_create_prepare`, `amm_deposit_prepare`, `amm_withdraw_prepare`, `amm_vote_prepare`, `amm_bid_prepare`, `amm_clawback_prepare`, `amm_delete_prepare`
- **prepare** (NFT): `nft_mint_prepare`, `nft_burn_prepare`, `nft_modify_prepare`, `nft_create_offer_prepare`, `nft_accept_offer_prepare`, `nft_cancel_offer_prepare`
- **prepare** (Vault): `vault_create_prepare`, `vault_set_prepare`, `vault_deposit_prepare`, `vault_withdraw_prepare`, `vault_clawback_prepare`, `vault_delete_prepare`
- **prepare** (Loan + LoanBroker): `loan_set_prepare`, `loan_manage_prepare`, `loan_pay_prepare`, `loan_delete_prepare`, `loan_broker_{set,delete,cover_deposit,cover_withdraw,cover_clawback}_prepare`
- **prepare** (Credentials / DID / Oracle / MPT / PermissionedDomain): `credential_{create,accept,delete}_prepare`, `did_{set,delete}_prepare`, `oracle_{set,delete}_prepare`, `mpt_issuance_{create,destroy,set}_prepare`, `mpt_authorize_prepare`, `permissioned_domain_{set,delete}_prepare`
- **prepare** (Escrow / Checks / PaymentChannel): `escrow_{create,finish,cancel}_prepare`, `check_{create,cash,cancel}_prepare`, `payment_channel_{create,fund,claim}_prepare`
- **prepare** (xChain bridges): `xchain_{create_bridge,modify_bridge,commit,claim,create_claim_id,account_create_commit,add_account_create_attestation,add_claim_attestation}_prepare`
- **submit / decode**: `tx_submit_signed`, `tx_decode_blob`, `tx_explain`, `tx_preflight`, `subscribe`, `unsubscribe`, `path_find_close`

## Signing transactions

This plugin **only** proxies to the server and holds **no** keys. To actually submit transactions, install `xrpl-signer` alongside it:

```
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

Cloud does `prepare` → signer (locally) does `sign` → cloud does `submit_signed`. See each plugin's README and their skills for details.

## Security

- The cloud server **never** receives a seed/private key — all write tools accept only a signed blob.
- Authentication is OAuth 2.1: the server validates short-lived JWTs from `auth.mcp.staticbit.ai`; only allow-listed accounts can sign in. Failed attempts are sent to the VPS admin via Telegram.
- HTTPS-only.
