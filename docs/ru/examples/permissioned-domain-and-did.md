>  🌐 **Язык**: [English](../../examples/permissioned-domain-and-did.md) | **Русский**

# Пример: PermissionedDomain + DID identity (XLS-80 + XLS-40)

Объединённый рецепт: устанавливаем on-chain identity (DID) для аккаунта, затем создаём Permissioned Domain, который принимает только holders определённых credential'ов. Используется для compliance-gated DEX, для permissioned AMM, или просто как regulated trading pool.

Референсы: [TestIPermissionedDomain.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIPermissionedDomain.cs), [TestIDID.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIDID.cs).

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_did_set_prepare`, `xrpl_did_delete_prepare`, `xrpl_permissioned_domain_set_prepare`, `xrpl_permissioned_domain_delete_prepare`, `xrpl_account_did`, `xrpl_account_permissioned_domains`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed` |
| **xrpl-signer** | `xrpl_sign` |

## Концепция

**DID (XLS-40)**: один ledger entry на аккаунт, содержит 3 hex-blob'а ≤256 bytes каждый:
- `Data` — публичные attestations (например, JSON-LD verifiable credential).
- `DIDDocument` — DID document (W3C DID spec).
- `URI` — ссылка на external storage (IPFS, HTTPS).
В DIDSet любое подмножество — хоть один из трёх обязателен.

**PermissionedDomain (XLS-80)**: ledger entry, описывающий "кто допущен в этот домен" через массив accepted credentials. Down-stream amendments (permissioned DEX, permissioned AMM) могут привязываться к Domain через `DomainID`.

## Архитектура

```
Step 1: Identity setup
  ACCOUNT ──DIDSet(URI="did:example:user", DIDDocument=<...>)──► rippled
                                                                  ↓ creates LODID entry owned by ACCOUNT

Step 2: Credential issuers grant credentials (per XLS-70, отдельный рецепт)
  ISSUER_A ──CredentialCreate(Subject=USER1, Type="KYC")──►
  ISSUER_B ──CredentialCreate(Subject=USER2, Type="QI")──►
  USER1/2 ──CredentialAccept──►

Step 3: Domain owner creates the permissioned domain
  DOMAIN_OWNER ──PermissionedDomainSet(AcceptedCredentials=[
    {Issuer:ISSUER_A, CredentialType:"KYC"},
    {Issuer:ISSUER_B, CredentialType:"QI"}
  ])──► rippled
                ↓ creates LOPermissionedDomain entry, returns DomainID (64-hex)

Step 4: Domain owner shares DomainID with downstream protocols
  (e.g. permissioned AMM pool uses DomainID to gate trades)
```

## Pre-requisites

- ≥ 1 funded аккаунт для DID owner.
- ≥ 1 funded аккаунт для PermissionedDomain owner.
- Реальные credentials уже выписаны и accepted (см. [kyc-credentials-flow.md](kyc-credentials-flow.md)).
- XLS-40 и XLS-80 amendments активированы. На standalone testnet — да. Проверка через `xrpl_server_state.feature`.

## Промт агента

```markdown
---
name: identity-and-domain
description: Manages DID identity for an account and creates/modifies permissioned
  domains gated by credential allow-lists.
tools: xrpl_did_set_prepare, xrpl_did_delete_prepare,
  xrpl_permissioned_domain_set_prepare, xrpl_permissioned_domain_delete_prepare,
  xrpl_account_did, xrpl_account_permissioned_domains,
  xrpl_tx_preflight, xrpl_tx_submit_signed, xrpl_sign
---

Inputs:
- {"step":"did_set","network":"testnet","account":"r...","uriPlain":"did:example:user",
   "didDocumentPlain":"<JSON-LD>","dataPlain":"<...>"}
- {"step":"did_delete","network":"...","account":"r..."}
- {"step":"domain_create","network":"...","owner":"r...",
   "acceptedCredentials":[{"issuer":"r...","credentialTypePlain":"KYC"},...]}
- {"step":"domain_modify","network":"...","owner":"r...","domainId":"<64-hex>",
   "acceptedCredentials":[...]}
- {"step":"domain_delete","network":"...","owner":"r...","domainId":"<64-hex>"}

For each step:
1. For domain_*, prepare `acceptedCredentialsJson` by hex-encoding each
   `credentialTypePlain` to UTF-8 hex (use existing helpers).
2. Build via the matching `*_prepare` tool.
3. `xrpl_tx_preflight` → bail on `feasible=false`.
4. Sign + submit.
5. On domain_create success, extract `DomainID` from
   `xrpl_account_permissioned_domains(account=owner)` (or from metadata).

Return `{txHash, engineResult, domainId?}`.
```

## Step-by-step

### 1. Establish DID for an account

```text
agent ← {"step":"did_set","account":"rAlice...","uriPlain":"did:example:alice",
         "didDocumentPlain":"{\"@context\":\"https://www.w3.org/ns/did/v1\",
                              \"id\":\"did:example:alice\",
                              \"verificationMethod\":[...]}"}

→ xrpl_did_set_prepare(network, account="rAlice...",
   uriPlain="did:example:alice", didDocumentPlain="...")
→ preflight
→ sign by Alice → submit → tesSUCCESS
```

