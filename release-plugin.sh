#!/usr/bin/env bash
# release-plugin.sh — orchestrate a release of one or more XRPL plugins.
#
# Designed for the monorepo layout — source code and marketplace manifests
# live side by side in the same repo. Owns the publish-time chores:
#   1. Rebuild self-contained binaries for the plugins that ship them.
#   2. Copy binaries into plugins/<name>/bin/<rid>/.
#   3. Bump version in .claude-plugin/plugin.json and .claude-plugin/marketplace.json.
#   4. Append a CHANGELOG.md entry derived from git log.
#   5. Commit everything as one atomic change.
#   6. Create a git tag via `claude plugin tag` (per-plugin semver tag).
#   7. Optionally fast-forward push (commits + tags).
#
# Does NOT do:
#   - Cloud server redeploy (DEPLOY.md procedure, separate from plugin releases).
#   - GHCR image push (handled by GitHub Actions on push to main).
#   - Force push of anything. Fast-forward only.
#
# This script is also the workhorse for .github/workflows/release-plugin.yml —
# the CI workflow just calls it without --push and then pushes itself.
#
# Usage:
#   ./release-plugin.sh <plugin>[,<plugin>...] <bump>          [flags]
#   ./release-plugin.sh <plugin>[,<plugin>...] --version X.Y.Z [flags]
#
# Positional:
#   plugin    one of: xrpl-cloud, xrpl-local, xrpl-signer (comma-separated for multi)
#   bump      one of: patch, minor, major   (mutually exclusive with --version)
#
# Flags:
#   --version X.Y.Z   set an exact version instead of semver bump
#   --no-build        skip the binary rebuild (useful for docs-only releases)
#   --build-only      build & stage binaries, no version bump / commit / tag
#   --push            git push after committing & tagging (fast-forward only)
#   --dry-run         print intended actions, change nothing on disk or git
#   -h, --help
#
# Example flows:
#   ./release-plugin.sh xrpl-signer patch                # bump 0.1.1 → 0.1.2, build, commit, tag
#   ./release-plugin.sh xrpl-signer patch --push         # …and push
#   ./release-plugin.sh xrpl-cloud  patch --no-build     # docs-only fix in a non-binary plugin
#   ./release-plugin.sh xrpl-local,xrpl-signer minor --push
#   ./release-plugin.sh xrpl-signer --build-only         # local sanity check
#   ./release-plugin.sh xrpl-signer --version 1.0.0-rc.1 --push

set -euo pipefail

# ─────────────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$SCRIPT_DIR"

# Maps a plugin name to the "kind" of binary it ships (or "none" for HTTP-only).
declare -A PLUGIN_KIND=(
  [xrpl-cloud]="none"
  [xrpl-local]="server"
  [xrpl-signer]="signer"
)

# ─────────────────────────────────────────────────────────────────────────────
# Output helpers
# ─────────────────────────────────────────────────────────────────────────────

if [ -t 1 ]; then
  RED='\033[0;31m'; YEL='\033[0;33m'; GRN='\033[0;32m'; BLU='\033[0;34m'; DIM='\033[2m'; RST='\033[0m'
else
  RED=''; YEL=''; GRN=''; BLU=''; DIM=''; RST=''
fi

info()  { echo -e "${BLU}▸${RST} $*"; }
warn()  { echo -e "${YEL}▸${RST} $*" >&2; }
ok()    { echo -e "${GRN}✓${RST} $*"; }
fail()  { echo -e "${RED}✗${RST} $*" >&2; exit 1; }
step()  { echo -e "\n${BLU}──${RST} $*"; }
dim()   { echo -e "${DIM}$*${RST}"; }

# ─────────────────────────────────────────────────────────────────────────────
# Argument parsing
# ─────────────────────────────────────────────────────────────────────────────

PLUGINS_ARG=""
BUMP=""
EXPLICIT_VERSION=""
NO_BUILD=false
BUILD_ONLY=false
PUSH=false
DRY_RUN=false

usage() {
  sed -n '2,46p' "$0" | sed 's/^# \{0,1\}//'
  exit "${1:-0}"
}

