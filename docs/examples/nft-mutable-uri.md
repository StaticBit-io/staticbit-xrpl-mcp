>  🌐 **Language**: **English** | [Русский](../ru/examples/nft-mutable-uri.md)

# Example: Mutable NFT URI (XLS-46)

An NFT with an updatable metadata URI. The owner or an authorized minter may change the URI any time after mint — useful for dynamic NFTs (status badges, achievements, gaming items that level up).

Without the `tfMutable=16` flag on NFTokenMint, the URI is **immutable** after creation — no way to change it. So the choice is made at mint time.

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_nft_mint_prepare` (with flag), `xrpl_nft_modify_prepare`, `xrpl_account_nfts`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_tx_lookup` |
| **xrpl-signer** | `xrpl_sign` |

## Concept (XLS-46)

- NFTs are immutable by default: the metadata (URI) is fixed at mint and never changes.
- XLS-46 added the `tfMutable=16` flag to `NFTokenMint`. It sets the `lsfNFTokenMutable` flag on the NFToken ledger entry.
- With that flag, the **owner** (or an authorized minter when `Issuer` was set at mint) may submit `NFTokenModify` with a new `URI`.
- `clearUri` option — remove the URI entirely.
- URI is limited to 256 raw bytes / 512 hex chars.

## Architecture

```
Step 1: Mint mutable NFT
  MINTER ──NFTokenMint(NFTokenTaxon=X, Flags=tfMutable|tfTransferable, URI=<initial>)──►
              ↓ NFTokenID returned in metadata
              ↓ NFT owned by MINTER (or by issuer if minter is authorized minter)

Step 2 (optional): Transfer to USER
  MINTER ──NFTokenCreateOffer + USER ── NFTokenAcceptOffer ──►
                                          ↓ USER now owns the NFT

Step 3: Update URI
  USER (or original issuer if applicable) ──NFTokenModify(NFTokenID, URI=<new>)──►
                                                                        ↓ URI updated on-chain

Step 4 (optional): Clear URI
  USER ──NFTokenModify(NFTokenID, clearUri=true)──►
```

## Pre-requisites

- 1 funded account for the minter.
- (Optional) Funded user account for the transfer scenario.
- XLS-46 amendment is active (yes on testnet).

## Agent prompt

```markdown
---
name: mutable-nft-manager
description: Mints mutable NFTs (tfMutable flag set) and updates their URI
  in response to events (level-up, status change, etc).
tools: xrpl_nft_mint_prepare, xrpl_nft_modify_prepare,
  xrpl_account_nfts, xrpl_tx_preflight, xrpl_tx_submit_signed, xrpl_tx_lookup, xrpl_sign
---

Inputs:
- {"action":"mint","network":"testnet","account":"r...",
   "nfTokenTaxon":42, "uriPlain":"ipfs://Qm.../initial.json",
   "transferable":true, "mutable":true}
- {"action":"modify","network":"...","account":"r...","nfTokenId":"<64-hex>",
   "uriPlain":"ipfs://Qm.../updated.json","owner":"r... (if account != owner)"}
- {"action":"clear","network":"...","account":"r...","nfTokenId":"<64-hex>"}

For mint:
1. Compute flags bitmap: tfMutable(16) | tfTransferable(8) | other (1=Burnable, 4=TrustLine, 2=OnlyXRP).
2. xrpl_nft_mint_prepare(network, account, nfTokenTaxon, uriPlain, flags=<bitmap>)
3. preflight + sign + submit.
4. After tesSUCCESS, extract NFTokenID from metadata (xrpl_tx_lookup).

For modify:
1. xrpl_nft_modify_prepare(network, account, nfTokenId, uriPlain, owner?)
2. preflight (NFTokenID format, URI hex parity)
3. sign + submit.
4. Verify NFT still exists with new URI: xrpl_account_nfts(account=owner).

For clear:
1. xrpl_nft_modify_prepare(network, account, nfTokenId, clearUri=true, owner?)
2. sign + submit.

Return:
- For mint: { "nfTokenId":"...","txHash":"...","engineResult":"..." }
- For modify/clear: { "txHash":"...","engineResult":"..."}
```

## Step-by-step

### 1. Mint a mutable NFT

```text
agent ← {"action":"mint","account":"rMinter...","nfTokenTaxon":42,
         "uriPlain":"ipfs://QmInitial/badge.json","transferable":true,"mutable":true}

flags = 8 (tfTransferable) | 16 (tfMutable) = 24

→ xrpl_nft_mint_prepare(
    network="testnet",
    account="rMinter...",
    nfTokenTaxon=42,
    uriPlain="ipfs://QmInitial/badge.json",
    flags=24
  )
→ preflight → feasible=true
→ xrpl_sign → submit → tesSUCCESS

→ xrpl_tx_lookup(txHash)
   → meta.affected_nodes → find CreatedNode with LedgerEntryType=NFTokenPage
   → extract new NFTokenID from FinalFields.NFTokens array
```