После: `xrpl_account_did(account=rAlice)` → `{ "hasDid":true, "uriUtf8":"did:example:alice", "didDocumentUtf8":"{...}" }`.

DID можно обновлять — повторный `xrpl_did_set_prepare` с новыми полями патчит entry. Опущенные поля **не очищаются** автоматически (rippled keeps previous values для unspecified fields).

### 2. Delete DID

```text
agent ← {"step":"did_delete","account":"rAlice..."}
→ xrpl_did_delete_prepare(network, account="rAlice...")
→ sign + submit → tesSUCCESS
```

Reserve освобождается.

### 3. Create PermissionedDomain

```text
agent ← {"step":"domain_create","owner":"rDomainOwner...",
         "acceptedCredentials":[
           {"issuer":"rIssuerKYC...","credentialTypePlain":"KYC-Tier1"},
           {"issuer":"rIssuerKYC...","credentialTypePlain":"KYC-Tier2"},
           {"issuer":"rIssuerQI...","credentialTypePlain":"QI"}
         ]}

→ build acceptedCredentialsJson:
   [{"issuer":"rIssuerKYC...","credentialType":"4B59432D5469657231"},
    {"issuer":"rIssuerKYC...","credentialType":"4B59432D5469657232"},
    {"issuer":"rIssuerQI...","credentialType":"5149"}]

→ xrpl_permissioned_domain_set_prepare(network, account=owner,
   acceptedCredentialsJson, domainId=null /* CREATE */)
→ preflight (1..10 entries, dedup, hex-format) → feasible=true
→ sign by domainOwner → submit → tesSUCCESS

→ xrpl_account_permissioned_domains(account=owner)
   → { "domains":[{ "domainId":"<64-hex>", "sequence":..., ... }] }
```

Запоминаем `DomainID` (это index ledger entry'а) для использования в permissioned-DEX/AMM.

### 4. Modify domain (replace credential list)

```text
agent ← {"step":"domain_modify","owner":"...","domainId":"<64-hex>",
         "acceptedCredentials":[{"issuer":"...","credentialTypePlain":"KYC-Tier2"}]}
```

⚠️ **Critical**: `PermissionedDomainSet` с заданным `DomainID` **полностью заменяет** список — не merge. Если хочешь "добавить" credential — сначала read через `xrpl_account_permissioned_domains`, потом resubmit с full new list.

```text
→ read existing: xrpl_account_permissioned_domains(account=owner) → пр-р прежний массив
→ merge in agent с новой entry
→ xrpl_permissioned_domain_set_prepare(...domainId=existingId, acceptedCredentialsJson=merged)
```

### 5. Delete domain

```text
agent ← {"step":"domain_delete","owner":"...","domainId":"<64-hex>"}
→ xrpl_permissioned_domain_delete_prepare(network, account=owner, domainId)
→ sign + submit → tesSUCCESS
```

Reserve возвращается. **Down-stream**: domain-gated AMM/DEX entry'и теперь не могут опираться на этот DomainID — submit'ы будут падать с `tecNO_TARGET` (или аналогом). Проверь зависимости перед delete.

## Verification

- DID: `xrpl_account_did(account)` — `hasDid=true`, проверка всех полей в правильных формах.
- Domain: `xrpl_account_permissioned_domains(account=owner)` — `domainCount ≥ 1`, верный `acceptedCredentials` массив с decoded UTF-8.

## Тестовые предпосылки

- **Amendment activation**: `xrpl_server_state` → `feature` массив должен содержать `DID` и `PermissionedDomains` в `enabled` state. На стандартном testnet оба — да; на mainnet — обновлять (XLS-40 активирован 2024-Q4, XLS-80 — позже).
- Если `temDISABLED` — рецепт не работает, нужен другой узел.

## Use-cases

- **Identity-attestable account**: эквивалент Ethereum ENS / verifiable credentials profile. DID указывает на DID Document с verification methods.
- **Permissioned DEX**: order book принимает orders только от holders определённых credentials. Используется для регулируемых рынков (security tokens, RWA, accredited investors).
- **Permissioned AMM**: liquidity pool gated by domain. Только KYC'ed accounts могут swap/add/remove liquidity.
- **DID-gated MPT**: combining MPT `tfMPTRequireAuth` + DID. Issuer проверяет subject's DID Document перед issuer-side authorize.

## Расширения

- **Off-chain DID resolver service** — берёт `xrpl_account_did` и резолвит `URI` (IPFS/HTTPS), возвращает full DID Document. Полезно когда `didDocumentPlain` слишком большой для on-chain.
- **Multi-issuer domain federation** — domain принимает credentials от M разных issuers (e.g., KYC от Jumio, AML от Chainalysis). Каждый issuer — отдельная entry в `AcceptedCredentials`.
- **Time-bounded membership** — credential'ы с `Expiration` → domain автоматически "забывает" expired holders. Combined с XLS-70.
