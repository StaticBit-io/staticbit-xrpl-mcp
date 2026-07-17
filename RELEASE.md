> 🇷🇺 [Прочесть на русском](RELEASE.ru.md)

# RELEASE — publishing a new plugin version

This is the maintainer guide for shipping an update of one of the plugins to the marketplace. The canonical path is the **`release-plugin` workflow**; its workhorse is [`release-plugin.sh`](release-plugin.sh), which you normally do **not** run yourself for a real release.

## TL;DR

```bash
# 1. You committed code, pushed, CI is green — standard flow.
git status            # must be clean
git push

# 2. Release — Actions → release-plugin, or from the CLI:
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-signer -f bump=patch

# 3. Verify the release exists with its artifacts:
gh release view xrpl-signer--v0.4.2 --json assets --jq '.assets[].name'
```

The workflow runs the unit tests, calls `release-plugin.sh` (rebuild binaries → bump `plugin.json` + `marketplace.json` → CHANGELOG entry → commit → tag), then signs the binaries, generates a CycloneDX SBOM, attaches an SLSA build-provenance attestation, and creates the **GitHub Release** with per-RID tarballs + `.sha256` files.

> ⚠️ **Do not run `./release-plugin.sh <plugin> <bump> --push` for a real release.**
> The script stops at commit+tag. Signing, SBOM, SLSA provenance and the GitHub Release exist **only** in `release-plugin.yml`, and that workflow is **dispatch-only** — it has no `push: tags` trigger, so a locally-pushed tag produces no Release at all. The marketplace would still serve the new version (binaries are committed in-repo), leaving a version that silently lacks its supply-chain artifacts. Locally the script is for `--build-only` / `--dry-run` checks (Scenario D).

---

## When to bump what