while [ $# -gt 0 ]; do
  case "$1" in
    -h|--help)         usage 0 ;;
    --version)         EXPLICIT_VERSION="${2:?--version requires X.Y.Z}"; shift 2 ;;
    --no-build)        NO_BUILD=true; shift ;;
    --build-only)      BUILD_ONLY=true; shift ;;
    --push)            PUSH=true; shift ;;
    --dry-run)         DRY_RUN=true; shift ;;
    patch|minor|major) BUMP="$1"; shift ;;
    -*)                fail "Unknown flag: $1 (try --help)" ;;
    *)
      if [ -z "$PLUGINS_ARG" ]; then PLUGINS_ARG="$1"
      else                            fail "Unexpected positional: $1"
      fi
      shift
      ;;
  esac
done

[ -n "$PLUGINS_ARG" ] || fail "Plugin name(s) required (try --help)"

if ! $BUILD_ONLY; then
  if [ -n "$BUMP" ] && [ -n "$EXPLICIT_VERSION" ]; then
    fail "Choose either a semver bump (patch/minor/major) OR --version X.Y.Z, not both."
  fi
  if [ -z "$BUMP" ] && [ -z "$EXPLICIT_VERSION" ]; then
    fail "Specify a semver bump (patch/minor/major) or --version X.Y.Z (try --help)."
  fi
fi

IFS=',' read -ra PLUGINS <<< "$PLUGINS_ARG"
for p in "${PLUGINS[@]}"; do
  if [ -z "${PLUGIN_KIND[$p]:-}" ]; then
    fail "Unknown plugin '$p'. Known: ${!PLUGIN_KIND[*]}"
  fi
done

# ─────────────────────────────────────────────────────────────────────────────
# Pre-flight
# ─────────────────────────────────────────────────────────────────────────────

run() {
  if $DRY_RUN; then echo -e "${DIM}DRY  $*${RST}"; else "$@"; fi
}

step "Pre-flight"
[ -f "$REPO_ROOT/.claude-plugin/marketplace.json" ] || fail "Not in repo root: marketplace.json missing"

if ! git -C "$REPO_ROOT" diff --quiet || ! git -C "$REPO_ROOT" diff --cached --quiet; then
  fail "Repo has uncommitted changes. Commit or stash before releasing."
fi

actual_branch="$(git -C "$REPO_ROOT" rev-parse --abbrev-ref HEAD)"
if [ "$actual_branch" != "main" ]; then
  warn "Releasing from branch '$actual_branch', not 'main'."
fi

command -v node >/dev/null   || fail "node is required (used for JSON manipulation)"
command -v dotnet >/dev/null || $NO_BUILD || fail "dotnet is required unless --no-build"
command -v claude >/dev/null || warn "claude CLI not found — git tag will be created directly without manifest validation"

ok "Repo clean, tools available."
ok "Repo root: $REPO_ROOT"

# ─────────────────────────────────────────────────────────────────────────────
# JSON helpers (node-driven, jq-free, ENV-routed paths for MSYS compatibility)
# ─────────────────────────────────────────────────────────────────────────────

json_read_version() {
  local plugin_json="$1"
  FILE_PATH="$plugin_json" node -e "
    const fs = require('fs');
    process.stdout.write(JSON.parse(fs.readFileSync(process.env.FILE_PATH,'utf8')).version);
  "
}

bump_semver() {
  local current="$1" kind="$2"
  CUR="$current" KIND="$kind" node -e "
    const cur = process.env.CUR;
    const m = cur.match(/^(\d+)\.(\d+)\.(\d+)(?:[-+].*)?$/);
    if (!m) { console.error('Invalid current version: ' + cur); process.exit(2); }
    let [_, a, b, c] = m;
    a = +a; b = +b; c = +c;
    const k = process.env.KIND;
    if      (k === 'major') { a++; b = 0; c = 0; }
    else if (k === 'minor') { b++; c = 0; }
    else if (k === 'patch') { c++; }
    else { console.error('Bad bump kind: ' + k); process.exit(2); }
    process.stdout.write(a + '.' + b + '.' + c);
  "
}

