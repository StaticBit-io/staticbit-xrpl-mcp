>  🌐 **Language**: **English** | [Русский](ru/features.md)

# StaticBit XRPL MCP — Features

Catalogue of implemented functionality. Not a roadmap — only features that already exist in the code and are covered by tests.

**Current numbers**:

- **131 MCP tools** (machine-readable JSON-Schema catalogue: [docs/tools-schema.json](tools-schema.json)).
- **432 unit tests** (Core: 357, Server: 47, Signer: 28) + **34 integration smoke** tests against testnet (23 more `[Ignore]`'d for draft amendments — Vault / XChain / Loan).
- All 4 projects (Abstractions, Core, Server, Signer) + 4 test projects + the `tools/SchemaGen` utility + plugin marketplace with 3 plugins (`xrpl-cloud`, `xrpl-local`, `xrpl-signer`).

Related documents:

- [docs/glossary.md](glossary.md) — XRPL terminology used in tool descriptions.
- [docs/supply-chain.md](supply-chain.md) — release artifacts and verification.
- [docs/examples/](examples/) — Cowork-agent recipes on top of the plugins.
- [docs/branch-protection.md](branch-protection.md) — recommended `main` branch protection settings.
- [INSTALL.md](INSTALL.md), [DEPLOY.md](DEPLOY.md), [RELEASE.md](../RELEASE.md) — installation, VPS deployment, release process.

---

## 1. XRPL transaction coverage (typed prepare wrappers)

Each listed tool returns a **PreparedTransaction** — autofilled unsigned blob + signing data + human-readable summary for the approval flow. The server never sees a seed; signing happens offline through `xrpl-signer`.

### 1.1. Account management

- `xrpl_account_set_prepare` — `AccountSet` with all flags (`DefaultRipple`, `RequireAuth`, `RequireDest`, `DepositAuth`, `AllowTrustLineClawback`, `DisallowIncomingXRP/NFTokenOffer/Check/PayChan/Trustline`), and fields `Domain`, `EmailHash`, `MessageKey`, `TickSize`, `TransferRate`, `NFTokenMinter`.
- `xrpl_set_regular_key_prepare` — `SetRegularKey`.
- `xrpl_signer_list_set_prepare` — `SignerListSet` (for multi-sign).
- `xrpl_signer_list_status` — helper on top of `account_objects(type=SignerList)`: returns `{quorum, totalAvailableWeight, signers[]}`; accepts `alreadySignedAccountsCsv` and computes `collectedWeight` + `deltaToQuorum` + `quorumReached`.
- `xrpl_deposit_preauth_prepare` — `DepositPreauth` (sender whitelist), including the XLS-70 credential-based variant: `authorizeCredentialsJson` / `unauthorizeCredentialsJson` (arrays of `{issuer, credentialType-hex}`, 1–8 entries, mutually exclusive with address-based authorization).
- `xrpl_account_delete_prepare` — `AccountDelete`. Pre-flight is extended inside `xrpl_tx_preflight`: checks `account_objects(deletion_blockers_only=true)` (when blockers exist → `feasible=false` + their types), and the rule `currentLedger ≥ account.Sequence + 256` (reports how many ledgers left to wait when not met).

### 1.2. NFT (XLS-20)

- Write: `NFTokenMint`, `NFTokenBurn`, `NFTokenCreateOffer`, `NFTokenCancelOffer`, `NFTokenAcceptOffer`.
- Read: `account_nfts`, `nft_buy_offers`, `nft_sell_offers`.

### 1.3. Escrow

- `EscrowCreate`, `EscrowFinish`, `EscrowCancel` prepare tools.
- PREIMAGE-SHA-256 crypto-conditions support for `Condition`/`Fulfillment`.
- Read helper `xrpl_account_escrows` — `account_objects(type=Escrow)` split into `sent[]` (account = funder) and `received[]` (account = destination); each entry includes counterparty, amount + human-readable form, finishAfter/cancelAfter (UTC), condition, source/destination tags, previousTxnId.

### 1.4. PaymentChannel

- `PaymentChannelCreate`, `PaymentChannelFund`, `PaymentChannelClaim` prepare tools.

### 1.5. Check

- `CheckCreate`, `CheckCash`, `CheckCancel` prepare tools.

### 1.6. AMM (XLS-30)

- Full coverage: `AMMCreate`, `AMMDeposit`, `AMMWithdraw`, `AMMVote`, `AMMBid`, `AMMDelete`.

### 1.7. Token issuer flow

- `xrpl_clawback_prepare` — `Clawback`.
- Freeze/Unfreeze helper on top of `TrustSet` (flags `tfSetFreeze`/`tfClearFreeze`, `tfSetDeepFreeze`/`tfClearDeepFreeze`).

### 1.8. Multi-sign UX

- `xrpl_sign_multi`, `xrpl_sign_combine` — collect a partial signature and merge multiple into one.
- `xrpl_signer_list_status` (see §1.1) — shows quorum, currently collected weight, delta to quorum, and `unknownSigners` flag (when the csv includes addresses absent from the on-chain SignerList).

### 1.9. Multi-Purpose Tokens (XLS-33)

Non-trustline fungible tokens: issuer creates `MPTokenIssuance`, holders own per-account `MPToken`. All 4 prepare tools return an autofilled UNSIGNED transaction; the on-chain `MPTokenIssuanceID` (48-hex) is derived from `Issuer + Sequence` after validation.

- `xrpl_mpt_issuance_create_prepare` — `MPTokenIssuanceCreate`. Accepts `assetScale` (0..10), `maximumAmount` (uint64 string), `transferFee` (0..50000 = 0..50% in 1/10 bps), `metadataHex` (≤1024 raw bytes / ≤2048 hex chars), plus flags: either `flagsBitmask` (uint) or the booleans `canLock`/`requireAuth`/`canEscrow`/`canTrade`/`canTransfer`/`canClawback` (mutually exclusive with bitmask). Internal validation: `transferFee>0` requires `tfMPTCanTransfer`; unknown bits in bitmask are rejected.
- `xrpl_mpt_issuance_destroy_prepare` — `MPTokenIssuanceDestroy`. Allowed only when `OutstandingAmount=0`.
- `xrpl_mpt_issuance_set_prepare` — `MPTokenIssuanceSet`. Global lock/unlock (entire issuance) or per-holder lock/unlock via optional `holder`. The `lockBalance` parameter: `true` → tfMPTLock, `false` → tfMPTUnlock, `null` → no-op (reserved for future fields).
- `xrpl_mpt_authorize_prepare` — `MPTokenAuthorize`. Dual role: holder opt-in (account=holder, no `holder` param) or issuer authorizes a specific holder (account=issuer, holder=target). `unauthorize=true` → tfMPTUnauthorize (revoke).
- Read helpers (see §2): `xrpl_account_mpt_issuances` (issuances owned by the account as issuer), `xrpl_account_mpts` (MPT holdings of the account issued by other accounts).
- `xrpl_tx_preflight` (§3) extended: AssetScale > 10, TransferFee > 50000, TransferFee>0 without tfMPTCanTransfer, MaximumAmount overflow, metadata > 2048 hex chars, MPTokenIssuanceSet with both lock+unlock set, Authorize with Holder=Account — all surface as warnings + `feasible=false`.
- `xrpl_tx_explain` (§3) recognises all 4 MPT tx types and decodes flags into a human string (`[CanLock|RequireAuth|CanTransfer]`).

### 1.10. Batch (XLS-56)

Atomic execution of up to 8 inner transactions under a single outer Batch. The outer Batch account pays the fee and submits the transaction; inner tx may belong to any Account but require BatchSigners for multi-account flows.

- `xrpl_batch_prepare` — accepts `mode` ∈ {`AllOrNothing`, `OnlyOne`, `UntilFailure`, `Independent`} and `innerTransactionsJson` (JSON array of 1..8 tx objects). The tool automatically: ORs `tfInnerBatchTxn` (0x40000000) into each inner Flags, forces `Fee="0"` and `SigningPubKey=""`, strips `TxnSignature`/`Signers`, rejects nested Batches, requires `Sequence` or `TicketSequence` on every inner (autofill cannot cross account boundaries). Optional `batchSignersJson` — array of `{account, signingPubKey?, txnSignature?, signers?}` for multi-account batches.
- `xrpl_tx_preflight` (§3) — Batch warnings: exactly one mode flag, ≤8 inner tx, no nested Batch, `tfInnerBatchTxn` present on every inner, `Fee="0"`, `SigningPubKey=""`, no `TxnSignature`/`Signers`.
- `xrpl_tx_explain` (§3) unpacks Batch: prints mode, inner-tx count, and recursively explains each inner-tx on one line.

### 1.11. Spot amendments (quick wins)

Five standalone prepare tools that close the SDK lag against mainnet-active amendments:

- `xrpl_ticket_create_prepare` — `TicketCreate`. Reserves 1..250 sequence numbers as Tickets (+1 owner-object and ~2 XRP reserve per ticket). Any later transaction may use `TicketSequence` instead of `Sequence`. Preflight checks the 1..250 range.
- `xrpl_nft_modify_prepare` — XLS-46 `NFTokenModify`. Changes the URI of an NFT minted with `tfMutable=16`. Accepts `uriHex` / `uriPlain` (UTF-8 → hex auto) / `clearUri=true` (mutually exclusive). Optional `owner` when the submitter is the issuer / authorized minter. Preflight validates the 64-char hex NFTokenID and URI hex parity.
- `xrpl_oracle_set_prepare` / `xrpl_oracle_delete_prepare` — XLS-47 price oracles. OracleSet: `oracleDocumentId` (uint32), `lastUpdateTimeUnix` (Unix seconds, ±300s from ledger close), `priceDataSeriesJson` (1..10 entries: `{baseAsset, quoteAsset, assetPrice?, scale?}`; `assetPrice`/`scale` are paired, scale 0..10, base/quote — 3-char or 40-char hex); optional ASCII `provider`/`uri`/`assetClass` are auto hex-encoded. OracleDelete: just `oracleDocumentId`.
- `xrpl_delegate_set_prepare` — XLS-75 `DelegateSet`. Delegates a subset of transactions to another account via `permissionsCsv` (1..10 tx-type names, no duplicates). Non-delegable types (AccountSet, SetRegularKey, SignerListSet, DelegateSet) are rejected client-side. Empty CSV → clear delegation.
- `xrpl_tx_explain` (§3) recognises all 5 types; `xrpl_tx_preflight` (§3) — type-specific warnings (TicketCount range, OracleSet PriceDataSeries count, OracleSet LastUpdateTime, DelegateSet duplicates / non-delegable / Authorize=Account, NFTokenModify NFTokenID format / URI hex parity).

### 1.12. Compliance / Identity (XLS-37 / 40 / 70 / 80)

Related bundle for controlled issuance and on-chain identity. Stitches together with the existing `xrpl_deposit_preauth_prepare` (XLS-70 credential-based authorize) and MPT `tfMPTRequireAuth` (§1.9).

**Standalone Credentials (XLS-70)** — `xrpl_credential_create_prepare` (issuer → subject, provisional), `xrpl_credential_accept_prepare` (subject ratifies, reserve moves to subject), `xrpl_credential_delete_prepare` (issuer revoke / subject un-accept / any account after expiration). Accept `credentialType` / `uri` as hex **or** plain text (mutually exclusive). Optional `expirationUtc`. Limits: CredentialType ≤ 128 hex chars, URI ≤ 512 hex chars (= 256 bytes).

**PermissionedDomains (XLS-80)** — `xrpl_permissioned_domain_set_prepare` (create / modify via optional 64-hex `domainId`) with `acceptedCredentialsJson` (JSON array `{issuer, credentialType-hex}`, 1..10, dedup by (issuer, type-uppercase)). `xrpl_permissioned_domain_delete_prepare`. On modify, AcceptedCredentials **fully replaces** the previous list.

**DID (XLS-40)** — `xrpl_did_set_prepare` accepts 3 fields (`data` / `didDocument` / `uri`), each in hex or plain (mutually exclusive within a pair); at least one field is required; each ≤ 512 hex chars (= 256 bytes). `xrpl_did_delete_prepare` — no fields. An account owns at most one DID.

**AMMClawback (XLS-37)** — `xrpl_amm_clawback_prepare`. Issuer (`account`) reclaims their own tokens deposited by `holder` into an AMM pool. `asset1` is the issued token (`asset1Issuer` MUST equal `account`); `asset2` is the counterpart (XRP allowed). Optional `amountValue` (decimal string) limits the claw-back. Requires `asfAllowTrustLineClawback`.

**Read helpers (see §2)** — `xrpl_account_credentials` (split into `issued` / `held`, flag `accepted=lsfAccepted`, UTF-8 decode of hex fields), `xrpl_account_did` (single object or null), `xrpl_account_permissioned_domains` (with AcceptedCredentials unpacked).

**`xrpl_tx_explain` (§3)** recognises all 8 tx types. **`xrpl_tx_preflight` (§3)** — type-specific warnings (CredentialType hex parity + length, Subject/Issuer ≠ Account, CredentialDelete requires at least one of Subject/Issuer, PermissionedDomainSet AcceptedCredentials 1..10 + dedup, DIDSet at least one field + size, AMMClawback Holder ≠ Account + Asset.issuer == Account).

### 1.13. Single-Asset Vault (XLS-65)

A DeFi vault primitive: the owner creates a vault with a single asset (XRP / IOU / MPT), depositors receive shares-MPT (auto-issued from the pseudo-account), withdrawals follow `WithdrawalPolicy` (uint code). Optionally private (requires credentialled access via PermissionedDomain) and/or with non-transferable shares.

- `xrpl_vault_create_prepare` — create. Parameters: `assetCurrency`/`assetIssuer`, `amountValue` (initial deposit); optional `assetsMaximum` (STNumber), `metadataHex` (for share-MPT, ≤2048 hex), `dataHex` (≤512 hex), `domainId`, `withdrawalPolicy`, `scale` (0..18), `isPrivate` / `sharesNonTransferable` (creation-time only). Composite flags: `tfVaultPrivate=0x10000` + `tfVaultShareNonTransferable=0x20000`.
- `xrpl_vault_set_prepare` — modify (Data / AssetsMaximum / DomainID). At least one field required. Empty `domainId=""` clears it.
- `xrpl_vault_delete_prepare` — empty vault only (AssetsTotal=0).
- `xrpl_vault_deposit_prepare` — deposit asset, receive shares-MPT.
- `xrpl_vault_withdraw_prepare` — dual-mode: `amountKind='asset'` (exact asset amount, shares burned at rate) or `'shares'` (redeem N shares for asset). Shares mode requires `shareMptIssuanceId` (48-hex, from `vault.ShareMPTID`). Optional `destination` + `destinationTag`.
- `xrpl_vault_clawback_prepare` — issuer clawback from the vault. Omit amount → max available. XRP cannot be clawed back.
- **Read helper** `xrpl_account_vaults` (see §2) — returns `vaultId`, `pseudoAccount`, `asset`, `assetsTotal/Available/Maximum`, `lossUnrealized`, `shareMptIssuanceId`, `withdrawalPolicy`, `scale`, `dataHex` + `dataUtf8`, `domainId`.

### 1.14. XChain bridges (XLS-38)

Cross-chain bridge flow between locking and issuing chains via door accounts. All 8 prepare tools use the common 4-tuple `bridgeJson`: `{LockingChainDoor, LockingChainIssue, IssuingChainDoor, IssuingChainIssue}`. Supports XRP-XRP and IOU-IOU bridges.

**Lifecycle:**
1. `xrpl_xchain_create_bridge_prepare` — the door account on each chain mirrors the bridge (`signatureRewardDrops` required; `minAccountCreateDrops` optional for XRP-XRP — enables the AccountCreate flow).
2. `xrpl_xchain_modify_bridge_prepare` — change `SignatureReward` / `MinAccountCreateAmount`. `clearMinAccountCreate=true` sets `tfClearAccountCreateAmount=0x10000` (mutually exclusive with `minAccountCreateDrops`).
3. `xrpl_xchain_create_claim_id_prepare` — recipient on the destination chain reserves a claim-id, fixing `otherChainSource` (sender address on source chain).
4. `xrpl_xchain_commit_prepare` — sender on the source chain commits funds with the claim-id. Optional `otherChainDestination` (omit → requires an explicit XChainClaim).
5. `xrpl_xchain_claim_prepare` — explicit claim on the destination chain (when commit didn't specify destination).
6. `xrpl_xchain_account_create_commit_prepare` — XRP-XRP only, creates a new account on the destination chain through the bridge.
7. `xrpl_xchain_add_claim_attestation_prepare` — witness server submits an ECDSA attestation for the claim flow. Fields: `attestationRewardAccount`, `attestationSignerAccount`, `otherChainSource`, `publicKeyHex`, `signatureHex`, `wasLockingChainSend` (0/1).
8. `xrpl_xchain_add_account_create_attestation_prepare` — same for the account-create flow, plus `xchainAccountCreateCount`.

- **Read helper** `xrpl_account_bridges` (see §2) — returns the `xchainBridge` 4-tuple, `signatureReward`, `minAccountCreateAmount`, current counters `xchainClaimId` / `xchainAccountCreateCount` / `xchainAccountClaimCount`.
- **`xrpl_tx_explain` (§3)** recognises all 14 tx types (Vault + XChain). **`xrpl_tx_preflight` (§3)** — type-specific warnings: VaultID 64-hex, Scale 0..18, Data parity + size, Holder ≠ Account for clawback; bridge {LockingChainDoor, IssuingChainDoor} must differ, Issues must be objects, required fields for attestation flows, `WasLockingChainSend ∈ {0,1}`.

### 1.15. Lending (XLS-66 LoanBroker / Loan)

Bleeding-edge DeFi lending: a LoanBroker sits on top of a Vault and manages a pool of loans with cover capital (first-loss). All 9 prepare tools rely on 64-hex hash IDs (LoanBrokerID, LoanID).

**LoanBroker write flow** ([LoanBrokerTools.cs](../src/StaticBit.Xrpl.Mcp.Core/Tools/LoanBrokerTools.cs)):

- `xrpl_loan_broker_set_prepare` — create (omit `loanBrokerId`) / modify. Parameters: `vaultId`, `coverRateMinimum` / `coverRateLiquidation` (1/100th bp, 0..100000; liquidation ≤ minimum), `managementFeeRate` (1/10th bp, 0..10000), `debtMaximum` (STNumber), `dataHex` (≤512 hex).
- `xrpl_loan_broker_delete_prepare` — empty broker only.
- `xrpl_loan_broker_cover_deposit_prepare` / `_cover_withdraw_prepare` — first-loss capital deposit/withdraw; the asset must match the vault's.
- `xrpl_loan_broker_cover_clawback_prepare` — issuer claw-back. At least one of `loanBrokerId` or `(amountValue + assetCurrency)`. XRP cannot be clawed back.

**Loan write flow** ([LoanTools.cs](../src/StaticBit.Xrpl.Mcp.Core/Tools/LoanTools.cs)):

- `xrpl_loan_set_prepare` — origination. Lender ≠ borrower. All 5 rate fields (interest/late/close/overpayment/overpayment-fee) are validated ≤100000. Optional fee fields (STNumber): origination/service/late/close. `paymentTotal` / `paymentInterval` / `gracePeriod` (defaults 1/60/60 in the SDK). `allowOverpayment=true` → tfLoanOverpayment.
- `xrpl_loan_manage_prepare` — `action` ∈ {`default`, `impair`, `unimpair`} (mutually exclusive flags `tfLoanDefault=0x10000` / `tfLoanImpair=0x20000` / `tfLoanUnimpair=0x40000`).
- `xrpl_loan_pay_prepare` — borrower payment. `paymentKind` ∈ {`scheduled` (default, no flag), `overpayment`, `full`, `late`} — the last three mutually exclusive.
- `xrpl_loan_delete_prepare` — closed / fully repaid loan only.

**Read helpers (see §2):**

- `xrpl_account_loan_brokers` — `loanBrokerId`, `pseudoAccount`, `vaultId`, debt counters (`debtTotal`/`debtMaximum`), `coverAvailable`, cover-rates, `managementFeeRate`, `activeLoanCount`, `dataUtf8`.
- `xrpl_account_loans` — `loanId`, `borrower`, `loanBrokerId`, all interest/fee rates, principal counters (`principalOutstanding`/`principalRequested`/`totalValueOutstanding`), `periodicPayment`, `managementFeeOutstanding`, schedule (`startDate`/`previousPaymentDueDate`/`nextPaymentDueDate` UTC ISO-8601, `paymentInterval`/`gracePeriod`/`paymentRemaining`), `loanScale`.

**`xrpl_tx_explain` (§3)** recognises all 9 types. **`xrpl_tx_preflight` (§3)** — type-specific warnings: LoanBrokerID/LoanID/VaultID 64-hex, CoverRates ≤100000 + liquidation ≤ minimum, ManagementFeeRate ≤10000, LoanSet rate fields ≤100000, Counterparty ≠ Account, LoanManage exactly-one action flag, LoanPay payment-kind mutual exclusivity, LoanBrokerCoverClawback requires at least one of ID/Amount.

> Note: XLS-66 is still in draft. The SDK contains the types, but activation on mainnet is unconfirmed — preflight catches SDK-level validation, but network-side rules (lender authorization, cover-rate post-tx check, etc.) are checked only on on-chain submit.

---

## 2. Read API and streaming

| Tool | Status | Notes |
|---|---|---|
| `xrpl_ripple_path_find` | ✅ | One-shot pathfinder for cross-currency Payment (HTTP/WebSocket). |
| `xrpl_path_find_create` / `_status` / `_close` | ✅ | WebSocket-only long-running pathfinder; only works in stdio deployment. |
| `xrpl_gateway_balances` | ✅ | Issuer obligations, with hotwallet exclusion. |
| `xrpl_server_state` | ✅ | Machine-readable load factors, validation quorum, build. |
| `xrpl_server_definitions` | ✅ | FIELDS/LEDGER_ENTRY_TYPES/TRANSACTION_TYPES + content hash. |
| `xrpl_subscribe` / `xrpl_unsubscribe` | ⚠️ Pass-through | Subscription is set on the pool's shared WebSocket; events are **not delivered back** through MCP (see below). |
| `xrpl_account_tx_since` | ✅ | Polling-based alternative to subscribe: stateless, works in any cloud/local/HTTP deployment. |
| `xrpl_manifest` | ✅ | Validator manifest by public_key. SDK lacks a typed method — implemented via `GRequest<JsonNode, ManifestRequest>` with inline envelope description. |
| `xrpl_hash_credential` | ✅ | Pure-local SHA-512/2 computation of a Credential ledger-object id from (subject, issuer, credentialType). Used to build `Payment.CredentialIDs` for XLS-70 credential-gated deposits. No network call. |

### Subscribe/unsubscribe — honest limitations

MCP is request/response, **not bidirectional**. The cloud server holds ONE WebSocket per network through `XrplClientPool`, shared between all MCP clients. `xrpl_subscribe` installs the subscription, but events arrive on that shared WebSocket and **are not routed back** to the particular MCP call.

The tool is kept as plumbing for future server-side watchers / admin alerts (e.g. an AdminAlerter could consume them).

**For real monitoring** — use `xrpl_account_tx_since`: passing the highest `ledger_index` seen previously gives stateless, idempotent polling that works in any deployment mode.

---

## 3. Transaction-preparation UX

- **`xrpl_tx_simulate`** — dry-run simulation via `xrpl_tx_preflight` + (for Payment) `ripple_path_find` + current open-ledger fee + recommended LastLedgerSequence.
- **`xrpl_tx_explain`** — turns a decoded tx (blob hex or JSON) into a human one-liner: *"Payment from rA... to rB...: 10000000 drops XRP. [fee=12 drops, seq=42, LLS=1234]"*. Pure local, no network calls. Covers all main types (Payment, TrustSet, Offer\*, AMM\*, NFToken\*, Escrow\*, Check\*, PaymentChannel\*, AccountSet, SetRegularKey, DepositPreauth, SignerListSet, AccountDelete, Clawback, MPToken\*, Batch, Vault\*, XChain\*, LoanBroker\*, Loan\*, Credential\*, DID\*, PermissionedDomain\*, AMMClawback, TicketCreate, DelegateSet, NFTokenModify, Oracle\*) + generic fallback.
- **`xrpl_tx_preflight`** — inspects `account_info` + `server_state`, computes reserves (base + owner_count × inc), compares against balance; for Payment additionally checks `RequireDestinationTag`/`DepositAuth`/`DisallowIncomingXRP` on the destination. Returns a structured report with `warnings[]`.
- **Fee escalation policy** — `LastLedgerSequenceOffset` is applied in `TransactionPreparer`; `FeeBumpMultiplier` for proactive over-pay during open-ledger escalation.
- **`xrpl_tx_decode_blob`** — returns meaningful errors (odd length / non-hex / decode exception / null result) instead of an empty `{}`.

---

## 4. Signer

- **HD wallets (BIP-44)** — `KeystoreEntry` supports optional fields (`Kind="seed"|"mnemonic"`, separately-encrypted mnemonic + BIP-39 passphrase + `DerivationPathTemplate`). Old seed records (without `Kind`) load as `Kind="seed"` without format change; the first HD record bumps file version 1 → 2. Default for HD = secp256k1 (XRPL BIP-44 convention from xrpl.js).
  - Tools: `xrpl_wallet_generate_mnemonic` (generates BIP-39 12/15/18/21/24 words, returns the mnemonic once for backup), `xrpl_wallet_derive_address` (returns `{address, publicKey}` for an arbitrary index without secrets), `xrpl_wallet_export_index` (returns an XRPL family-seed for a specific HD index — for importing into other tools).
  - Extended: `xrpl_wallet_import_mnemonic` (default `storeAsHd=true`, falls back to legacy seed mode via `storeAsHd=false`), `xrpl_wallet_export` (for mnemonic-kind returns the mnemonic + bip39Passphrase + template + algorithm), `xrpl_sign`/`xrpl_sign_multi` (parameter `index=0`; for seed-kind strictly 0).
- **Audit log** — `IAuditLogger` interface + `FileAuditLogger` (JSONL format `{ts,event,wallet,result,...}`) + `NullAuditLogger`. Opt-in via the `XRPL_SIGNER_AUDIT_LOG` env var (default: off). Logs successful signatures (`event="sign"`), failed decryptions (`event="decrypt_fail"` for compliance — failed passphrase attempts), and tx-level errors (`event="sign", result="error"`). Write failures → warning on stderr; the signer never crashes. Per-file perms 0600 on Unix.
- **Encrypted keystore** (initial baseline, preserved) — PBKDF2-SHA256 600k iterations + AES-256-GCM, per-record salt/IV. Atomic write (temp + rename). Master passphrase via `XRPL_SIGNER_PASSPHRASE` or `XRPL_SIGNER_PASSPHRASE_FILE`.

---

## 5. Server infrastructure

- **Server tests**: 47 unit tests covering BearerAuthMiddleware (health bypass, 404 for scanners, HTTPS enforcement, X-Forwarded-Proto, empty tokens, missing/wrong/valid bearer, label assignment, X-Forwarded-For first hop); AdminAlerter (disabled, event-disabled, dedup window + after window, distinct tags don't collapse, rate cap + reset after a minute + 0=off, queue capacity DropOldest); RequestLoggingMiddleware (disabled, health-skip, query-string opt-in/opt-out, label, status code, logs-on-exception); ServerOptions defaults; ResolveRateLimitPartitionKey (ip/token/both/unknown, case-insensitive).
- **Structured request logging** with redaction — `RequestLoggingMiddleware` logs Method, Path, Status, Duration, IP, label. Bodies are NOT logged (may contain r-addresses / amounts). Query string opt-in via `RequestLogging.IncludeQueryString`.
- **CORS** — `ServerOptions.Cors` (`Enabled`, `AllowedOrigins[]`, `AllowedHeaders[]`, `AllowedMethods[]`, `AllowCredentials`). Supports wildcard `["*"]` for AllowedOrigins. Wired BEFORE bearer auth so OPTIONS preflights don't require a token.
- **Graceful shutdown** for long-poll `wait_for_validation` — narrowed catch in `TransactionTools.SubmitSignedAsync` (`Exception` only, excluding `OperationCanceledException`). Cancellation propagates immediately.
- **Rate-limit per token** — `RateLimitOptions.PartitionBy={ip|token|both}` (default `ip` for compatibility). On `token`/`both` the partition key includes the bearer label. AdminAlert on excess includes both label and ip.
- **OpenTelemetry / metrics** — `XrplMcpMetrics` Meter in Core: `xrpl_mcp_pool_connections` gauge, `xrpl_mcp_pool_reconnects_total{network,reason}` counter with reasons cold/dropped/ttl/error, `xrpl_mcp_pool_connect_duration_seconds` histogram, `xrpl_mcp_tool_calls_total{tool,status,label}` + `xrpl_mcp_tool_duration_seconds` for HTTP MCP calls. The server optionally enables a Prometheus scrape endpoint via `ServerOptions.Metrics.Enabled`, `Path` (default `/metrics`); auth is bypassed, defence via reverse proxy.
- **Connection pool health** — `XrplMcpOptions.ConnectionTtlMinutes` (default 0 = off). Reactive eviction on the next `GetAsync`: age > TTL → reconnect with reason="ttl" in the metric. The existing dropped-socket reconnect increments the counter with reason="dropped". `PoolEntry` stores `CreatedAt`. No separate background timer.
- **Integration tests** — project `tests/StaticBit.Xrpl.Mcp.Integration.Tests/` with `[TestCategory("Integration")]`, convention `*TestsI.cs` / `TestI_*`. **34 active + 23 [Ignore]** smoke tests against testnet (≈18s full run):
  - **Ledger** (5): `server_info`, `server_state`, `fee`, `account_info` funded address, `ledger validated`.
  - **Read helpers** (9): `account_mpt_issuances`, `account_mpts`, `account_credentials`, `account_did`, `account_permissioned_domains`, `account_vaults`, `account_bridges`, `account_loan_brokers`, `account_loans` — verify the correct JSON shape even when the result is empty.
  - **Prepare smoke** (20 active): MPT (4), Batch, Oracle (2), Ticket, Delegate (2), NFTokenModify (2), Credentials (3), PermissionedDomains (2), DID (2), AMMClawback. Each round-trips autofill + binary encoding + summary without signing/submit. The shared helper `PrepareSmokeAssert.Standard` checks `TxBlobUnsigned` non-empty, `Sequence`/`Fee`/`LastLedgerSequence` autofilled, `TransactionType` matches.
  - **[Ignore]'d** (23): Vault (XLS-65, 6 tests), XChain (XLS-38, 8 tests, sidechain-only), LoanBroker / Loan (XLS-66, 9 tests) — all three amendments are draft and not active on the standard rippletest.net. Remove `[Ignore]` when testing against a node where the amendments are active.
  - **`[DoNotParallelize]`** on every class — SDK `RequestManager` has an ID race when concurrent requests share a WebSocket; distinct classes (with their own pools) run in parallel, methods within a class run sequentially.
  - Workflow [.github/workflows/integration-tests.yml](../.github/workflows/integration-tests.yml) — daily cron + workflow_dispatch with URL override. Endpoint override via `XRPL_TESTNET_WS`.

---

## 6. Code quality

- `TransactionTools.ExtractBool` / `ExtractUInt` — replaced with `ReadBool`/`ReadUInt` working on `JsonNode`. The old implementation re-serialised then re-parsed the object on every call; in the `SubmitSignedAsync` polling loop that was 3 re-serialisations per iteration, now one. `TryGetTxHash` was rewritten on `JsonNode` too.
- Test naming convention `*TestsU.cs` + `TestU_` prefix — documented in README.md ("Test convention" section). `U`=Unit, filter `--filter TestU`. Reserved `*TestsI.cs` / `TestI_*` for integration.
- `Directory.Build.props` — `<TargetFramework>net10.0</TargetFramework>` as default; removed from every csproj except `Abstractions.csproj` (which keeps the explicit `netstandard2.1` override for embedding into older hosts).
- Unified `network` and `ledgerIndex` parameter descriptions — extracted into `public const string`s in `Services/ToolDescriptions.cs`, replaced across 20 files (the two variants of the ledgerIndex phrasing now converge to one canonical form).

---

## 7. Distribution and supply chain

Full summary + setup guide: [docs/supply-chain.md](supply-chain.md).

- **Auto-generated GitHub release notes** — `release-plugin.sh::group_by_conventional_commit` parses conventional commits (feat/fix/perf/refactor/docs/test/build/ci/chore) between tags and groups them under `### Features` / `### Fixes` / ... `### Other` subheadings.
- **SBOM (CycloneDX)** — `dotnet CycloneDX` in the release workflow generates `<plugin>-v<X>.cdx.json` for `xrpl-signer` and `xrpl-local` (skipped for `xrpl-cloud` — no shipped binaries). Attached to the Release.
- **SLSA build provenance attestation** — `actions/attest-build-provenance@v2` via GitHub OIDC. No secrets. Verified by `gh attestation verify`.
- **Per-RID tarballs + SHA-256 sidecars** — each `plugins/<name>/bin/<rid>/` is bundled into `<plugin>-v<X>-<rid>.tar.gz` + `.sha256` alongside.
- **Reproducible builds** — `Deterministic=true` always, `ContinuousIntegrationBuild=true` when `CI=true`/`GITHUB_ACTIONS=true`. Bit-identity for managed builds between runs of the same commit on the same SDK version.
- **macOS signer-binary notarization** — the workflow uses `rcodesign` (pure-Rust, runs on Linux runners without a macOS host). Conditionally skipped when `APPLE_*` secrets are unset. Required secrets are described in [docs/supply-chain.md](supply-chain.md).
- **Windows Authenticode for binaries** — workflow uses `osslsigncode` on a Linux runner. Conditionally skipped when `WINDOWS_PFX*` is unset. Required secrets are described in [docs/supply-chain.md](supply-chain.md).
- **Dependabot** ([.github/dependabot.yml](../.github/dependabot.yml)) — weekly NuGet + GitHub Actions updates. Groups minor/patch into a single PR; major upgrades stay solo. Closes recurring CVE warnings (e.g. OpenTelemetry.Api 1.9.0).
- **CodeQL** ([.github/workflows/codeql.yml](../.github/workflows/codeql.yml)) — weekly + on-PR static security analysis for C# (security-extended query set). Results publish to the Security tab.

---

## 8. Documentation

- [INSTALL.md](INSTALL.md) §13 Troubleshooting — 3 sub-sections for unsigned binaries: macOS Gatekeeper (`xattr -dr com.apple.quarantine`, `spctl --add`), Windows SmartScreen/Defender (`Unblock-File`, `Add-MpPreference -ExclusionPath`), Linux SELinux/AppArmor (`ausearch`, `chcon`/`semanage`, `DOTNET_BUNDLE_EXTRACT_BASE_DIR`).
- **JSON-schema tools catalogue** — the mini-project [`tools/SchemaGen/`](../tools/SchemaGen/) reflects over `[McpServerToolType]`+`[McpServerTool]` in the Core and Signer assemblies, emits [`docs/tools-schema.json`](tools-schema.json) in MCP `tools/list` format (name + description + JSON-Schema inputSchema, sorted by name). **131 tools covered.** Regenerate: `dotnet run --project tools/SchemaGen -- docs/tools-schema.json`.
- **Cowork agent recipes** — [`docs/examples/`](examples/) contains **12 ready-made workflows**, each cross-checked against an integration test from the upstream [XrplCSharp](https://github.com/StaticBit-io/XrplCSharp/tree/release/Tests/Xrpl.Tests/Integration/transactions) project. Index: [`docs/examples/README.md`](examples/README.md). Covered scenarios:
  - **Read + polling**: monitor-balance-telegram.
  - **MPT (XLS-33)**: controlled-mpt-issuance.
  - **Batch (XLS-56)**: atomic-batch-payment (pending BatchV1_1 — amendment removed in rippled v3.1.1).
  - **Compliance / identity**: kyc-credentials-flow (XLS-70), permissioned-domain-and-did (XLS-40+80).
  - **Oracle (XLS-47)**: oracle-price-feed (periodic publisher).
  - **Tickets**: tickets-parallel-submit (head-of-line-blocking free batch).
  - **NFT (XLS-46)**: nft-mutable-uri.
  - **Multi-sign**: multi-sign-collection (signature collection + combine workflow).
  - **AMM (XLS-37)**: amm-clawback (issuer reclaims tokens from a pool).
  - **DelegateSet (XLS-75)**: delegate-permissions (per-tx-type delegation, bot acts on owner's behalf).
  - **Vault (XLS-65, draft)**: vault-deposit-redeem.
  All recipes follow a unified structure (use-case → tools → arch → pre-req → prompt → step-by-step → verification → gotchas → use-cases → extensions).
- XRPL glossary — [`docs/glossary.md`](glossary.md): drops, reserve (base+owner), Sequence, LastLedgerSequence, Ripple epoch, ledger states, trust lines (NoRipple/DefaultRipple/Freeze), engine results (tec/tef/tem/ter/tes), DEX, AMM, Regular Key, Signer List, DepositAuth, Clawback, Escrow, Payment Channel, Check, NFT.
- [docs/supply-chain.md](supply-chain.md) — what ships with every release, how to verify, which secrets to configure for optional macOS notarization / Windows Authenticode.
- [docs/branch-protection.md](branch-protection.md) — recommended `main` branch protection rules (PR review, status checks, no force-push, signed commits) and how to configure them in GitHub Settings.
- [docs/bilingual-convention.md](bilingual-convention.md) — sibling-file `.md` / `.ru.md` documentation convention (English default, Russian alongside).
