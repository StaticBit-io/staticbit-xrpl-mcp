>  🌐 **Language**: **English** | [Русский](README.ru.md)

# xrpl-signer plugin

An offline stdio MCP — XRPL wallet management and transaction signing. Runs locally as a subprocess with **no** network calls: the `Xrpl.Wallet` package uses cryptography only, without `Xrpl.Client`.

## Why

A companion plugin to `xrpl-cloud` or `xrpl-local`. Those do `prepare` and `submit_signed`; this one does `sign`. The cloud/local servers **never** see your seed: they receive only a signed hex blob as output.

## Installation

```
/plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

### Required ENV — keystore passphrase

```powershell
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE", "<длинный-пароль>", "User")
```

```bash
echo 'export XRPL_SIGNER_PASSPHRASE="<длинный-пароль>"' >> ~/.bashrc
source ~/.bashrc
```

Alternative (if you don't want the passphrase directly in ENV):

```powershell
# File with the passphrase on the first line, mode 0600 (on Unix)
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_PASSPHRASE_FILE", "C:\path\to\passphrase.txt", "User")
```

> **Losing the passphrase = losing all wallets in the keystore** (unless you have a seed backup).

After setting the ENV — **restart Claude Code**.

### Optional — keystore path

By default:
- Windows: `%USERPROFILE%\.staticbit-xrpl-signer\keystore.json`
- Linux/macOS: `~/.staticbit-xrpl-signer/keystore.json` (mode 0600)

To override:
```powershell
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_KEYSTORE_PATH", "C:\my\keystore.json", "User")
```

### Signing policy (enforced in code)

The signer refuses, whatever the chat says:

- a transaction whose `Account` is not the address of the wallet it is signed with (`xrpl_sign`);
- transactions that hand control of the account to another key or account in one signature: `SetRegularKey`, `SignerListSet`, `AccountDelete`, `DelegateSet` and `AccountSet` with `asfDisableMaster` — also when wrapped in a `Batch` (`xrpl_sign`, `xrpl_sign_multi`).

To allow a kind, list it in `XRPL_SIGNER_ALLOW_TX_TYPES` (comma-separated; tokens `SetRegularKey`, `SignerListSet`, `AccountDelete`, `DelegateSet`, `DisableMaster`) and restart Claude Code. An unknown token stops the signer at startup. The variable belongs to the signer process, so it cannot be changed from the chat — only the operator can widen the policy.

```powershell
[Environment]::SetEnvironmentVariable("XRPL_SIGNER_ALLOW_TX_TYPES", "SetRegularKey", "User")
```

### Audit log

Every signature, refusal and failed decryption is appended to `signer-audit.log` next to the keystore (JSON lines, mode 0600 on Unix). Set `XRPL_SIGNER_AUDIT_LOG` to another path to move it, or to `off` to disable it.

## Verification

```
/mcp
```
```
xrpl-signer  node bin/signer.js  ✓ Connected
```

## Features

### Import / create a wallet — 5 ways

| Tool | Input format |
|---|---|
| `xrpl_wallet_generate` | random entropy → new wallet |
| `xrpl_wallet_import_seed` | XRPL seed `sEd...` or `sn...` |
| `xrpl_wallet_import_mnemonic` | BIP39 phrase (12/15/18/21/24 words), optional derivation path + BIP39 passphrase |
| `xrpl_wallet_import_xumm` | 8 groups of 6 digits (Xumm secret numbers) |
| `xrpl_wallet_import_text` | arbitrary text + KDF (`sha256` or `pbkdf2`, optional salt) |

### Management

- `xrpl_wallet_list` — list wallets (no secrets)
- `xrpl_wallet_address` — metadata for a single wallet
- `xrpl_wallet_remove` — delete
- `xrpl_wallet_export` — backup: return the seed in plaintext (requires `confirm=true`)

### Signing

- `xrpl_sign` — single-sign a transaction
- `xrpl_sign_multi` — partial signature for multi-sign (one of N signatures)
- `xrpl_sign_combine` — aggregate several partial signatures into the final blob

## Keystore cryptography

| Parameter | Value |
|---|---|
| KDF | PBKDF2-SHA256, 600 000 iterations |
| Cipher | AES-256-GCM (authenticated) |
| Salt | 32 bytes, **per-wallet** |
| IV | 12 bytes, **per-wallet** |
| Tag | 16 bytes (GCM auth tag) |

Per-wallet salt/IV means: even if two wallets with the same seed and the same passphrase are encrypted in one keystore, their ciphertexts differ. A leak of one entry does not help decode another.

## Platforms

5 self-contained .NET binaries:
- `win-x64` (~81 MB)
- `linux-x64` (~80 MB)
- `linux-arm64` (~88 MB)
- `osx-x64` (~80 MB)
- `osx-arm64` (~87 MB)

The Node.js launcher `bin/signer.js` picks the right one by `os.platform()/os.arch()`.

## Using together

With the cloud flow:
```
/plugin install xrpl-cloud@staticbit-xrpl-mcp
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

With the local flow (no external service):
```
/plugin install xrpl-local@staticbit-xrpl-mcp
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

In both cases the agent itself routes calls between the prepare server and the signer.

## Security

- The signer **never** makes network calls — isolated by dependency selection in the .csproj (no `Xrpl.Client`).
- The passphrase lives **only** in the signer process's RAM. It is not written to disk.
- Seeds are encrypted in the keystore — they cannot be extracted without the passphrase.
- Keystore file compromised? An attacker still has to guess your passphrase, and PBKDF2 600k iters makes that very expensive even on a GPU.