write_json_field() {
  local file="$1" path="$2" value="$3"
  FILE_PATH="$file" FIELD_PATH="$path" FIELD_VALUE="$value" node -e "
    const fs = require('fs');
    const file = process.env.FILE_PATH;
    const j = JSON.parse(fs.readFileSync(file,'utf8'));
    const segs = process.env.FIELD_PATH.split('.');
    let cur = j;
    for (let i = 0; i < segs.length - 1; i++) cur = cur[segs[i]];
    cur[segs[segs.length - 1]] = process.env.FIELD_VALUE;
    fs.writeFileSync(file, JSON.stringify(j, null, 2) + '\n');
  "
}

write_marketplace_plugin_version() {
  local mp_json="$1" plugin_name="$2" new_version="$3"
  FILE_PATH="$mp_json" PLUGIN_NAME="$plugin_name" NEW_VERSION="$new_version" node -e "
    const fs = require('fs');
    const file = process.env.FILE_PATH;
    const j = JSON.parse(fs.readFileSync(file,'utf8'));
    const entry = j.plugins.find(p => p.name === process.env.PLUGIN_NAME);
    if (!entry) {
      console.error('Plugin ' + process.env.PLUGIN_NAME + ' not found in marketplace.json');
      process.exit(2);
    }
    entry.version = process.env.NEW_VERSION;
    fs.writeFileSync(file, JSON.stringify(j, null, 2) + '\n');
  "
}

# ─────────────────────────────────────────────────────────────────────────────
# Build & stage
# ─────────────────────────────────────────────────────────────────────────────

build_for_kind() {
  local kind="$1"
  case "$kind" in
    server)   step "Building server binaries (5 RIDs)"; run bash "$REPO_ROOT/build-server-binaries.sh" ;;
    signer)   step "Building signer binaries (5 RIDs)"; run bash "$REPO_ROOT/build-signer-binaries.sh" ;;
    none)     dim   "Plugin has no binaries (HTTP wrapper) — skip build." ;;
    *)        fail  "Unknown build kind: $kind" ;;
  esac
}

stage_for_plugin() {
  local plugin="$1" kind="${PLUGIN_KIND[$plugin]}"
  case "$kind" in
    server)  stage_binaries "$plugin" "server" "StaticBit.Xrpl.Mcp.Server"  ;;
    signer)  stage_binaries "$plugin" "signer" "StaticBit.Xrpl.Mcp.Signer"  ;;
    none)    dim "Plugin $plugin has no binaries to stage." ;;
  esac
}

stage_binaries() {
  local plugin="$1" kind="$2" bin_basename="$3"
  local plugin_bin_dir="$REPO_ROOT/plugins/$plugin/bin"
  local artifacts_root="$REPO_ROOT/artifacts/$kind"

  [ -d "$artifacts_root" ] || fail "Artifacts not found at $artifacts_root (did the build step succeed?)"

  for rid in win-x64 linux-x64 linux-arm64 osx-x64 osx-arm64; do
    local src="$artifacts_root/$rid"
    local dst="$plugin_bin_dir/$rid"

    if [ ! -d "$src" ]; then
      warn "Artifacts missing for $rid at $src — skipping"
      continue
    fi

    info "Staging $plugin/$rid"
    run mkdir -p "$dst"
    if [[ "$rid" == win-* ]]; then
      run cp "$src/${bin_basename}.exe" "$dst/${bin_basename}.exe"
    else
      run cp "$src/$bin_basename" "$dst/$bin_basename"
      run chmod +x "$dst/$bin_basename" 2>/dev/null || true
    fi
  done

  # Strip ASP.NET asset junk that publish-singlefile emits.
  run find "$plugin_bin_dir" -name "*.staticwebassets.endpoints.json" -delete 2>/dev/null || true
}

# ─────────────────────────────────────────────────────────────────────────────
# CHANGELOG (single repo — one log source)
# ─────────────────────────────────────────────────────────────────────────────

