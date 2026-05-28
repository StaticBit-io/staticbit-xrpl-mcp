## v0.3.0 — 2026-05-28

### Changed
- **OAuth 2.1 auth** — the cloud server (`xrpl-mcp.staticbit.io`) now authenticates via OAuth against `auth.mcp.staticbit.io` instead of a static `XRPL_MCP_BEARER`. The plugin `.mcp.json` uses an `oauth` block (dynamic client registration); run `/mcp` once to log in. **Breaking**: `XRPL_MCP_BEARER` is gone; only allow-listed accounts can log in. Plugin README updated (INSTALL.md/DEPLOY.md OAuth rewrite pending).

## v0.2.5 — 2026-05-27

### Features
- structured error envelopes via XrplErrorClassifier + McpException (97692e8)

## v0.2.4 — 2026-05-27

### Fixes
- graceful errors on invalid XRPL addresses (d682b63)

## v0.2.3 — 2026-05-27

### Build / CI
- bump actions/attest-build-provenance from 2 to 4 (#6) (d34494e)
- bump docker/login-action from 3 to 4 (#5) (3fa0098)
- bump actions/setup-dotnet from 4 to 5 (#4) (aa67ba7)
- bump docker/build-push-action from 6 to 7 (#3) (572ed84)

### Other
- deps: Bump the dotnet-minor-patch group with 2 updates (#7) (cc08c0f)

## v0.2.2 — 2026-05-27

### Fixes
- production polish — stale VPS paths, hardcoded IP, script output (87593ab)

## v0.2.1 — 2026-05-27

### Documentation
- post-release fixes — plugin update syntax, stale marketplace name (4e1b768)

## v0.2.0 — 2026-05-27

### Features
- payment credentialIdsJson + xrpl_hash_credential (62bc29c)
- add 50 prepare/read tools for 12 XRPL amendments (2ee21e2)

### Documentation
- bilingual EN/RU convention + 12 Cowork recipes + schema regen (0f25e5d)

### Tests
- unit + integration smoke for 50 new MCP tools (603aa77)

### Build / CI
- mark CodeQL upload continue-on-error (GHAS unavailable on free private) (4a13cc9)
- Dependabot + CodeQL + branch-protection docs (c530621)

# xrpl-cloud changelog

All notable changes to this plugin are listed here. Newest at the top.

## v0.1.1 — 2026-05-27

### Features
- audit log — JSONL append-only file backend (b153002)
- HD wallets (BIP-44, encrypted mnemonic) (f22a66b)
- §5 OpenTelemetry metrics + Prometheus + pool TTL (4ef56bb)
- close §1/§2 tail tools — manifest, escrows, signer status, XLS-70, AccountDelete preflight (59a9d91)
- rate-limit per token, CORS, request logging + Server.Tests (+57 tests) (8b76ea0)
- add tx_explain / tx_preflight / tx_simulate + fee escalation (9636ea3)
- add Path / Gateway / ServerState / Subscribe tools (1c0e7bc)
- add typed prepare wrappers for Account/NFT/Escrow/Check/PayChan/AMM/Issuer (835a544)

### Refactoring
- §6 code quality cleanup (eba642e)

### Documentation
- rewrite features.md from roadmap to feature catalogue (2a3a67f)
- §8 glossary, per-OS troubleshooting, Cowork example (7308bea)
- §7 supply chain — features.md tick + supply-chain.md guide (381f3b4)
- mark §5 server-infra (5 of 8) as complete (dc0aeda)
- mark §3 UX prepare-flow as complete (e1c0d42)
- update features §2 with implementation status (0766ef8)
- add features.md roadmap (e967590)
- switch paths to /opt/staticbit-xrpl-mcp + new repo URL (9487f33)

### Tests
- new integration-tests project + daily CI workflow (8af9b2e)
- cover explain/decode/preflight/options (+39 unit tests) (fd4fd53)
- cover Path / Gateway / Subscription tools (+26 unit tests) (afb37a6)
- cover typed prepare wrappers (+57 unit tests) (cf18fc8)

### Build / CI
- SchemaGen + docs/tools-schema.json (74 tools) (0db37f0)
- §7 supply-chain pipeline — SBOM, SLSA, signing, deterministic (d7700ef)

### Build / CI
- mark SLSA attestation step continue-on-error (free-plan limit) (fdfb529)
- mark shell scripts executable in git (mode 755) (8345e55)
- normalize line endings — fix release-plugin pre-flight on Linux (5224168)

### Other
- release: xrpl-local v0.2.0 (794053b)
- release: xrpl-signer v0.2.0 (d4bec4c)
- chore: import StaticBitXrplMcp + xrpl-{cloud,local,signer} plugins (c631919)
