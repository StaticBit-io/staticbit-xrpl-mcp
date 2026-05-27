> 🇷🇺 [Прочесть на русском](controlled-mpt-issuance.ru.md)

# Example: Controlled MPT issuance (XLS-33)

A Cowork agent that runs the lifecycle of a Multi-Purpose Token: creates an issuance with allow-listing + clawback, authorises a holder, transfers tokens, optionally locks balances, claws back, and destroys.

Uses exactly the sequences validated by the XrplCSharp integration tests ([TestIMPTokenCreate](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIMPTokenCreate.cs), [TestIMPTokenAuthorize](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIMPTokenAuthorize.cs), [TestIMPTokenManage](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIMPTokenManage.cs)).

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_mpt_issuance_create_prepare`, `xrpl_mpt_authorize_prepare`, `xrpl_mpt_issuance_set_prepare`, `xrpl_payment_prepare`, `xrpl_mpt_issuance_destroy_prepare`, `xrpl_account_mpt_issuances`, `xrpl_account_mpts`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_tx_lookup` |
| **xrpl-signer** | `xrpl_sign` ×N |

## Architecture and roles

```
ISSUER  ───── MPTokenIssuanceCreate ─────►  rippled
   │            (tfMPTRequireAuth + tfMPTCanClawback)
   │            ↓ returns MPTokenIssuanceID in metadata
   │
   │  HOLDER  ── MPTokenAuthorize (holder opt-in) ──►
   │  ISSUER  ── MPTokenAuthorize (issuer authorize holder) ──►
   │
   │  ISSUER  ── Payment to HOLDER with MPT amount ──►
   │
   │  (when needed)
   │  ISSUER  ── MPTokenIssuanceSet (tfMPTLock) ──►
   │  ISSUER  ── Clawback ──►
   │  ISSUER  ── MPTokenIssuanceDestroy ──►
```

**Bilateral authorization under `tfMPTRequireAuth`**: the holder must submit `MPTokenAuthorize` (opt-in), and the issuer submits a separate `MPTokenAuthorize` with `Holder = <addr>` (approval).

## Pre-requisites

- 1 funded issuer account.
- ≥ 1 funded holder account.
- Master passphrase for `xrpl-signer` (env `XRPL_SIGNER_PASSPHRASE`).
- Both accounts imported into the keystore via `xrpl_wallet_import_seed`.

## Agent prompt

```markdown
---
name: mpt-lifecycle
description: Manages a Multi-Purpose Token issuance with allow-listing — creates,
  authorizes holders bilaterally, sends payments, locks, claws back, destroys.
tools: xrpl_mpt_issuance_create_prepare, xrpl_mpt_authorize_prepare,
  xrpl_mpt_issuance_set_prepare, xrpl_payment_prepare,
  xrpl_mpt_issuance_destroy_prepare, xrpl_account_mpt_issuances,
  xrpl_account_mpts, xrpl_tx_preflight, xrpl_tx_submit_signed, xrpl_tx_lookup,
  xrpl_sign
---

You execute a single step of an MPT lifecycle.

Input is one of:
- `{"action":"create","network":"testnet","issuer":"r...","assetScale":2,
   "transferFee":0,"requireAuth":true,"canClawback":true,"canTransfer":false,
   "metadataPlain":"<optional>"}`
- `{"action":"authorize","network":"...","mptokenIssuanceId":"<48-hex>",
   "issuer":"r...","holder":"r...","role":"holder"|"issuer"}`
- `{"action":"pay","network":"...","mptokenIssuanceId":"<48-hex>",
   "issuer":"r...","destination":"r...","amount":"<decimal>"}`
- `{"action":"lock","mptokenIssuanceId":"<48-hex>","issuer":"r...","lock":true|false}`
- `{"action":"destroy","mptokenIssuanceId":"<48-hex>","issuer":"r..."}`

For every action:
1. Call the appropriate `*_prepare` tool with given parameters.
2. Pass the unsigned blob to `xrpl_tx_preflight` first; if `feasible=false`,
   return the warnings and stop.
3. Sign via `xrpl_sign(walletName, txBlobUnsigned)` (walletName == account).
4. Submit via `xrpl_tx_submit_signed(txBlobSigned, waitForValidation=true)`.
5. For `create` action: after success, call `xrpl_tx_lookup(txHash)` and
   extract `MPTokenIssuanceID` from metadata.affected_nodes.
6. Return `{txHash, engineResult, ...action-specific output}`.

Never sign without preflight; never proceed past `tec*` engine results.
```

