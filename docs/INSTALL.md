> 🇷🇺 [Прочесть на русском](ru/INSTALL.md)

# INSTALL — installing and connecting the XRPL plugins

Self-contained step-by-step guide. Share a link to this file with a new user — they should be able to go from a clean Claude Code install to their first signed XRPL transaction without reading any other document.

## Table of contents

1. [Prerequisites](#1-prerequisites)
2. [Choosing a scenario — which plugins to install](#2-choosing-a-scenario)
3. [Getting a GitHub PAT](#3-getting-a-github-pat)
4. [Adding the marketplace](#4-adding-the-marketplace)
5. [Installing the plugins](#5-installing-the-plugins)
6. [Getting access — admin allow-list and secrets](#6-getting-access)
7. [Setting environment variables](#7-setting-environment-variables)
8. [Restart Claude Code](#8-restart-claude-code)
9. [Verify the connection — and OAuth login for cloud](#9-verify-the-connection)
10. [First wallet + first transaction (testnet)](#10-first-wallet--first-transaction-testnet)
11. [Day-two — updates, backups, rotation](#11-day-two)
12. [Removal](#12-removal)
13. [Troubleshooting](#13-troubleshooting)

---

## 1. Prerequisites

| What | Why | How to check |
|---|---|---|
| Claude Code 2.1+ | `/plugin` commands and plugin MCP support were added in this version | `claude --version` |
| Node.js 18+ | `xrpl-local` and `xrpl-signer` launch the .NET binary via a Node launcher | `node --version` |
| ~600 MB of disk | self-contained binaries for 5 platforms bundled inside the plugins | — |
| GitHub PAT (Personal Access Token) | the marketplace repo is private — read access required | see §3 |

> If you're on Claude Code < 2.1 — update via `claude update` or the [official updater](https://claude.com/claude-code).

---

## 2. Choosing a scenario

The marketplace contains **three independent** plugins. You can install them in any combination. Pick based on the scenario:

| What you want | Install | Why |
|---|---|---|
| **Hosted setup**: simpler, lighter, Cowork agents available — but the cloud server sees request metadata | `xrpl-cloud` + `xrpl-signer` | cloud handles prepare/submit, signer signs locally |
| **Privacy-first**: nothing goes through our VPS; the WebSocket to XRPL nodes opens from your machine | `xrpl-local` + `xrpl-signer` | the local server does what cloud does but locally |
| **Read-only via cloud**: dashboards, balance monitors, no signing | `xrpl-cloud` | reads only without the signer |
| **Read-only locally** | `xrpl-local` | same without cloud |
| **Wallet management only** (generate/import/back up, no network) | `xrpl-signer` | offline keystore on its own |

> If unsure — go with the first option (cloud + signer). It's the lightest combination and you can extend or swap later without losing the wallets in your keystore.

### Why signing is always local

The cloud server **never** accepts a seed or a private key — all write tools require an already-signed hex blob. Signing happens **only** in `xrpl-signer` on your machine, through a passphrase-encrypted keystore. This is an architectural invariant, not a scenario.

### What the tools cover (at a glance)

The walkthrough below uses the basic `xrpl_fee` / `xrpl_payment_prepare` / `xrpl_sign` / `xrpl_tx_submit_signed` tools as a hello-world, but the surface is much bigger — **<!-- toolcount:total -->131<!-- /toolcount:total --> tools** across `xrpl-cloud` / `xrpl-local` (<!-- toolcount:xrpl -->116<!-- /toolcount:xrpl -->) and `xrpl-signer` (<!-- toolcount:xrpl-signer -->15<!-- /toolcount:xrpl-signer -->). Categories you can reach with the same `prepare → sign → submit` flow:

| Domain | Representative tools |
|---|---|
| **Payments & DEX** | `payment_prepare`, `offer_create_prepare`, `offer_cancel_prepare`, `trustset_prepare`, `book_offers`, `ripple_path_find` |
| **AMM** | `amm_create_prepare`, `amm_deposit_prepare`, `amm_withdraw_prepare`, `amm_vote_prepare`, `amm_bid_prepare`, `amm_clawback_prepare`, `amm_delete_prepare`, `amm_info` |
| **NFT** | `nft_mint_prepare`, `nft_burn_prepare`, `nft_modify_prepare`, `nft_create_offer_prepare`, `nft_accept_offer_prepare`, `nft_cancel_offer_prepare`, `nft_buy_offers`, `nft_sell_offers` |
| **Vault** (XLS-65) | `vault_create_prepare`, `vault_set_prepare`, `vault_deposit_prepare`, `vault_withdraw_prepare`, `vault_clawback_prepare`, `vault_delete_prepare` |
| **MPT** (XLS-33) | `mpt_issuance_{create,destroy,set}_prepare`, `mpt_authorize_prepare`, `account_mpts`, `account_mpt_issuances` |
| **Loan / Loan Broker** | `loan_set_prepare`, `loan_manage_prepare`, `loan_pay_prepare`, `loan_delete_prepare`, `loan_broker_*_prepare` |
| **Credentials** (XLS-70) | `credential_{create,accept,delete}_prepare`, `hash_credential` |
| **DID** (XLS-40) | `did_set_prepare`, `did_delete_prepare` |
| **Oracle** (XLS-47) | `oracle_set_prepare`, `oracle_delete_prepare` |
| **Permissioned Domains** | `permissioned_domain_set_prepare`, `permissioned_domain_delete_prepare`, `deposit_preauth_prepare` |
| **Escrow / Checks / PayChan** | `escrow_{create,finish,cancel}_prepare`, `check_{create,cash,cancel}_prepare`, `payment_channel_{create,fund,claim}_prepare` |
| **xChain bridges** (XLS-38) | `xchain_{create_bridge,modify_bridge,commit,claim,create_claim_id,account_create_commit,add_account_create_attestation,add_claim_attestation}_prepare` |
| **Account admin** | `account_set_prepare`, `set_regular_key_prepare`, `signer_list_set_prepare`, `ticket_create_prepare`, `account_delete_prepare`, `delegate_set_prepare`, `clawback_prepare`, `trustline_freeze_prepare` |
| **Batch** | `batch_prepare`, `tx_prepare_generic` |
| **Reads** (XLS-aware) | `account_{info,lines,tx,objects,nfts,mpts,vaults,credentials,permissioned_domains,escrows,bridges,did,loans,loan_brokers}`, `server_{info,state,definitions}`, `manifest`, `ledger`, `gateway_balances`, `tx_{lookup,simulate,preflight,decode_blob,explain}` |
| **Subscriptions** | `subscribe`, `unsubscribe`, `path_find_{create,status,close}` |
| **Signer / wallet** | `wallet_{generate,generate_mnemonic,import_seed,import_mnemonic,import_text,import_xumm,export,export_index,address,derive_address,list,remove}`, `sign`, `sign_multi`, `sign_combine` |

Full per-parameter reference is auto-generated: [`docs/TOOLS.generated.md`](TOOLS.generated.md). Category breakdown with usage notes lives in [`plugins/xrpl-cloud/README.md`](../plugins/xrpl-cloud/README.md). The agent picks the right tool from `xrpl-cloud-operations` / `xrpl-local-operations` / `xrpl-wallet-operations` skills — you describe the intent in natural language, the skill maps it to a tool.

---

## 3. Getting a GitHub PAT

The marketplace repo is **private**, so Claude Code will prompt for a token on the first `marketplace add`. Generate the PAT ahead of time:

### 3.1 Generate a PAT

1. Open [github.com/settings/tokens](https://github.com/settings/tokens).
2. **Generate new token** → **Fine-grained personal access token** (recommended) or **Tokens (classic)**.

**If fine-grained:**
- Token name: `staticbit-xrpl-mcp-readonly`
- Expiration: 90 days (or longer — your call)
- Resource owner: `StaticBit-io`
- Repository access: **Only select repositories** → `staticbit-xrpl-mcp`
- Permissions → Repository permissions:
  - **Contents**: Read-only
  - **Metadata**: Read-only (auto)
- **Generate token**. Copy and save it (shown **once**).

**If classic:**
- Note: `staticbit-xrpl-mcp-readonly`
- Expiration: 90 days
- Scopes: just `repo` (Full control of private repositories — nothing else needed)
- **Generate token**. Copy it.

### 3.2 Where to put the token

You'll need the PAT **twice**:

1. **For `claude plugin marketplace add`** — Claude Code will store it in its credential store. No need to put it in ENV manually, just enter it when the CLI asks (§4).
2. **Optional** — for `git clone` if you ever want to work with the marketplace by hand.

Save the token in your password manager (1Password / Bitwarden / KeePass) — useful during rotation.

---

## 4. Adding the marketplace

```powershell
claude plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
```

Claude Code will ask for the token — paste the PAT from §3.

**Verify:**

```powershell
claude plugin marketplace list
```

Expected:
```
staticbit-xrpl-mcp  https://github.com/StaticBit-io/staticbit-xrpl-mcp  ✔ enabled
```

---

## 5. Installing the plugins

Pick the commands matching your scenario from §2:

### Cloud + signing (recommended default)
```powershell
claude plugin install xrpl-cloud@staticbit-xrpl-mcp
claude plugin install xrpl-signer@staticbit-xrpl-mcp
```

### Local + signing (privacy-first)
```powershell
claude plugin install xrpl-local@staticbit-xrpl-mcp
claude plugin install xrpl-signer@staticbit-xrpl-mcp
```

### Cloud only (read-only)
```powershell
claude plugin install xrpl-cloud@staticbit-xrpl-mcp
```

### Signer only
```powershell
claude plugin install xrpl-signer@staticbit-xrpl-mcp
```

### Check the list
```powershell
claude plugin list
```

Every installed plugin must show `Status: ✔ enabled`.

---

## 6. Getting access

### 6.1 Allow-list — only for `xrpl-cloud`

If you're installing `xrpl-cloud`, the cloud server (`xrpl-mcp.staticbit.io`) is protected by **OAuth 2.1** against `auth.mcp.staticbit.io`. There is **no bearer token and no ENV var** to set — instead, your account must be on the server **allow-list**. Ask the admin of `xrpl-mcp.staticbit.io` to add you.

Once you're on the allow-list, you log in interactively from Claude Code via `/mcp` (see §9) — the browser flow takes you through `auth.mcp.staticbit.io`, Claude Code performs dynamic client registration, stores the resulting token and refreshes it automatically. Nothing to copy or paste, nothing to save in a password manager.

If your access is later revoked (admin disables your account), your tokens are invalidated immediately and `/mcp` will start failing — re-request access from the admin.

### 6.2 `XRPL_SIGNER_PASSPHRASE` — only for `xrpl-signer`

If you're installing `xrpl-signer`, **choose the passphrase yourself** — it encrypts the local keystore. Nobody but you knows it, not even the admin.

**Requirements:**
- At least 16 characters (32+ is better).
- Not a dictionary word (a brute-force attack via PBKDF2 600k iter would be slow, but not impossible against a dictionary).
- Unique — do not reuse passwords from other services.

**Generate a strong one:**
```powershell
# Windows / PowerShell — 32 random base64 chars
[Convert]::ToBase64String((1..24 | ForEach-Object { Get-Random -Maximum 256 }))
```
```bash
# Linux / macOS
openssl rand -base64 24
```

**Save it in your password manager immediately.** Losing the passphrase = losing every wallet in the keystore (unless you back up seeds separately).

---

## 7. Setting environment variables

Only `xrpl-signer` needs an ENV variable — its keystore passphrase. `xrpl-cloud` needs **none**: it authenticates via OAuth at login time (§9), not through an env var. ENV variables are read by the plugins when Claude Code starts. After `setx` you need a **full restart of Claude Code**, not just a new PowerShell window.

### Windows (PowerShell)

```powershell
# For xrpl-signer:
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE", "<your-passphrase>", "User")
```

### Windows (cmd) — alternative
```cmd
setx XRPL_SIGNER_PASSPHRASE "<your-passphrase>"
```

### macOS / Linux (bash or zsh)
```bash
cat >> ~/.bashrc <<'EOF'
export XRPL_SIGNER_PASSPHRASE="<your-passphrase>"
EOF
source ~/.bashrc
# zsh users — ~/.zshrc instead of ~/.bashrc
```

### Alternative for signer — passphrase in a file

If you don't want the passphrase directly in ENV, put it in a file:

```powershell
"<your-passphrase>" | Out-File -Encoding ASCII -NoNewline "$env:USERPROFILE\.staticbit-xrpl-signer\passphrase.txt"
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE_FILE", "$env:USERPROFILE\.staticbit-xrpl-signer\passphrase.txt", "User")
# And DO NOT set XRPL_SIGNER_PASSPHRASE — signer reads the first line of the file
```

```bash
mkdir -p ~/.staticbit-xrpl-signer
echo -n "<your-passphrase>" > ~/.staticbit-xrpl-signer/passphrase.txt
chmod 600 ~/.staticbit-xrpl-signer/passphrase.txt
echo 'export XRPL_SIGNER_PASSPHRASE_FILE="$HOME/.staticbit-xrpl-signer/passphrase.txt"' >> ~/.bashrc
source ~/.bashrc
```

### Verify
```powershell
[Environment]::GetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE", "User")
```
```bash
echo $XRPL_SIGNER_PASSPHRASE
```

If you get an empty string back — the variable isn't set. Check the scope (`User` vs `Machine`) and restart your shell.

---

## 8. Restart Claude Code

After changing ENV or installing/updating plugins, you **must** fully restart Claude Code:

```
/exit
```
And launch it again (double-click / `claude` in a shell).

Plugins load when the process starts. ENV variables are also read once at signer-subprocess startup. Without a restart, `${XRPL_SIGNER_PASSPHRASE}` will be substituted with whatever value was set before your change.

---

## 9. Verify the connection

In a new Claude Code session:

```
/mcp
```

Depending on what you installed, you'll see one or more lines:

```
xrpl-cloud   https://xrpl-mcp.staticbit.io/mcp (HTTP)   ⚠ Needs login
xrpl-local   node …/bin/server.js                       ✓ Connected
xrpl-signer  node …/bin/signer.js                       ✓ Connected
```

### OAuth login for `xrpl-cloud`

If you installed `xrpl-cloud`, the first time you'll see it asking you to authenticate. Still inside `/mcp`, pick `xrpl-cloud` and follow the login prompt — a browser window opens against `auth.mcp.staticbit.io`. Sign in there; Claude Code performs dynamic client registration, stores the token and refreshes it automatically going forward. After login `/mcp` shows:

```
xrpl-cloud   https://xrpl-mcp.staticbit.io/mcp (HTTP)   ✓ Connected
```

If login is refused, your account is not on the allow-list — ask the admin to add you (§6.1).

All must end up `✓ Connected`. If `disconnected` or `failed` — see §13.

### Verify tools

In the chat, ask Claude to make a read-only call:
```
What is the current XRPL fee on mainnet?
```

The agent will pick the `xrpl-cloud-operations` (or `xrpl-local-operations`) skill and call `xrpl_fee`. It should return `base_fee: 10` (drops) and a ledger sequence.

---

## 10. First wallet + first transaction (testnet)

A full hello-world on testnet — nothing of value moves, you can learn safely.

### 10.1 Generate a testnet wallet

In the Claude Code chat:
```
Generate a new XRPL wallet named test1, ed25519
```

The agent will call `mcp__plugin_xrpl-signer_xrpl-signer__xrpl_wallet_generate` and return:
```
{
  "name": "test1",
  "address": "rN7n7otQDd6FczFgLdhmKfNVrPBcA...",
  "publicKey": "ED...",
  "algorithm": "ed25519"
}
```

**Save the address.** The seed is encrypted in the keystore and not returned. If you want a plaintext backup (for recovery outside the signer):
```
Export the seed of wallet test1, I want to save it in my password manager. confirm=true
```

Copy the resulting seed into your password manager **immediately**, and delete that piece of the chat if you can. The seed will appear in the transcript.

### 10.2 Fund the wallet via the testnet faucet

Open [xrpl.org/xrp-testnet-faucet.html](https://xrpl.org/xrp-testnet-faucet.html). Enter the address from §10.1, hit **Generate**. The faucet credits 1000 testnet XRP within seconds.

### 10.3 Check the balance

```
What is the XRP balance of <your-address> on testnet?
```

The agent will call `xrpl_xrp_balance` (via cloud or local). Should show `1000` or so.

### 10.4 Create a second wallet (recipient)
```
Generate XRPL wallet test2
```

### 10.5 Send a testnet payment

```
Send 5 XRP from test1 to <test2-address> on testnet
```

The agent:
1. Calls `*_payment_prepare(network=testnet, account=<test1>, destination=<test2>, amount=5000000)`.
2. Shows a human-readable summary:
   ```
   Payment from rTest1... to rTest2...: 5000000 drops XRP (=5 XRP). Fee 12 drops. Expires at ledger 17638xxx. Confirm?
   ```
3. Asks for confirmation — **read it carefully** (this is your last chance to catch a typo).
4. After your "yes", calls `xrpl_sign(name=test1, transaction=<txJson>)`.
5. Then `xrpl_tx_submit_signed(txBlobSigned=<blob>, waitForValidation=true)`.
6. Returns `engineResult: tesSUCCESS` and a tx hash.

### 10.6 Look up the transaction
```
Find transaction <hash> on testnet
```

`xrpl_tx_lookup` returns the full transaction with metadata, `validated: true`. Congrats — your first XRPL transaction.

---

## 11. Day-two

### 11.1 Update the marketplace

When I (or you) push a new marketplace version:

```
claude plugin marketplace update staticbit-xrpl-mcp
claude plugin list
```

Updated plugins will be marked as having a newer version.

### 11.2 Update a single plugin

```
claude plugin update xrpl-signer@staticbit-xrpl-mcp
```

> ⚠️ The `<plugin>@<marketplace>` form is required. The short form
> `claude plugin update xrpl-signer` fails with `Plugin not found` in
> the current Claude Code CLI — the resolver doesn't fall back to the
> marketplace when the suffix is omitted.

Restart Claude Code after updating.

### 11.3 Back up a seed

```
Export seed of test1, confirm=true
```

Copy the seed into your password manager. Erase that fragment of chat if you can.

Alternative — back up the entire keystore file (encrypted, useless without the passphrase):
```powershell
Copy-Item "$env:USERPROFILE\.staticbit-xrpl-signer\keystore.json" "<somewhere-in-encrypted-storage>"
```

### 11.4 Rotating the passphrase

**The current signer doesn't support on-the-fly re-encryption.** Recipe:
1. Use `xrpl_wallet_export` (with `confirm=true`) to dump **every** seed from every wallet. Save in a temporary secure list.
2. Delete `~/.staticbit-xrpl-signer/keystore.json`.
3. Set the new `XRPL_SIGNER_PASSPHRASE`.
4. Restart Claude Code.
5. Re-import every seed (`xrpl_wallet_import_seed`).
6. Erase the temporary seed list.

### 11.5 Cloud access — there's no bearer to rotate

With OAuth there is no static secret to leak or rotate. Tokens are short-lived RS256 JWTs that Claude Code refreshes automatically. If you suspect a device is compromised, ask the admin to disable your account on the allow-list — that revokes your refresh tokens immediately on every device — then re-enable it and log in again via `/mcp`. You can also clear the stored token locally: `/mcp` → select `xrpl-cloud` → clear authentication, then log in again.

### 11.6 Adding another client (new MCP user)

If a colleague wants to connect to the same cloud server — ask the admin to add **their** account to the allow-list. Each person logs in via their own browser session at `auth.mcp.staticbit.io`; access is per-account and granted or revoked independently. There's no shared secret to hand out.

### 11.7 Installing on a second PC / migration

Two scenarios — pick by whether you need the **same** wallets on the new PC.

#### A. New PC, new wallets (from scratch)

Just walk through INSTALL.md §1–§10 on the new PC. For `xrpl-cloud` you simply log in again via `/mcp` on the new machine (same allow-listed account, fresh browser login — there's no secret to copy over). The signer passphrase is new (or the same — your call), wallets are generated / imported through `xrpl-signer` separately. Each PC has an independent keystore.

#### B. Migrating existing wallets (same keystore)

If you have wallets on the first PC (e.g. `main`, `cold`, `dex`) and want them available on the laptop / second PC — copy `keystore.json`.

**What you migrate:**
- The file `~/.staticbit-xrpl-signer/keystore.json` (Windows: `%USERPROFILE%\.staticbit-xrpl-signer\keystore.json`).
- `XRPL_SIGNER_PASSPHRASE` — the **same** as on the first PC. AES-GCM will not decrypt the keystore with a different key — that's by design.

**What you do NOT migrate:**
- ENV variables — set them again with `[Environment]::SetEnvironmentVariable(...)`.
- The GitHub PAT — each device has its own.
- Cloud auth — nothing to copy. On the second PC just log in via `/mcp` against `auth.mcp.staticbit.io` with the same allow-listed account; Claude Code registers and stores a token per device.

**Recipe:**

1. On the first PC — install `xrpl-signer` if you haven't yet, and confirm the keystore works (`xrpl_wallet_list` returns what you expect).

2. **On the first PC** — export the keystore:
   ```powershell
   # Windows
   Copy-Item "$env:USERPROFILE\.staticbit-xrpl-signer\keystore.json" "$env:USERPROFILE\Desktop\keystore-backup.json"
   ```
   ```bash
   # macOS / Linux
   cp ~/.staticbit-xrpl-signer/keystore.json ~/Desktop/keystore-backup.json
   ```

3. Transfer the file to the second PC over a **secure channel**:
   - ✅ USB stick (encrypted or wiped right after copy)
   - ✅ `scp` / `sftp` over SSH
   - ✅ Encrypted cloud (1Password Attach, Bitwarden Send)
   - ❌ Email, plain Slack/Telegram/Discord, unencrypted cloud drive

   > The file itself is encrypted with AES-GCM under your passphrase, but its protection still depends on how strong the passphrase is. PBKDF2 600k iter makes brute-force very expensive, but not impossible against dictionary passphrases. Don't let the file roam the internet in plaintext.

4. On the second PC — walk through INSTALL.md §1–§9 **up to** §10 (don't generate new wallets!). In §6.2 — set the **same** passphrase as on the first PC, not a new one.

5. **Before** or **after** setting ENV (but before restarting Claude Code) — put the file in place:
   ```powershell
   # Windows
   New-Item -ItemType Directory -Force "$env:USERPROFILE\.staticbit-xrpl-signer" | Out-Null
   Copy-Item "<wherever-you-transferred>\keystore-backup.json" "$env:USERPROFILE\.staticbit-xrpl-signer\keystore.json"
   ```
   ```bash
   # macOS / Linux
   mkdir -p ~/.staticbit-xrpl-signer
   cp <wherever-you-transferred>/keystore-backup.json ~/.staticbit-xrpl-signer/keystore.json
   chmod 600 ~/.staticbit-xrpl-signer/keystore.json
   ```

6. Restart Claude Code (§8) → verify (§9):
   ```
   List my XRPL wallets
   ```
   The agent calls `xrpl_wallet_list` — should return **all** wallets from the first PC.

7. Delete the transferred `keystore-backup.json` from both PCs (no longer needed, originals are in place on each).

#### What happens if the passphrase doesn't match

All wallet tools (including `xrpl_sign`) on the second PC will fail with:
```
Failed to decrypt wallet 'X'. The passphrase is likely wrong
(or the keystore file is corrupted).
```

This doesn't damage the keystore — it just means the signer can't decrypt it. Set the right passphrase in ENV and restart Claude Code.

#### Syncing between PCs

The keystore is a **local file**. Changes on one PC (added a wallet, removed one, renamed one) do **not** sync automatically. If you need that:

- Easy option — re-copy the file after every change.
- More advanced — put the file in an **encrypted** sync folder (Cryptomator + Dropbox, e.g.), not plaintext cloud. The file is already encrypted with the keystore passphrase, but an extra layer doesn't hurt.
- Beware concurrent edits: both PCs change the keystore at the same time → one set of changes is lost. If you actively work from two PCs — use different wallet names (e.g. `main-desktop` and `main-laptop`) or just separate keystore files (via `XRPL_SIGNER_KEYSTORE_PATH`).

---

## 12. Removal

### 12.1 Uninstall a single plugin

```powershell
claude plugin uninstall xrpl-cloud
claude plugin uninstall xrpl-local
claude plugin uninstall xrpl-signer
```

### 12.2 Remove the whole marketplace

```powershell
claude plugin marketplace remove staticbit-xrpl-mcp
```

This removes every plugin from the marketplace too.

### 12.3 Delete the keystore (full wipe)

**Dangerous** — every wallet in it is lost unless you have seed backups.

```powershell
Remove-Item "$env:USERPROFILE\.staticbit-xrpl-signer" -Recurse
```
```bash
rm -rf ~/.staticbit-xrpl-signer
```

### 12.4 Delete the ENV variables

Only the signer passphrase lives in ENV — `xrpl-cloud` has no env var to remove (it used OAuth, not a bearer).

```powershell
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE", $null, "User")
```
```bash
# Remove the `export XRPL_SIGNER_PASSPHRASE=...` line from ~/.bashrc or ~/.zshrc
unset XRPL_SIGNER_PASSPHRASE
```

### 12.5 Revoke cloud access

For `xrpl-cloud` there's no local secret to wipe. To fully cut access: ask the admin to disable your account on the `xrpl-mcp.staticbit.io` allow-list (this revokes your refresh tokens), and clear the token Claude Code stored — `/mcp` → select `xrpl-cloud` → clear authentication.

---

## 13. Troubleshooting

### `/mcp` shows `disconnected` or `needs login` for xrpl-cloud

| Check | Command |
|---|---|
| Logged in via OAuth | `/mcp` → select `xrpl-cloud` → follow the browser login against `auth.mcp.staticbit.io` |
| Account is on the allow-list | If login is refused, ask the admin of `xrpl-mcp.staticbit.io` to add (or re-enable) your account |
| Token went stale | `/mcp` → select `xrpl-cloud` → clear authentication, then log in again |
| Server is up | `curl https://xrpl-mcp.staticbit.io/healthz` — should return `{"status":"ok"}` |
| Restarted Claude Code after install | Close fully and relaunch |

### `/mcp` shows `disconnected` for xrpl-local or xrpl-signer

| Check | How |
|---|---|
| Node.js installed | `node --version` — needs 18+ |
| Binary exists | `claude plugin list` shows the plugin path; check that `bin/<rid>/StaticBit.Xrpl.Mcp.*` contains a file for your OS |
| Passphrase is set (signer only) | `[Environment]::GetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE", "User")` |
| Antivirus isn't blocking | A self-contained .NET binary sometimes trips false positives in Defender. Add the plugin folder to exceptions |

### `Failed to decrypt wallet 'X'. The passphrase is likely wrong`

You changed `XRPL_SIGNER_PASSPHRASE` after the wallet was created. AES-GCM cannot decrypt ciphertext under a different key — by design. Options:
- Restore the old passphrase.
- If lost — the wallet is lost (that's why the keystore is encrypted). Recover from a seed backup if you have one.

### Marketplace add fails with `authentication required`

PAT wasn't saved or has expired. Remove and re-add:
```powershell
claude plugin marketplace remove staticbit-xrpl-mcp
claude plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
```
Paste a fresh PAT.

### `command not found: claude`

Claude Code isn't on PATH. Find the install and add `bin/`:
- Windows: usually `%LOCALAPPDATA%\Programs\Claude`
- macOS: `/Applications/Claude.app/Contents/MacOS/`
- Linux: wherever you unpacked the .deb / .AppImage

Or use the full path.

### Transaction returns `tecPATH_DRY` / `tecUNFUNDED_PAYMENT`

Not a plugin bug — this is a rippled-level error:
- `tecUNFUNDED_PAYMENT` — sending account doesn't have enough XRP (including reserve = 1 XRP + 0.2 XRP per owned object).
- `tecPATH_DRY` — no liquid path for a cross-currency payment. Try going through the DEX directly.
- `tefMAX_LEDGER` — prepare done long ago, the ledger window closed. Redo the prepare.
- `tem*` — malformed transaction (a plugin bug — please report).

### I want verbose logs

For cloud — ask the admin to dump them from the VPS:
```bash
ssh root@<vps> 'cd /opt/staticbit-xrpl-mcp && docker compose logs --tail 200 xrpl-mcp'
```

For local / signer — Claude Code writes subprocess stderr into its own logs. Open `/logs` (or the equivalent in your version).

### Lots of auth-failure spam in the admin Telegram chat

Most likely third-party scanners probing `.well-known/oauth-protected-resource/mcp` and similar paths. The cloud server has a filter that drops them with 404 without alerting (commit `45e267c`). If still spamming — ask the admin to update the server.

### macOS — "StaticBit.Xrpl.Mcp.Signer cannot be opened because the developer cannot be verified"

The binary in the plugin is **not signed with an Apple Developer ID** (see [docs/supply-chain.md](supply-chain.md) — signing is optional, the marketplace owner may or may not set it up). When the release workflow runs with `APPLE_*` secrets — the binary is notarized and Gatekeeper accepts it; otherwise you need to manually clear the quarantine attribute macOS attaches to downloaded files:

```bash
# Find the plugin path
claude plugin list

# Drop quarantine on the whole plugin folder
xattr -dr com.apple.quarantine ~/.claude/plugins/xrpl-signer/

# Alternative: allow a specific executable via spctl
sudo spctl --add ~/.claude/plugins/xrpl-signer/bin/osx-arm64/StaticBit.Xrpl.Mcp.Signer
```

If System Settings → Privacy & Security shows "App was blocked" — click "Open Anyway" right after the failed launch.

### Windows — SmartScreen "Windows protected your PC" or Defender blocks the .exe

Same story — binary without Authenticode signing. Options:

1. **On the SmartScreen dialog** click `More info` → `Run anyway`. One-time per binary after a plugin update.
2. **Strip Mark-of-the-Web** from the plugin folder:

   ```powershell
   $pluginDir = "$env:USERPROFILE\.claude\plugins\xrpl-signer"
   Get-ChildItem $pluginDir -Recurse | Unblock-File
   ```

3. **Add a Defender exclusion** (if it touches the single-file .NET binary):

   ```powershell
   # Run as admin
   Add-MpPreference -ExclusionPath "$env:USERPROFILE\.claude\plugins\xrpl-signer"
   Add-MpPreference -ExclusionPath "$env:USERPROFILE\.claude\plugins\xrpl-local"
   ```

   A self-contained AOT-style .NET binary sometimes trips a false positive due to native library self-extraction — known behaviour, not a plugin bug.

### Linux — SELinux/AppArmor blocks the signer

A self-contained .NET binary extracts native libs into `/tmp` or `~/.cache/dotnet_bundle_extract` on first launch. On enforcing SELinux (RHEL, Fedora) or strict AppArmor (Ubuntu server, snap-confined apps) this can be denied.

Diagnostics:

```bash
# SELinux
sudo ausearch -m AVC -ts recent | grep StaticBit
sudo setenforce 0   # temporary permissive — to test the hypothesis

# AppArmor
sudo journalctl -k | grep -E "DENIED.*StaticBit|DENIED.*dotnet"
sudo aa-status
```

If you find it's blocking:

- **SELinux** — attach the right context to the plugin folder:
  ```bash
  sudo chcon -t bin_t ~/.claude/plugins/xrpl-signer/bin/linux-x64/StaticBit.Xrpl.Mcp.Signer
  # make it persistent:
  sudo semanage fcontext -a -t bin_t "$HOME/.claude/plugins/xrpl-signer/bin/linux-x64/.*"
  sudo restorecon -Rv ~/.claude/plugins/xrpl-signer/
  ```
- **AppArmor** — best to exclude the whole Claude Code from confinement if it's under a snap profile. Alternative — move plugins out of `~/snap/claude-code/.../plugins/` into a regular `~/.claude/plugins/`.

Universal alternative — point `DOTNET_BUNDLE_EXTRACT_BASE_DIR` at a path your policy definitely allows:

```bash
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="$HOME/.cache/staticbit-mcp"
mkdir -p "$DOTNET_BUNDLE_EXTRACT_BASE_DIR"
chmod 700 "$DOTNET_BUNDLE_EXTRACT_BASE_DIR"
```

Add it to `~/.bashrc` / `~/.zshrc` or to the plugin's env block to make it persist.

---

## Related documents

- [README.md](../README.md) — marketplace overview and available plugins
- [plugins/xrpl-cloud/README.md](../plugins/xrpl-cloud/README.md) — cloud plugin details
- [plugins/xrpl-local/README.md](../plugins/xrpl-local/README.md) — local plugin details
- [plugins/xrpl-signer/README.md](../plugins/xrpl-signer/README.md) — signer plugin details
- [StaticBit-io/staticbit-xrpl-mcp](https://github.com/StaticBit-io/staticbit-xrpl-mcp) — cloud server and signer sources
- [StaticBit-io/staticbit-xrpl-mcp/DEPLOY.md](https://github.com/StaticBit-io/staticbit-xrpl-mcp/blob/main/docs/DEPLOY.md) — for the cloud-server admin: how to deploy your own instance
