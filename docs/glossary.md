> 🇷🇺 [Прочесть на русском](glossary.ru.md)

# XRPL glossary

A short reference for the terms used in the plugin's MCP tool descriptions. The full official reference is at [xrpl.org/docs](https://xrpl.org/docs).

## Money and units

### drops
The minimum unit of XRP. **1 XRP = 1,000,000 drops**. All XRP amounts in the XRPL protocol are transferred as a drops string (uint64). In our prepare tools, `amount` for XRP is a drops string (`"10000000"` = 10 XRP); for tokens — a JSON object `{value, currency, issuer}`.

### token amount
Amount of an issued token. JSON object of the form:
```json
{ "value": "100.50", "currency": "USD", "issuer": "rIssuerAddress..." }
```
- `value` — decimal string (XRPL supports 15 significant digits).
- `currency` — 3-char ISO code (`USD`, `EUR`) or 40-char hex for non-standard.
- `issuer` — issuer's r-address.

### MPT (XLS-33)
Multi-Purpose Token — a new token type with extended policies. Identified by `mpt_issuance_id` instead of the `currency+issuer` pair. The plugin supports what the XrplCSharp SDK exposes.

## Reserves and fees

### base reserve
The minimum XRP that must sit on every account just for the account to exist in the ledger. On mainnet this is **1 XRP** (current value — via `xrpl_server_state`, field `state.validated_ledger.reserve_base`).

### owner reserve
Extra XRP per `owned object` (trust line, offer, escrow, NFT, signer list, etc.). On mainnet this is **0.2 XRP per object**. Field `state.validated_ledger.reserve_inc`.

Total required minimum: `base + owner_count × owner_reserve`. If `balance` drops below it, all transactions except `AccountDelete` start rejecting with `tecINSUFFICIENT_RESERVE`. `xrpl_tx_preflight` computes this automatically.

### Fee
Transaction fee in drops. Minimum — `10 drops` (open-ledger reference fee). Under load rippled raises the "open-ledger fee" — discoverable via `xrpl_fee`. The `FeeBumpMultiplier` option in `XrplMcpOptions` lets you proactively scale the autofilled fee.

## Identifiers and time windows

### Sequence
A uint32 counter of transactions per account. Every successful or fee-paying transaction increments Sequence by 1. A tx with the wrong Sequence is rejected. Autofill substitutes the correct value from `account_info`.

### TicketSequence
Alternative to `Sequence` — a pre-reserved "slot" via `TicketCreate`. Lets you submit transactions out of strict order.

### LastLedgerSequence (LLS)
The ledger after which the transaction must NOT be validated. Protects against "stuck" transactions. Recommended: `current + 20` (our `XrplMcpOptions.LastLedgerSequenceOffset`).

### Ripple epoch
XRPL stores times as "seconds since 2000-01-01T00:00:00Z". **Not Unix epoch.** Conversion: `unix_time - 946684800`. The SDK converts automatically when using typed prepare tools.

## Ledger states

The `ledger_index` field in read-only tools accepts four variants:

- **`validated`** (default) — the latest fully validated ledger. Final data. Use this whenever you don't need fresher state.
- **`closed`** — the latest closed but not yet validated by consensus. May roll back.
- **`current`** — the currently open ledger, where new tx are landing right now. Not final.
- **`<number>`** — a specific historical ledger sequence (if the node retains it — see `complete_ledgers` from `xrpl_server_info`).

## Trust lines

### TrustSet
A transaction that creates or modifies a trust line — a bilateral commitment to hold someone's issued token up to a stated limit. Bilateral: each side sets its own `limit`. A trust line is a `RippleState` ledger object that costs 2 XRP owner reserve.

### NoRipple
A trust-line flag that blocks cross-currency rippling through that line. When both sides of a trust line set `NoRipple`, the path through that pair is excluded from path finding.

In `xrpl_account_lines`:
- `no_ripple` — your side's flag.
- `no_ripple_peer` — the other side's flag.

### DefaultRipple
An account-level flag (`asfDefaultRipple = 8`). When set, `NoRipple` is **off by default** on new trust lines. Issuers typically enable it.

### Freeze / DeepFreeze
Freezes a trust line — the counterparty cannot spend the token. `tfSetFreeze`/`tfClearFreeze` in `TrustSet`. DeepFreeze (`tfSetDeepFreeze`) additionally blocks receiving. See `xrpl_trustline_freeze_prepare`.

## Transaction results

`engine_result` in the response to `submit` or `tx`:

- **`tesSUCCESS`** — applied successfully.
- **`tec*`** — claimed fee, failed. Transaction landed in the ledger, fee was charged, but the intended effect didn't happen (e.g. `tecUNFUNDED_OFFER`, `tecPATH_DRY`, `tecNO_PERMISSION`, `tecINSUFFICIENT_RESERVE`).
- **`tef*`** — failed before applying, no fee claimed. Did not land in the ledger at all (e.g. `tefMAX_LEDGER` — LLS already passed).
- **`tem*`** — malformed transaction. Never lands in the ledger. If you see `tem*` from our prepare tools — it's a bug, please report.
- **`ter*`** — retry, may succeed later (e.g. `terPRE_SEQ` — Sequence not yet reached).