NFTokenID — 64-char hex, unique identifier. Save it for subsequent modifies.

### 2. (optional) Transfer to user

Standard NFT transfer flow (not specific to XLS-46):

```text
MINTER → NFTokenCreateOffer(NFTokenID, Amount=0, Flags=tfSellOffer)
USER   → NFTokenAcceptOffer(NFTokenSellOffer=<offer_id>)
```

After accept, the NFT belongs to USER. NFTokenModify may be submitted by:
- **USER** (current owner) — always.
- **Original Issuer**, when `Issuer` was set at mint and USER is NOT the issuer. This is the authorized-minter case (XLS-43 + XLS-46 interaction).

### 3. Modify URI

USER wants to update the URI after a level-up:

```text
agent ← {"action":"modify","account":"rUser...","nfTokenId":"<NFTokenID>",
         "uriPlain":"ipfs://QmLevelUp/badge_lvl2.json"}

→ xrpl_nft_modify_prepare(
    network="testnet",
    account="rUser...",
    nfTokenId=<NFTokenID>,
    uriPlain="ipfs://QmLevelUp/badge_lvl2.json"
  )
→ preflight (NFTokenID 64-hex, URI hex parity)
→ xrpl_sign → submit → tesSUCCESS
```

If the NFT wasn't minted with `tfMutable` — `tecNO_PERMISSION`. This is the **immutable-by-default** safety.

### 4. Issuer modifies user-owned NFT

If the original minter was set as `Issuer` (for the authorized-minter case), they can modify the URI even when the NFT is held by USER:

```text
agent ← {"action":"modify","account":"rIssuer...","nfTokenId":"<...>",
         "uriPlain":"ipfs://QmRevoked/badge.json",
         "owner":"rUser..."}

→ xrpl_nft_modify_prepare(
    network, account="rIssuer...", nfTokenId,
    uriPlain="...", owner="rUser..."
  )
→ sign by rIssuer → submit
```

Use-case: the issuer "deprecates" an NFT (e.g. organisation scandal, badge URI must point at a disclaimer).

### 5. Clear URI

```text
agent ← {"action":"clear","account":"rUser...","nfTokenId":"<...>"}

→ xrpl_nft_modify_prepare(
    network, account="rUser...", nfTokenId, clearUri=true
  )
→ sign + submit
```

Useful for "hiding" an NFT without burning it: URI becomes empty, off-chain readers cannot show metadata.

## Verification

After every modify:

```text
agent → xrpl_account_nfts(network, account=owner)
       → { "NFTokens": [
           { "NFTokenID":"<...>", "URI":"<new hex>", "Flags":24, ... }
         ] }
       ↓ verify URI != prior value
```

`Flags=24` confirms `tfMutable` is set.

## Gotchas

- **Mutable is opt-in**: forgetting `tfMutable` at mint is forever. You cannot retro-fit mutability.
- **URI 256 bytes max** — for larger metadata store the payload off-chain (IPFS), keep only the CID in the URI.
- **Issuer must be set at mint** for the authorized-modify-after-transfer case. If the minter signed the mint and the issuer field was empty — only the owner can modify later.
- **Burned NFT** is not modifiable — `tecNO_ENTRY`.

## Use-cases

- **Dynamic gaming NFTs**: character levels up → updateURI with the new stats JSON.
- **Status badges**: NFT certificate with an expiration date → revoke via `clear` URI.
- **Real-world asset (RWA) tokenization**: NFT represents real estate / art piece, metadata updates on transfer / inspection.
- **Soulbound credentials** (XLS-46 + non-transferable mint, no `tfTransferable`): immutable identity, mutable attributes — e.g. "PhD from Univ X" (immutable) + "current employer" (mutable).
- **Dynamic ticketing**: concert ticket → URI updates to "USED" after admission.

## Extensions

- **Combined with DID** (see [permissioned-domain-and-did.md](permissioned-domain-and-did.md)): mutable NFT references a DID document that updates on its own. Double mutability.
- **Modify-on-event**: an agent listens via `xrpl_account_tx_since(account)` for specific triggers (Payment in, AMM trade) — bumps the URI on each event.
- **Cooperative modify**: a SignerList on the NFT-owning account, modify requires a quorum. Used for DAO-controlled NFTs.

## Verification checklist

- [ ] Mint: `xrpl_account_nfts(minter)` shows the NFT with `Flags=24`.
- [ ] Modify: URI field changed, `previous_txn_id` updated.
- [ ] Clear: URI = `null` or empty in JSON shape.
- [ ] Negative: an attempt to modify an NFT without `tfMutable` → `tecNO_PERMISSION`.
- [ ] Negative: an attempt to modify someone else's NFT without issuer-rights → `tecNO_PERMISSION`.
