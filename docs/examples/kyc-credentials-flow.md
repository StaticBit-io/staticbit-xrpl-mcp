>  🌐 **Language**: **English** | [Русский](../ru/examples/kyc-credentials-flow.md)

# Example: KYC credentials flow (XLS-70)

Full credential-based KYC lifecycle: the issuer creates a credential for the subject, the subject accepts it, the recipient enables DepositAuth + DepositPreauth referencing the credential, the sender (subject) makes a Payment presenting the credential, the recipient verifies.

Reference: [TestICredential.TestCredential_EndToEnd_DepositPreauthAndPayment](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestICredential.cs#L312).

## What is used

| Plugin | Tools |
|---|---|
| **xrpl-cloud** or **xrpl-local** | `xrpl_credential_create_prepare`, `xrpl_credential_accept_prepare`, `xrpl_credential_delete_prepare`, `xrpl_account_set_prepare`, `xrpl_deposit_preauth_prepare`, `xrpl_payment_prepare` (with `credentialIdsJson`), `xrpl_hash_credential` (pure-local helper), `xrpl_account_credentials`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed` |
| **xrpl-signer** | `xrpl_sign` |

## Concept (XLS-70)

- **Credential** ≈ an on-chain "badge" of the form `(Issuer, Subject, CredentialType-hex)` with optional `URI` and `Expiration`.
- States: `provisional` (after CredentialCreate, before Accept) and `accepted` (after CredentialAccept, the `lsfAccepted=0x10000` flag flips in the ledger entry).
- Reserve: held by the issuer until Accept, then moves to the subject.
- **DepositPreauth with AuthorizeCredentials**: the recipient authorises not a specific address, but a **set of credentials**. Any holder of an accepted credential passes.
- **Payment.CredentialIDs**: the sender attaches a credential hash (SHA-512/2 of subject+issuer+type-hex). The ledger checks that the credential exists, is accepted, not expired, and that the recipient has pre-authorised it.

## Architecture

```
Step 1: Credential creation
  ISSUER ──CredentialCreate(Subject=SUBJECT,Type="KYC-Tier1-hex")──► rippled
                                                                       ↓ provisional Credential entry on rippled
Step 2: Subject accepts
  SUBJECT ──CredentialAccept(Issuer=ISSUER,Type=...)──► rippled
                                                          ↓ lsfAccepted=0x10000 flipped
Step 3: Recipient enables DepositAuth
  RECIPIENT ──AccountSet(asfDepositAuth)──► rippled
                                              ↓ recipient now blocks unsolicited Payments
Step 4: Recipient authorizes credential
  RECIPIENT ──DepositPreauth(AuthorizeCredentials=[{Issuer,Type}])──► rippled
                                                                        ↓ now any holder of (Issuer,Type) credential can pay
Step 5: Subject pays with credential
  SUBJECT ──Payment(Destination=RECIPIENT, CredentialIDs=[hash])──► rippled
                                                                      ↓ tecNO_PERMISSION if no/wrong/unaccepted/expired credential
                                                                      ↓ tesSUCCESS otherwise
Step 6 (optional): Revocation
  ISSUER ──CredentialDelete(Subject=...,Type=...)──► rippled
   OR
  SUBJECT ──CredentialDelete(Issuer=...,Type=...)──► rippled
```

## Pre-requisites

- 3 funded accounts: ISSUER, SUBJECT, RECIPIENT.
- All three imported into the keystore.
- XLS-70 amendment is active (yes on standalone testnet; on mainnet — verify via `xrpl_server_state`).

## Agent prompt

```markdown
---
name: kyc-credentials
description: Orchestrates the XLS-70 KYC credentials end-to-end flow:
  create credential, accept, enable DepositAuth, AuthorizeCredentials,
  pay with credential, optionally revoke.
tools: xrpl_credential_create_prepare, xrpl_credential_accept_prepare,
  xrpl_credential_delete_prepare, xrpl_account_set_prepare,
  xrpl_deposit_preauth_prepare, xrpl_payment_prepare, xrpl_hash_credential,
  xrpl_account_credentials, xrpl_tx_preflight, xrpl_tx_submit_signed, xrpl_sign
---

Inputs (one per call):
- {"step":"create","network":"testnet","issuer":"r...","subject":"r...",
   "credentialTypePlain":"KYC-Tier1","uriPlain":"https://kyc.example.com/...",
   "expirationUtc":"2026-12-31T23:59:59Z"}
- {"step":"accept","network":"...","issuer":"r...","subject":"r...",
   "credentialTypePlain":"KYC-Tier1"}
- {"step":"recipient_enable","network":"...","recipient":"r..."}
- {"step":"preauth","network":"...","recipient":"r...","issuer":"r...",
   "credentialTypePlain":"KYC-Tier1"}
- {"step":"pay","network":"...","subject":"r...","recipient":"r...",
   "amountDrops":"1000000","credentialTypePlain":"KYC-Tier1","issuer":"r..."}
- {"step":"verify","network":"...","subject":"r...","credentialTypePlain":"...",
   "issuer":"r..."}
- {"step":"revoke","network":"...","by":"issuer"|"subject","subject":"r...",
   "issuer":"r...","credentialTypePlain":"KYC-Tier1"}

For each tx step:
1. Build via the appropriate `*_prepare` tool.
2. For "pay": first call `xrpl_hash_credential` to compute the hash, then pass
   it in `credentialIdsJson` to `xrpl_payment_prepare`.
3. `xrpl_tx_preflight` → bail if `feasible=false`.
4. `xrpl_sign(walletName=submitter, txBlobUnsigned)`.
5. `xrpl_tx_submit_signed(..., waitForValidation=true)`.

For "verify":
- Call `xrpl_account_credentials(account=subject)` and check that the
  matching `(issuer, credentialType)` entry has `accepted=true` and is
  not past `expirationUtc`.

Return `{txHash, engineResult}` for tx steps; `{matched, accepted, expired}`
for verify.
```

## Step-by-step

### 1. Create credential

```text
agent ← {"step":"create","issuer":"rIssuerKYC...","subject":"rUser...",
         "credentialTypePlain":"KYC-Tier1","expirationUtc":"2026-12-31T23:59:59Z"}

→ xrpl_credential_create_prepare(network, account=issuer, subject,
   credentialTypePlain="KYC-Tier1", expirationUtc=2026-12-31T23:59:59Z)
   ↓ tx_blob_unsigned
→ xrpl_tx_preflight → feasible=true (subject != account, type ≤128 hex)
→ xrpl_sign(walletName=issuer, blob)
→ xrpl_tx_submit_signed → tesSUCCESS
```

CredentialType `KYC-Tier1` → UTF-8 hex `4B59432D54696572 31` → 16 hex chars (8 raw bytes). Limit 64 raw bytes / 128 hex.

### 2. Subject accepts

```text
agent ← {"step":"accept","issuer":"...","subject":"...","credentialTypePlain":"KYC-Tier1"}

→ xrpl_credential_accept_prepare(network, account=subject, issuer,
   credentialTypePlain="KYC-Tier1")
→ sign by subject → submit → tesSUCCESS
```

After accept: reserve moves to the subject (+2 XRP owner reserve), `lsfAccepted` flag is set.

### 3. Recipient enables DepositAuth

```text
agent ← {"step":"recipient_enable","recipient":"rMerchant..."}

→ xrpl_account_set_prepare(network, account=recipient, setFlag=9 /*asfDepositAuth*/)
→ sign by recipient → submit → tesSUCCESS
```

The recipient now blocks every unsolicited Payment. Selective whitelisting follows.

### 4. Recipient preauthorises the credential

```text
agent ← {"step":"preauth","recipient":"...","issuer":"rIssuerKYC...",
         "credentialTypePlain":"KYC-Tier1"}

→ xrpl_deposit_preauth_prepare(network, account=recipient,
   authorizeCredentialsJson="[{\"issuer\":\"rIssuerKYC...\",
     \"credentialType\":\"4B59432D54696572 31\"}]")
→ sign by recipient → submit → tesSUCCESS
```

From now on, **any** account holding an accepted credential `(rIssuerKYC..., 4B59...)` can pay the recipient.

### 5. Subject pays with credential

The sender attaches the credential hash in `Payment.CredentialIDs`. The hash is computed locally via `xrpl_hash_credential` (pure-local SHA-512/2, no network call):

```text
agent ← {"step":"pay","subject":"rSubject...","recipient":"rRecipient...","amountDrops":"1000000",
         "credentialTypePlain":"KYC-Tier1","issuer":"rIssuer..."}

Step 5a: compute credential hash (pure local)
→ xrpl_hash_credential(
    subject="rSubject...",
    issuer="rIssuer...",
    credentialTypePlain="KYC-Tier1"
  ) → "<64-hex Hash256>"

Step 5b: build payment with credential reference
→ xrpl_payment_prepare(
    network="testnet",
    account="rSubject...",
    destination="rRecipient...",
    amount="1000000",
    credentialIdsJson='["<64-hex Hash256>"]'
  )
→ xrpl_tx_preflight → feasible=true
→ xrpl_sign(walletName=rSubject, blob)
→ xrpl_tx_submit_signed → tesSUCCESS
```

`credentialIdsJson` — array of 1..8 64-hex Hash256 strings. The tool validates format (length, hex charset, dedup) before submit. Without a correct credential: `tecNO_PERMISSION`.

### 6. Verify deposit_authorized

```text
agent → xrpl_account_credentials(account=subject)
       → { "held": [{ "issuer":"rIssuerKYC...","credentialTypeHex":"...",
                       "accepted":true,"expirationUtc":"2026-12-31T23:59:59Z" }] }
       ↓ assert: matched && accepted && now < expirationUtc
```

Additionally you can call the rippled `deposit_authorized` RPC directly through an `xrpl_tx_prepare_generic`-style escape to get the authoritative answer.

### 7. Revocation

**Issuer revoke** (e.g. KYC compliance violation):

```text
agent ← {"step":"revoke","by":"issuer","subject":"...","credentialTypePlain":"KYC-Tier1"}
→ xrpl_credential_delete_prepare(account=issuer, subject, credentialTypePlain)
→ sign by issuer → submit → tesSUCCESS
```

**Subject un-accept** (the user opts out):

```text
agent ← {"step":"revoke","by":"subject","issuer":"...","credentialTypePlain":"KYC-Tier1"}
→ xrpl_credential_delete_prepare(account=subject, issuer, credentialTypePlain)
→ sign by subject → submit → tesSUCCESS
```

After delete: the next Payment with this credentialID → `tecNO_PERMISSION`. Reserve returns to whoever was holding it.

### 8. Expiry sweep (anyone)

After Expiration any account may submit `CredentialDelete` with both `Subject` and `Issuer` — it releases the reserve, no specific party pays.

## Verification checklist

- [ ] Step 1: `xrpl_account_credentials(account=issuer)` → entry in `issued[]`, `accepted=false`.
- [ ] Step 2: same call → `accepted=true`. Also `xrpl_account_credentials(account=subject)` → entry in `held[]`.
- [ ] Step 3: `xrpl_account_info(account=recipient)` → `account_flags.depositAuth=true`.
- [ ] Step 4: `xrpl_account_objects(account=recipient, type="DepositPreauth")` → entry with `AuthorizeCredentials` array.
- [ ] Step 5: recipient's balance grew by amount−fee; subject's dropped.
- [ ] Step 7: `xrpl_account_credentials` for both no longer shows the entry.

## Closed feature gaps

- ✅ **`feature-gap-001` closed** — `xrpl_payment_prepare` now accepts an optional `credentialIdsJson` parameter (1..8 64-hex Hash256 strings).
- ✅ **`feature-gap-002` closed** — `xrpl_hash_credential(subject, issuer, credentialTypeHex|credentialTypePlain)` tool. Pure-local SHA-512/2 via the SDK helper `Xrpl.Utils.Hashes.HashCredential`.

The recipe is drop-in with no workarounds.

## Use-cases

- **Compliance KYC**: a single issuer (regulated entity) issues credentials to many subjects. Merchants only accept payments from KYC-holders.
- **Permissioned DEX/AMM**: combined with XLS-80 PermissionedDomain — a pool trades only between accredited investors.
- **Subscription gating**: a provider issues expirable credentials. On expiry — automatic revocation.
- **Whitelist airdrop**: airdrop via Payment + CredentialIDs only to confirmed users.
