>  🌐 **Language**: **English** | [Русский](README.ru.md)

# xrpl-cloud plugin

A lightweight HTTP MCP client for our cloud XRPL server at `xrpl-mcp.staticbit.io`. No binaries, no local processes — Claude Code authenticates to the server via **OAuth 2.1** (run `/mcp` once to sign in).

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

The server is protected by OAuth; only accounts on the **allow-list** can sign in — ask the `xrpl-mcp.staticbit.io` admin to add your account. No bearer/ENV needs to be set. Then, in Claude Code:

```
/mcp
```

Complete the browser sign-in to `auth.mcp.staticbit.io` — Claude Code will perform dynamic client registration, store the token, and refresh it automatically.

## Verification

```
/mcp
```
```
xrpl-cloud  https://xrpl-mcp.staticbit.io/mcp (HTTP)  ✓ Connected
```

All 21 tools are available as `mcp__plugin_xrpl-cloud_xrpl-cloud__*`:
- read: server_info, fee, ledger, tx_lookup, account_{info,lines,tx,offers,objects}, xrp_balance, book_offers, amm_info
- prepare: payment, trustset, offer_create, offer_cancel, amm_deposit, amm_withdraw
- submit/utils: tx_submit_signed, tx_decode_blob, tx_prepare_generic

## Signing transactions

This plugin **only** proxies to the server and holds **no** keys. To actually submit transactions, install `xrpl-signer` alongside it:

```
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

Cloud does `prepare` → signer (locally) does `sign` → cloud does `submit_signed`. See each plugin's README and their skills for details.

## Security

- The cloud server **never** receives a seed/private key — all write tools accept only a signed blob.
- Authentication is OAuth 2.1: the server validates short-lived JWTs from `auth.mcp.staticbit.io`; only allow-listed accounts can sign in. Failed attempts are sent to the VPS admin via Telegram.
- HTTPS-only.