Always check `meta.TransactionResult`, not just HTTP status.

## DEX and AMM

### Offer / OfferCreate
A limit order on the built-in DEX. `TakerGets` (what you give) and `TakerPays` (what you want) are pair entries of XRP-drops or token amount. On creation, the offer auto-matches with the open side of the book (partial fills possible). Flags: `tfPassive`, `tfImmediateOrCancel`, `tfFillOrKill`, `tfSell`.

### AMM (XLS-30)
Automated Market Maker. One pool = two assets in a single `AMM` ledger object. The pool has a **special pseudo-account** (no master key) that owns the assets and issues **LP tokens** (a liquidity provider's share). Trading fee 0..1% (basis points / 10). See `xrpl_amm_*` tools.

### LP token
An issued token that the AMM mints to depositors proportional to deposited liquidity. Issuer = AMM pseudo-account. Burned on withdraw.

## Authorization

### Regular Key
An alternate key for signing tx for this account. Set via `SetRegularKey`. The master key can be disabled via `asfDisableMaster` (only if a regular key or signer list exists — otherwise the account becomes locked).

### Signer List
Multi-sig: a list of other accounts + their weights + a threshold (`SignerQuorum`). Set via `SignerListSet`. Signatures are collected through `xrpl_sign_multi` + `xrpl_sign_combine`.

### DepositAuth
Account flag (`asfDepositAuth = 9`). When set, the account does not accept Payments except from senders pre-authorised via `DepositPreauth`. Useful for regulated gateways.

## Other

### Path / RipplePathFind
For cross-currency Payment you must explicitly provide `Paths` — sequences of intermediate trust lines / order books / AMM pools through which value flows. `xrpl_ripple_path_find` finds options and returns `paths_computed` ready to plug into a prepare tool.

### Clawback
The issuer of an issued token reclaims their tokens from holders' balances. Requires `asfAllowTrustLineClawback` on the issuer (set BEFORE the first token issuance). See `xrpl_clawback_prepare`.

### Escrow
Conditional / time-based XRP hold (or, with the TokenEscrow amendment, tokens). Supports `FinishAfter` (time), `CancelAfter` (deadline for refund to the sender), `Condition` (PREIMAGE-SHA-256 crypto-condition). See `xrpl_escrow_*` tools.

### Payment Channel
A one-way channel between sender and receiver. Sender funds, signs offline claims (signed amounts), receiver eventually shows up and redeems the largest. Reduces on-chain load. See `xrpl_payment_channel_*` tools.

### Check
Deferred payment — sender creates a Check, receiver must explicitly cash it. No automatic transfer. Useful for compliance flows. See `xrpl_check_*` tools.

### NFT (XLS-20)
An NFToken sits in the owner's `NFTokenPage`, identified by a 256-bit `NFTokenID`. Mint/Burn/Transfer through NFToken*Offer tx. URI is stored hex-encoded.

### Credential (XLS-70)
On-chain "badge": (Issuer, Subject, CredentialType-hex) tuple with optional `URI` and `Expiration`. States: provisional (after `CredentialCreate`, before `CredentialAccept`) and accepted (after `CredentialAccept`, `lsfAccepted` flag set). Used for KYC-style gating: senders carry `Payment.CredentialIDs` (SHA-512/2 hashes — see `xrpl_hash_credential`) and recipients pre-authorise via `DepositPreauth(AuthorizeCredentials)`.

### PermissionedDomain (XLS-80)
Ledger entry listing 1..10 `(issuer, credentialType)` accepted-credential pairs. Account membership in a domain = holding any of the listed credentials. Downstream amendments (permissioned DEX, permissioned AMM) can gate access by `DomainID`.

### DID (XLS-40)
Decentralized Identifier — one ledger entry per account containing up to 3 hex-blob fields (`Data` / `DIDDocument` / `URI`, each ≤256 bytes). At least one is required on `DIDSet`. Used for on-chain identity attestation referencing off-chain DID documents.

### MPT (XLS-33)
See "token amount" above. Non-trustline fungible token type. The issuer creates an `MPTokenIssuance` entry; holders own per-account `MPToken` entries. Identified by a 48-hex `MPTokenIssuanceID` derived from `Issuer + Sequence` post-validation.

### Vault (XLS-65, draft)
A single-asset pooled vault. Owner deposits an asset, depositors receive shares-MPT (auto-issued from the vault's pseudo-account). Optionally private (PermissionedDomain-gated) and/or with non-transferable shares.

### LoanBroker / Loan (XLS-66, draft)
DeFi lending primitive on top of Vault. LoanBroker manages a pool of loans backed by cover (first-loss) capital. Loans have configurable interest/late/close/overpayment rates, fees, payment schedules, and lifecycle states (active, impaired, defaulted).