# Group raw "<subject> (<hash>)" lines by Conventional Commit type
# (feat / fix / docs / refactor / test / perf / chore / etc.). Anything that
# doesn't parse as a conventional commit falls into Other.
# Stdin: raw log (one commit per line). Stdout: markdown subsections.
group_by_conventional_commit() {
  local raw_log="$1"
  awk '
    BEGIN {
      # Ordered list of section headings; bucket name is everything before "|".
      n_sections = 8
      sections[1] = "feat|Features"
      sections[2] = "fix|Fixes"
      sections[3] = "perf|Performance"
      sections[4] = "refactor|Refactoring"
      sections[5] = "docs|Documentation"
      sections[6] = "test|Tests"
      sections[7] = "build|Build / CI"
      sections[8] = "ci|Build / CI"
      bucket["feat"] = 1; bucket["fix"] = 2; bucket["perf"] = 3
      bucket["refactor"] = 4; bucket["docs"] = 5; bucket["test"] = 6
      bucket["build"] = 7; bucket["ci"] = 8; bucket["chore"] = 0
    }
    {
      line = $0
      # Match "type:" or "type(scope):" prefix, case-insensitively.
      if (match(line, /^[a-zA-Z]+(\([^)]*\))?(!?):[ \t]+/)) {
        prefix = substr(line, 1, RLENGTH)
        rest = substr(line, RLENGTH + 1)
        # Extract bare type (lowercase).
        if (match(prefix, /^[a-zA-Z]+/)) {
          type = tolower(substr(prefix, 1, RLENGTH))
          if (type in bucket) {
            idx = bucket[type]
            if (idx == 0) { other[++other_n] = "- " line; next }
            entries[idx, ++count[idx]] = "- " rest
            next
          }
        }
      }
      other[++other_n] = "- " line
    }
    END {
      first = 1
      for (i = 1; i <= n_sections; i++) {
        if (count[i] > 0) {
          if (!first) print ""
          split(sections[i], parts, "|")
          print "### " parts[2]
          for (j = 1; j <= count[i]; j++) print entries[i, j]
          first = 0
        }
      }
      if (other_n > 0) {
        if (!first) print ""
        print "### Other"
        for (k = 1; k <= other_n; k++) print other[k]
      }
    }
  ' <<<"$raw_log"
}

append_changelog() {
  local plugin="$1" new_version="$2"
  local changelog="$REPO_ROOT/plugins/$plugin/CHANGELOG.md"

  local last_tag
  last_tag="$(git -C "$REPO_ROOT" tag --list "${plugin}--v*" --sort=-version:refname | head -n 1 || true)"

  local log_range
  if [ -n "$last_tag" ]; then
    log_range="${last_tag}..HEAD"
  else
    log_range="HEAD"
  fi

  # Raw "subject (hash)" list, one per commit, used as input for categorization below.
  local raw_log
  raw_log="$(git -C "$REPO_ROOT" log --pretty=format:'%s (%h)' "$log_range" 2>/dev/null || true)"

  local log_body
  if [ -z "$raw_log" ]; then
    log_body="- No changes recorded since previous release."
  else
    log_body="$(group_by_conventional_commit "$raw_log")"
  fi

  local today
  today="$(date -u +%Y-%m-%d)"

  local entry
  entry=$(cat <<EOF
## v${new_version} — ${today}

${log_body}
EOF
)

  if $DRY_RUN; then
    echo -e "${DIM}DRY would prepend to $changelog:${RST}"
    echo -e "${DIM}---${RST}"
    echo "$entry"
    echo -e "${DIM}---${RST}"
    return
  fi

  if [ -f "$changelog" ]; then
    {
      printf '%s\n\n' "$entry"
      cat "$changelog"
    } > "${changelog}.tmp"
    mv "${changelog}.tmp" "$changelog"
  else
    {
      printf '# %s changelog\n\nAll notable changes to this plugin are listed here. Newest at the top.\n\n' "$plugin"
      printf '%s\n' "$entry"
    } > "$changelog"
  fi

  ok "Updated $changelog"
}

# ─────────────────────────────────────────────────────────────────────────────
# Build phase
# ─────────────────────────────────────────────────────────────────────────────

