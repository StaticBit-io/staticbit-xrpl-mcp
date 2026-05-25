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

