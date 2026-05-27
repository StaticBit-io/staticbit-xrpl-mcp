> 🇷🇺 [Прочесть на русском](RELEASE.ru.md)

# RELEASE — publishing a new plugin version

This document is for **you**, the release manager, when you need to ship an update of one of the plugins to the marketplace. Orchestrator script: [`release-plugin.sh`](release-plugin.sh).

## TL;DR

```bash
# Run from the monorepo root (source + marketplace live in the same repo).

# 1. You committed code, pushed, ran tests — standard flow.
git status            # must be clean
git push

# 2. Release:
./release-plugin.sh xrpl-signer patch --push
```

The script rebuilds binaries, copies them into `plugins/<name>/bin/`, bumps the version in `plugin.json` + `marketplace.json`, appends a CHANGELOG entry from git log, creates a commit and a git tag, and fast-forward-pushes.

---

## When to bump what

All plugins are currently at `0.1.0` — no public users, you can move freely. Once there are actual subscribers to your marketplace — follow [semver](https://semver.org):

| Change | Bump |
|---|---|
| Bug fix without API change (tool name, parameters, behaviour) | `patch` |
| New tool, new optional parameter on an existing tool, new optional ENV | `minor` |
| Removed tool, renamed parameter, changed semantics (breaking) | `major` |
| SKILL.md / README text only — no API change | `patch` |
| Updated self-contained .NET binary without an API change | `patch` (or `minor` if performance/storage changed noticeably) |

The exact version can be set manually with `--version`:
```bash
./release-plugin.sh xrpl-signer --version 1.0.0-rc.1
```

## Which plugin depends on which source

| Plugin | Source | Bump when changing |
|---|---|---|
| `xrpl-cloud` | plugin manifest + skill + .mcp.json (URL/headers) | manifest/skill only — `--no-build` |
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

# Release — the script does everything:
./release-plugin.sh xrpl-signer patch --push
```

### Scenario B — updated skill / README of a plugin (no rebuild)

```bash
# Edit in place — source and marketplace live in the same monorepo:
vim plugins/xrpl-cloud/skills/xrpl-cloud-operations/SKILL.md
git add plugins/xrpl-cloud/skills/xrpl-cloud-operations/SKILL.md
git commit -m "docs(xrpl-cloud): clarify two-phase signing flow in skill"
git push

# Release without a build:
./release-plugin.sh xrpl-cloud patch --no-build --push
```

### Scenario C — large feature in server code, touches both cloud and local

```bash
# 1. Commit branch in the main repo, merged into main, pushed.
# 2. CLOUD-SERVER DEPLOY — separate procedure (DEPLOY.md):
ssh root@195.26.227.83 'cd /opt/StaticBitXrplMcp && git pull && docker compose up -d --build'
# Test the cloud endpoint manually to make sure it works.

# 3. Release the local plugin with the new self-contained binary:
./release-plugin.sh xrpl-local minor --push

# 4. Usually NO need to bump the cloud plugin —
#    it's just an HTTP wrapper, new functionality becomes available via the same URL
#    automatically. Bump if the URL/headers changed in .mcp.json.
```

### Scenario D — sanity-check without publishing

```bash
# Want to make sure fresh code builds and tests pass,
# and to test the plugin locally before release:
./release-plugin.sh xrpl-signer --build-only

# This rebuilt binaries + copied them into the marketplace, but did NOT do
# version bump / commit / tag. You'll see a diff in the marketplace,
# which you can revert (git checkout) or commit as
# a prep-commit before the normal release.

# Local check by reinstalling the plugin:
claude plugin marketplace update staticbit-xrpl-mcp
claude plugin update xrpl-signer@staticbit-xrpl-mcp
# Restart Claude Code, test live.

# If everything's OK — normal release:
./release-plugin.sh xrpl-signer patch --push
```

### Scenario E — Multi-plugin release

```bash
# Changed something in Core that affected the server, and you also want to
# refresh the manifest of both:
./release-plugin.sh xrpl-local,xrpl-signer minor --push
```
Both plugin versions bump simultaneously, binaries of each are built, with one shared commit.

## What the script does **not** do

| Task | Where to do it |
|---|---|
| Re-deploy the cloud server on VPS | `ssh root@<vps>` + procedure in `DEPLOY.md` (separate step — not tied to plugins) |
| Push a Docker image to GHCR | GitHub Actions automatically on push to main of the source repo |
| Create a GitHub Release (with release-notes UI on GitHub) | Only if you want to. The script creates a **git tag** — usually that's enough. You can later `gh release create <tag>` manually |
| Force-push | Intentionally unsupported. If push is rejected (non-fast-forward) — sort it out manually: `git pull --rebase` in the relevant repo, then retry with `--push` |

## Your responsibility after release

1. **Cloud deployment**, if server code is affected and there's a live VPS. The script doesn't SSH on its own.
2. **Notify users** that the update is available (if there are more than just you). They run:
   ```
   /plugin marketplace update staticbit-xrpl-mcp
   /plugin update xrpl-signer@staticbit-xrpl-mcp
   ```
   The `<plugin>@<marketplace>` form is required — the short
   `claude plugin update xrpl-signer` fails with `Plugin not found`
   in the current Claude Code CLI.
3. **GitHub Release with UI notes** — if desired — `gh release create xrpl-signer--v0.1.1 --notes-from-tag`. The plugin's CHANGELOG.md is a great base for notes.

## Script flags (full list)

```
./release-plugin.sh --help
```

Useful:

| Flag | Purpose |
|---|---|
| `--no-build` | Skip rebuild (docs-only / manifest-only fix) |
| `--build-only` | Build + copy only, no bump/commit/tag |
| `--push` | After all commits, fast-forward-push both repos |
| `--version X.Y.Z` | Explicit version instead of semver bump |
| `--dry-run` | Show what would happen, change nothing |

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `Repo … has uncommitted changes` | Script requires clean repos | `git status` + commit or stash |
| `Plugin … not found in marketplace.json` | Plugin name not registered in the marketplace | Check `plugins[].name` in `.claude-plugin/marketplace.json` |
| `Artifacts not found at …` | Build script failed or didn't run | Run `bash build-signer-binaries.sh` separately to see errors |
| `non-fast-forward` on push | Someone (or you from another device) pushed earlier | `git pull --rebase` in the relevant repo → retry with `--push` |
| `claude plugin tag` complains about validation | Plugin manifest or marketplace entry are out of sync | Open both and verify that `version` in `plugin.json` matches `marketplace.json/plugins[i]` (the script does this automatically — but a manual edit may have desynced them) |

## Extension to other plugins

If a plugin from a **different** source repo joins this marketplace (e.g. `x-mcp-cloud` from `Platonenkov/XMcp`) — you need the same `release-plugin.sh` in that source repo. It would know about its own binaries (if any) and copy them into `staticbit-xrpl-mcp/plugins/x-mcp-cloud/`. The JSON-helpers / changelog / commit-tag-push logic is one-to-one — you can copy it across and substitute your `PLUGIN_KIND` values.
