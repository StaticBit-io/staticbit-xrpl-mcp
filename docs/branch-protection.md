> 🇷🇺 [Прочесть на русском](branch-protection.ru.md)

# Branch protection — recommended settings

These rules are configured manually in **Settings → Branches** (owner-only access). They cannot live in the repo as a file — they are GitHub-side infrastructure.

The documentation below describes the target state for the `main` branch and why each rule matters.

## Target state: `main` branch protection

### Required settings

| Setting | Value | Why |
|---|---|---|
| **Restrict who can push to matching branches** | ✅ Enabled | Direct push to `main` only through PR merge. Protects against accidental force push and review bypass. |
| **Require a pull request before merging** | ✅ Enabled | All changes go through a PR — enables code review and status checks. |
| **Require approvals** | ✅ 1 approval (or 2 for critical repos) | Minimum one independent review. Required where multiple maintainers exist. |
| **Dismiss stale pull request approvals when new commits are pushed** | ✅ Enabled | If the author pushes new commits after an approve, the approve is dismissed. Defends against "approve then sneak in change". |
| **Require status checks to pass before merging** | ✅ Enabled | See the list below. |
| **Require branches to be up to date before merging** | ✅ Enabled | Protects against merge conflicts and the "tests passed on old base" trap. |
| **Require conversation resolution before merging** | ✅ Enabled | Reviewers must explicitly resolve their comments — otherwise merge is blocked. |
| **Require signed commits** | ⚠️ Optional | Enable when all contributors have GPG/SSH keys. Otherwise skip. |
| **Require linear history** | ⚠️ Optional | Forbids merge commits — only rebase/squash. Cleaner history but demands more attention from contributors. |
| **Allow force pushes** | ❌ Disabled | Never. Force push to `main` is a disaster waiting to happen. |
| **Allow deletions** | ❌ Disabled | Never. |

### Required status checks

These status checks **must** pass before merge (add them to "Status checks that are required"):

| Check | Workflow | What it verifies |
|---|---|---|
| `test` | [dotnet-test.yml](../.github/workflows/dotnet-test.yml) | Build + unit tests (Core/Server/Signer, `--filter TestU`). |
| `Analyze (csharp)` | [codeql.yml](../.github/workflows/codeql.yml) | CodeQL security scan. |

**Do not include** in required:
- `integration-tests` — runs on cron (daily), not on PR. May intermittently flake on testnet.
- `release-plugin` — separate workflow_dispatch event, not PR-gated.

### Restrict who can dismiss reviews

Only maintainers / org admins. Defends against "dismissing approvals from outside reviewers" by anyone other than the responsible parties.

## How to configure

1. GitHub → repository → **Settings** → **Branches**.
2. Under "Branch protection rules" → **Add rule** (or edit the existing one for `main`).
3. Branch name pattern: `main`.
4. Tick the checkboxes from the table above.
5. In "Status checks that are required" — find and add `test` and `Analyze (csharp)`.
6. Save changes.

For org-wide enforcement (on Pro/Team plans) use **Repository rulesets** (Settings → Code and automation → Rules → Rulesets) — they apply across multiple repositories at once.

## Additional org-wide settings

In **Settings → Code security and analysis** (org-level or repo-level):

- **Dependency graph** — ✅ Enabled (free for every repo).
- **Dependabot alerts** — ✅ Enabled (free). Fires on vulnerabilities in NuGet dependencies.
- **Dependabot security updates** — ✅ Enabled. Auto-PR for critical CVEs.
- **CodeQL analysis** — ✅ configured via [codeql.yml](../.github/workflows/codeql.yml).
- **Secret scanning** — ✅ Enabled. Scans for leaked secrets (API keys, tokens). Free for public; paid for private (GitHub gives free credits).
- **Push protection for secrets** — ✅ Enabled. Blocks the push when a secret pattern is detected in a commit.

## Verifying the setup

After configuration:

1. Open a test PR.
2. Confirm the **merge button is disabled** while:
   - status checks aren't green;
   - no approver has ✅'d;
   - conversations are unresolved;
   - the branch is behind main.
3. Locally try `git push --force origin main` — it should **fail** with "protected branch".
4. Try deleting the branch via UI — the button should be hidden.

## Related documents

- [supply-chain.md](supply-chain.md) — supply-chain hardening (SBOM, SLSA, signing).
- [features.md](features.md) §7 — what ships with every release.
- [.github/dependabot.yml](../.github/dependabot.yml) — Dependabot updates configuration.
- [.github/workflows/codeql.yml](../.github/workflows/codeql.yml) — CodeQL workflow.
