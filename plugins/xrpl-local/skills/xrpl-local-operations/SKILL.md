---
name: xrpl-local-operations
description: Use this skill when the user wants XRPL operations to run fully on their own machine without going through the StaticBit cloud server — for privacy, no-server-dependency, or to use custom rippled endpoints. Recognizes phrases like "use local XRPL", "without the cloud server", "directly via xrplcluster", "offline-first XRPL", and ordinary requests ("check balance", "send XRP", "create trustline") WHEN the cloud plugin is not installed. Routes calls through the local stdio `xrpl-local` MCP. For signing, expects the `xrpl-signer` plugin to be installed alongside.
---

# XRPL operations via local stdio MCP

The `xrpl-local` plugin runs the same 21 tools as `xrpl-cloud`, but on a subprocess launched by Claude Code on the user's machine. The WebSocket to public XRPL nodes (`xrplcluster.com`, `s.altnet.rippletest.net`, or whatever the user configured) is established from the user's process. **There is no StaticBit-hosted middleman.**

Tool prefix: `mcp__plugin_xrpl-local_xrpl-local__xrpl_*`.

## When to prefer this skill over xrpl-cloud-operations

- The user explicitly mentioned "local", "offline", "without server", "privacy".
- The user's machine should be the only network observer for their XRPL traffic.
- Only `xrpl-local` is installed (no `xrpl-cloud`).
- The user is testing against a non-public XRPL endpoint (their own rippled, a custom sidechain) — they can override via `XRPL_LOCAL_MAINNET_URL` etc.

If both `xrpl-cloud` and `xrpl-local` are installed and the user didn't specify — ask which to use, or default to `xrpl-local` if you sense privacy concerns.

## Read & write operations

Same as cloud — see `xrpl-cloud-operations` skill for the tool catalog and the strict prepare → sign → submit_signed flow. The **only** difference is the MCP namespace:

| Cloud | Local |
|---|---|
| `mcp__plugin_xrpl-cloud_xrpl-cloud__xrpl_payment_prepare` | `mcp__plugin_xrpl-local_xrpl-local__xrpl_payment_prepare` |
| `mcp__plugin_xrpl-cloud_xrpl-cloud__xrpl_tx_submit_signed` | `mcp__plugin_xrpl-local_xrpl-local__xrpl_tx_submit_signed` |
| … (same 21 tools) | … (same 21 tools) |

Signing path is identical — both flows use `mcp__plugin_xrpl-signer_xrpl-signer__xrpl_sign`.

## Critical rules

- **Never** ask the user for seeds / passphrase in chat.
- **Never** call write tools without `humanSummary` confirmation.
- If `xrpl-signer` is not installed, prompt the user to install it before any write operation.
- Don't mix namespaces in the middle of a flow — once you used `xrpl-local` for prepare, you must use `xrpl-local` for submit_signed (and vice-versa). The signed blob is network-independent but the sequence/lastLedgerSequence are not; the safest pattern is one server for the whole flow.

## When this skill is NOT for you

- The user explicitly asks for cloud / Cowork / 24-7 routine — use `xrpl-cloud-operations`.
- The user only manages wallets — `xrpl-wallet-operations` in the signer plugin handles that.

## Example (offline-first)

> User: "Without involving any server, check my XRPL balance on rEb8TK3gBgk5auZkwc6sHnwrGVJH8DuaLh on mainnet."
>
> 1. `mcp__plugin_xrpl-local_xrpl-local__xrpl_xrp_balance(network=mainnet, account=rEb8...)`
> 2. Report the balance.
>
> Under the hood: Claude Code's local `xrpl-local` subprocess opened a WebSocket to `xrplcluster.com` (or whatever `XRPL_LOCAL_MAINNET_URL` points to), no traffic touched the StaticBit VPS.
