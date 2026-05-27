> 🇷🇺 [Прочесть на русском](README.ru.md)

# Cowork-agent recipes for StaticBit XRPL MCP

Each recipe is a ready-made workflow for a Cowork agent covering one use-case through a combination of MCP tools from the `xrpl-cloud` / `xrpl-local` / `xrpl-signer` plugins. All sequences are cross-checked against integration tests in the upstream [XrplCSharp](https://github.com/StaticBit-io/XrplCSharp/tree/release/Tests/Xrpl.Tests/Integration/transactions) project — every step matches a real on-chain sequence verified by testnet/devnet runs.

## Catalogue

| File | Use-case | Amendment(s) | Status on mainnet/testnet |
|---|---|---|---|
| [monitor-balance-telegram.md](monitor-balance-telegram.md) | Polling watcher for incoming tx + Telegram notifications | base read API + polling | ✅ works on standard testnet |
| [controlled-mpt-issuance.md](controlled-mpt-issuance.md) | MPT lifecycle with RequireAuth + CanClawback (regulated issuance) | XLS-33 | ✅ activated |
| [atomic-batch-payment.md](atomic-batch-payment.md) | Atomic 3-way swap via Batch | XLS-56 | ⚠️ pending BatchV1_1 (removed in v3.1.1) |
| [kyc-credentials-flow.md](kyc-credentials-flow.md) | End-to-end KYC via XLS-70 Credentials + DepositPreauth | XLS-70 | ✅ activated, drop-in |
| [permissioned-domain-and-did.md](permissioned-domain-and-did.md) | DID identity + PermissionedDomain gating | XLS-40 + XLS-80 | ✅ activated |
| [oracle-price-feed.md](oracle-price-feed.md) | Periodic off-chain price publisher (CoinGecko → OracleSet) | XLS-47 | ✅ activated |
| [tickets-parallel-submit.md](tickets-parallel-submit.md) | Tickets for parallel tx submission without head-of-line blocking | TicketCreate (long-activated) | ✅ |
| [nft-mutable-uri.md](nft-mutable-uri.md) | Dynamic NFT with mutable URI (gaming, badges, RWA) | XLS-46 | ✅ activated |
| [multi-sign-collection.md](multi-sign-collection.md) | Multi-sign signature collection workflow (treasury, DAO) | base SignerListSet (XRPL core) | ✅ |
| [amm-clawback.md](amm-clawback.md) | Issuer reclaims tokens from an AMM pool (compliance) | XLS-37 | ✅ activated |
| [delegate-permissions.md](delegate-permissions.md) | Per-tx-type delegation (bot acts on owner's behalf) | XLS-75 | ✅ activated |
| [vault-deposit-redeem.md](vault-deposit-redeem.md) | Single-asset vault lifecycle (deposit/withdraw/clawback) | XLS-65 | ⚠️ draft, `temDISABLED` on standard testnet |

## Recipe structure

All recipes follow a unified structure:

1. **Use-case** — the business task addressed.
2. **What is used** — plugin ↔ tool mapping table.
3. **Concept / Architecture** — flow diagram + key ledger entries.
4. **Pre-requisites** — what needs to be ready.
5. **Agent prompt** — ready-made `~/.claude/agents/<name>.md` markdown file.
6. **Step-by-step** — sequence of tool calls with examples.
7. **Verification checklist** — how to confirm success.
8. **Gotchas** — known pitfalls.
9. **Use-cases** — where this applies in production.
10. **Extensions** — directions for further development.

## Closed feature gaps

Discovered while writing the recipes and **closed in the current version**:

- ✅ **`feature-gap-001`** — `xrpl_payment_prepare` accepts an optional `credentialIdsJson` (array of 1..8 64-hex Hash256 strings), normalises to uppercase, validates dedup + hex format.
- ✅ **`feature-gap-002`** — `xrpl_hash_credential(subject, issuer, credentialTypeHex|credentialTypePlain)` tool. Pure-local SHA-512/2 via the SDK helper `Xrpl.Utils.Hashes.HashCredential` — no network call, used offline to build `Payment.CredentialIDs`.

## Mapping to XrplCSharp integration tests

Every recipe is cross-checked against an integration test in the upstream repo:

| Recipe | Matching integration test |
|---|---|
| controlled-mpt-issuance | `TestIMPTokenCreate`, `TestIMPTokenAuthorize`, `TestIMPTokenManage` |
| atomic-batch-payment | `TestIBatch` (marked `[Ignore]` after v3.1.1) |
| kyc-credentials-flow | `TestICredential.TestCredential_EndToEnd_DepositPreauthAndPayment` |
| permissioned-domain-and-did | `TestIPermissionedDomain`, `TestIDID` |
| oracle-price-feed | `TestIOracle.TestOracleSet_CreateOracle_*` |
| tickets-parallel-submit | `ticketCreate.cs` (lowercase in the SDK test set) |
| nft-mutable-uri | (NFTokenModify covered through the NFT base set) |
| multi-sign-collection | `TestIMultisign` (Payment_SubmitMulti / ManualSigning / CombineBlobs) |
| amm-clawback | `TestIAMMClawback` |
| delegate-permissions | `TestIDelegateSet` |
| vault-deposit-redeem | `TestIVault`, `TestIVaultBase` |

## How to use the recipes

### Option 1 — single manual run

1. Install the required plugins from the relevant `plugin.json` manifest.
2. Copy the "Agent prompt" block into `~/.claude/agents/<name>.md` or `.claude/agents/<name>.md` of your project.
3. Run the agent via `claude agent <name>` or invocation from a main claude session.

### Option 2 — embed in an existing workflow

The agent prompt is a ready, modular component. Use it as a sub-agent through the `Agent` tool from a top-level orchestrator:

```markdown
Agent({
  description: "Run KYC credentials flow for Alice and Merchant",
  subagent_type: "kyc-credentials",
  prompt: "{step:'create', issuer:'rIssuer...', subject:'rAlice...', credentialTypePlain:'KYC-Tier1'}"
})
```

### Option 3 — recurring runs

Combine with `/loop` or `/schedule`:

```bash
/schedule "every 10 minutes" oracle-feed --params '{"oracleOwner":"...","oracleDocumentId":42,...}'
```

## Contributing

If you find a bug in a recipe or want to add a new use-case:

1. Check the upstream integration test in [XrplCSharp/Tests/Xrpl.Tests/Integration/transactions/](https://github.com/StaticBit-io/XrplCSharp/tree/release/Tests/Xrpl.Tests/Integration/transactions).
2. If the sequence works in the integration test — port it into a recipe.
3. Run our smoke tests against testnet: `dotnet test --filter "TestCategory=Integration"`.
4. Submit a PR with the new `.md` file (+ `.ru.md` sibling) and an updated entry in this index.
