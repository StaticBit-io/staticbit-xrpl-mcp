> 🇷🇺 [Прочесть на русском](permissioned-domain-and-did.ru.md)

# Example: PermissionedDomain + DID identity (XLS-80 + XLS-40)

A combined recipe: establish on-chain identity (DID) for an account, then create a Permissioned Domain that accepts only holders of specific credentials. Used for compliance-gated DEX, permissioned AMM, or simply as a regulated trading pool.

References: [TestIPermissionedDomain.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIPermissionedDomain.cs), [TestIDID.cs](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestIDID.cs).

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_did_set_prepare`, `xrpl_did_delete_prepare`, `xrpl_permissioned_domain_set_prepare`, `xrpl_permissioned_domain_delete_prepare`, `xrpl_account_did`, `xrpl_account_permissioned_domains`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed` |
| **xrpl-signer** | `xrpl_sign` |

## Concept

**DID (XLS-40)**: one ledger entry per account, holds 3 hex-blob fields ≤256 bytes each:
- `Data` — public attestations (e.g. a JSON-LD verifiable credential).
- `DIDDocument` — DID document (W3C DID spec).
- `URI` — link to external storage (IPFS, HTTPS).
DIDSet accepts any subset — at least one of the three is required.

**PermissionedDomain (XLS-80)**: a ledger entry describing "who is admitted to this domain" through an accepted-credentials array. Downstream amendments (permissioned DEX, permissioned AMM) can wire to the Domain via `DomainID`.

## Architecture

```
Step 1: Identity setup
  ACCOUNT ──DIDSet(URI="did:example:user", DIDDocument=<...>)──► rippled
                                                                  ↓ creates LODID entry owned by ACCOUNT

Step 2: Credential issuers grant credentials (per XLS-70, separate recipe)
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

- ≥ 1 funded account for the DID owner.
- ≥ 1 funded account for the PermissionedDomain owner.
- Real credentials already issued and accepted (see [kyc-credentials-flow.md](kyc-credentials-flow.md)).
- XLS-40 and XLS-80 amendments are active. On standalone testnet — yes. Verify via `xrpl_server_state.feature`.

## Agent prompt

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
1. For domain_*, build `acceptedCredentialsJson` by hex-encoding each
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

After: `xrpl_account_did(account=rAlice)` → `{ "hasDid":true, "uriUtf8":"did:example:alice", "didDocumentUtf8":"{...}" }`.

DID is updatable — a subsequent `xrpl_did_set_prepare` with new fields patches the entry. Omitted fields are **not** auto-cleared (rippled keeps previous values for unspecified fields).

### 2. Delete DID

```text
agent ← {"step":"did_delete","account":"rAlice..."}
→ xrpl_did_delete_prepare(network, account="rAlice...")
→ sign + submit → tesSUCCESS
```

Reserve is released.

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

Save the `DomainID` (it's the ledger-entry index) for use in permissioned DEX/AMM.

### 4. Modify domain (replace credential list)

```text
agent ← {"step":"domain_modify","owner":"...","domainId":"<64-hex>",
         "acceptedCredentials":[{"issuer":"...","credentialTypePlain":"KYC-Tier2"}]}
```

⚠️ **Critical**: `PermissionedDomainSet` with a given `DomainID` **fully replaces** the list — does not merge. To "add" a credential, first read via `xrpl_account_permissioned_domains`, then resubmit with the full new list.

```text
→ read existing: xrpl_account_permissioned_domains(account=owner) → existing array
→ merge in agent with the new entry
→ xrpl_permissioned_domain_set_prepare(...domainId=existingId, acceptedCredentialsJson=merged)
```

### 5. Delete domain

```text
agent ← {"step":"domain_delete","owner":"...","domainId":"<64-hex>"}
→ xrpl_permissioned_domain_delete_prepare(network, account=owner, domainId)
→ sign + submit → tesSUCCESS
```

Reserve is returned. **Downstream**: domain-gated AMM/DEX entries can no longer reference this DomainID — submits will fail with `tecNO_TARGET` (or similar). Check dependencies before delete.

## Verification

- DID: `xrpl_account_did(account)` — `hasDid=true`, verify all fields in the correct shapes.
- Domain: `xrpl_account_permissioned_domains(account=owner)` — `domainCount ≥ 1`, correct `acceptedCredentials` array with decoded UTF-8.

## Test prerequisites

- **Amendment activation**: `xrpl_server_state` → `feature` array must contain `DID` and `PermissionedDomains` in the `enabled` state. On standard testnet both — yes; on mainnet — keep up to date (XLS-40 activated 2024-Q4, XLS-80 later).
- If `temDISABLED` — recipe doesn't work, you need a different node.

## Use-cases

- **Identity-attestable account**: equivalent to Ethereum ENS / verifiable-credentials profile. DID points at a DID Document with verification methods.
- **Permissioned DEX**: the order book accepts orders only from holders of specific credentials. Used for regulated markets (security tokens, RWA, accredited investors).
- **Permissioned AMM**: liquidity pool gated by a domain. Only KYC'd accounts can swap/add/remove liquidity.
- **DID-gated MPT**: combining MPT `tfMPTRequireAuth` + DID. Issuer checks the subject's DID Document before the issuer-side authorize.

## Extensions

- **Off-chain DID resolver service** — takes `xrpl_account_did` and resolves `URI` (IPFS/HTTPS), returns the full DID Document. Useful when `didDocumentPlain` is too large for on-chain.
- **Multi-issuer domain federation** — a domain accepts credentials from M different issuers (e.g. KYC from Jumio, AML from Chainalysis). Each issuer is a separate entry in `AcceptedCredentials`.
- **Time-bounded membership** — credentials with `Expiration` → domain automatically "forgets" expired holders. Combined with XLS-70.
