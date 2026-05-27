> 🇬🇧 [Read in English](nft-mutable-uri.md)

# Пример: Mutable NFT URI (XLS-46)

NFT с обновляемой metadata URI. Owner или authorized minter может изменять URI в любой момент после mint'а — полезно для dynamic NFTs (status badges, achievements, gaming items с прокачкой).

Без `tfMutable=16` flag'а на NFTokenMint — URI становится **immutable** после создания, никаким способом не изменить. Поэтому solution выбирается на этапе mint'а.

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_nft_mint_prepare` (с flag), `xrpl_nft_modify_prepare`, `xrpl_account_nfts`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed`, `xrpl_tx_lookup` |
| **xrpl-signer** | `xrpl_sign` |

## Концепция (XLS-46)

- NFT обычно immutable: вся metadata (URI) фиксируется при mint и не меняется.
- XLS-46 добавил `tfMutable=16` flag в `NFTokenMint`. Set'ит флаг `lsfNFTokenMutable` в NFToken ledger entry'и.
- При этом флаге **owner** (или authorized minter, если был `Issuer` set'нут при mint'е) может submit'ить `NFTokenModify` с новым `URI`.
- `clearUri` опция — удалить URI полностью.
- URI ограничен 256 bytes raw / 512 hex chars.

## Архитектура

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

- 1 funded аккаунт для minter.
- (Optional) Funded user-account для transfer scenario.
- XLS-46 amendment активирован (на testnet — да).

## Промт агента

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

### 1. Mint mutable NFT

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

NFTokenID — 64-char hex, unique identifier. Сохраняем для последующих modify.

### 2. (optional) Transfer to user

Стандартный NFT transfer flow (не специфично для XLS-46):

```text
MINTER → NFTokenCreateOffer(NFTokenID, Amount=0, Flags=tfSellOffer)
USER   → NFTokenAcceptOffer(NFTokenSellOffer=<offer_id>)
```

После accept'а NFT принадлежит USER'у. NFTokenModify может submit'ить:
- **USER** (текущий owner), всегда.
- **Original Issuer**, если на mint'е был `Issuer` field set и USER — НЕ issuer. Это authorized-minter case (по XLS-43 + XLS-46 interaction).

### 3. Modify URI

USER хочет обновить URI после, например, level-up:

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

Если NFT не был mint'нут с `tfMutable` — `tecNO_PERMISSION`. Это **immutable-by-default** safety.

### 4. Issuer modifies user-owned NFT

Если original minter был set'нут как `Issuer` (для authorized-minter case), он может modify URI даже когда NFT уже у USER:

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

Use-case: issuer "deprecates" NFT (например, скандал с organisation', нужно обновить badge URI на disclaimer).

### 5. Clear URI

```text
agent ← {"action":"clear","account":"rUser...","nfTokenId":"<...>"}

→ xrpl_nft_modify_prepare(
    network, account="rUser...", nfTokenId, clearUri=true
  )
→ sign + submit
```

Полезно для "hide" NFT'а без burn'а: URI становится пустой, off-chain reader'ы не могут show metadata.

## Verification

После каждого modify:

```text
agent → xrpl_account_nfts(network, account=owner)
       → { "NFTokens": [
           { "NFTokenID":"<...>", "URI":"<new hex>", "Flags":24, ... }
         ] }
       ↓ verify URI != prior value
```

`Flags=24` подтверждает `tfMutable` set.

## Подводные камни

- **Mutable is opt-in**: если забыл `tfMutable` на mint — навсегда. Нельзя retrofit'но добавить мутабельность.
- **URI 256 bytes max** — для больших metadata храните payload off-chain (IPFS) и в URI только CID.
- **Issuer must be set at mint** для authorized-modify-after-transfer case. Если minter подписал mint'е и issuer field был пустой — потом modify только owner может делать.
- **Burn'd NFT** не модифицируется — `tecNO_ENTRY`.

## Use-cases

- **Dynamic gaming NFTs**: персонаж прокачивается → updateURI с новым stats JSON.
- **Status badges**: NFT-сертификат с expiration date → revoke через `clear` URI.
- **Real-world asset (RWA) tokenization**: NFT представляет real estate / art piece, metadata обновляется при transfer / inspection.
- **Soulbound credentials** (XLS-46 + non-transferable mint via `tfTransferable` omitted): immutable identity, mutable attributes — например, "PhD от Univ X" (immutable) + "current employer" (mutable).
- **Dynamic ticketing**: концертный билет → URI обновляется на "USED" после input'а на event.

## Расширения

- **Combined with DID** (см. [permissioned-domain-and-did.md](permissioned-domain-and-did.md)): mutable NFT references DID document, который сам обновляется. Двойная mutability.
- **Modify-on-event**: agent слушает `xrpl_account_tx_since(account)` для конкретных trigger'ов (Payment in, AMM trade) — на каждом event'е bump'ит URI.
- **Cooperative modify**: SignerList на NFT-owning account, modify требует quorum. Используется для DAO-controlled NFTs.

## Verification checklist

- [ ] Mint: `xrpl_account_nfts(minter)` показывает NFT с `Flags=24`.
- [ ] Modify: URI поле изменилось, `previous_txn_id` обновлён.
- [ ] Clear: URI = `null` или empty в JSON shape.
- [ ] Negative: попытка modify на NFT без `tfMutable` → `tecNO_PERMISSION`.
- [ ] Negative: попытка modify of someone else's NFT без issuer-rights → `tecNO_PERMISSION`.
