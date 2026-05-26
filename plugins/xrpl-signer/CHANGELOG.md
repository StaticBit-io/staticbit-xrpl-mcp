## v0.2.0 — 2026-05-26

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
- chore: import StaticBitXrplMcp + xrpl-{cloud,local,signer} plugins (c631919)

# xrpl-signer changelog

All notable changes to this plugin are listed here. Newest at the top.

## v0.1.1 — 2026-05-25

### Source-repo changes (StaticBitXrplMcp)
- build(release): add release-plugin.sh orchestrator + RELEASE.md guide (1de9501)
- build: enable single-file compression for all RIDs (edd60de)
- feat: offline stdio signer + cross-platform binaries build (28fd54d)
- fix(alerts): drop scanner noise — 404 without alert for non-/mcp paths (45e267c)
- feat(alerts): admin Telegram alerts mirror of TelegramMCP (cf9b8e9)
- docs(deploy): add explicit ENV var setup for bearer + project-scope .mcp.json (752e29b)
- chore: include .env.xrpl-mcp.example in repo (whitelist in .gitignore) (44163d5)
- feat: bearer auth, rate limit, Traefik-platform compose (7dda029)
- feat: dockerize server with multi-arch ghcr publishing (16d5a08)
- docs: update README with release publish workflow and tool catalog (f32a911)
- feat: add prepare/submit two-phase write flow with 9 new tools (a0af00e)
- feat: add 11 read-only XRPL MCP tools (f170098)
- chore: initial commit (4329a82)

### Marketplace-repo changes (manifests, skills, docs)
- refactor: split xrpl plugin into three modular plugins (3d46e16)

