# CLAUDE.md — repo conventions for AI agents

XRPL MCP. Three plugins: **xrpl-cloud** (cloud HTTP MCP, OAuth 2.1), **xrpl-local** (local
stdio MCP), **xrpl-signer** (offline stdio signer, encrypted keystore). Server is
`src/StaticBit.Xrpl.Mcp.Server`. XRPL coding rules: see the user/global CLAUDE.md (read SDK
sources from `E:\GIT\XRPL\XrplCSharp`, never decompile NuGet).

## Documentation — bilingual, enforced by CI

**English is canonical; every doc has a Russian counterpart.**

- Repo root, `plugins/*`, `examples/*`, `servers/*`, `infra/`: `X.md` (EN) + `X.ru.md` (RU).
- Under `docs/`: `docs/<path>.md` (EN) + `docs/ru/<path>.md` (RU).

When you add or change a doc, **update BOTH languages** — same content; translate only prose,
keep code/commands/paths/links byte-identical. The gate `mcp-i18ncheck` (shared `Mcp.I18nCheck` tool)
(workflow `.github/workflows/docs-i18n.yml`) fails CI if an English doc lacks its Russian
counterpart, or a Russian file is a stub (< 200 bytes). The convention is documented in
`docs/bilingual-convention.md`.

- English-only / generated / agent files → add the repo-relative path to **`.i18nignore`**.
- Bilingual pairs outside the conventional locations → add `en:ru` to **`.i18npairs`**.

This convention + gate are identical across all our MCP repos.

## Tool reference docs

Tool reference docs are generated from `[McpServerToolType]` / `[McpServerTool]` /
`[Description]` attributes by the shared **`Mcp.ToolsDoc`** dotnet tool (repo `mcp-tooling`).
When wired here (`.config/dotnet-tools.json` + `toolsdoc.json`):

```bash
dotnet tool restore
dotnet tool run mcp-toolsdoc            # (re)generate docs/TOOLS.generated.md
dotnet tool run mcp-toolsdoc --check    # CI gate (fails on drift)
```

Never hand-edit `docs/TOOLS.generated.md`; regenerate after adding/renaming a tool. The
generated file is EN-only — list it in `.i18nignore`. (Note: `tools/SchemaGen` is a separate
generator for XRPL transaction schemas, unrelated to tool-reference docs.)

## Releases

Per-plugin tags `<plugin>--vX.Y.Z` drive plugin releases (`release-plugin.yml` /
`release-plugin.sh`, with signing/SBOM/SLSA). See `RELEASE.md` and per-plugin CHANGELOGs.

## CI/CD — build-from-source deploy

The stdio plugin **release/signing** flow (per-plugin tags, SBOM/SLSA) is unchanged — see the
Releases section above.

The **cloud** server is deployed by **`deploy-build.yml`** (Actions → **deploy-build**), which
builds the image **from source on the host** as a non-root `mcpdeploy` user — there is no GHCR
image to publish or pull:
- It transfers the repo source to the VPS (`git archive` piped over `ssh tar`), reconstructs `.env`
  (plus a minimal `.env.xrpl-mcp`) from **GitHub Secrets / Variables**, then runs
  `docker compose up -d --build` (no `docker login`, no GHCR auth) and smoke-tests `/healthz`.
- A downstream `register` job pushes the self-registration descriptor `.mcp-registry.json` to the
  AS (`PUT /api/admin/mcps`, `X-Service-Token`) so it knows the `xrpl` scope/resource.
- There is no `deploy/deploy.sh`, no root forced-command key, no `DEPLOY_*` image-shipping secrets
  and no `docker save | ssh` tarball.

The shared platform — **Traefik + wildcard TLS for `*.mcp.staticbit.ai` + the `mcp-net` network** —
lives in the separate **mcp-infra** repo, not here.

## Operational conventions

- **Add a new cloud MCP**: add the `Mcp.Auth.ResourceServer` package + an `OAuth` config section
  (`Issuer` = https://auth.mcp.staticbit.ai, `Resource` = this server's canonical `https://…/mcp`,
  `RequiredScope`); wire `AddMcpResourceServer` + `MapMcpProtectedResourceMetadata` +
  `RequireAuthorization(McpAuth.Policy)`. Register it in the AS admin panel `/admin/mcps` (scope +
  resource + secret definitions). Ship the plugin `.mcp.json` with an `oauth` block (DCR; users run
  `/mcp` once). Per-user secrets live in the AS vault (`/cabinet/secrets`), resolved via `IMcpSecretResolver`.
- **Logging**: keep `System.Net.Http.HttpClient` at `Warning` — it logs request URLs at Information
  and the Telegram admin-bot token lives in those URLs. Do not lower it.
- **Secrets**: never commit `.env` / `*.key` / `secrets/`. Admin alerts use a shared admin bot via
  `Server__AdminAlerts__{Enabled,BotToken,ChatId}`.
- **Deploy**: behind the shared Traefik on `mcp-net` (platform from **mcp-infra**, wildcard TLS for
  `*.mcp.staticbit.ai`). Build-from-source deploy via `deploy-build.yml` as the non-root
  `mcpdeploy` user: the runner transfers the repo source to the VPS, reconstructs `.env` (+ a
  minimal `.env.xrpl-mcp`) from GitHub Secrets/Variables, runs `docker compose up -d --build` on
  the host (no GHCR auth), smoke-tests `/healthz`, then a `register` job pushes `.mcp-registry.json`
  to the AS (`PUT /api/admin/mcps`, `X-Service-Token`). The cloud server is OAuth-only (no static
  bearer). Deploy via **Actions → deploy-build**. No `deploy/deploy.sh`, no root forced-command,
  no `DEPLOY_*` secrets, no `docker save`.
