---
name: xrpl-wallet-operations
description: Use this skill when the user wants to manage XRPL wallets (create, import, list, remove, backup) or sign transactions locally. Recognizes phrases like "create a new XRPL wallet", "import seed", "import mnemonic", "import Xumm secret numbers", "import from text passphrase", "list my XRPL wallets", "back up wallet seed", "sign this transaction", "multi-sign transaction", "combine signatures". Routes through the offline stdio `xrpl-signer` MCP. Pairs with `xrpl-cloud` or `xrpl-local` for the prepare/submit halves of write flows.
---

# XRPL wallet & signing — local, offline, keystore-backed

The `xrpl-signer` plugin runs a stdio subprocess that does **only** cryptography — no network at all. It holds an encrypted keystore (PBKDF2 + AES-256-GCM) of XRPL seeds and exposes <!-- toolcount:xrpl-signer -->15<!-- /toolcount:xrpl-signer --> tools for managing them and signing transactions.

Tool prefix: `mcp__plugin_xrpl-signer_xrpl-signer__xrpl_*`.

## When this skill applies

- User wants to **create a new XRPL wallet** (`xrpl_wallet_generate`).
- User wants to **import** an existing wallet (5 different formats — see below).
- User wants to **list / look up / remove / back up** wallets in the keystore.
- User wants to **sign** a prepared transaction (single-sign, multi-sign, or combine).

## Five ways to import a wallet

The signer SDK supports five distinct entropy sources — use the one matching how the user describes their secret:

| User says... | Tool | Required input |
|---|---|---|
| "Here's my seed `sEd...`" / "`sn...`" | `xrpl_wallet_import_seed` | `seed` |
| "BIP39 mnemonic" / "12 words" / "24 words" | `xrpl_wallet_import_mnemonic` | `mnemonic`, opt. `derivationPath`, `bip39Passphrase` |
| "Xumm secret numbers" / "8 groups of 6 digits" | `xrpl_wallet_import_xumm` | `secretNumbers` (string with 8 groups) |
| "Recreate from a passphrase" / "brain wallet" / "arbitrary text" | `xrpl_wallet_import_text` | `text`, opt. `salt`, `kdf=sha256\|pbkdf2` |
| "Generate a new one for me" | `xrpl_wallet_generate` | `name`, opt. `algorithm` |

**Warn** the user about `xrpl_wallet_import_text` with short/low-entropy input — a short phrase is brute-forceable.

## Critical rules — NEVER let the secret leak into the chat

- **NEVER** ask the user to paste the seed/mnemonic/secret numbers/passphrase into the chat directly. Instead:
  - Direct them to one of the import tools, then prompt them to pass the secret as a tool argument (which they paste directly into the tool input field on the UI side — Claude Code masks/handles tool inputs more carefully than chat text).
  - If the user already pasted the secret into the chat by mistake — **acknowledge the mistake, suggest rotating** (generating a new wallet and moving funds), and don't echo the secret back.
- **NEVER** call `xrpl_wallet_export` with `confirm=true` unless the user explicitly asked to back up the seed in plaintext — the seed will appear in the conversation transcript.
- **NEVER** suggest committing the keystore file or passphrase to git / cloud sync without encryption awareness.

## Signing flow (used by xrpl-cloud / xrpl-local skills)

When the user wants to send a transaction, you're called from the prepare/submit skill:

1. Caller skill prepares the transaction via cloud or local MCP and gets `txJson` + `txBlobUnsigned`.
2. Caller skill shows `humanSummary` and waits for user confirmation.
3. Caller skill invokes `xrpl_sign(name=<wallet alias>, transaction=<txJson or blob>)` from this skill.
4. You return `{txBlob, hash}`.
5. Caller skill submits the signed blob.

For **multi-sign** (multi-signer accounts):

- Each signer calls `xrpl_sign_multi(name=<their wallet>, transaction=<tx>, signingFor=<account>?)` independently. Returns a partial-signed blob.
- After collecting partial blobs from all required signers, call `xrpl_sign_combine(signedBlobs=[blob1, blob2, ...])` to produce the final signed blob.

## Wallet management quick reference

| User intent | Tool | Notes |
|---|---|---|
| Create new wallet | `xrpl_wallet_generate` | Always offer `algorithm` choice (ed25519 default) |
| List all wallets | `xrpl_wallet_list` | Returns metadata only (name, address, algorithm, createdAt) |
| Inspect one wallet | `xrpl_wallet_address` | Returns metadata for one name |
| Delete a wallet | `xrpl_wallet_remove` | Irreversible — confirm the user has a backup |
| Back up seed | `xrpl_wallet_export` with `confirm=true` | Seed appears in transcript; recommend backup via password manager, NOT chat log |

## Pairing with prepare/submit servers

This plugin is half the picture — alone it can't read the ledger or submit. Pair with:

- **`xrpl-cloud`** for read/prepare/submit via the StaticBit HTTPS server. Smaller, no local server bin.
- **`xrpl-local`** for fully-local read/prepare/submit. Bigger (~110 MB binary), no cloud dependency.

The user can install both side by side; this signer works with either.

## Example: create wallet + send testnet payment

> User: "Generate a testnet wallet called 'test1', then send 5 XRP to rDestination."
>
> 1. `mcp__plugin_xrpl-signer_xrpl-signer__xrpl_wallet_generate(name=test1, algorithm=ed25519)`
> 2. Report: "Created wallet test1, address rNewlyGenerated. Fund it via https://xrpl.org/xrp-testnet-faucet.html and tell me when it's funded so we can proceed with the payment."
> 3. After user confirms funding — fall into the prepare/submit flow (via xrpl-cloud or xrpl-local skill as appropriate):
>    - cloud-or-local prepare with `account=<test1's address>`, `destination=rDestination`, `amount=5000000`
>    - Show summary, ask confirm
>    - `xrpl_sign(name=test1, transaction=<txJson>)`
>    - cloud-or-local submit_signed
> 4. Report tx hash and engineResult.
