> 🇷🇺 [Прочесть на русском](README.ru.md)

# staticbit-xrpl-mcp

XRPL toolkit for Claude Code — **source + three plugins in one repo**. Contains:

- **C# / .NET 10 server** for the XRP Ledger (cloud deploy + local stdio in one binary).
- **Offline stdio signer** with an encrypted keystore (PBKDF2 + AES-256-GCM).
- **Three plugins** for Claude Code shaped as a marketplace inside the same repo.

> 📖 **[INSTALL.md](INSTALL.md)** — step-by-step instructions for the end user of the plugins: from a fresh Claude Code install to the first signed XRPL transaction.
> 📖 **[DEPLOY.md](DEPLOY.md)** — for an admin deploying the cloud server on a VPS.
> 📖 **[RELEASE.md](RELEASE.md)** — for me (the release manager) — how to publish new plugin versions.
> 📖 **[docs/features.md](docs/features.md)** — full feature catalogue (131 tools, 432 unit tests, 12 covered amendments).
> 📖 **[docs/glossary.md](docs/glossary.md)** — XRPL terminology used in tool descriptions.
> 📖 **[docs/supply-chain.md](docs/supply-chain.md)** — what ships with every release (SBOM, SLSA, optional notarization) and how to verify it.
> 📖 **[docs/tools-schema.json](docs/tools-schema.json)** — machine-readable JSON-Schema catalogue of all MCP tools (131), for third-party agents.
> 📖 **[docs/examples/](docs/examples/)** — recipes for cross-plugin Cowork agents.
> 📖 **[docs/bilingual-convention.md](docs/bilingual-convention.md)** — bilingual documentation convention (`docs/ru/` mirror subtree + `.ru.md` suffix outside `docs/`).

## Marketplace plugins

| Plugin | What it does | Size |
|---|---|---|
| [`xrpl-cloud`](plugins/xrpl-cloud/) | HTTP MCP to the StaticBit cloud server at `xrpl-mcp.staticbit.io`. Read / prepare / submit over OAuth 2.1-authed HTTPS. | ~10 KB |
| [`xrpl-local`](plugins/xrpl-local/) | Local stdio MCP — same tools, but entirely on your machine; WebSocket directly to public XRPL nodes. | ~260 MB (5 RIDs) |
| [`xrpl-signer`](plugins/xrpl-signer/) | Offline stdio MCP for wallet management and signing — encrypted keystore, zero network code. Pairs with cloud or local. | ~200 MB (5 RIDs) |

## How they compose

```
┌────────────────────────────────────────────────────────┐
│ Cloud + Signer                                         │
│                                                        │
│   xrpl-cloud (HTTP) ─┐                                 │
│                      ├─ prepare → user-confirm         │
│   xrpl-signer (stdio)┘                                 │
│                      ├─ sign locally                   │
│   xrpl-cloud (HTTP) ─┘                                 │
│                      └─ submit_signed                  │
└────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────┐
│ Local + Signer (fully offline-ish, no cloud middleman) │
│                                                        │
│   xrpl-local (stdio) ─┐                                │
│                       ├─ prepare → user-confirm        │
│   xrpl-signer (stdio) ┘                                │
│                       ├─ sign locally                  │
│   xrpl-local (stdio) ─┘                                │
│                       └─ submit_signed                 │
└────────────────────────────────────────────────────────┘
```

The signer is identical in both flows — it's pure cryptography, knows nothing about the prepare/submit side.

## Install (for plugin users)

```
/plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
```

The marketplace is private; Claude Code will prompt for a GitHub PAT (read access) on first `marketplace add`.

Then — pick which plugins to install:

```
# Cloud + signer (lightweight, depends on the StaticBit VPS):
/plugin install xrpl-cloud@staticbit-xrpl-mcp
/plugin install xrpl-signer@staticbit-xrpl-mcp

# Local + signer (offline-first, no cloud dependency):
/plugin install xrpl-local@staticbit-xrpl-mcp
/plugin install xrpl-signer@staticbit-xrpl-mcp

# Read-only via cloud (no signing — Cowork agents, dashboards):
/plugin install xrpl-cloud@staticbit-xrpl-mcp
```