## Step-by-step sequence

### 1. Issuer creates the issuance

```text
USER → agent: {"action":"create","network":"testnet","issuer":"rIssuer...",
              "assetScale":2,"requireAuth":true,"canClawback":true}
```

What the agent does:
1. `xrpl_mpt_issuance_create_prepare(network, account=issuer, assetScale=2, requireAuth=true, canClawback=true)` → unsigned blob.
2. `xrpl_tx_preflight(txJson)` — reserve check, AssetScale ≤ 10, transferFee requires canTransfer.
3. `xrpl_sign(walletName=issuer, txBlobUnsigned)` via xrpl-signer.
4. `xrpl_tx_submit_signed(txBlobSigned, waitForValidation=true)`.
5. After `tesSUCCESS` → `xrpl_tx_lookup(txHash)` → parse metadata → `MPTokenIssuanceID` (48-hex).

Returns: `{mptokenIssuanceId, txHash, ledgerIndex}`.

### 2. Bilateral authorization

Under `tfMPTRequireAuth`, **two** transactions are required:

```text
agent: {"action":"authorize", ..., "holder":"rHolder...", "role":"holder"}
agent: {"action":"authorize", ..., "holder":"rHolder...", "role":"issuer"}
```

- **role=holder**: `account=holder`, no `holder` parameter (this is the holder's own opt-in).
- **role=issuer**: `account=issuer`, `holder=<addr>` (issuer approves the specific holder).

Without allow-listing (`requireAuth=false`) only the holder opt-in is needed.

### 3. Payment with MPT amount

```text
agent: {"action":"pay", ..., "destination":"rHolder...", "amount":"500"}
```

Inside, the prepare tool builds an amount of the shape:
```json
{ "value": "500", "mpt_issuance_id": "<48-hex>" }
```

`xrpl_payment_prepare` accepts that via the `amount` parameter (JSON form). The holder must **already** be authorised (see step 2), otherwise `tecNO_PERMISSION`.

### 4. Lock balances (optional)

```text
agent: {"action":"lock","lock":true}
```

`xrpl_mpt_issuance_set_prepare(lockBalance=true)` → `tfMPTLock`. All MPT balances become frozen — payments fail with `tecLOCKED`. Unlock: `lock=false` → `tfMPTUnlock`.

### 5. Per-holder lock

Lock a single holder without a global lock:

```text
agent: {"action":"lock","lock":true,"holder":"rBadActor..."}
```

`xrpl_mpt_issuance_set_prepare(lockBalance=true, holder="rBadActor")`. Other holders keep trading.

### 6. Clawback

With `tfMPTCanClawback` the issuer can reclaim tokens:

```text
agent: {"action":"clawback","holder":"rBadActor...","amount":"500"}
```

Uses the existing `xrpl_clawback_prepare` with an MPT-shape amount.

### 7. Destroy

```text
agent: {"action":"destroy"}
```

Allowed only when `OutstandingAmount=0`. If balances remain — `tecHAS_OBLIGATIONS`. Claw back everything first, then destroy.

## Verification checklist

After each step:

- [ ] `engine_result == "tesSUCCESS"`.
- [ ] `meta.TransactionResult == "tesSUCCESS"`.
- [ ] On create: `xrpl_account_mpt_issuances(account=issuer)` shows the new issuance with the right flags.
- [ ] On holder authorize: `xrpl_account_mpts(account=holder)` shows a holding with `accepted=true` (under allow-listing — after the issuer-side authorize).
- [ ] On pay: `xrpl_account_mpts(account=holder)` shows increased `amount`.
- [ ] On lock: `xrpl_account_mpts(account=holder)` → `locked=true`.

## Test prerequisites and known issues

- On standard testnet (`s.altnet.rippletest.net:51233`) XLS-33 is **activated** — the recipe works out of the box.
- For a custom rippled — verify `xrpl_server_state` → `feature` array contains `MPTokensV1` enabled.
- A holder without opt-in receives `tecNO_AUTH` when payment is attempted.
- TransferFee > 0 requires `canTransfer=true` (preflight catches this).

## Possible extensions

- **XLS-89 metadata**: `metadataPlain` serialises into `{"n":"My Token","w":"example.com"}` JSON and hex-encodes — the SDK provides `MPTokenMetadataSchema`. Our equivalent — pass ready hex via `metadataHex`.
- **Auction-style minting**: use `tfMPTCanTrade` + an AMM pool of these MPTs.
- **Multi-issuer cooperation**: issuer sets up a signer list and delegates control to multiple accounts.
