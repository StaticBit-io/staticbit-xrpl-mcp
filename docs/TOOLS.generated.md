# Tool reference (auto-generated)

> ⚠️ **Do not edit by hand.** Generated from `[McpServerTool]` / `[Description]`
> attributes by the `Mcp.ToolsDoc` tool. CI fails if it drifts.
>
> Regenerate:
>
> ```
> dotnet tool run mcp-toolsdoc
> ```

**131 tools across 2 server(s).**

- [`xrpl`](#xrpl) — 116 tools
- [`xrpl-signer`](#xrpl-signer) — 15 tools

## `xrpl`

XRPL MCP — read/prepare/submit ledger, account, AMM, DEX, NFT, vault and more.

**116 tools.**

### `xrpl_account_bridges`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Lists every Bridge ledger object owned by 'account' (XLS-38 — typically the door account). Each entry includes the bridge spec (LockingChain/IssuingChain doors + issues), SignatureReward, MinAccountCreateAmount (null = AccountCreate disabled), current XChainClaimID counter, XChainAccountCreateCount / XChainAccountClaimCount counters, previousTxnId.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address — bridge owner (door account on this chain). |
| `limit` | `int?` | no | `null` | Page size (server-clamped). Omit for default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_credentials`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Lists every Credential ledger object touching the account. Splits into 'issued' (account is the issuer of the credential — provisional until subject accepts) and 'held' (account is the subject of the credential). Each entry includes counterparty, credentialType (hex + decoded UTF-8 if printable), URI (hex + decoded), accepted flag (subject accepted = lsfAccepted=0x10000), expirationUtc, previousTxnId.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address to inspect (issuer or subject). |
| `limit` | `int?` | no | `null` | Page size (server-clamped). Omit for default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_delete_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountManagementTools.cs`</sub>

Prepares an UNSIGNED AccountDelete. The account must have no owned objects (trust lines, offers, escrows, etc.) and Sequence + 256 must be ≤ current ledger sequence. Reserve fee is high — typically 2 XRP — and the residual XRP balance is sent to 'destination'.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account to be deleted. |
| `destination` | `string` | **yes** | — | Funded destination that receives leftover XRP. Must NOT equal account. |
| `destinationTag` | `uint?` | no | `null` | Optional destination tag (uint32). |

### `xrpl_account_did`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Returns the single DID ledger object owned by 'account' (XLS-40). Each account owns at most one DID — returns null if none exists. Includes raw hex fields (data/didDocument/uri) plus UTF-8-decoded variants where printable.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address to inspect. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_escrows`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Lists every Escrow ledger object owned by the account. Splits into 'sent' (account is the source/funder, waiting for Finish or Cancel) and 'received' (account is the destination, can call EscrowFinish once FinishAfter passes). Each entry includes counterparty, amount, finishAfter, cancelAfter, condition (if conditional), and the EscrowCreate Sequence (use as offerSequence in xrpl_escrow_finish_prepare / xrpl_escrow_cancel_prepare).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address to inspect. |
| `limit` | `int?` | no | `null` | Page size (server-clamped). Omit for default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_info`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountTools.cs`</sub>

Account root data: XRP balance (drops), sequence, flags, reserves.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address (starts with 'r...'). |
| `ledgerIndex` | `string?` | no | `null` |  |
| `strict` | `bool` | no | `true` | If true, the server checks the account against an STRICT mode (verifies the address). |
| `queue` | `bool` | no | `false` | If true, include queued transactions for this account. |
| `signerLists` | `bool` | no | `false` | If true, include signer-list ledger entries. |

### `xrpl_account_lines`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountTools.cs`</sub>

Trust lines for an account. Use 'limit' and 'marker' for pagination.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address. |
| `peer` | `string?` | no | `null` | Optional peer address to filter lines to a single counterparty. |
| `limit` | `int?` | no | `null` | Page size (max ~400). Omit to use the server default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_loan_brokers`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Lists every LoanBroker ledger object owned by 'account' (XLS-66). Each entry includes the 64-hex LoanBrokerID (use for set/delete/cover-deposit/etc.), pseudo-account, underlying VaultID, debt counters (DebtTotal / DebtMaximum, STNumber strings), CoverAvailable, cover-rate thresholds (CoverRateMinimum/Liquidation in 1/100th bp), ManagementFeeRate (1/10th bp), Data hex blob, sequence, owner count (active loans).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address — broker owner. |
| `limit` | `int?` | no | `null` | Page size (server-clamped). Omit for default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_loans`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Lists every Loan ledger object touching 'account' (XLS-66) — typically as borrower (account == Loan.Borrower) or via the broker pseudo-account. Each entry includes 64-hex LoanID (use for manage/pay/delete), Borrower, LoanBrokerID, loan sequence, all interest/fee rates and fees, principal counters (PrincipalRequested / PrincipalOutstanding / TotalValueOutstanding), PeriodicPayment, ManagementFeeOutstanding, payment schedule (PaymentInterval / GracePeriod / PaymentRemaining), StartDate / PreviousPaymentDueDate / NextPaymentDueDate (UTC ISO-8601), LoanScale, previousTxnId.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address — borrower or broker pseudo-account. |
| `limit` | `int?` | no | `null` | Page size (server-clamped). Omit for default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_mpt_issuances`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Lists every MPTokenIssuance ledger object owned by the account (i.e. issuances where the account is the issuer). Returns: id (MPTokenIssuanceID, 48-hex), assetScale, maximumAmount, outstandingAmount, lockedAmount, transferFee, flags (decoded), metadataHex, metadataAscii (best-effort), sequence, previousTxnId. Use this to enumerate issuances you control before destroy/set operations.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address to inspect (the issuer). |
| `limit` | `int?` | no | `null` | Page size (server-clamped). Omit for default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_mpts`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Lists every MPToken ledger object held by the account (i.e. balances of MPTs issued by OTHER accounts). Returns: id (MPTokenIssuanceID), amount, lockedAmount, flags (locked/authorized), previousTxnId. Use this to enumerate non-trustline token holdings.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address to inspect (the holder). |
| `limit` | `int?` | no | `null` | Page size (server-clamped). Omit for default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_nfts`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/NftTools.cs`</sub>

Read-only: lists NFTs currently owned by an account. Pageable via limit/marker.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address. |
| `limit` | `int?` | no | `null` | Page size (server-clamped). Omit for default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_objects`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountTools.cs`</sub>

All ledger objects owned by the account (trust lines, offers, escrows, checks, channels, NFTs, etc.).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address. |
| `type` | `string?` | no | `null` | Optional object type filter (e.g. 'offer', 'state', 'escrow', 'check', 'payment_channel', 'signer_list', 'nft_offer'). |
| `limit` | `int?` | no | `null` | Page size. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `deletionBlockersOnly` | `bool` | no | `false` | If true, return only objects that prevent account deletion. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_offers`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountTools.cs`</sub>

Active DEX offers owned by the account.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address. |
| `limit` | `int?` | no | `null` | Page size. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_permissioned_domains`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Lists every PermissionedDomain ledger object owned by 'account' (XLS-80). Each entry includes the on-chain DomainID (use it for set/delete), sequence (creator's tx sequence), AcceptedCredentials list with issuer + credentialType (hex + UTF-8 decoded), previousTxnId.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address — owner of the domains. |
| `limit` | `int?` | no | `null` | Page size (server-clamped). Omit for default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_account_set_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountManagementTools.cs`</sub>

Prepares an UNSIGNED AccountSet. Use setFlag/clearFlag to toggle ONE asf flag per transaction (e.g. asfDefaultRipple=8, asfRequireAuth=2, asfRequireDest=1, asfDisallowXRP=3, asfDisableMaster=4, asfNoFreeze=6, asfGlobalFreeze=7, asfDepositAuth=9, asfAuthorizedNFTokenMinter=10, asfDisallowIncomingNFTokenOffer=12, asfDisallowIncomingCheck=13, asfDisallowIncomingPayChan=14, asfDisallowIncomingTrustline=15, asfAllowTrustLineClawback=16, asfAllowTrustLineLocking=17). Domain must be lowercase ASCII hex-encoded.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account whose settings are being modified. |
| `setFlag` | `uint?` | no | `null` | Optional asf flag to ENABLE (one per transaction). |
| `clearFlag` | `uint?` | no | `null` | Optional asf flag to DISABLE (one per transaction). |
| `domain` | `string?` | no | `null` | Optional domain (ASCII lowercase, hex-encoded; max 256 chars). |
| `emailHash` | `string?` | no | `null` | Optional 32-byte hex MD5 of an email address (for Gravatar). |
| `messageKey` | `string?` | no | `null` | Optional public key for encrypted messages, hex. |
| `transferRate` | `uint?` | no | `null` | Optional transfer rate (1_000_000_000 = 1.0, 0 = no fee, max 2_000_000_000). |
| `tickSize` | `uint?` | no | `null` | Optional tick size for issued offers (3..15, or 0 to disable). |
| `nfTokenMinter` | `string?` | no | `null` | Optional authorized NFToken minter (r-address). |

### `xrpl_account_tx`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountTools.cs`</sub>

Transaction history for an account, paginated via marker. Returns latest first by default.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address. |
| `ledgerIndexMin` | `int?` | no | `-1` | Lower bound of the ledger range. -1 means earliest available. |
| `ledgerIndexMax` | `int?` | no | `-1` | Upper bound of the ledger range. -1 means latest available. |
| `limit` | `int?` | no | `50` | Page size (max 200). |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `forward` | `bool` | no | `false` | If true, return results in ascending (oldest first) order. |
| `binary` | `bool` | no | `false` | If true, return raw binary blobs instead of expanded JSON. |

### `xrpl_account_tx_since`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/SubscriptionTools.cs`</sub>

Polling-based account monitor. Returns transactions affecting the account starting from sinceLedger (exclusive) up to current validated ledger. Pageable via 'limit' and 'marker'. The intended pattern: caller stores the highest ledger_index it saw and passes it as sinceLedger on the next poll. Works on cloud/local/HTTP — no streaming required.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address. |
| `sinceLedger` | `int` | no | `0` | Inclusive lower bound: return transactions from this ledger onward. Pass 0 (default) for 'earliest available'. |
| `limit` | `int?` | no | `50` | Page size (max 200). |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call (continue from prior page). |
| `forward` | `bool` | no | `true` | If true, return results in ascending order (oldest first) — usually the right thing for monitoring. |

### `xrpl_account_vaults`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Lists every Vault ledger object owned by 'account' (XLS-65). Each entry includes the 64-hex VaultID (use for set/delete/deposit/etc.), pseudo-account, asset spec, AssetsTotal / AssetsAvailable / AssetsMaximum / LossUnrealized (STNumber strings), the share-MPTokenIssuanceID (ShareMPTID), withdrawal policy, scale, data (hex + parsed VaultDataFormat {n,w} when present), and the optional permissioned-domain id.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address — vault owner. |
| `limit` | `int?` | no | `null` | Page size (server-clamped). Omit for default. |
| `marker` | `string?` | no | `null` | Pagination cursor returned by the previous call. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_amm_bid_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AmmManagementTools.cs`</sub>

Prepares an UNSIGNED AMMBid for the AMM auction slot (discounted trading fee for a period). bidMin/bidMax are LP-token amounts as JSON {value,currency,issuer} where issuer = AMM account. authAccountsJson is an optional JSON array of up to 4 r-addresses: ["r...","r..."].

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Bidder account (must hold LP tokens of this pool). |
| `asset1Currency` | `string` | **yes** | — | First pool asset currency code. |
| `asset1Issuer` | `string?` | no | — | First pool asset issuer (empty for XRP). |
| `asset2Currency` | `string` | **yes** | — | Second pool asset currency code. |
| `asset2Issuer` | `string?` | no | — | Second pool asset issuer (empty for XRP). |
| `bidMin` | `string?` | no | `null` | Optional minimum bid (LP tokens, JSON {value,currency,issuer}). |
| `bidMax` | `string?` | no | `null` | Optional maximum bid (LP tokens, JSON {value,currency,issuer}). |
| `authAccountsJson` | `string?` | no | `null` | Optional JSON array of up to 4 r-addresses authorized to trade at the discounted fee. |

### `xrpl_amm_clawback_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AmmManagementTools.cs`</sub>

Prepares an UNSIGNED AMMClawback (XLS-37). The token issuer claws back tokens previously deposited into an AMM pool by 'holder'. The issuer must have asfAllowTrustLineClawback enabled. 'asset1' is the issuer's token in the pool (its issuer field MUST equal the sender 'account'); 'asset2' is the pool counterpart. Optional 'amountValue' (string) limits the claw-back to that amount of the issuer's token; omit to claw back the maximum available.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Issuer account submitting the clawback. |
| `holder` | `string` | **yes** | — | Holder whose AMM-deposited tokens are being clawed back. |
| `asset1Currency` | `string` | **yes** | — | Issuer's token currency code (3-char or 40-char hex). Cannot be 'XRP' — only issued currencies can be clawed back. |
| `asset1Issuer` | `string` | **yes** | — | Issuer's token issuer — MUST equal account. |
| `asset2Currency` | `string` | **yes** | — | Counterpart pool asset currency code. |
| `asset2Issuer` | `string?` | no | `null` | Counterpart pool asset issuer. Empty for XRP. |
| `amountValue` | `string?` | no | `null` | Optional clawback amount as a decimal string. Omit to claw back the maximum available of the issuer's token. |

### `xrpl_amm_create_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AmmManagementTools.cs`</sub>

Prepares an UNSIGNED AMMCreate. Both amounts must be supplied (this defines the initial pool composition). tradingFeeBasisPoints is in 1/10 bps (0..1000 = 0%..1%). Caller pays the full amounts when this lands.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender account that funds the AMM and becomes initial LP. |
| `amount` | `string` | **yes** | — | First asset amount. Drops string for XRP or JSON {value,currency,issuer}. |
| `amount2` | `string` | **yes** | — | Second asset amount. Same format as 'amount'. |
| `tradingFeeBasisPoints` | `uint` | **yes** | — | Trading fee in 1/10 bps (0..1000). 1 = 0.001%, 1000 = 1%. |

### `xrpl_amm_delete_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AmmManagementTools.cs`</sub>

Prepares an UNSIGNED AMMDelete to fully delete an empty AMM (after AMMWithdraw left residual trust lines). May need to be run multiple times to fully clean up.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender account. |
| `asset1Currency` | `string` | **yes** | — | First pool asset currency code. |
| `asset1Issuer` | `string?` | no | — | First pool asset issuer (empty for XRP). |
| `asset2Currency` | `string` | **yes** | — | Second pool asset currency code. |
| `asset2Issuer` | `string?` | no | — | Second pool asset issuer (empty for XRP). |

### `xrpl_amm_deposit_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AmmTransactionTools.cs`</sub>

Prepares an UNSIGNED AMMDeposit. Provide the pool's two assets and ONE of: (a) amount only — single-asset deposit; (b) amount + amount2 — two-asset deposit; (c) lpTokenOut only — double-asset deposit by LP amount; (d) amount + lpTokenOut — single-asset deposit by LP amount.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender address (liquidity provider). |
| `asset1Currency` | `string` | **yes** | — | First pool asset — currency code ('XRP' or 3-char / 40-hex). |
| `asset1Issuer` | `string?` | no | — | First pool asset — issuer. Empty for XRP. |
| `asset2Currency` | `string` | **yes** | — | Second pool asset — currency code. |
| `asset2Issuer` | `string?` | no | — | Second pool asset — issuer. Empty for XRP. |
| `amount` | `string?` | no | `null` | Optional amount to deposit (drops string for XRP, JSON token object for issued currencies). |
| `amount2` | `string?` | no | `null` | Optional second amount (only with 'amount' for tfTwoAsset mode). |
| `lpTokenOut` | `string?` | no | `null` | Optional LP token amount to receive (JSON {value,currency,issuer} where issuer is the AMM account). |
| `ePrice` | `string?` | no | `null` | Optional effective price (JSON token object). Used with single-asset deposit. |

### `xrpl_amm_info`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AmmTools.cs`</sub>

Returns state of an AMM instance — pool assets, LP token supply, trading fee, auction slot. Identify by asset pair OR by AMM account.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `asset1Currency` | `string?` | no | `null` | First asset of the pool — currency code ('XRP' or 3-char/40-hex token). |
| `asset1Issuer` | `string?` | no | `null` | Issuer of the first asset. Leave empty for XRP. |
| `asset2Currency` | `string?` | no | `null` | Second asset of the pool — currency code. |
| `asset2Issuer` | `string?` | no | `null` | Issuer of the second asset. Leave empty for XRP. |
| `ammAccount` | `string?` | no | `null` | Optional: AMM account address. If set, asset1/asset2 are ignored. |
| `lpAccount` | `string?` | no | `null` | Optional: liquidity provider account — when set, returns LP token amount this account holds. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_amm_vote_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AmmManagementTools.cs`</sub>

Prepares an UNSIGNED AMMVote — LP votes on the desired trading fee of the pool. Vote weight is proportional to held LP tokens. tradingFeeBasisPoints in 1/10 bps (0..1000).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Voter (must hold LP tokens of this pool). |
| `asset1Currency` | `string` | **yes** | — | First pool asset currency code ('XRP' or 3-char/40-hex). |
| `asset1Issuer` | `string?` | no | — | First pool asset issuer (empty for XRP). |
| `asset2Currency` | `string` | **yes** | — | Second pool asset currency code. |
| `asset2Issuer` | `string?` | no | — | Second pool asset issuer (empty for XRP). |
| `tradingFeeBasisPoints` | `uint` | **yes** | — | Vote: trading fee in 1/10 bps (0..1000). |

### `xrpl_amm_withdraw_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AmmTransactionTools.cs`</sub>

Prepares an UNSIGNED AMMWithdraw. Provide the pool's two assets and either lpTokenIn, amount(s), or set withdrawAll=true to redeem all LP tokens.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender address (liquidity provider). |
| `asset1Currency` | `string` | **yes** | — | First pool asset — currency code ('XRP' or 3-char / 40-hex). |
| `asset1Issuer` | `string?` | no | — | First pool asset — issuer. Empty for XRP. |
| `asset2Currency` | `string` | **yes** | — | Second pool asset — currency code. |
| `asset2Issuer` | `string?` | no | — | Second pool asset — issuer. Empty for XRP. |
| `amount` | `string?` | no | `null` | Optional amount to withdraw (drops string for XRP, JSON token object for issued currencies). |
| `amount2` | `string?` | no | `null` | Optional second amount for two-asset withdrawals. |
| `lpTokenIn` | `string?` | no | `null` | Optional LP token amount to redeem (JSON token object, issuer = AMM account). |
| `withdrawAll` | `bool` | no | `false` | If true, set tfWithdrawAll to redeem ALL held LP tokens (mutually exclusive with explicit amounts). |
| `oneAssetWithdrawAll` | `bool` | no | `false` | If true and an amount is provided, redeem all LP tokens via tfOneAssetWithdrawAll for a single asset. |

### `xrpl_batch_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/BatchTools.cs`</sub>

Prepares an UNSIGNED Batch transaction (XLS-56) that atomically executes up to 8 inner transactions. Modes: 'AllOrNothing' (all must succeed), 'OnlyOne' (first success wins, others not attempted), 'UntilFailure' (apply in order, stop at first failure), 'Independent' (apply all, each evaluated independently). 'innerTransactionsJson' is a JSON array of inner tx objects — each must have Account/Sequence (or TicketSequence) populated; Fee/SigningPubKey/TxnSignature/Signers and the tfInnerBatchTxn flag are forced by this tool. For multi-account batches, supply 'batchSignersJson' — a JSON array of {account, signingPubKey?, txnSignature?, signers?} entries (one per non-outer inner Account).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Outer batch account — pays the network fee and submits the Batch. |
| `mode` | `string` | **yes** | — | Execution mode: 'AllOrNothing', 'OnlyOne', 'UntilFailure', or 'Independent'. |
| `innerTransactionsJson` | `string` | **yes** | — | JSON array of inner tx objects (1..8). Each must include its own Account and Sequence/TicketSequence. Inner-only fields (Fee, SigningPubKey, tfInnerBatchTxn flag) are forced. |
| `batchSignersJson` | `string?` | no | `null` | Optional JSON array of BatchSigner entries: [{"account":"r...","signingPubKey":"...","txnSignature":"...","signers":[...]}]. Required when any inner tx Account differs from the outer 'account'. |

### `xrpl_book_offers`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/DexTools.cs`</sub>

Returns the order book (offers) for a currency pair on the XRPL DEX. Use 'XRP' currency with empty issuer for XRP.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `takerGetsCurrency` | `string` | **yes** | — | Currency the taker would RECEIVE. 'XRP' or 3-char/40-hex token code. |
| `takerGetsIssuer` | `string?` | no | — | Issuer for the taker_gets currency. Leave empty for XRP. |
| `takerPaysCurrency` | `string` | **yes** | — | Currency the taker would PAY. 'XRP' or 3-char/40-hex token code. |
| `takerPaysIssuer` | `string?` | no | — | Issuer for the taker_pays currency. Leave empty for XRP. |
| `taker` | `string?` | no | `null` | Optional address used as the offer-taker's perspective (for filtering unfunded offers). |
| `limit` | `uint?` | no | `null` | Page size. Server may cap this value. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_check_cancel_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/CheckTools.cs`</sub>

Prepares an UNSIGNED CheckCancel. Can be sent by the Check's source or destination at any time; by anyone after the Check has expired.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender of the cancel. |
| `checkId` | `string` | **yes** | — | Check ledger object ID (64-char hex). |

### `xrpl_check_cash_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/CheckTools.cs`</sub>

Prepares an UNSIGNED CheckCash. Submitted by the Check's destination. Provide EITHER amount (cash for exactly this amount) OR deliverMin (cash for at least this amount, up to the Check's sendMax). Currency must match the Check's SendMax.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender — must be the Check's destination. |
| `checkId` | `string` | **yes** | — | Check ledger object ID (64-char hex). |
| `amount` | `string?` | no | `null` | Cash for EXACTLY this amount. Drops string for XRP or JSON token object. Mutually exclusive with deliverMin. |
| `deliverMin` | `string?` | no | `null` | Cash for AT LEAST this amount. Drops string for XRP or JSON token object. Mutually exclusive with amount. |

### `xrpl_check_create_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/CheckTools.cs`</sub>

Prepares an UNSIGNED CheckCreate. sendMax = upper bound the sender allows to be debited (includes transfer fees). Same format as xrpl_payment_prepare amounts.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender (issuer of the Check). |
| `destination` | `string` | **yes** | — | Destination — the only account that can cash the Check. |
| `sendMax` | `string` | **yes** | — | SendMax: max amount the Check can debit. Drops string for XRP, JSON {value,currency,issuer} for tokens. |
| `destinationTag` | `uint?` | no | `null` | Optional destination tag. |
| `expirationUtc` | `DateTime?` | no | `null` | Optional UTC expiration; the Check is invalid after this time. |
| `invoiceId` | `uint?` | no | `null` | Optional InvoiceID (uint32). |

### `xrpl_clawback_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/IssuerTools.cs`</sub>

Prepares an UNSIGNED Clawback. The issuer claws back its own tokens from a holder. amount.issuer must be the HOLDER (the account to claw FROM), NOT the issuer (the sender). amount = JSON {value,currency,issuer}. value=0 is rejected; if value > holder's balance, the entire balance is clawed. Requires asfAllowTrustLineClawback enabled on the issuer (set BEFORE issuing any tokens).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender = issuer (the account that originally issued these tokens). |
| `amount` | `string` | **yes** | — | Amount JSON: {value,currency,issuer} where 'issuer' is the HOLDER to claw FROM. |
| `holder` | `string?` | no | `null` | Optional Holder (r-address). Required only when clawing back MPT tokens; must be omitted for trust-line tokens. |

### `xrpl_credential_accept_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/CredentialTools.cs`</sub>

Prepares an UNSIGNED CredentialAccept (XLS-70). The credential SUBJECT submits this to ratify a provisionally-issued credential; once accepted, the reserve transfers from the issuer to the subject and the credential becomes usable for DepositPreauth and PermissionedDomain access. Provide credentialType as hex OR plain text.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Subject account (transaction sender) that accepts the credential. |
| `issuer` | `string` | **yes** | — | Issuer that originally created the credential. Must differ from account. |
| `credentialTypeHex` | `string?` | no | `null` | Credential type as hex (1..128 hex chars). Mutually exclusive with credentialTypePlain. |
| `credentialTypePlain` | `string?` | no | `null` | Credential type as plain string (auto-hex). Mutually exclusive with credentialTypeHex. |

### `xrpl_credential_create_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/CredentialTools.cs`</sub>

Prepares an UNSIGNED CredentialCreate (XLS-70). 'account' is the ISSUER; 'subject' is the recipient. Provide credentialType as either credentialTypeHex (≤128 hex chars = 64 raw bytes) or credentialTypePlain (UTF-8 auto-hex-encoded; ≤64 chars) — mutually exclusive. Optional URI via uriHex (≤512 hex chars = 256 raw bytes) or uriPlain (auto-hex). Optional expirationUtc — credential is automatically deletable after that time. Reserve is held by the issuer until the subject calls CredentialAccept.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Issuer account (transaction sender). |
| `subject` | `string` | **yes** | — | Subject account that receives the credential. Must differ from account. |
| `credentialTypeHex` | `string?` | no | `null` | Credential type as a hex string (1..128 hex chars). Mutually exclusive with credentialTypePlain. |
| `credentialTypePlain` | `string?` | no | `null` | Credential type as a plain ASCII/UTF-8 string (auto-hex-encoded). Mutually exclusive with credentialTypeHex. |
| `uriHex` | `string?` | no | `null` | Optional URI as a hex string (≤512 hex chars). Mutually exclusive with uriPlain. |
| `uriPlain` | `string?` | no | `null` | Optional URI as a plain string (auto-hex-encoded). Mutually exclusive with uriHex. |
| `expirationUtc` | `DateTime?` | no | `null` | Optional UTC expiration. After this, the credential is auto-revoked and any account may delete it. |

### `xrpl_credential_delete_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/CredentialTools.cs`</sub>

Prepares an UNSIGNED CredentialDelete (XLS-70). Submittable by: the issuer (revoke), the subject (un-accept), or anyone after expiration. Exactly one of 'subject'/'issuer' must be provided — the OTHER party (not the transaction sender). If account=issuer, supply subject; if account=subject, supply issuer; if anyone deleting an expired credential, supply both (otherwise the chain will infer 'account' as one side).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Transaction sender — issuer, subject, or any account if the credential has expired. |
| `credentialTypeHex` | `string?` | no | `null` | Credential type as hex (1..128 hex chars). Mutually exclusive with credentialTypePlain. |
| `credentialTypePlain` | `string?` | no | `null` | Credential type as plain string. Mutually exclusive with credentialTypeHex. |
| `subject` | `string?` | no | `null` | Subject of the credential. Omit ONLY if account==subject. |
| `issuer` | `string?` | no | `null` | Issuer of the credential. Omit ONLY if account==issuer. |

### `xrpl_delegate_set_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountManagementTools.cs`</sub>

Prepares an UNSIGNED DelegateSet (XLS-75). Grants 'delegate' permission to submit, on behalf of 'account', transactions of the listed types. 'permissionsCsv' is a comma-separated list of transaction-type names (e.g. 'Payment,TrustSet,OfferCreate'); 1..10 entries, no duplicates. The following types CANNOT be delegated: AccountSet, SetRegularKey, SignerListSet, DelegateSet. Pass an empty/whitespace permissionsCsv to clear the delegation.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account granting (or clearing) the delegation. |
| `delegateAccount` | `string` | **yes** | — | Delegatee classic XRP address — the account allowed to submit on behalf of 'account'. |
| `permissionsCsv` | `string` | **yes** | — | Comma-separated transaction-type names (1..10), e.g. 'Payment,TrustSet'. Empty string clears all delegated permissions. |

### `xrpl_deposit_preauth_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountManagementTools.cs`</sub>

Prepares an UNSIGNED DepositPreauth. Pass EXACTLY ONE of: authorize (grant by address), unauthorize (revoke by address), authorizeCredentialsJson (XLS-70 grant by credential set, 1-8 entries), unauthorizeCredentialsJson (XLS-70 revoke by credential set, 1-8 entries). Only meaningful if the account has asfDepositAuth enabled. Credential entries shape: [{"issuer":"r...","credentialType":"<hex>"}, ...].

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account granting/revoking the deposit preauthorization. |
| `authorize` | `string?` | no | `null` | Address to preauthorize (mutually exclusive with other variants). |
| `unauthorize` | `string?` | no | `null` | Address whose preauthorization should be revoked (mutually exclusive with other variants). |
| `authorizeCredentialsJson` | `string?` | no | `null` | XLS-70: JSON array of {issuer,credentialType-hex}, 1-8 entries. Holders presenting ALL of these credentials are preauthorized. Mutually exclusive with the other variants. |
| `unauthorizeCredentialsJson` | `string?` | no | `null` | XLS-70: JSON array of {issuer,credentialType-hex}, 1-8 entries — revoke a credential-based preauth granted earlier. Mutually exclusive with the other variants. |

### `xrpl_did_delete_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/DidTools.cs`</sub>

Prepares an UNSIGNED DIDDelete (XLS-40). Removes the DID ledger entry associated with 'account'. No additional fields.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account whose DID is being deleted. |

### `xrpl_did_set_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/DidTools.cs`</sub>

Prepares an UNSIGNED DIDSet (XLS-40). Creates or updates the DID associated with 'account'. At least one of (data, didDocument, uri) must be provided in some form. For each field you can pass either the *Hex variant (raw hex string, ≤512 hex chars = 256 bytes) or the *Plain variant (auto-UTF-8-hex-encoded) — *Hex and *Plain for the same logical field are mutually exclusive.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account that owns the DID. |
| `dataHex` | `string?` | no | `null` | Optional 'Data' field as hex string (≤512 hex chars). Mutually exclusive with dataPlain. |
| `dataPlain` | `string?` | no | `null` | Optional 'Data' field as plain text (auto-hex-encoded). Mutually exclusive with dataHex. |
| `didDocumentHex` | `string?` | no | `null` | Optional 'DIDDocument' field as hex string. Mutually exclusive with didDocumentPlain. |
| `didDocumentPlain` | `string?` | no | `null` | Optional 'DIDDocument' field as plain text. Mutually exclusive with didDocumentHex. |
| `uriHex` | `string?` | no | `null` | Optional 'URI' field as hex string. Mutually exclusive with uriPlain. |
| `uriPlain` | `string?` | no | `null` | Optional 'URI' field as plain text. Mutually exclusive with uriHex. |

### `xrpl_escrow_cancel_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/EscrowTools.cs`</sub>

Prepares an UNSIGNED EscrowCancel. Only valid after the escrow's CancelAfter time has passed; refunds the funds to the original owner.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender of the cancel (typically the original owner, but anyone can cancel after CancelAfter). |
| `owner` | `string` | **yes** | — | Owner = original funder address. |
| `offerSequence` | `uint` | **yes** | — | Sequence number of the original EscrowCreate transaction. |

### `xrpl_escrow_create_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/EscrowTools.cs`</sub>

Prepares an UNSIGNED EscrowCreate. amount is XRP drops (string), an issued-token JSON object, or an MPT amount (requires TokenEscrow amendment). Must specify FinishAfter or CancelAfter (or both). Condition (PREIMAGE-SHA-256, hex) makes it a conditional escrow that requires a matching Fulfillment to finish. All times are UTC; they are converted to Ripple-epoch by the SDK.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender account (escrow funder). |
| `destination` | `string` | **yes** | — | Destination account that receives the escrowed funds on finish. |
| `amount` | `string` | **yes** | — | Amount: drops string for XRP, JSON {value,currency,issuer} for tokens. |
| `finishAfterUtc` | `DateTime?` | no | `null` | UTC time after which the destination can claim funds (recipient-side). |
| `cancelAfterUtc` | `DateTime?` | no | `null` | UTC time after which the escrow can be cancelled (refund to sender). |
| `conditionHex` | `string?` | no | `null` | Hex PREIMAGE-SHA-256 crypto-condition. If set, EscrowFinish requires a matching Fulfillment. |
| `destinationTag` | `uint?` | no | `null` | Optional destination tag. |

### `xrpl_escrow_finish_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/EscrowTools.cs`</sub>

Prepares an UNSIGNED EscrowFinish. owner is the original escrow funder; offerSequence is the Sequence of the EscrowCreate transaction. For conditional escrows, provide both conditionHex and fulfillmentHex.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender of the finish (often the destination). |
| `owner` | `string` | **yes** | — | Owner = original funder address. |
| `offerSequence` | `uint` | **yes** | — | Sequence number of the original EscrowCreate transaction. |
| `conditionHex` | `string?` | no | `null` | Hex PREIMAGE-SHA-256 condition (must match the escrow). Required for conditional escrows. |
| `fulfillmentHex` | `string?` | no | `null` | Hex PREIMAGE-SHA-256 fulfillment matching the condition. Required for conditional escrows. |

### `xrpl_fee`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LedgerTools.cs`</sub>

Returns current open-ledger transaction cost (drops). Use this to size Fee before submitting.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |

### `xrpl_gateway_balances`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountTools.cs`</sub>

Issuer-side balance summary: total obligations (tokens this account has issued and are held by non-excluded addresses), assets held that were issued by others, and balances held by the listed hotwallets. Pass hotwalletsJson as a JSON array of r-addresses to exclude operational/hot wallets from obligations. Strict mode rejects non-r-address inputs.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Issuer address to inspect. |
| `hotwalletsJson` | `string?` | no | `null` | Optional JSON array of r-addresses to treat as hotwallets and exclude from obligations. |
| `strict` | `bool` | no | `true` | If true, only accept r-address/public-key for account. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_hash_credential`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/HashTools.cs`</sub>

Computes the canonical XRPL Credential ledger-object identifier (SHA-512/2, 64-hex Hash256) from (subject, issuer, credentialType). Used to build Payment.CredentialIDs for XLS-70 credential-gated deposits. credentialType may be provided as hex (even-length, ≤128 chars) OR as plain UTF-8 text — mutually exclusive. Pure local computation, no network call.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `subject` | `string` | **yes** | — | Credential subject — classic XRP r-address (the credential holder). |
| `issuer` | `string` | **yes** | — | Credential issuer — classic XRP r-address. |
| `credentialTypeHex` | `string?` | no | `null` | Credential type as hex (1..128 hex chars, even length). Mutually exclusive with credentialTypePlain. |
| `credentialTypePlain` | `string?` | no | `null` | Credential type as plain UTF-8 text (auto-hex-encoded). Mutually exclusive with credentialTypeHex. |

### `xrpl_ledger`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LedgerTools.cs`</sub>

Returns a ledger header (and optionally its transactions) for the specified ledger.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `ledgerIndex` | `string?` | no | `null` |  |
| `transactions` | `bool` | no | `false` | If true, include the transaction list of the ledger. |
| `expand` | `bool` | no | `false` | If true, expand the transactions to full JSON instead of hashes only. |

### `xrpl_loan_broker_cover_clawback_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LoanBrokerTools.cs`</sub>

Prepares an UNSIGNED LoanBrokerCoverClawback (XLS-66). Asset issuer claws back cover capital from a LoanBroker. At least ONE of loanBrokerId or amount (currency+value) must be specified. With only loanBrokerId: max available from that broker. With only amount: amount-only clawback (broker chosen by amendment rules). With both: amount-from-broker.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Issuer account submitting the clawback. |
| `loanBrokerId` | `string?` | no | `null` | Optional 64-hex LoanBrokerID. Omit to let the network select the broker by amount. |
| `assetCurrency` | `string?` | no | `null` | Optional asset currency (with amount). |
| `assetIssuer` | `string?` | no | `null` | Optional asset issuer. |
| `amountValue` | `string?` | no | `null` | Optional decimal amount. At least one of loanBrokerId or amount must be present. |

### `xrpl_loan_broker_cover_deposit_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LoanBrokerTools.cs`</sub>

Prepares an UNSIGNED LoanBrokerCoverDeposit (XLS-66). Deposits cover (first-loss) capital into the LoanBroker's pseudo-account. The amount currency must match the underlying vault's asset.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account depositing cover capital (typically the broker owner or a backstop LP). |
| `loanBrokerId` | `string` | **yes** | — | 64-hex LoanBrokerID. |
| `assetCurrency` | `string` | **yes** | — | Vault asset currency ('XRP', 3-char, or 40-hex). |
| `assetIssuer` | `string?` | no | — | Vault asset issuer (empty for XRP). |
| `amountValue` | `string` | **yes** | — | Decimal amount to deposit (drops for XRP, decimal value for IOU/MPT). |

### `xrpl_loan_broker_cover_withdraw_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LoanBrokerTools.cs`</sub>

Prepares an UNSIGNED LoanBrokerCoverWithdraw (XLS-66). Withdraws cover capital from a LoanBroker. Allowed only up to the amount that keeps CoverRate above CoverRateMinimum (the rippled check is post-tx). Optional destination defaults to account.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Owner/depositor account submitting the withdrawal. |
| `loanBrokerId` | `string` | **yes** | — | 64-hex LoanBrokerID. |
| `assetCurrency` | `string` | **yes** | — | Vault asset currency. |
| `assetIssuer` | `string?` | no | — | Vault asset issuer (empty for XRP). |
| `amountValue` | `string` | **yes** | — | Decimal amount to withdraw. |
| `destination` | `string?` | no | `null` | Optional destination address (defaults to account). |
| `destinationTag` | `uint?` | no | `null` | Optional destination tag. |

### `xrpl_loan_broker_delete_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LoanBrokerTools.cs`</sub>

Prepares an UNSIGNED LoanBrokerDelete (XLS-66). Removes a LoanBroker — allowed only when no active loans remain and cover is fully withdrawn.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | LoanBroker owner account. |
| `loanBrokerId` | `string` | **yes** | — | 64-hex LoanBrokerID to delete. |

### `xrpl_loan_broker_set_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LoanBrokerTools.cs`</sub>

Prepares an UNSIGNED LoanBrokerSet (XLS-66). Creates a new LoanBroker (omit loanBrokerId) or modifies an existing one (provide its 64-hex LoanBrokerID). The submitting account must own the vault identified by vaultId. CoverRateMinimum / CoverRateLiquidation are in 1/100th bp (0..100000 = 0..100%). ManagementFeeRate is in 1/10th bp (0..10000 = 0..100%). DebtMaximum is an STNumber decimal string (omit or '0' for no cap). 'dataHex' is ≤512 hex chars (= 256 bytes).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Vault owner account (also becomes LoanBroker owner). |
| `vaultId` | `string` | **yes** | — | 64-hex VaultID of the underlying Vault. Required even when modifying — must match the broker's vault. |
| `loanBrokerId` | `string?` | no | `null` | Optional 64-hex LoanBrokerID — omit to create a new broker. |
| `coverRateMinimum` | `uint?` | no | `null` | Minimum cover rate (1/100th bp, 0..100000). Defines the lower-bound coverage required from cover capital. |
| `coverRateLiquidation` | `uint?` | no | `null` | Liquidation cover rate (1/100th bp, 0..100000). When coverage drops to this threshold, defaults trigger liquidation. |
| `managementFeeRate` | `uint?` | no | `null` | Management fee rate (1/10th bp, 0..10000). Charged on each loan payment to the broker. |
| `debtMaximum` | `string?` | no | `null` | Optional debt ceiling (STNumber decimal string). Omit or '0' = no limit. |
| `dataHex` | `string?` | no | `null` | Optional hex blob (≤512 hex chars = 256 bytes). |

### `xrpl_loan_delete_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LoanTools.cs`</sub>

Prepares an UNSIGNED LoanDelete (XLS-66). Removes a loan ledger entry — allowed only when the loan is fully repaid (or otherwise closed by the amendment rules).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Submitter account (typically LoanBroker owner). |
| `loanId` | `string` | **yes** | — | 64-hex LoanID to delete. |

### `xrpl_loan_manage_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LoanTools.cs`</sub>

Prepares an UNSIGNED LoanManage (XLS-66). Submitter must own the LoanBroker (or be authorized). Pass action='default' (tfLoanDefault), 'impair' (tfLoanImpair), or 'unimpair' (tfLoanUnimpair) — mutually exclusive.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | LoanBroker owner (or authorized manager). |
| `loanId` | `string` | **yes** | — | 64-hex LoanID. |
| `action` | `string` | **yes** | — | 'default' \| 'impair' \| 'unimpair' (mutually exclusive). |

### `xrpl_loan_pay_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LoanTools.cs`</sub>

Prepares an UNSIGNED LoanPay (XLS-66). Borrower makes a payment on a loan. 'amountValue' is the payment amount in the vault-asset units. Optional 'paymentKind' ∈ {'scheduled','overpayment','full','late'} — at most one of overpayment/full/late may be set; 'scheduled' (default) sets no kind-flag. Overpayment requires the loan was originated with tfLoanOverpayment.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Borrower account (the loan's Counterparty). |
| `loanId` | `string` | **yes** | — | 64-hex LoanID. |
| `assetCurrency` | `string` | **yes** | — | Vault asset currency. |
| `assetIssuer` | `string?` | no | — | Vault asset issuer (empty for XRP). |
| `amountValue` | `string` | **yes** | — | Payment amount (decimal string). |
| `paymentKind` | `string?` | no | `null` | Optional payment kind: 'scheduled' (default), 'overpayment', 'full', or 'late'. |

### `xrpl_loan_set_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LoanTools.cs`</sub>

Prepares an UNSIGNED LoanSet (XLS-66). Originates a new loan against a LoanBroker. The borrower (counterparty) must co-sign — submit this inside a Batch with both signatures, or expect the chain to require an out-of-band CounterpartySignature. Rate fields are in 1/100th bp (0..100000 = 0..100%). Fee fields are STNumber decimal strings in the vault-asset units. Set 'allowOverpayment=true' to mark the loan with tfLoanOverpayment.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Loan originator (typically the LoanBroker owner). |
| `loanBrokerId` | `string` | **yes** | — | 64-hex LoanBrokerID under which to originate. |
| `counterparty` | `string` | **yes** | — | Borrower account (counterparty). |
| `principalRequested` | `string` | **yes** | — | Principal amount requested (STNumber decimal string, in vault-asset units). |
| `interestRate` | `uint?` | no | `null` | Optional interest rate (1/100th bp, 0..100000). |
| `lateInterestRate` | `uint?` | no | `null` | Optional late-payment interest rate (1/100th bp). |
| `closeInterestRate` | `uint?` | no | `null` | Optional early-close interest rate (1/100th bp). |
| `overpaymentInterestRate` | `uint?` | no | `null` | Optional overpayment interest rate (1/100th bp). |
| `overpaymentFee` | `uint?` | no | `null` | Optional overpayment fee (1/100th bp). |
| `loanOriginationFee` | `string?` | no | `null` | Optional loan origination fee (STNumber decimal string). |
| `loanServiceFee` | `string?` | no | `null` | Optional per-payment service fee (STNumber decimal string). |
| `latePaymentFee` | `string?` | no | `null` | Optional late payment fee (STNumber decimal string). |
| `closePaymentFee` | `string?` | no | `null` | Optional close payment fee (STNumber decimal string). |
| `paymentTotal` | `uint?` | no | `null` | Optional total number of payments. Default 1. |
| `paymentInterval` | `uint?` | no | `null` | Optional interval between payments in seconds. Default 60. |
| `gracePeriod` | `uint?` | no | `null` | Optional grace period in seconds. Default 60. |
| `dataHex` | `string?` | no | `null` | Optional hex blob (≤512 hex chars = 256 bytes). |
| `allowOverpayment` | `bool` | no | `false` | If true, sets tfLoanOverpayment — permits the borrower to overpay scheduled installments. |

### `xrpl_manifest`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LedgerTools.cs`</sub>

Returns the validator manifest (publish slot, master key, ephemeral signing key, sequence) for a given public_key. Useful for inspecting which validator is behind a public key seen in 'validations' stream messages. SDK has no typed wrapper for this — dispatched as a generic command.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `publicKey` | `string` | **yes** | — | Validator master public key (base58 with 'n...' prefix) or ephemeral signing key. |

### `xrpl_mpt_authorize_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/MptTools.cs`</sub>

Prepares an UNSIGNED MPTokenAuthorize (XLS-33). Two roles: (a) a HOLDER opts in to hold an MPT by submitting with their own account and no 'holder' field; (b) an ISSUER explicitly authorizes a specific holder by submitting with the issuer account and 'holder' set to the holder's address. In either role, set unauthorize=true to revoke (tfMPTUnauthorize) — a holder with non-zero balance cannot revoke; an issuer can only revoke for MPTs that use allow-listing (tfMPTRequireAuth).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender account — either the holder (opting in) or the issuer (authorizing a specific holder). |
| `mptokenIssuanceId` | `string` | **yes** | — | MPTokenIssuanceID — 48-hex uppercase. |
| `holder` | `string?` | no | `null` | Optional holder address — set when 'account' is the issuer authorizing/revoking that holder. Omit when 'account' is the holder opting in/out. |
| `unauthorize` | `bool` | no | `false` | true → tfMPTUnauthorize (revoke); false (default) → authorize/opt-in. |

### `xrpl_mpt_issuance_create_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/MptTools.cs`</sub>

Prepares an UNSIGNED MPTokenIssuanceCreate (XLS-33). Creates a new MPTokenIssuance ledger object owned by 'account'. The on-chain MPTokenIssuanceID is derived from the issuer + Sequence after the tx is validated. Provide capability flags either via 'flagsBitmask' (raw uint) OR via the boolean convenience parameters (canLock/requireAuth/canEscrow/canTrade/canTransfer/canClawback) — these are mutually exclusive.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Issuer account that will own this MPTokenIssuance. |
| `assetScale` | `uint?` | no | `null` | Asset scale (0..10). The fractional unit equals 10^(-scale) of one standard unit. Default 0. |
| `maximumAmount` | `string?` | no | `null` | Optional maximum total amount that may ever be issued. Decimal string (uint64, 0..9223372036854775807). Omit for no cap. |
| `transferFee` | `uint?` | no | `null` | Optional transfer fee in 1/10 bps, 0..50000 (=0%..50%, increments of 0.001%). Only meaningful with tfMPTCanTransfer. |
| `metadataHex` | `string?` | no | `null` | Optional metadata bytes as a hex string. Max 1024 raw bytes (= 2048 hex chars). XLS-89 schema recommended. |
| `flagsBitmask` | `uint?` | no | `null` | Optional raw bitmask combining MPTokenIssuanceCreateFlags (tfMPTCanLock=2, tfMPTRequireAuth=4, tfMPTCanEscrow=8, tfMPTCanTrade=16, tfMPTCanTransfer=32, tfMPTCanClawback=64). Mutually exclusive with the boolean convenience flags. |
| `canLock` | `bool?` | no | `null` | Convenience: enable tfMPTCanLock (issuer can lock balances). |
| `requireAuth` | `bool?` | no | `null` | Convenience: enable tfMPTRequireAuth (holders must be authorized). |
| `canEscrow` | `bool?` | no | `null` | Convenience: enable tfMPTCanEscrow (holders can escrow balances). |
| `canTrade` | `bool?` | no | `null` | Convenience: enable tfMPTCanTrade (holders can trade via DEX/AMM). |
| `canTransfer` | `bool?` | no | `null` | Convenience: enable tfMPTCanTransfer (non-issuer accounts can transfer balances). |
| `canClawback` | `bool?` | no | `null` | Convenience: enable tfMPTCanClawback (issuer can clawback balances). |

### `xrpl_mpt_issuance_destroy_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/MptTools.cs`</sub>

Prepares an UNSIGNED MPTokenIssuanceDestroy (XLS-33). Removes an MPTokenIssuance ledger object. Allowed only when OutstandingAmount=0 — i.e. no holders currently hold any balance.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Issuer account that owns this MPTokenIssuance. |
| `mptokenIssuanceId` | `string` | **yes** | — | MPTokenIssuanceID — 48-hex uppercase identifier of the issuance to destroy. |

### `xrpl_mpt_issuance_set_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/MptTools.cs`</sub>

Prepares an UNSIGNED MPTokenIssuanceSet (XLS-33). Locks or unlocks an MPTokenIssuance globally — or, when 'holder' is provided, only that holder's balance. Pass lock=true for tfMPTLock, lock=false for tfMPTUnlock, lock=null for a no-op (e.g. when the SDK adds additional non-flag fields in the future).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Issuer account that owns this MPTokenIssuance. |
| `mptokenIssuanceId` | `string` | **yes** | — | MPTokenIssuanceID — 48-hex uppercase. |
| `holder` | `string?` | no | `null` | Optional holder address — applies the lock/unlock to a single holder. Omit for global lock/unlock. |
| `lockBalance` | `bool?` | no | `null` | true → tfMPTLock; false → tfMPTUnlock; null → no flag (no-op). |

### `xrpl_nft_accept_offer_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/NftTools.cs`</sub>

Prepares an UNSIGNED NFTokenAcceptOffer. DIRECT mode: pass either sellOfferId OR buyOfferId. BROKERED mode: pass both AND optionally a brokerFee (the broker's cut, same format as xrpl_payment_prepare).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account accepting the offer (the broker in brokered mode). |
| `sellOfferId` | `string?` | no | `null` | Sell-offer NFTokenOffer ID (direct sell-accept, or brokered mode). |
| `buyOfferId` | `string?` | no | `null` | Buy-offer NFTokenOffer ID (direct buy-accept, or brokered mode). |
| `brokerFee` | `string?` | no | `null` | Optional broker fee (brokered mode only). Same format as xrpl_payment_prepare amount. |

### `xrpl_nft_burn_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/NftTools.cs`</sub>

Prepares an UNSIGNED NFTokenBurn. owner is only needed when burning a token you don't currently hold but are authorized to burn (e.g. issuer burning a tfBurnable NFT).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account submitting the burn (usually the current holder). |
| `nfTokenId` | `string` | **yes** | — | NFTokenID (64-char hex) of the NFT to burn. |
| `owner` | `string?` | no | `null` | Optional current owner if different from account (issuer burning a tfBurnable NFT). |

### `xrpl_nft_buy_offers`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/NftTools.cs`</sub>

Read-only: lists outstanding BUY offers for the given NFTokenID.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `nfTokenId` | `string` | **yes** | — | NFTokenID (64-char hex). |

### `xrpl_nft_cancel_offer_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/NftTools.cs`</sub>

Prepares an UNSIGNED NFTokenCancelOffer. nftOfferIds is the comma-separated list of NFTokenOffer object IDs (64-char hex each) to cancel.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender account. |
| `nftOfferIds` | `string` | **yes** | — | Comma-separated list of NFTokenOffer object IDs (each 64-char hex). |

### `xrpl_nft_create_offer_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/NftTools.cs`</sub>

Prepares an UNSIGNED NFTokenCreateOffer. SELL offer: set isSellOffer=true and DO NOT pass owner. BUY offer: set isSellOffer=false and pass owner (the current NFT holder). amount uses same format as xrpl_payment_prepare (drops string for XRP, JSON token object otherwise).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender account. |
| `nfTokenId` | `string` | **yes** | — | NFTokenID (64-char hex). |
| `amount` | `string` | **yes** | — | Price the offer GIVES (sell offer) or OFFERS (buy offer). Drops string for XRP or JSON {value,currency,issuer}. |
| `isSellOffer` | `bool` | **yes** | — | True for sell offer, false for buy offer. |
| `owner` | `string?` | no | `null` | Current owner of the NFT. Required for BUY offers; must be omitted for SELL offers. |
| `destination` | `string?` | no | `null` | Optional destination — only this account will be able to accept the offer. |
| `expirationUtc` | `DateTime?` | no | `null` | Optional UTC expiration time (offer becomes invalid after). |

### `xrpl_nft_mint_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/NftTools.cs`</sub>

Prepares an UNSIGNED NFTokenMint. uriHex must be hex-encoded (use uriPlain if you have a plain-string URI and want auto-encoding). Flags are a sum of: tfBurnable=1, tfOnlyXRP=2, tfTrustLine=4, tfTransferable=8, tfMutable=16. transferFee 0..50000 (0.000%..50.000%); requires tfTransferable.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Minter account (and owner of the freshly minted NFT, unless issuer is set). |
| `nfTokenTaxon` | `uint` | **yes** | — | NFTokenTaxon (uint32). Group identifier; set 0 if you don't use it. |
| `issuer` | `string?` | no | `null` | Optional issuer account, only if account is an authorized minter on behalf of someone else (sets the Issuer field). |
| `transferFee` | `uint?` | no | `null` | Optional transfer fee (0..50000) for secondary sales. Requires tfTransferable. |
| `uriHex` | `string?` | no | `null` | Optional NFT URI as a hex string. Mutually exclusive with uriPlain. |
| `uriPlain` | `string?` | no | `null` | Optional NFT URI as a plain string; will be UTF-8-hex-encoded. Mutually exclusive with uriHex. |
| `flags` | `uint` | no | `0` | Sum of NFTokenMint flags (tfBurnable=1, tfOnlyXRP=2, tfTrustLine=4, tfTransferable=8, tfMutable=16). |

### `xrpl_nft_modify_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/NftTools.cs`</sub>

Prepares an UNSIGNED NFTokenModify (XLS-46). Updates the URI of an existing NFT — only allowed when the NFT was minted with the tfMutable flag (16). Provide uriHex or uriPlain (auto-UTF-8-hex); pass clearUri=true to set an empty URI. The submitter must be either the current owner or the authorized minter for the issuer.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Submitter account — current owner of the NFT, or the issuer/authorized minter when 'owner' is set. |
| `nfTokenId` | `string` | **yes** | — | NFTokenID (64-char hex) of the NFT to modify. |
| `owner` | `string?` | no | `null` | Optional current owner if different from account (e.g. issuer modifying a held NFT). |
| `uriHex` | `string?` | no | `null` | New NFT URI as a hex string. Mutually exclusive with uriPlain / clearUri. |
| `uriPlain` | `string?` | no | `null` | New NFT URI as a plain string; will be UTF-8-hex-encoded. Mutually exclusive with uriHex / clearUri. |
| `clearUri` | `bool` | no | `false` | If true, omit URI entirely (clears the existing URI). Mutually exclusive with uriHex / uriPlain. |

### `xrpl_nft_sell_offers`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/NftTools.cs`</sub>

Read-only: lists outstanding SELL offers for the given NFTokenID.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `nfTokenId` | `string` | **yes** | — | NFTokenID (64-char hex). |

### `xrpl_offer_cancel_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/OfferTools.cs`</sub>

Prepares an UNSIGNED OfferCancel transaction. offerSequence is the Sequence number of the OfferCreate to remove.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender address (the offer owner). |
| `offerSequence` | `uint` | **yes** | — | Sequence number of the OfferCreate transaction to cancel. |

### `xrpl_offer_create_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/OfferTools.cs`</sub>

Prepares an UNSIGNED OfferCreate (limit order on the XRPL DEX). Amounts use the same format as xrpl_payment_prepare (drops string for XRP, JSON {value,currency,issuer} for tokens).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender address (the offer owner). |
| `takerGets` | `string` | **yes** | — | TakerGets — what the OFFER GIVES UP. Drops string for XRP or JSON token object. |
| `takerPays` | `string` | **yes** | — | TakerPays — what the OFFER WANTS. Drops string for XRP or JSON token object. |
| `expirationUtc` | `DateTime?` | no | `null` | Optional Ripple-epoch expiration (DateTime UTC). Use null for no expiration. |
| `offerSequence` | `uint?` | no | `null` | OfferSequence of an existing offer to cancel atomically. Optional. |
| `passive` | `bool` | no | `false` | If true, set tfPassive (don't consume exact-match offers). |
| `immediateOrCancel` | `bool` | no | `false` | If true, set tfImmediateOrCancel. |
| `fillOrKill` | `bool` | no | `false` | If true, set tfFillOrKill. |
| `sell` | `bool` | no | `false` | If true, set tfSell (exchange entire TakerGets even if you get more than TakerPays). |

### `xrpl_oracle_delete_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/OracleTools.cs`</sub>

Prepares an UNSIGNED OracleDelete (XLS-47). Removes the Oracle ledger entry identified by (Account, OracleDocumentID). Only the owner may delete.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Owner account of the Oracle entry. |
| `oracleDocumentId` | `uint` | **yes** | — | Oracle document id (uint32) to delete. |

### `xrpl_oracle_set_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/OracleTools.cs`</sub>

Prepares an UNSIGNED OracleSet (XLS-47). Creates a new Oracle ledger entry or updates an existing one. 'priceDataSeriesJson' is a JSON array (1..10) of PriceData objects: [{"baseAsset":"XRP","quoteAsset":"USD","assetPrice":"...","scale":6}, ...]. 'baseAsset' / 'quoteAsset' may be a 3-char or 40-char hex currency code; 'assetPrice' must be a decimal uint64 string and is required together with 'scale' (0..10). 'lastUpdateTimeUnix' is seconds since epoch — must be within 300s of ledger close. 'provider', 'uri', 'assetClass' are ASCII strings (will be hex-encoded); provider/assetClass are required when creating, optional on updates.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Owner account of the Oracle entry. |
| `oracleDocumentId` | `uint` | **yes** | — | Unique uint32 oracle id within this Account. Combined with Account it identifies the on-ledger Oracle object. |
| `lastUpdateTimeUnix` | `long` | **yes** | — | Unix timestamp (seconds) of when the price data was observed. Must be within 300s of the ledger's close. |
| `priceDataSeriesJson` | `string` | **yes** | — | JSON array of PriceData (1..10 entries). Shape per entry: {baseAsset, quoteAsset, assetPrice?, scale?}. assetPrice and scale must both be present or both absent. |
| `provider` | `string?` | no | `null` | Optional ASCII oracle provider name (Chainlink, Band, DIA, ...). Required on creation; optional on update. Max 256 chars before hex-encoding. |
| `uri` | `string?` | no | `null` | Optional ASCII URI for off-chain reference. Max 256 bytes before hex-encoding. |
| `assetClass` | `string?` | no | `null` | Optional ASCII asset class ('currency','commodity','index',...). Required on creation; optional on update. Max 16 chars before hex-encoding. |

### `xrpl_path_find_close`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PathTools.cs`</sub>

WebSocket-only. Closes the currently-open path_find subscription on the pool's connection.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — | Network identifier — must match the one used in xrpl_path_find_create. |

### `xrpl_path_find_create`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PathTools.cs`</sub>

WebSocket-only. Opens a long-running pathfinding subscription on the pool's shared connection. Returns the initial set of alternatives; subsequent updates are delivered to the pool's WebSocket but NOT relayed back through MCP — for ongoing updates, re-poll via xrpl_path_find_status. Only one open path_find request per WebSocket; calling this twice replaces the previous one.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — | Network identifier — 'mainnet', 'testnet', 'devnet' or a wss:// URL. JSON-RPC URLs WILL NOT work with this method. |
| `sourceAccount` | `string` | **yes** | — | Source account address. |
| `destinationAccount` | `string` | **yes** | — | Destination account address. |
| `destinationAmount` | `string` | **yes** | — | Destination amount (same format as xrpl_payment_prepare). |
| `sendMax` | `string?` | no | `null` | Optional: maximum the source is willing to spend. |

### `xrpl_path_find_status`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PathTools.cs`</sub>

WebSocket-only. Requests an immediate update for the currently-open path_find subscription on the pool's connection. Returns the latest alternatives. No-op if there is no open path_find.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — | Network identifier — must match the one used in xrpl_path_find_create. |

### `xrpl_payment_channel_claim_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PaymentChannelTools.cs`</sub>

Prepares an UNSIGNED PaymentChannelClaim. Used by either side to claim XRP and/or renew/close the channel. signatureHex + publicKeyHex are required when the destination is claiming OR when the source is redeeming a signed claim (sign offline via the signer plugin). Pass renew=true to clear the channel's Expiration; close=true to schedule closure.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender of the claim (source or destination of the channel). |
| `channelId` | `string` | **yes** | — | Channel ID (64-char hex). |
| `amountDrops` | `string?` | no | `null` | Cumulative XRP drops authorized by the signature. |
| `balanceDrops` | `string?` | no | `null` | Cumulative XRP drops delivered after this claim (required unless closing). |
| `signatureHex` | `string?` | no | `null` | Hex signature over (channelId, amount). Required when destination claims or when redeeming a third-party signed claim. |
| `publicKeyHex` | `string?` | no | `null` | Hex public key matching the channel's PublicKey. Required when signatureHex is set. |
| `renew` | `bool` | no | `false` | If true, set tfRenew to clear the channel's Expiration (source only). |
| `close` | `bool` | no | `false` | If true, set tfClose to schedule channel closure (source or destination). |

### `xrpl_payment_channel_create_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PaymentChannelTools.cs`</sub>

Prepares an UNSIGNED PaymentChannelCreate. amountDrops is XRP drops (string). settleDelaySeconds is the source-side close grace period. publicKeyHex is the secp256k1/ed25519 public key the source will sign claims with — get it from xrpl_wallet_address (publicKey).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Source account funding the channel. |
| `destination` | `string` | **yes** | — | Destination account that can claim from this channel. |
| `amountDrops` | `string` | **yes** | — | Amount of XRP in drops (string) to fund the channel. |
| `settleDelaySeconds` | `uint` | **yes** | — | Settle delay in seconds — minimum wait before source can close with unclaimed XRP. |
| `publicKeyHex` | `string` | **yes** | — | Hex public key the source will sign claims with (33-byte secp256k1 or 32-byte ed25519, hex). |
| `cancelAfterUtc` | `DateTime?` | no | `null` | Optional UTC time after which the channel auto-closes (immutable). |
| `destinationTag` | `uint?` | no | `null` | Optional destination tag. |
| `sourceTag` | `uint?` | no | `null` | Optional source tag. |

### `xrpl_payment_channel_fund_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PaymentChannelTools.cs`</sub>

Prepares an UNSIGNED PaymentChannelFund. Adds XRP to an open channel and optionally bumps its Expiration. Only the source account can fund.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Source account (channel owner). |
| `channelId` | `string` | **yes** | — | Channel ID (64-char hex). |
| `amountDrops` | `string` | **yes** | — | Amount of XRP in drops to add. |
| `expirationUtc` | `DateTime?` | no | `null` | Optional new UTC Expiration time (must be later than current expiration + settleDelay). |

### `xrpl_payment_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PaymentTools.cs`</sub>

Prepares an UNSIGNED Payment transaction. Returns autofilled tx JSON + unsigned blob + signing data. Caller signs locally and then calls xrpl_tx_submit_signed. Amount: numeric drops string for XRP (1 XRP = 1000000 drops), or {"value":"...","currency":"...","issuer":"..."} JSON for tokens, or {"value":"...","mpt_issuance_id":"<48-hex>"} for MPT. For XLS-70 credential-gated deposits, pass credentialIdsJson with the SHA-512/2 credential hashes (compute via xrpl_hash_credential).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender address (classic r-address). Server does NOT need its seed. |
| `destination` | `string` | **yes** | — | Destination XRP address. |
| `amount` | `string` | **yes** | — | Amount. XRP drops as a numeric string (e.g. '10000000' = 10 XRP) OR token amount as JSON {"value":"...","currency":"...","issuer":"..."}. |
| `destinationTag` | `uint?` | no | `null` | Optional destination tag (uint32, e.g. for exchange deposits). |
| `sourceTag` | `uint?` | no | `null` | Optional source tag. |
| `invoiceId` | `string?` | no | `null` | Optional invoice ID (32-byte hex). |
| `credentialIdsJson` | `string?` | no | `null` | Optional XLS-70 CredentialIDs — JSON array of 64-hex SHA-512/2 credential hashes (compute via xrpl_hash_credential). When recipient has DepositAuth + DepositPreauth(AuthorizeCredentials) set, this proves the sender holds the required accepted credentials. 1..8 entries; each entry MUST be a 64-char hex Hash256. |

### `xrpl_permissioned_domain_delete_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PermissionedDomainTools.cs`</sub>

Prepares an UNSIGNED PermissionedDomainDelete (XLS-80). Removes a permissioned domain owned by the account. domainId is the 64-hex DomainID returned at creation (or visible in account_objects).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Owner account of the permissioned domain. |
| `domainId` | `string` | **yes** | — | 64-hex DomainID of the domain to delete. |

### `xrpl_permissioned_domain_set_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PermissionedDomainTools.cs`</sub>

Prepares an UNSIGNED PermissionedDomainSet (XLS-80). Creates a new permissioned domain (omit domainId) or modifies an existing one (provide its 64-hex DomainID). 'acceptedCredentialsJson' is a JSON array of 1..10 entries: [{"issuer":"r...","credentialType":"<hex>"}, ...]. No duplicates by (issuer, credentialType). On modify, the new list FULLY REPLACES the previous one.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Owner account of the permissioned domain. |
| `acceptedCredentialsJson` | `string` | **yes** | — | JSON array of 1..10 accepted credentials: [{"issuer":"r...","credentialType":"<hex 1..128 chars>"}]. |
| `domainId` | `string?` | no | `null` | Optional 64-hex DomainID of an existing domain to modify. Omit to create a new domain. |

### `xrpl_ripple_path_find`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PathTools.cs`</sub>

One-shot cross-currency pathfinder. Returns the alternatives array — every entry has a 'source_amount' you can drop straight into a Payment as SendMax along with its 'paths_computed' as Paths. destinationAmount uses the same format as xrpl_payment_prepare (drops string for XRP or JSON {value,currency,issuer} for tokens). sourceCurrenciesJson optionally restricts what the source can spend: JSON array of {currency,issuer?} objects (max 18).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `sourceAccount` | `string` | **yes** | — | Source account address. |
| `destinationAccount` | `string` | **yes** | — | Destination account address. |
| `destinationAmount` | `string` | **yes** | — | Destination amount: drops string for XRP, JSON {value,currency,issuer} for tokens. Pass value='-1' to ask 'deliver as much as possible up to sendMax'. |
| `sendMax` | `string?` | no | `null` | Optional: maximum the source is willing to spend. Same format as destinationAmount. Mutually exclusive with sourceCurrenciesJson per docs. |
| `sourceCurrenciesJson` | `string?` | no | `null` | Optional: JSON array of {currency,issuer?} the source is willing to spend (max 18). |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_server_definitions`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LedgerTools.cs`</sub>

Returns the binary-format definition tables the node uses (FIELDS, LEDGER_ENTRY_TYPES, TRANSACTION_RESULTS, TRANSACTION_TYPES, TYPES) plus a content hash. Pass the previous hash to short-circuit if nothing changed (server returns empty result). Use this for feature/amendment detection on the node.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `hash` | `string?` | no | `null` | Optional content hash from a previous call — if it matches, the server returns nothing. |

### `xrpl_server_info`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LedgerTools.cs`</sub>

Returns rippled node status: version, validated ledger range, build info, peers. Read-only.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |

### `xrpl_server_state`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LedgerTools.cs`</sub>

Machine-readable version of server_info. Returns load factors, validated/closed ledger info, state-accounting buckets, validation quorum, build version. Use this when you need numeric thresholds rather than human strings.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |

### `xrpl_set_regular_key_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountManagementTools.cs`</sub>

Prepares an UNSIGNED SetRegularKey. Pass regularKey=null (or omit) to REMOVE the existing regular key pair. Must not match the master key pair for the account.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account whose regular key is being assigned/cleared. |
| `regularKey` | `string?` | no | `null` | New regular key address (classic r-address). Omit to REMOVE. |

### `xrpl_signer_list_set_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountManagementTools.cs`</sub>

Prepares an UNSIGNED SignerListSet. signerQuorum=0 DELETES the signer list (must omit signerEntries). Otherwise signerEntries is a JSON array of objects: [{"account":"r...","weight":1,"walletLocator":"<optional 64-char hex>"}, ...]. Quorum must be ≤ sum of weights; up to 32 entries.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account that owns this signer list. |
| `signerQuorum` | `uint` | **yes** | — | Required signer weight sum. 0 = DELETE the signer list. |
| `signerEntriesJson` | `string?` | no | `null` | JSON array of signer entries: [{"account":"r...","weight":1,"walletLocator":"<hex>"}]. Required when signerQuorum>0; must be omitted when signerQuorum=0. |

### `xrpl_signer_list_status`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountObjectsHelperTools.cs`</sub>

Reads the account's SignerList ledger object and returns multi-sign status: quorum, total available weight (sum of all signer weights), and per-signer breakdown. Pass alreadySignedAccountsCsv to compute the weight already collected and how much more is needed to reach quorum — useful when collecting signatures for a multi-sign transaction.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address whose SignerList to inspect. |
| `alreadySignedAccountsCsv` | `string?` | no | `null` | Optional comma-separated list of signer addresses that have already signed; used to compute deltaToQuorum. |
| `ledgerIndex` | `string?` | no | `null` |  |

### `xrpl_subscribe`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/SubscriptionTools.cs`</sub>

Installs a subscription on the pool's shared WebSocket. WARNING: streaming events are not delivered back through MCP — prefer xrpl_account_tx_since for polling-based monitoring. streamsCsv: comma-separated subset of {ledger,transactions,transactions_proposed,validations,manifests,server,peer_status,consensus,book_changes}. accountsJson / accountsProposedJson: JSON array of r-addresses.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `streamsCsv` | `string?` | no | `null` | Comma-separated stream names. Empty to subscribe only by account/book. |
| `accountsJson` | `string?` | no | `null` | JSON array of r-addresses to subscribe to (validated transactions affecting them). |
| `accountsProposedJson` | `string?` | no | `null` | JSON array of r-addresses to subscribe to (proposed + validated, unfinalized). |

### `xrpl_ticket_create_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountManagementTools.cs`</sub>

Prepares an UNSIGNED TicketCreate. Reserves ticketCount sequence numbers as Tickets that can later be consumed via the TicketSequence field on any future transaction (instead of Sequence). Each Ticket is one owner-object (+2 XRP reserve). ticketCount must be 1..250 and the account must end up owning ≤250 Tickets total.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account that will own the new Tickets. |
| `ticketCount` | `uint` | **yes** | — | How many Tickets to create (1..250). Each reserves one sequence number + one owner-object slot (~2 XRP reserve). |

### `xrpl_trustline_freeze_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/IssuerTools.cs`</sub>

Prepares an UNSIGNED TrustSet that freezes or unfreezes a specific trust line — wrapper over TrustSet with tfSetFreeze/tfClearFreeze (or tfSetDeepFreeze/tfClearDeepFreeze when deep=true). Only meaningful when sender is the token issuer. limitValue defaults to '0' (do not change the trust limit) — pass a positive value only if you also want to adjust the limit.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender (issuer) freezing the trust line. |
| `currency` | `string` | **yes** | — | Token currency code (3-char or 40-hex). |
| `holder` | `string` | **yes** | — | Counterparty (holder) address — the side being frozen. |
| `freeze` | `bool` | **yes** | — | True = freeze; false = unfreeze. |
| `deep` | `bool` | no | `false` | If true, use DeepFreeze flags (tfSetDeepFreeze / tfClearDeepFreeze) instead of regular Freeze. |
| `limitValue` | `string` | no | `"0"` | Limit value to set on the trust line. Default '0' — keep current behavior (just toggle the flag). |

### `xrpl_trustset_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PaymentTools.cs`</sub>

Prepares an UNSIGNED TrustSet transaction to create or modify a trust line. Set limitValue to '0' to remove a trust line (only succeeds when balance and flags are at defaults).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account that holds the trust line (sender). |
| `currency` | `string` | **yes** | — | Token currency code (3-char or 40-hex). |
| `issuer` | `string` | **yes** | — | Token issuer address. |
| `limitValue` | `string` | **yes** | — | Trust limit value as a decimal string (e.g. '1000000'). Use '0' to attempt removal. |
| `qualityIn` | `uint?` | no | `null` | Quality in (rate at which received tokens are valued), 1_000_000_000 = 1.0. Optional. |
| `qualityOut` | `uint?` | no | `null` | Quality out (rate at which sent tokens are valued). Optional. |

### `xrpl_tx_decode_blob`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/TransactionTools.cs`</sub>

Decodes a binary transaction blob (signed or unsigned) into JSON for inspection. Pure local operation — no network calls.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `txBlob` | `string` | **yes** | — | Hex-encoded transaction blob. |

### `xrpl_tx_explain`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/ExplainTools.cs`</sub>

Converts a transaction into a one-line human summary for approval prompts. Pass EITHER txBlobHex (signed or unsigned canonical hex) OR txJson (the JSON object directly). Returns {transactionType, humanSummary, txJson} — humanSummary is a sentence like 'Payment from rA... to rB...: 10000000 drops XRP. [fee=12 drops, seq=42, LLS=1234]'. Pure local operation — no network calls.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `txBlobHex` | `string?` | no | `null` | Hex-encoded transaction blob (signed or unsigned). Mutually exclusive with txJson. |
| `txJson` | `string?` | no | `null` | Transaction as a JSON object string. Mutually exclusive with txBlobHex. |

### `xrpl_tx_lookup`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/LedgerTools.cs`</sub>

Looks up a single transaction by hash. Returns engine result, metadata and validated flag.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `txHash` | `string` | **yes** | — | 64-char hex transaction hash. |
| `binary` | `bool` | no | `false` | If true, return the binary blob instead of expanded JSON. |

### `xrpl_tx_preflight`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PreflightTools.cs`</sub>

Read-only pre-flight check. Inspects the sender (account_info + server_state) and, for Payment, the destination, to surface common gotchas BEFORE signing/submitting: insufficient balance, insufficient reserve after Amount+Fee, RequireDestinationTag without DestinationTag, DepositAuth without preauth, DisallowIncomingXRP. Pass the prepared transaction as txJson (from a *_prepare tool). Returns a JSON report — { feasible, balanceXrp, reserveXrp, freeXrp, requiredXrp, sourceFlags, destinationFlags, warnings[] }. NOT a guarantee — does not check path liquidity or AMM state.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `txJson` | `string` | **yes** | — | Transaction JSON string (e.g. the TxJson field from a *_prepare result). |

### `xrpl_tx_prepare_generic`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/TransactionTools.cs`</sub>

Escape hatch: prepares any XRPL transaction described as a JSON object (TransactionType + fields). Autofills Sequence/Fee/LastLedgerSequence and returns unsigned blob + signing data. Use for tx types not covered by dedicated *_prepare tools (Escrow, NFToken, Check, PaymentChannel, AccountSet, …).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `txJson` | `string` | **yes** | — | Raw transaction as a JSON object, e.g. {"TransactionType":"AccountSet","Account":"r...","SetFlag":8}. |
| `humanSummary` | `string?` | no | `null` | Optional one-line human summary shown to the user in the approval prompt. |

### `xrpl_tx_simulate`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/PreflightTools.cs`</sub>

Dry-run a transaction without submitting. Runs xrpl_tx_preflight plus type-specific checks: for Payment, calls ripple_path_find to confirm a path exists for cross-currency / token deliveries and includes the first alternative's source_amount + paths_computed. Returns { preflight, suggestedPathfind, recommendedFee, recommendedLastLedgerSequence, warnings[] }. Useful for showing the user a realistic 'what will happen' before they approve a signature.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `txJson` | `string` | **yes** | — | Transaction JSON string. |

### `xrpl_tx_submit_signed`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/TransactionTools.cs`</sub>

Submits a SIGNED transaction blob to the network. The blob must already be signed locally — the server NEVER signs. Optionally polls until the transaction is included in a validated ledger.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `txBlobSigned` | `string` | **yes** | — | Signed transaction blob as a hex string. Produced locally by signing the tx_blob_unsigned returned by a *_prepare tool. |
| `failHard` | `bool` | no | `true` | If true, do NOT retry or relay if the transaction fails locally (rippled fail_hard). |
| `waitForValidation` | `bool` | no | `false` | If true, after submission poll for the transaction hash until it is in a validated ledger or LastLedgerSequence is reached. |
| `pollIntervalSeconds` | `int` | no | `2` | Polling interval in seconds when wait_for_validation is true. Default 2. |
| `maxPolls` | `int` | no | `30` | Max number of polls. Default 30 (≈60 seconds at default interval, longer than the LastLedgerSequence + 20 window). |

### `xrpl_unsubscribe`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/SubscriptionTools.cs`</sub>

Mirror of xrpl_subscribe — removes subscriptions from the pool's shared WebSocket. Same parameter shape.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `streamsCsv` | `string?` | no | `null` | Comma-separated stream names to unsubscribe. |
| `accountsJson` | `string?` | no | `null` | JSON array of r-addresses to unsubscribe. |
| `accountsProposedJson` | `string?` | no | `null` | JSON array of r-addresses to unsubscribe from the proposed stream. |

### `xrpl_vault_clawback_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/VaultTools.cs`</sub>

Prepares an UNSIGNED VaultClawback (XLS-65). Asset issuer claws back vault-deposited assets from a holder. The issuer must own the vault's asset (via tfMPTCanClawback for MPT, or asfAllowTrustLineClawback for IOU). Omit amountValue/Currency fields to claw back the maximum available.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Issuer account submitting the clawback. |
| `vaultId` | `string` | **yes** | — | 64-hex VaultID. |
| `holder` | `string` | **yes** | — | Holder whose deposited assets are being clawed back. |
| `assetCurrency` | `string?` | no | `null` | Optional vault-asset currency ('XRP' not allowed for clawback). Omit together with amountValue for max. |
| `assetIssuer` | `string?` | no | `null` | Optional asset issuer (empty allowed for MPT amounts). |
| `amountValue` | `string?` | no | `null` | Optional amount value (decimal string). Omit for the maximum available. |

### `xrpl_vault_create_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/VaultTools.cs`</sub>

Prepares an UNSIGNED VaultCreate (XLS-65). Creates a new pooled-asset vault owned by 'account'. The vault auto-issues an MPT representing pool shares. assetCurrency='XRP' (with empty issuer) for an XRP vault, otherwise 3-char/40-hex currency code + issuer. amountValue is the initial deposit (decimal string in vault-asset units). 'assetsMaximum' is an STNumber decimal string cap (omit for no cap). 'metadataHex' is for the share MPT (≤2048 hex chars). 'dataHex' is arbitrary blob (≤512 hex chars = 256 bytes). Set 'isPrivate=true' to require domain-credentialed access (set only at creation). Set 'sharesNonTransferable=true' to lock shares to depositors (set only at creation). 'withdrawalPolicy' (uint) selects the strategy (e.g. 1=FirstComeFirstServed); 'scale' (uint) controls share decimal precision.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Owner account of the new vault. |
| `assetCurrency` | `string` | **yes** | — | Vault asset currency code ('XRP', 3-char, or 40-hex). |
| `assetIssuer` | `string?` | no | — | Vault asset issuer (empty for XRP). |
| `amountValue` | `string` | **yes** | — | Initial deposit as a decimal string in vault-asset units. For XRP pass drops as a decimal string. |
| `assetsMaximum` | `string?` | no | `null` | Optional max total assets (STNumber decimal string). Omit for uncapped. |
| `metadataHex` | `string?` | no | `null` | Optional hex-encoded metadata for the share MPT (≤2048 hex chars = 1024 bytes). |
| `dataHex` | `string?` | no | `null` | Optional hex blob attached to the vault (≤512 hex chars = 256 bytes). |
| `domainId` | `string?` | no | `null` | Optional permissioned-domain ID (64-hex). |
| `withdrawalPolicy` | `uint?` | no | `null` | Optional withdrawal policy code (uint). Reserved values are amendment-defined. |
| `scale` | `uint?` | no | `null` | Optional share precision scale (0..18 for IOU; fixed at 0 for XRP/MPT). |
| `isPrivate` | `bool` | no | `false` | Set true to make the vault private (domain-credentialed access). Only honoured at creation. |
| `sharesNonTransferable` | `bool` | no | `false` | Set true to make the shares non-transferable between accounts. Only honoured at creation. |

### `xrpl_vault_delete_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/VaultTools.cs`</sub>

Prepares an UNSIGNED VaultDelete (XLS-65). Removes an empty vault (AssetsTotal must be 0). Only the vault owner may delete.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Vault owner account. |
| `vaultId` | `string` | **yes** | — | 64-hex VaultID of the vault to delete. |

### `xrpl_vault_deposit_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/VaultTools.cs`</sub>

Prepares an UNSIGNED VaultDeposit (XLS-65). The depositor sends the vault's asset; the vault mints share-MPTs and credits them to the depositor. For a private vault, the depositor must hold a credential from the vault's PermissionedDomain.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Depositor account. |
| `vaultId` | `string` | **yes** | — | 64-hex VaultID. |
| `assetCurrency` | `string` | **yes** | — | Vault asset currency code ('XRP', 3-char, or 40-hex). Must match the vault's asset. |
| `assetIssuer` | `string?` | no | — | Vault asset issuer (empty for XRP). |
| `amountValue` | `string` | **yes** | — | Amount to deposit (decimal string; drops for XRP). |

### `xrpl_vault_set_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/VaultTools.cs`</sub>

Prepares an UNSIGNED VaultSet (XLS-65). Modifies an existing vault's mutable fields: Data, AssetsMaximum, DomainID. At least one of those must be provided.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Vault owner account. |
| `vaultId` | `string` | **yes** | — | 64-hex VaultID of the vault to modify. |
| `dataHex` | `string?` | no | `null` | Optional new Data hex blob (≤512 hex chars). Mutually with no-op only. |
| `assetsMaximum` | `string?` | no | `null` | Optional new AssetsMaximum (STNumber decimal string). |
| `domainId` | `string?` | no | `null` | Optional new domain ID (64-hex). Empty string clears it. |

### `xrpl_vault_withdraw_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/VaultTools.cs`</sub>

Prepares an UNSIGNED VaultWithdraw (XLS-65). Pass amountKind='asset' to withdraw an exact amount of the underlying asset (rounded shares burned), or amountKind='shares' to redeem an exact share count for the equivalent asset. The destination receives the asset and must be able to receive it (no DepositAuth blocks, trust line if IOU). Withdrawals are subject to the vault's WithdrawalPolicy.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Shareholder account. |
| `vaultId` | `string` | **yes** | — | 64-hex VaultID. |
| `amountKind` | `string` | **yes** | — | 'asset' to specify an exact underlying-asset amount, or 'shares' to redeem an exact share amount. |
| `amountValue` | `string` | **yes** | — | Decimal amount (drops for XRP-asset; shares for amountKind='shares'). |
| `assetCurrency` | `string?` | no | `null` | For amountKind='asset': vault asset currency ('XRP'/3-char/40-hex). |
| `assetIssuer` | `string?` | no | `null` | For amountKind='asset': vault asset issuer (empty for XRP). |
| `shareMptIssuanceId` | `string?` | no | `null` | For amountKind='shares': 48-hex MPTokenIssuanceID of the share MPT (vault.ShareMPTID). |
| `destination` | `string?` | no | `null` | Destination account that receives the assets. Omit to receive into 'account'. |
| `destinationTag` | `uint?` | no | `null` | Optional destination tag. |

### `xrpl_xchain_account_create_commit_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/XChainTools.cs`</sub>

Prepares an UNSIGNED XChainAccountCreateCommit (XLS-38). XRP-XRP bridges only. On the source chain, locks XRP to create a NEW destination account on the destination chain. 'amountDrops' must be ≥ the bridge's MinAccountCreateAmount; 'signatureRewardDrops' must match the bridge's SignatureReward.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender on the source chain. |
| `bridgeJson` | `string` | **yes** | — | Bridge spec JSON (must be XRP-XRP). |
| `destination` | `string` | **yes** | — | Destination address on the destination chain (will be created if absent). |
| `amountDrops` | `string` | **yes** | — | Amount in XRP drops to fund the new account (≥ bridge.MinAccountCreateAmount). |
| `signatureRewardDrops` | `string` | **yes** | — | Signature reward in XRP drops (must match bridge value). |

### `xrpl_xchain_add_account_create_attestation_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/XChainTools.cs`</sub>

Prepares an UNSIGNED XChainAddAccountCreateAttestation (XLS-38). Witness-server step — attests that an XChainAccountCreateCommit happened on the source chain (XRP-XRP bridges only). 'xchainAccountCreateCount' is the bridge's sequence counter for account-creates (order of processing).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Witness account submitting the attestation. |
| `bridgeJson` | `string` | **yes** | — | Bridge spec JSON. |
| `xchainAccountCreateCount` | `string` | **yes** | — | XChainAccountCreateCount value (decimal string). |
| `amountDrops` | `string` | **yes** | — | Amount committed on the source chain (XRP drops decimal string). |
| `signatureRewardDrops` | `string` | **yes** | — | Signature reward in XRP drops (must match bridge value). |
| `otherChainSource` | `string` | **yes** | — | Address on the source chain that submitted the AccountCreateCommit. |
| `destination` | `string` | **yes** | — | Destination address on the destination chain (account to be created). |
| `attestationRewardAccount` | `string` | **yes** | — | Address that should receive this signer's share of the SignatureReward. |
| `attestationSignerAccount` | `string` | **yes** | — | Witness signer-list account on the door account. |
| `publicKeyHex` | `string` | **yes** | — | Public key (hex). |
| `signatureHex` | `string` | **yes** | — | Signature (hex). |
| `wasLockingChainSend` | `byte` | **yes** | — | 0 if the source was the issuing chain, 1 if the source was the locking chain. |

### `xrpl_xchain_add_claim_attestation_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/XChainTools.cs`</sub>

Prepares an UNSIGNED XChainAddClaimAttestation (XLS-38). Witness-server step — submits proof on the DESTINATION chain that an XChainCommit happened on the SOURCE chain. wasLockingChainSend=1 if the source was the locking chain, 0 if the source was the issuing chain.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Witness account submitting the attestation. |
| `bridgeJson` | `string` | **yes** | — | Bridge spec JSON. |
| `xchainClaimId` | `string` | **yes** | — | XChainClaimID the attestation is for. |
| `amountValue` | `string` | **yes** | — | Committed amount from the source chain (drops string or IOU JSON). |
| `attestationRewardAccount` | `string` | **yes** | — | Address that should receive this signer's share of the SignatureReward. |
| `attestationSignerAccount` | `string` | **yes** | — | Witness signer-list account on the door account. |
| `otherChainSource` | `string` | **yes** | — | Address on the SOURCE chain that submitted the XChainCommit. |
| `publicKeyHex` | `string` | **yes** | — | Public key used to verify the attestation signature (hex). |
| `signatureHex` | `string` | **yes** | — | Signature attesting to the event on the other chain (hex). |
| `wasLockingChainSend` | `byte` | **yes** | — | 0 if the source was the issuing chain, 1 if the source was the locking chain. |
| `destination` | `string` | **yes** | — | Destination of the funds on the destination chain. |

### `xrpl_xchain_claim_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/XChainTools.cs`</sub>

Prepares an UNSIGNED XChainClaim (XLS-38). On the destination chain, the claim id owner finalizes the transfer after attestations reach quorum. Used when XChainCommit did NOT include OtherChainDestination (so the recipient must claim explicitly), or to redirect to a different destination. The amount must match the attested amount.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Account on the destination chain that owns the claim id. |
| `bridgeJson` | `string` | **yes** | — | Bridge spec JSON. |
| `xchainClaimId` | `string` | **yes** | — | XChainClaimID (same value used in XChainCommit). |
| `destination` | `string` | **yes** | — | Final destination on the destination chain. |
| `amountValue` | `string` | **yes** | — | Amount to claim — must match the attested amount. |
| `destinationTag` | `uint?` | no | `null` | Optional destination tag. |

### `xrpl_xchain_commit_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/XChainTools.cs`</sub>

Prepares an UNSIGNED XChainCommit (XLS-38). On the SOURCE chain, the sender commits the cross-chain transfer amount referencing a previously-created claim ID on the destination chain. 'amountValue' is decimal drops (for XRP bridges) or a JSON IOU amount object (for IOU bridges). 'otherChainDestination' optionally specifies the destination on the destination chain — omit to require an explicit XChainClaim later.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Sender account on the source chain. |
| `bridgeJson` | `string` | **yes** | — | Bridge spec JSON. |
| `xchainClaimId` | `string` | **yes** | — | XChainClaimID — decimal string for XRP-XRP bridges, or hex Hash256 depending on bridge convention. |
| `amountValue` | `string` | **yes** | — | Amount to transfer: drops decimal string for XRP-asset, or JSON {value,currency,issuer} for IOU-asset. |
| `otherChainDestination` | `string?` | no | `null` | Optional destination address on the destination chain. Omit to require an explicit XChainClaim. |

### `xrpl_xchain_create_bridge_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/XChainTools.cs`</sub>

Prepares an UNSIGNED XChainCreateBridge (XLS-38). Submitted by the door account on each chain (mirrored on both chains). 'bridgeJson' is the bridge spec: {"LockingChainDoor":"r...","LockingChainIssue":{currency,issuer?},"IssuingChainDoor":"r...","IssuingChainIssue":{currency,issuer?}}. For XRP-XRP bridges, both Issues use {currency:'XRP'}. 'signatureRewardDrops' is the per-attestation reward in XRP drops. 'minAccountCreateDrops' (XRP drops) — if set, XChainAccountCreateCommit becomes available (XRP-XRP only); omit to disable.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Door account submitting the create (the bridge owner on this chain). |
| `bridgeJson` | `string` | **yes** | — | Bridge spec JSON {LockingChainDoor, LockingChainIssue, IssuingChainDoor, IssuingChainIssue}. |
| `signatureRewardDrops` | `string` | **yes** | — | Signature reward in XRP drops (string). |
| `minAccountCreateDrops` | `string?` | no | `null` | Optional MinAccountCreateAmount in XRP drops. Omit to leave AccountCreateCommit disabled. |

### `xrpl_xchain_create_claim_id_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/XChainTools.cs`</sub>

Prepares an UNSIGNED XChainCreateClaimID (XLS-38). The recipient on the destination chain reserves a claim ID for an upcoming cross-chain transfer. 'otherChainSource' is the address on the source chain that will submit the matching XChainCommit. 'signatureRewardDrops' must match the bridge's SignatureReward.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Recipient account on the destination chain (will own the claim id). |
| `bridgeJson` | `string` | **yes** | — | Bridge spec JSON. |
| `signatureRewardDrops` | `string` | **yes** | — | Signature reward in XRP drops (must match the on-ledger bridge value). |
| `otherChainSource` | `string` | **yes** | — | Address on the SOURCE chain that will send the matching XChainCommit. |

### `xrpl_xchain_modify_bridge_prepare`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/XChainTools.cs`</sub>

Prepares an UNSIGNED XChainModifyBridge (XLS-38). Modifies signatureReward and/or minAccountCreateAmount of an existing bridge. Pass clearMinAccountCreate=true to set tfClearAccountCreateAmount (removes the AccountCreate parameter).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Door account submitting the modification. |
| `bridgeJson` | `string` | **yes** | — | Bridge spec JSON identifying the bridge to modify. |
| `signatureRewardDrops` | `string?` | no | `null` | Optional new signature reward in XRP drops. |
| `minAccountCreateDrops` | `string?` | no | `null` | Optional new MinAccountCreateAmount in XRP drops. |
| `clearMinAccountCreate` | `bool` | no | `false` | If true, sets tfClearAccountCreateAmount — removes the existing MinAccountCreateAmount. Mutually exclusive with minAccountCreateDrops. |

### `xrpl_xrp_balance`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Core/Tools/AccountTools.cs`</sub>

Convenience: returns the spendable XRP balance for an account, as a decimal XRP string.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `network` | `string` | **yes** | — |  |
| `account` | `string` | **yes** | — | Classic XRP address. |

## `xrpl-signer`

Offline signer — wallet management and transaction signing (encrypted keystore).

**15 tools.**

### `xrpl_sign`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/SignTools.cs`</sub>

Signs a transaction with the named wallet's private key. The transaction can be supplied as a JSON object or as an unsigned hex blob (the same value xrpl_payment_prepare returns as 'txBlobUnsigned'). For HD (mnemonic-kind) wallets, pass 'index' to select which derived account to sign with (default 0). Returns the signed hex blob and its hash — ready to feed to xrpl_tx_submit_signed.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias from the keystore. |
| `transaction` | `string` | **yes** | — | Transaction to sign. Either a JSON object with 'TransactionType', 'Account', etc., OR a hex blob string from a *_prepare tool. |
| `index` | `int` | no | `0` | HD account index (0..2^31-1) for mnemonic-kind wallets. Ignored for seed-kind wallets. Default 0. |

### `xrpl_sign_combine`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/SignTools.cs`</sub>

Aggregates several multi-signed partial-blobs into one fully-signed transaction blob. Pass the array of hex blobs produced by xrpl_sign_multi from each authorized signer. Returns the combined signed blob and its hash — ready for xrpl_tx_submit_signed.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `signedBlobs` | `string` | **yes** | — | Array of multi-signed transaction blobs (hex strings) — output of xrpl_sign_multi from each signer. Accept as JSON array string '["blob1","blob2",...]' or as newline-separated blobs. |

### `xrpl_sign_multi`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/SignTools.cs`</sub>

Produces a single-slot multi-sign for a transaction. Each authorized signer runs this once; the resulting partial-signed transactions are then aggregated with xrpl_sign_combine before submission. Returns the signed hex blob containing this signer's Signers entry.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias from the keystore — used as one of the multi-sign authorized accounts. |
| `transaction` | `string` | **yes** | — | Transaction to sign — JSON object or hex blob. The transaction's SigningPubKey must be empty string for multi-sign. |
| `signingFor` | `string?` | no | `null` | Optional: the account this signature is FOR (defaults to the wallet's own classic address). Use this only if the wallet is a Regular Key signing on behalf of a different master account. |
| `index` | `int` | no | `0` | HD account index for mnemonic-kind wallets. Ignored for seed-kind. Default 0. |

### `xrpl_wallet_address`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Returns metadata (kind, address, public key, algorithm, derivationPathTemplate for HD, creation time) for one wallet by name. For HD wallets the address shown is the preview at index 0 — use xrpl_wallet_derive_address for other indices. No secrets are returned.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias. |

### `xrpl_wallet_derive_address`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Derives an XRPL address at the given index for an HD (mnemonic-kind) wallet. Decrypts the mnemonic, derives via BIP-44 template, returns address + public key. No seed material in the response. Throws for seed-kind wallets.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias. |
| `index` | `int` | no | `0` | Account index (0..2^31-1). Default 0. |

### `xrpl_wallet_export`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Returns the plaintext secret for backup. For seed-kind: the XRPL family seed. For HD (mnemonic-kind): the BIP-39 mnemonic + optional BIP-39 passphrase + derivation path template + algorithm. The secret will appear in the MCP transcript — handle the chat carefully. Requires confirm=true to guard against accidental invocation.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias. |
| `confirm` | `bool` | no | `false` | Must be set to true to actually return the secret — a safety interlock. |

### `xrpl_wallet_export_index`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Derives an XRPL family seed for a specific index of an HD (mnemonic-kind) wallet. Useful for backing up a derived account independently, or importing it into other XRPL tools that only accept a seed. Requires confirm=true. Throws for seed-kind wallets.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias. |
| `index` | `int` | **yes** | — | Account index to derive. |
| `confirm` | `bool` | no | `false` | Must be set to true to actually return the seed. |

### `xrpl_wallet_generate`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Generates a brand-new single-address XRPL wallet with random entropy, encrypts the seed with the keystore passphrase, and stores it under the given name. For an HD (multi-address) wallet generate a mnemonic instead — xrpl_wallet_generate_mnemonic.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias used in subsequent sign calls. Allowed chars: letters, digits, '-', '_', '.'. |
| `algorithm` | `string` | no | `"ed25519"` | Signing curve: 'ed25519' (default) or 'secp256k1'. |

### `xrpl_wallet_generate_mnemonic`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Generates a random BIP-39 mnemonic (12/15/18/21/24 words) and stores it encrypted as an HD wallet. Returns the mnemonic in plaintext ONCE — write it down for backup, it will not be shown again unless you call xrpl_wallet_export. Sets up XRPL standard BIP-44 path m/44'/144'/{i}'/0/0; address at index 0 is returned as preview.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias. |
| `wordCount` | `int` | no | `12` | Mnemonic length in words: 12 (default, 128-bit entropy), 15, 18, 21 or 24 (256-bit). |
| `bip39Passphrase` | `string?` | no | `null` | Optional BIP-39 passphrase that augments the mnemonic. Stored encrypted separately. Empty by default. |
| `algorithm` | `string` | no | `"secp256k1"` | Signing curve for derived accounts: 'secp256k1' (default, XRPL BIP-44 convention) or 'ed25519'. |

### `xrpl_wallet_import_mnemonic`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Imports a BIP-39 mnemonic (12/15/18/21/24 words) as an HD wallet — the mnemonic itself is stored encrypted, and any number of XRPL addresses can be derived later via xrpl_wallet_derive_address. Set storeAsHd=false to use the legacy behaviour where only the seed for a single derivation path is stored (mnemonic discarded after import).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias. |
| `mnemonic` | `string` | **yes** | — | BIP-39 mnemonic — words separated by single spaces. |
| `derivationPathTemplate` | `string?` | no | `null` | Optional BIP-44 derivation path TEMPLATE with '{i}' placeholder for index. Default: 'm/44'/144'/{i}'/0/0' (XRPL standard). |
| `bip39Passphrase` | `string?` | no | `null` | Optional BIP-39 passphrase that augments the mnemonic. Stored encrypted separately. Empty by default. |
| `algorithm` | `string` | no | `"secp256k1"` | Signing curve: 'secp256k1' (default, XRPL BIP-44 convention) or 'ed25519'. |
| `storeAsHd` | `bool` | no | `true` | If true (default), store the mnemonic encrypted as an HD wallet — multi-address. If false, derive ONLY index 0's seed and store that as a seed-kind wallet (mnemonic discarded). |

### `xrpl_wallet_import_seed`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Imports an existing XRPL seed string (e.g. 'sEd...' for Ed25519, 'sn...' for secp256k1). Encrypted and stored as a single-address (seed-kind) wallet.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias. |
| `seed` | `string` | **yes** | — | XRPL seed — typically starts with 'sEd' or 'sn'. |
| `algorithm` | `string?` | no | `null` | Optional algorithm override. If omitted the SDK infers it from the seed prefix. |

### `xrpl_wallet_import_text`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Imports a wallet derived from an arbitrary text passphrase. The text is normalized and run through SHA-256 or PBKDF2 to produce 16 bytes of entropy. Useful for brain-wallets or human-memorable seeds. WARNING: short or low-entropy text is trivially brute-forceable — only use long, unique phrases. Stored as single-address (seed-kind).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias. |
| `text` | `string` | **yes** | — | The text — any length. Longer & more unique = better. |
| `salt` | `string?` | no | `null` | Optional salt mixed into the KDF. Increases isolation between different wallets derived from similar text. |
| `caseInsensitive` | `bool` | no | `true` | If true the text is lowercased before hashing — easier on humans, slightly weaker against typos vs intentional variants. |
| `kdf` | `string` | no | `"sha256"` | KDF: 'sha256' (default, fast) or 'pbkdf2' (slow, 100K iter, much harder to brute-force). |
| `algorithm` | `string?` | no | `null` | Optional algorithm override: 'ed25519' or 'secp256k1'. Default: secp256k1. |

### `xrpl_wallet_import_xumm`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Imports a wallet from Xumm 'secret numbers' format (8 groups of 6 digits, each including a checksum). Accepts either an array of 8 strings or one space-separated string like '554872 394230 ... ...'. Stored as single-address (seed-kind).

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias. |
| `secretNumbers` | `string` | **yes** | — | Either eight 6-digit strings (newline / space separated) or a JSON-style array. Whichever is convenient — the tool normalizes it. |
| `algorithm` | `string` | no | `"secp256k1"` | Optional algorithm override. Default: 'secp256k1' (Xumm's native curve). |

### `xrpl_wallet_list`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Lists all wallets in the keystore — name, kind (seed | mnemonic), address (preview for HD), public key, algorithm, derivationPathTemplate (HD only), creation timestamp. No secrets are returned.

_No parameters._

### `xrpl_wallet_remove`

<sub>Source: `src/StaticBit.Xrpl.Mcp.Signer/Tools/WalletTools.cs`</sub>

Permanently deletes a wallet from the keystore. The encrypted ciphertext is overwritten on disk via atomic rewrite — but if you have backups elsewhere they are not affected.

| Param | Type | Required | Default | Description |
|---|---|---|---|---|
| `name` | `string` | **yes** | — | Wallet alias. |

