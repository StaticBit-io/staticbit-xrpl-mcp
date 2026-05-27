> 🇬🇧 [Read in English](kyc-credentials-flow.md)

# Пример: KYC credentials flow (XLS-70)

Полный жизненный цикл credential-based KYC: issuer выписывает credential subject'у, subject его accepts, recipient включает DepositAuth + DepositPreauth со ссылкой на credential, sender (subject) делает Payment предъявляя credential, recipient (получатель) проверяет.

Референс: [TestICredential.TestCredential_EndToEnd_DepositPreauthAndPayment](https://github.com/StaticBit-io/XrplCSharp/blob/release/Tests/Xrpl.Tests/Integration/transactions/TestICredential.cs#L312).

## Что используется

| Плагин | Tools |
|---|---|
| **xrpl-cloud** или **xrpl-local** | `xrpl_credential_create_prepare`, `xrpl_credential_accept_prepare`, `xrpl_credential_delete_prepare`, `xrpl_account_set_prepare`, `xrpl_deposit_preauth_prepare`, `xrpl_payment_prepare` (с `credentialIdsJson`), `xrpl_hash_credential` (pure-local helper), `xrpl_account_credentials`, `xrpl_tx_preflight`, `xrpl_tx_submit_signed` |
| **xrpl-signer** | `xrpl_sign` |

## Концепция (XLS-70)

- **Credential** ≈ on-chain «бейдж» формата `(Issuer, Subject, CredentialType-hex)` с опциональным `URI` и `Expiration`.
- Состояния: `provisional` (после CredentialCreate, до Accept) и `accepted` (после CredentialAccept, флаг `lsfAccepted=0x10000` в ledger entry).
- Reserve: до Accept — на issuer'е, после Accept — переезжает на subject'а.
- **DepositPreauth с AuthorizeCredentials**: recipient авторизует не конкретный адрес, а **набор credentials**. Любой holder с принятым credential проходит.
- **Payment.CredentialIDs**: sender при отправке прикладывает hash credential'а (SHA-512/2 от subject+issuer+type-hex). Ledger проверяет что credential существует, accepted, не expired, и что recipient его preauthorized.

## Архитектура

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

- 3 funded аккаунта: ISSUER, SUBJECT, RECIPIENT.
- Все три импортированы в keystore.
- XLS-70 amendment активирован (на standalone testnet — да, на mainnet — проверить через `xrpl_server_state`).

## Промт агента

```markdown
---
name: kyc-credentials
description: Orchestrates the XLS-70 KYC credentials end-to-end flow:
  create credential, accept, enable DepositAuth, AuthorizeCredentials,
  pay with credential, optionally revoke.
tools: xrpl_credential_create_prepare, xrpl_credential_accept_prepare,
  xrpl_credential_delete_prepare, xrpl_account_set_prepare,
  xrpl_deposit_preauth_prepare, xrpl_payment_prepare,
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
   "amountDrops":"1000000","credentialIdsJson":"[\"<sha512half hex>\"]"}
- {"step":"verify","network":"...","subject":"r...","recipient":"r...",
   "credentialIdsJson":"[\"<hash>\"]"}
- {"step":"revoke","network":"...","by":"issuer"|"subject","subject":"r...",
   "issuer":"r...","credentialTypePlain":"KYC-Tier1"}

For each tx step:
1. Build via the appropriate `*_prepare` tool.
2. `xrpl_tx_preflight` → bail if `feasible=false`.
3. `xrpl_sign(walletName=submitter, txBlobUnsigned)`.
4. `xrpl_tx_submit_signed(..., waitForValidation=true)`.

For "verify" step:
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

После accept'а: reserve переезжает на subject'а (+2 XRP owner reserve), флаг `lsfAccepted` устанавливается.

### 3. Recipient enables DepositAuth

```text
agent ← {"step":"recipient_enable","recipient":"rMerchant..."}

→ xrpl_account_set_prepare(network, account=recipient, setFlag=9 /*asfDepositAuth*/)
→ sign by recipient → submit → tesSUCCESS
```

Теперь recipient блокирует все unsolicited Payment'ы. Дальше — selective whitelisting.

### 4. Recipient preauthorizes credential

```text
agent ← {"step":"preauth","recipient":"...","issuer":"rIssuerKYC...",
         "credentialTypePlain":"KYC-Tier1"}

→ xrpl_deposit_preauth_prepare(network, account=recipient,
   authorizeCredentialsJson="[{\"issuer\":\"rIssuerKYC...\",
     \"credentialType\":\"4B59432D54696572 31\"}]")
→ sign by recipient → submit → tesSUCCESS
```

С этого момента **любой** account с принятым credential `(rIssuerKYC..., 4B59...)` может посылать платежи recipient'у.

### 5. Subject pays with credential

Sender прикладывает hash credential'а в `Payment.CredentialIDs`. Hash считается локально через `xrpl_hash_credential` (pure-local SHA-512/2, без network call):

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

`credentialIdsJson` — массив 1..8 64-hex Hash256 строк. Tool валидирует формат (длина, hex-charset, dedup) перед submit'ом. Без правильного credential: `tecNO_PERMISSION`.

### 6. Verify deposit_authorized

```text
agent → xrpl_account_credentials(account=subject)
       → { "held": [{ "issuer":"rIssuerKYC...","credentialTypeHex":"...",
                       "accepted":true,"expirationUtc":"2026-12-31T23:59:59Z" }] }
       ↓ assert: matched && accepted && now < expirationUtc
```

Дополнительно можно вызвать rippled `deposit_authorized` RPC напрямую через `xrpl_tx_prepare_generic`-style escape, чтобы получить authoritative answer.

### 7. Revocation

**Issuer revoke** (например, KYC compliance violation):

```text
agent ← {"step":"revoke","by":"issuer","subject":"...","credentialTypePlain":"KYC-Tier1"}
→ xrpl_credential_delete_prepare(account=issuer, subject, credentialTypePlain)
→ sign by issuer → submit → tesSUCCESS
```

**Subject un-accept** (пользователь отказывается от credential'а):

```text
agent ← {"step":"revoke","by":"subject","issuer":"...","credentialTypePlain":"KYC-Tier1"}
→ xrpl_credential_delete_prepare(account=subject, issuer, credentialTypePlain)
→ sign by subject → submit → tesSUCCESS
```

После delete'а: следующая попытка Payment с этим credentialID → `tecNO_PERMISSION`. Reserve возвращается тому кто его держал.

### 8. Expiry sweep (anyone)

После Expiration любой account может сделать `CredentialDelete` с обоими `Subject` и `Issuer` — освобождает reserve, никто конкретно не платит.

## Verification checklist

- [ ] Step 1: `xrpl_account_credentials(account=issuer)` → entry в `issued[]`, `accepted=false`.
- [ ] Step 2: тот же запрос → `accepted=true`. Также `xrpl_account_credentials(account=subject)` → entry в `held[]`.
- [ ] Step 3: `xrpl_account_info(account=recipient)` → `account_flags.depositAuth=true`.
- [ ] Step 4: `xrpl_account_objects(account=recipient, type="DepositPreauth")` → запись с `AuthorizeCredentials` массивом.
- [ ] Step 5: balance recipient'а вырос на amount-fee; subject'а — упал.
- [ ] Step 7: `xrpl_account_credentials` для обоих больше не показывает entry.

## Закрытые feature-gaps

- ✅ **`feature-gap-001` closed** — `xrpl_payment_prepare` теперь принимает optional `credentialIdsJson` параметр (1..8 64-hex Hash256 строк).
- ✅ **`feature-gap-002` closed** — добавлен `xrpl_hash_credential(subject, issuer, credentialTypeHex|credentialTypePlain)` tool. Pure-local SHA-512/2 через SDK helper `Xrpl.Utils.Hashes.HashCredential`.

Рецепт работает drop-in без workaround'ов.

## Use-cases

- **Compliance KYC**: один issuer (regulated entity) выписывает credentials множеству subjects. Merchants могут принимать платежи только от holders' KYC'ов.
- **Permissioned DEX/AMM**: combined с XLS-80 PermissionedDomain — пул торгует только между accredited investors.
- **Subscription gating**: subscription provider выдаёт expirable credentials. По истечению — automatic revocation.
- **Whitelist airdrop**: airdrop'ить через Payment + CredentialIDs только подтверждённым users.
