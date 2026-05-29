>  🌐 **Язык**: [English](../branch-protection.md) | **Русский**

# Branch protection — рекомендуемые настройки

Эти правила настраиваются вручную в **Settings → Branches** репозитория (owner-only access). Их нельзя положить в репо как файл — это инфраструктура на стороне GitHub.

Документация ниже описывает целевое состояние для ветки `main` и почему каждое правило важно.

## Целевое состояние: `main` branch protection

### Required settings

| Параметр | Значение | Зачем |
|---|---|---|
| **Restrict who can push to matching branches** | ✅ Enabled | Прямой push на main только через PR-merge. Защита от случайного force push и обхода review. |
| **Require a pull request before merging** | ✅ Enabled | Все изменения проходят через PR — обеспечивает code review и status checks. |
| **Require approvals** | ✅ 1 approval (или 2 для критичных репо) | Минимум один независимый review. Включает требование к репозиториям с >1 maintainer'а. |
| **Dismiss stale pull request approvals when new commits are pushed** | ✅ Enabled | Если автор PR добавил коммит после approve'а — approve сбрасывается. Защита от "approve then sneak in change". |
| **Require status checks to pass before merging** | ✅ Enabled | См. список ниже. |
| **Require branches to be up to date before merging** | ✅ Enabled | Защищает от merge conflicts и от ситуации когда тесты прошли на старой базе. |
| **Require conversation resolution before merging** | ✅ Enabled | Reviewer'ы должны явно resolve'нуть свои комментарии — иначе блокирует merge. |
| **Require signed commits** | ⚠️ Optional | Включить если есть GPG/SSH-ключи у всех contributor'ов. Иначе пропустить. |
| **Require linear history** | ⚠️ Optional | Запрещает merge commits — только rebase/squash. Делает history чище но требует attention от contributor'ов. |
| **Allow force pushes** | ❌ Disabled | Никогда. На main force push — это disaster waiting to happen. |
| **Allow deletions** | ❌ Disabled | Никогда. |

### Required status checks

Эти status checks **должны** проходить перед merge'ем (выставить в "Status checks that are required"):

| Check | Workflow | Что проверяет |
|---|---|---|
| `test` | [dotnet-test.yml](../../.github/workflows/dotnet-test.yml) | Сборка + unit-тесты (Core/Server/Signer, `--filter TestU`). |
| `Analyze (csharp)` | [codeql.yml](../../.github/workflows/codeql.yml) | CodeQL security scan. |

**Не включать** в required:
- `integration-tests` — он на cron (daily), а не на PR. Может временно factor'нуться на testnet flakiness.
- `release-plugin` — отдельный workflow_dispatch event, не для PR-валидации.

### Restrict who can dismiss reviews

Только maintainers / org admins. Защита от "dismissing approvals from outside reviewers" со стороны кого-либо помимо ответственных.

## Как настроить

1. GitHub → repository → **Settings** → **Branches**.
2. Под "Branch protection rules" → **Add rule** (или edit existing для `main`).
3. Branch name pattern: `main`.
4. Включить чекбоксы из таблицы выше.
5. В "Status checks that are required" — найти и добавить `test` и `Analyze (csharp)`.
6. Save changes.

Для org-wide enforcement (если у организации Pro/Team plan) можно использовать **Repository rulesets** (Settings → Code and automation → Rules → Rulesets) — они применяются ко множеству репозиториев сразу.

## Дополнительные org-wide settings

В **Settings → Code security and analysis** (org-level или repo-level):

- **Dependency graph** — ✅ Enabled (бесплатно для всех репозиториев).
- **Dependabot alerts** — ✅ Enabled (бесплатно). Бьёт алертами на vulnerabilities в зависимостях из NuGet.
- **Dependabot security updates** — ✅ Enabled. Auto-PR для critical CVE.
- **CodeQL analysis** — ✅ настроен через [codeql.yml](../../.github/workflows/codeql.yml).
- **Secret scanning** — ✅ Enabled. Сканирует на leaked secrets (API keys, tokens). Бесплатно для public, paid для private (но GitHub раздаёт free credits).
- **Push protection for secrets** — ✅ Enabled. Блокирует push если в коммите detect'нут secret pattern.

## Проверка корректности настройки

После настройки:

1. Создать тестовый PR.
2. Убедиться что **merge button disabled** пока:
   - status checks не зелёные;
   - approver не дал ✅;
   - conversations not resolved;
   - branch не behind main.
3. Попробовать `git push --force origin main` локально — должен **fail** с "protected branch".
4. Попробовать удалить ветку через UI — кнопка должна быть hidden.

## Связанные документы

- [supply-chain.ru.md](supply-chain.md) — supply-chain hardening (SBOM, SLSA, signing).
- [features.ru.md](features.md) §7 — что прикладывается к каждому релизу.
- [.github/dependabot.yml](../../.github/dependabot.yml) — конфигурация Dependabot updates.
- [.github/workflows/codeql.yml](../../.github/workflows/codeql.yml) — CodeQL workflow.