Follow [semver](https://semver.org):

| Change | Bump |
|---|---|
| Bug fix without API change (tool name, parameters, behaviour) | `patch` |
| New tool, new optional parameter on an existing tool, new optional ENV | `minor` |
| Removed tool, renamed parameter, changed semantics (breaking) | `major` |
| SKILL.md / README text only — no API change | `patch` |
| Updated self-contained .NET binary without an API change | `patch` (or `minor` if performance/storage changed noticeably) |

Making an existing **required** parameter optional is `minor` — it widens what the schema accepts without breaking existing calls.

The workflow takes `patch` / `minor` / `major`. An exact version (e.g. a pre-release like `1.0.0-rc.1`) is a script-only feature — see `--version` in the flags table.

## Which plugin depends on which source

| Plugin | Source | Bump when changing |
|---|---|---|
| `xrpl-cloud` | plugin manifest + skill + .mcp.json (URL/headers) | manifest/skill only — `no_build` |
| `xrpl-local` | `src/StaticBit.Xrpl.Mcp.{Abstractions,Core,Server}` | the whole server project |
| `xrpl-signer` | `src/StaticBit.Xrpl.Mcp.Signer` | the signer project only (independent) |

If you change `StaticBit.Xrpl.Mcp.Core` — only `xrpl-local` is affected (the signer does not depend on Core). If you change `StaticBit.Xrpl.Mcp.Server` — only `xrpl-local`. If you change `StaticBit.Xrpl.Mcp.Signer` — only `xrpl-signer`. `xrpl-cloud` depends only on the URL endpoint and the manifest text.

## Typical scenarios

### Scenario A — small fix in signer code

```bash
# Edit src/StaticBit.Xrpl.Mcp.Signer/..., test:
dotnet test --filter TestU

# Commit to the main repo as usual:
git add -A
git commit -m "fix(signer): correct error message on missing wallet"
git push

# Release — the workflow does everything:
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-signer -f bump=patch
```

### Scenario B — updated skill / README of a plugin (no rebuild)

```bash
# Edit in place — source and marketplace live in the same monorepo:
vim plugins/xrpl-cloud/skills/xrpl-cloud-operations/SKILL.md
git add plugins/xrpl-cloud/skills/xrpl-cloud-operations/SKILL.md
git commit -m "docs(xrpl-cloud): clarify two-phase signing flow in skill"
git push

# Release without a build:
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-cloud -f bump=patch -f no_build=true
```

### Scenario C — feature in server code, touches both cloud and local

```bash
# 1. Code committed to main and pushed, CI green.

# 2. CLOUD-SERVER DEPLOY — Actions → deploy-build (builds from source on the host):
gh workflow run deploy-build.yml --ref main

# 3. Verify the live server picked up the new build. /healthz returns the
#    deployed commit's short SHA (the APP_VERSION build-arg), not a semver:
curl -s https://xrpl.mcp.staticbit.ai/healthz     # {"status":"ok","version":"<short-sha>"}
git rev-parse --short HEAD                        # must match

# 4. Release the local plugin with the new self-contained binary:
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-local -f bump=minor
```

Step 4 adds a release commit on top of `main`, so `/healthz` will then trail HEAD by that commit. Re-run `deploy-build` if you want the reported version to match HEAD exactly — the server code itself is unaffected (the release commit only touches `bin/` + manifests + CHANGELOG).

**Usually NO need to bump the cloud plugin** — it's just an HTTP wrapper; new tools and changed tool schemas become available through the same URL as soon as the server is deployed. Bump it only if the URL/headers changed in `.mcp.json`.

### Scenario D — sanity-check without publishing

This is what the local script is *for*:

```bash
# Make sure fresh code builds and tests pass, and test the plugin locally:
./release-plugin.sh xrpl-signer --build-only

# This rebuilt binaries + copied them into the marketplace, but did NOT do
# version bump / commit / tag. You'll see a diff in the marketplace,
# which you can revert (git checkout) or commit as
# a prep-commit before the normal release.

# Local check by reinstalling the plugin:
claude plugin marketplace update staticbit-xrpl-mcp
claude plugin update xrpl-signer@staticbit-xrpl-mcp
# Restart Claude Code, test live.

# If everything's OK — release through the workflow:
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-signer -f bump=patch
```

### Scenario E — Multi-plugin release

The workflow releases **one plugin per run**. Dispatch them **one at a time, waiting for each to complete** before starting the next:

```bash
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-local -f bump=minor
gh run watch "$(gh run list --workflow=release-plugin.yml --limit 1 --json databaseId --jq '.[0].databaseId')" --exit-status

gh workflow run release-plugin.yml --ref main -f plugin=xrpl-signer -f bump=minor
```

Each run checks out the dispatch-time SHA and pushes to `main` without rebasing, so dispatching several at once causes non-fast-forward failures — and the shared `concurrency: release-plugin` group cancels the middle run.

## What the release workflow does **not** do

| Task | Where to do it |
|---|---|
| Re-deploy the cloud server | Actions → **deploy-build** (`gh workflow run deploy-build.yml`). Builds from source on the VPS as non-root `mcpdeploy`; see `docs/DEPLOY.md`. Not tied to plugin releases |
| Push a Docker image | Nothing to push — the cloud deploy builds the image **from source on the host**. There is no GHCR image to publish or pull |
| Bump the `mcp-fleet` gitlink | Superproject chore: `git add staticbit-xrpl-mcp && git commit && git push` in `mcp-fleet` |
| Force-push | Intentionally unsupported. If push is rejected (non-fast-forward) — sort it out manually: `git pull --rebase`, then re-dispatch |

## After release

1. **Cloud deployment**, if server code is affected — Actions → `deploy-build`. The release workflow does not deploy.
2. **Bump the gitlink** in the `mcp-fleet` superproject so it points at the release commit.
3. **Notify users** that the update is available. They run:
   ```
   /plugin marketplace update staticbit-xrpl-mcp
   /plugin update xrpl-signer@staticbit-xrpl-mcp
   ```
   The `<plugin>@<marketplace>` form is required — the short
   `claude plugin update xrpl-signer` fails with `Plugin not found`
   in the current Claude Code CLI.

The GitHub Release itself (notes from the CHANGELOG, tarballs, `.sha256`, SBOM, attestation) is created by the workflow — nothing to do by hand.

## Script flags (full list)

```
./release-plugin.sh --help
```

Useful:

| Flag | Purpose |
|---|---|
| `--no-build` | Skip rebuild (docs-only / manifest-only fix) — workflow input `no_build` |
| `--build-only` | Build + copy only, no bump/commit/tag — local check, no workflow equivalent |
| `--push` | After all commits, fast-forward-push both repos — **CI-only; see the TL;DR warning** |
| `--version X.Y.Z` | Explicit version instead of semver bump (e.g. pre-releases) |
| `--dry-run` | Show what would happen, change nothing — workflow input `dry_run` |

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Tag exists but there's no GitHub Release / SBOM / attestation | The release was made by running `release-plugin.sh --push` locally instead of dispatching the workflow | `git revert` the release commit (it only touches `bin/` + manifests + CHANGELOG — code and prod are untouched), `git push origin --delete <tag>`, then dispatch the workflow. It recreates the same version cleanly; no force-push needed |
| `Repo … has uncommitted changes` | Script requires clean repos | `git status` + commit or stash |
| `Plugin … not found in marketplace.json` | Plugin name not registered in the marketplace | Check `plugins[].name` in `.claude-plugin/marketplace.json` |
| `Artifacts not found at …` | Build script failed or didn't run | Run `bash build-signer-binaries.sh` separately to see errors |
| `non-fast-forward` on push | A concurrent release run, or someone pushed earlier | Dispatch releases sequentially (Scenario E); `git pull --rebase` → re-dispatch |
| `claude plugin tag` complains about validation | Plugin manifest or marketplace entry are out of sync | Open both and verify that `version` in `plugin.json` matches `marketplace.json/plugins[i]` (the script does this automatically — but a manual edit may have desynced them) |

## Extension to other plugins

If a plugin from a **different** source repo joins this marketplace (e.g. `x-mcp-cloud` from `Platonenkov/XMcp`) — you need the same `release-plugin.sh` in that source repo. It would know about its own binaries (if any) and copy them into `staticbit-xrpl-mcp/plugins/x-mcp-cloud/`. The JSON-helpers / changelog / commit-tag-push logic is one-to-one — you can copy it across and substitute your `PLUGIN_KIND` values.