needed_kinds() {
  local -A seen=()
  for p in "${PLUGINS[@]}"; do
    local k="${PLUGIN_KIND[$p]}"
    [ "$k" = "none" ] && continue
    seen[$k]=1
  done
  printf '%s\n' "${!seen[@]}"
}

if ! $NO_BUILD; then
  for kind in $(needed_kinds); do
    build_for_kind "$kind"
  done
fi

step "Staging binaries"
for plugin in "${PLUGINS[@]}"; do
  stage_for_plugin "$plugin"
done

if $BUILD_ONLY; then
  ok "Build-only mode: no version bump, no commits, no tag."
  exit 0
fi

# ─────────────────────────────────────────────────────────────────────────────
# Version bump + changelog + commit + tag
# ─────────────────────────────────────────────────────────────────────────────

declare -A NEW_VERSIONS=()

step "Bumping versions"
for plugin in "${PLUGINS[@]}"; do
  plugin_json="$REPO_ROOT/plugins/$plugin/.claude-plugin/plugin.json"
  marketplace_json="$REPO_ROOT/.claude-plugin/marketplace.json"

  current_version="$(json_read_version "$plugin_json")"
  if [ -n "$EXPLICIT_VERSION" ]; then
    new_version="$EXPLICIT_VERSION"
  else
    new_version="$(bump_semver "$current_version" "$BUMP")"
  fi
  NEW_VERSIONS[$plugin]="$new_version"

  info "$plugin: $current_version → $new_version"

  if ! $DRY_RUN; then
    write_json_field "$plugin_json" "version" "$new_version"
    write_marketplace_plugin_version "$marketplace_json" "$plugin" "$new_version"
  fi
done

step "Appending CHANGELOG entries"
for plugin in "${PLUGINS[@]}"; do
  append_changelog "$plugin" "${NEW_VERSIONS[$plugin]}"
done

step "Committing"
COMMIT_TITLE="release: $(
  for p in "${PLUGINS[@]}"; do printf '%s v%s, ' "$p" "${NEW_VERSIONS[$p]}"; done | sed 's/, $//'
)"
COMMIT_BODY="Automated by release-plugin.sh"

run git -C "$REPO_ROOT" add -A
run git -C "$REPO_ROOT" commit -m "$COMMIT_TITLE" -m "$COMMIT_BODY"

step "Creating tags"
for plugin in "${PLUGINS[@]}"; do
  v="${NEW_VERSIONS[$plugin]}"
  tag_name="${plugin}--v${v}"

  if command -v claude >/dev/null; then
    info "Validating $plugin via claude plugin tag"
    if $DRY_RUN; then
      dim "DRY  claude plugin tag plugins/$plugin (would also create tag)"
    else
      (cd "$REPO_ROOT" && claude plugin tag "plugins/$plugin")
    fi
  else
    info "Creating tag $tag_name (claude CLI absent — no validation)"
    run git -C "$REPO_ROOT" tag -a "$tag_name" -m "release $plugin v$v"
  fi
done

# ─────────────────────────────────────────────────────────────────────────────
# Push (optional, fast-forward only)
# ─────────────────────────────────────────────────────────────────────────────

if $PUSH; then
  step "Pushing (fast-forward only)"
  run git -C "$REPO_ROOT" push --no-force
  run git -C "$REPO_ROOT" push --no-force --tags
fi

# ─────────────────────────────────────────────────────────────────────────────
# Report
# ─────────────────────────────────────────────────────────────────────────────

step "Done"
for plugin in "${PLUGINS[@]}"; do
  echo -e "${GRN}✓${RST} $plugin → v${NEW_VERSIONS[$plugin]}"
done
if $PUSH; then
  echo
  echo "Users can update with:"
  echo "  claude plugin marketplace update staticbit-xrpl-mcp"
  for plugin in "${PLUGINS[@]}"; do
    echo "  claude plugin update $plugin"
  done
else
  echo
  echo "Not pushed (no --push). Inspect commits/tags, then run with --push to publish."
fi
