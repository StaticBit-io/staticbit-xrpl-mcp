# Contributing to staticbit-xrpl-mcp

Thanks for your interest in improving the XRPL toolkit for Claude Code.

## Ground rules

- **Security first.** This project prepares, signs and submits real XRP Ledger transactions. Never weaken the architectural invariant that signing happens **only** in the offline `xrpl-signer` keystore, client-side — the read/prepare/submit side never sees a seed or private key.
- **Found a vulnerability?** Do **not** open a public issue — follow [SECURITY.md](SECURITY.md).

## Development

Requires the **.NET 10 SDK** and **Node 18+**.

```bash
dotnet restore
dotnet build
dotnet test --filter TestU          # fast unit tests
```

Integration smoke tests hit a live XRPL node (public testnet by default) and run separately:

```bash
dotnet test --filter "TestCategory=Integration"
# point at your own node:  XRPL_TESTNET_WS=ws://localhost:6006 XRPL_TESTNET_ACCOUNT=<funded-account>
```

## Required gates

A PR must keep all of these green (each runs in CI; run them locally first):

| Gate | Run locally |
|---|---|
| Unit tests | `dotnet test --filter TestU` |
| Prompt-injection guard | `dotnet tool restore && dotnet tool run mcp-injectionguard --check` |
| Tool-doc sync (after changing a `[McpServerTool]`) | `dotnet run --project tools/SchemaGen -- docs/tools-schema.json` |
| Bilingual docs | every `*.md` outside `docs/` and every `docs/<f>.md` needs its `*.ru.md` / `docs/ru/<f>.md` mirror — see [docs/bilingual-convention.md](docs/bilingual-convention.md) |

Any new `[McpServerTool]` that returns content sourced from the XRP Ledger **must** wrap its response via `UntrustedContent.Wrap(content, origin)` (see [SECURITY.md](SECURITY.md)) — the injection guard fails CI otherwise.

## Commits & pull requests

- Use [Conventional Commits](https://www.conventionalcommits.org) (`feat:`, `fix:`, `docs:`, `chore:`, …) — release changelogs are generated from commit subjects.
- Keep changes focused, and update EN **and** RU docs in the same PR.
- Open the PR against `main`. CI runs the unit tests, the injection guard, `Mcp.FleetLint` and the docs gates.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
