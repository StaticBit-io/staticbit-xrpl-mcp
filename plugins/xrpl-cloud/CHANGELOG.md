## v0.4.0 — 2026-06-22

### Features
- signing ceremony, scoped auto-sign, memo/tainted-destination guard, submit discipline (65b3659)
- stamp default SourceTag 100010011 + full-disclosure transaction preview (78c470b)

### Documentation
- prepare repository for public launch (LICENSE, CONTRIBUTING, CoC, public install, cloud repositioning) (2746bf9)

### Tests
- configurable integration endpoint/account + SourceTag/preview smoke assertions (5254373)

### Other
- release: xrpl-local v0.4.0 (3451440)
- release: xrpl-signer v0.4.0 (7627ef3)

## v0.3.3 — 2026-06-17

### Fixes
- make XRPL tool errors transparent via central classifier (9d69bb9)

### Other
- release: xrpl-local v0.3.8 (913f6ae)

## v0.3.2 — 2026-06-03

### Features
- The server now serves `/favicon.ico` so MCP connector clients show an icon.

## v0.3.1 — 2026-05-31

### Features
- apply UntrustedContent.Wrap to external-content tools + SECURITY.md + canary tests (Phase 4.4 Stage C) (#17) (5e9a202)
- report build version on /healthz and /readyz (2b88f23)
- structured error envelopes for all prepare-tools (f5058b7)

### Fixes
- point deploy.sh APP_DIR at the real host path /opt/staticbit-xrpl-mcp (182bb85)
- silence System.Net.Http.HttpClient (keeps admin-bot token out of logs) (702ecef)

### Documentation
- add Payment workflows end-to-end example (#16) (f70d10f)
- add 'What the tools cover' overview for 116-tool surface (#12) (1b7a51c)
- fix all 12 findings from cross-repo audit (2026-05-30) (77361c6)
- rewrite xrpl-cloud / xrpl-local SKILL.md bodies for the 116-tool surface (be88fe4)
- wire SKILL.md tool-count under mcp-toolsdoc markerFiles (25b66f6)
- full Russian translation of docs/DEPLOY.md (was a summary stub) (98b4b2d)
- standardize base docs under docs/ (DEPLOY/INSTALL) + add OPERATIONS (e09b5b6)
- document the live image-based CI/CD deploy (0d8f70e)
- document shared CI/CD reusable workflows in CLAUDE.md (6084274)
- add CLAUDE.md (doc conventions + tool-gen); ignore in i18n gate (2a4427a)
- xrpl-cloud is OAuth 2.1-authed, not static bearer (e4a1a95)

### Tests
- regression guard for cloud-server config pipeline (#15) (adc8f1f)

### Build / CI
- wire downstream SKILL.md trigger validation (backlog 1) (#18) (d008348)
- wire Mcp.FleetLint cross-repo consistency gate (Sprint 2) (#14) (4fda249)
- wire Mcp.LinkCheck markdown link integrity gate (#13) (59e6a7f)
- prune old GHCR versions after build (keep 5) (efa88b1)
- bump actions/checkout to v5 for Node.js 24 (81952a9)
- add reusable VPS deploy (deploy.yml + forced-command deploy.sh) (f3f1f3c)
- build cloud image inside the xrpl-cloud release (4550737)
- convert docker.yml to shared reusable build-push caller (36fce13)
- switch docs-i18n gate to shared Mcp.I18nCheck tool (c158120)
- wire shared Mcp.ToolsDoc — config + manifest + docs-codegen gate + TOOLS.generated.md (131 tools: xrpl + xrpl-signer) (c23377c)
- unify layout (docs/*.ru.md→docs/ru/) + bilingual plugin READMEs + unified gate (e598eca)

### Other
- deps: bump Mcp.Auth.ResourceServer 0.2.0 → 0.3.0 + Mcp.FleetLint 0.1.0 → 0.2.0 (#19) (4b972d9)
- chore(tools): bump mcp.toolsdoc 0.1.0 -> 0.1.1 (Windows EOL fix) (#10) (20f65af)
- release: xrpl-local v0.3.6 (0ecb6a5)

## v0.3.0 — 2026-05-28

### Changed
- **OAuth 2.1 auth** — the cloud server (`xrpl.mcp.staticbit.ai`) now authenticates via OAuth against `auth.mcp.staticbit.ai` instead of a static `XRPL_MCP_BEARER`. The plugin `.mcp.json` uses an `oauth` block (dynamic client registration); run `/mcp` once to log in. **Breaking**: `XRPL_MCP_BEARER` is gone; only allow-listed accounts can log in. Plugin README updated (INSTALL.md/DEPLOY.md OAuth rewrite pending).

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