Full instructions with every ENV variable — in [INSTALL.md](INSTALL.md).

## Repo layout

```
staticbit-xrpl-mcp/
├── .claude-plugin/marketplace.json        ← plugin registry
├── .github/workflows/                     ← CI: docker, dotnet-test, codeql, release-plugin, integration-tests
├── .github/dependabot.yml                 ← weekly NuGet + actions updates
├── plugins/
│   ├── xrpl-cloud/      (manifest + skill, no binaries)
│   ├── xrpl-local/      (+ bin/<rid>/ for 5 RIDs)
│   └── xrpl-signer/     (+ bin/<rid>/ for 5 RIDs)
├── src/
│   ├── StaticBit.Xrpl.Mcp.Abstractions/   ← shared models
│   ├── StaticBit.Xrpl.Mcp.Core/           ← 131 read/prepare/submit tools
│   ├── StaticBit.Xrpl.Mcp.Server/         ← HTTP+stdio host
│   └── StaticBit.Xrpl.Mcp.Signer/         ← offline signer
├── tests/                                  ← Core + Server + Signer unit tests + Integration smoke
├── docs/                                   ← features.md, glossary, examples, supply-chain, branch-protection
├── Dockerfile, docker-compose.yml          ← cloud deploy
├── build-server-binaries.sh                ← publish server for 5 RIDs
├── build-signer-binaries.sh                ← publish signer for 5 RIDs
├── release-plugin.sh                       ← release manager script
├── INSTALL.md, DEPLOY.md, RELEASE.md
└── StaticBitXrplMcp.sln, Directory.Build.props
```

## Development

```bash
# Build + tests
dotnet restore
dotnet build
dotnet test --filter TestU

# Local cloud-server in stdio mode for debugging
dotnet run --project src/StaticBit.Xrpl.Mcp.Server -- --transport stdio

# Local HTTP server
dotnet run --project src/StaticBit.Xrpl.Mcp.Server -- --transport http --urls http://localhost:5500

# Self-contained publish for one plugin (or all)
bash build-signer-binaries.sh           # 5 RIDs
bash build-server-binaries.sh win-x64   # single platform

# Plugin release (see RELEASE.md)
./release-plugin.sh xrpl-signer patch --push

# Regenerate docs/tools-schema.json (after adding/changing a [McpServerTool])
dotnet run --project tools/SchemaGen -- docs/tools-schema.json
```

### Test convention

Suffix `U` = **Unit** (test). Applied in two places:

- **Files** — `*TestsU.cs` (e.g. `CurrencyParserTestsU.cs`).
- **Test method names** — `TestU_` prefix (e.g. `TestU_Parse_Empty_Throws`).

This lets you run only the fast unit tests with a single command:

```bash
dotnet test --filter TestU
```

Integration tests live as `*TestsI.cs` / `TestI_*` (in `tests/StaticBit.Xrpl.Mcp.Integration.Tests/`) and run separately (daily cron in CI, not on every PR):

```bash
dotnet test --filter "TestCategory=Integration"
```

`Directory.Build.props` sets `TargetFramework=net10.0` by default; the sole exception is `StaticBit.Xrpl.Mcp.Abstractions`, which pins to `netstandard2.1` so it can embed into older hosts (Staticbit Wallet, etc).

## History

This repo merges what previously lived in two private repos:
- `StaticBit-io/StaticBitXrplMcp` (server source, signer source, tests, Docker, cloud deploy)
- `StaticBit-io/staticbit-plugins` (marketplace, plugin manifests, binaries)

Since the migration, code and marketplace live atomically: a single PR can touch both `src/` and `plugins/<name>/bin/`. The CI auto-release runs in one workflow. The old repos are archived for history.
