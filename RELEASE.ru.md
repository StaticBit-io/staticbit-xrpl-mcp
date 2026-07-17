> 🇬🇧 [Read in English](RELEASE.md)

# RELEASE — выпуск новой версии плагинов

Гайд для мейнтейнера: как опубликовать обновление одного из плагинов в marketplace. Канонический путь — **workflow `release-plugin`**; его рабочая лошадка — [`release-plugin.sh`](release-plugin.sh), который для настоящего релиза руками запускать **не** нужно.

## TL;DR

```bash
# 1. Закоммитил код, запушил, CI зелёный — стандартный flow.
git status            # должно быть clean
git push

# 2. Релиз — Actions → release-plugin, либо из CLI:
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-signer -f bump=patch

# 3. Проверить, что релиз создан вместе с артефактами:
gh release view xrpl-signer--v0.4.2 --json assets --jq '.assets[].name'
```

Workflow прогоняет unit-тесты, вызывает `release-plugin.sh` (пересборка бинарей → bump `plugin.json` + `marketplace.json` → запись в CHANGELOG → коммит → тег), затем подписывает бинари, генерирует CycloneDX SBOM, прикладывает SLSA build-provenance attestation и создаёт **GitHub Release** с per-RID тарболами и `.sha256`.

> ⚠️ **Не запускай `./release-plugin.sh <plugin> <bump> --push` для настоящего релиза.**
> Скрипт останавливается на commit+tag. Подпись, SBOM, SLSA-provenance и GitHub Release существуют **только** в `release-plugin.yml`, а этот workflow — **dispatch-only**: триггера `push: tags` у него нет, поэтому запушенный локально тег не создаёт Release вообще. Marketplace при этом всё равно отдаст новую версию (бинари закоммичены в репо) — получится версия, у которой молча нет supply-chain артефактов. Локально скрипт нужен для проверок `--build-only` / `--dry-run` (сценарий D).

---

## Когда какой bump

Соблюдай [semver](https://semver.org/lang/ru/):

| Что изменилось | Bump |
|---|---|
| Фикс бага без изменения API (имя tool'а, его параметры, поведение) | `patch` |
| Новый tool, новый опциональный параметр существующего tool'а, новая опциональная ENV | `minor` |
| Удалён tool, переименован параметр, изменена семантика (breaking) | `major` |
| Только текст SKILL.md / README — без изменения API | `patch` |
| Обновлён self-contained .NET бинарь без изменения API | `patch` (или `minor`, если заметно изменилась производительность/размер) |

Превращение существующего **обязательного** параметра в опциональный — это `minor`: схема начинает принимать больше, не ломая существующие вызовы.

Workflow принимает `patch` / `minor` / `major`. Точная версия (например, pre-release вида `1.0.0-rc.1`) — возможность только скрипта, см. `--version` в таблице флагов.

## Какой плагин от каких исходников зависит

| Плагин | Исходники | Bump при изменении |
|---|---|---|
| `xrpl-cloud` | манифест плагина + skill + .mcp.json (URL/headers) | только манифест/skill — `no_build` |
| `xrpl-local` | `src/StaticBit.Xrpl.Mcp.{Abstractions,Core,Server}` | весь серверный проект |
| `xrpl-signer` | `src/StaticBit.Xrpl.Mcp.Signer` | только проект сигнера (независим) |

Меняешь `StaticBit.Xrpl.Mcp.Core` — затронут только `xrpl-local` (сигнер от Core не зависит). Меняешь `StaticBit.Xrpl.Mcp.Server` — только `xrpl-local`. Меняешь `StaticBit.Xrpl.Mcp.Signer` — только `xrpl-signer`. `xrpl-cloud` зависит лишь от URL эндпоинта и текста манифеста.

## Типовые сценарии

### Сценарий A — небольшой фикс в коде сигнера

```bash
# Правишь src/StaticBit.Xrpl.Mcp.Signer/..., тестируешь:
dotnet test --filter TestU

# Коммитишь в основной репо как обычно:
git add -A
git commit -m "fix(signer): correct error message on missing wallet"
git push

# Релизишь — workflow сам всё сделает:
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-signer -f bump=patch
```

### Сценарий B — обновили skill / README плагина (без пересборки)

```bash
# Правишь прямо тут — исходники и marketplace в одном monorepo:
vim plugins/xrpl-cloud/skills/xrpl-cloud-operations/SKILL.md
git add plugins/xrpl-cloud/skills/xrpl-cloud-operations/SKILL.md
git commit -m "docs(xrpl-cloud): clarify two-phase signing flow in skill"
git push

# Релизишь без билда:
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-cloud -f bump=patch -f no_build=true
```

### Сценарий C — фича в серверном коде, затрагивает и cloud, и local

```bash
# 1. Код закоммичен в main и запушен, CI зелёный.

# 2. ДЕПЛОЙ CLOUD-СЕРВЕРА — Actions → deploy-build (собирает из исходников на хосте):
gh workflow run deploy-build.yml --ref main

# 3. Проверь, что живой сервер поднял новый билд. /healthz возвращает короткий
#    SHA задеплоенного коммита (build-arg APP_VERSION), а не semver:
curl -s https://xrpl.mcp.staticbit.ai/healthz     # {"status":"ok","version":"<short-sha>"}
git rev-parse --short HEAD                        # должно совпасть

# 4. Релизишь local-плагин с новым self-contained бинарём:
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-local -f bump=minor
```

Шаг 4 добавляет релизный коммит поверх `main`, поэтому `/healthz` после него будет отставать от HEAD на этот коммит. Перезапусти `deploy-build`, если нужно, чтобы версия на сервере точно совпадала с HEAD — сам код сервера при этом не меняется (релизный коммит трогает только `bin/`, манифесты и CHANGELOG).

**Обычно bump'ать cloud-плагин НЕ нужно** — это просто HTTP-обёртка; новые tools и изменённые схемы tools становятся доступны по тому же URL сразу после деплоя сервера. Bump нужен, только если в `.mcp.json` изменились URL/headers.

### Сценарий D — проверка без публикации

Именно для этого локальный скрипт и нужен:

```bash
# Хочешь убедиться, что свежий код собирается и тесты проходят,
# и проверить плагин локально перед релизом:
./release-plugin.sh xrpl-signer --build-only

# Пересобрало бинари + скопировало их в marketplace, но НЕ сделало
# version bump / commit / tag. Увидишь diff в marketplace,
# который можно откатить (git checkout) либо закоммитить
# как prep-коммит перед обычным релизом.

# Локальная проверка переустановкой плагина:
claude plugin marketplace update staticbit-xrpl-mcp
claude plugin update xrpl-signer@staticbit-xrpl-mcp
# Перезапусти Claude Code, проверь вживую.

# Если всё ок — релизишь через workflow:
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-signer -f bump=patch
```

### Сценарий E — релиз нескольких плагинов

Workflow релизит **один плагин за запуск**. Диспатчить нужно **по одному, дожидаясь завершения** каждого перед следующим:

```bash
gh workflow run release-plugin.yml --ref main -f plugin=xrpl-local -f bump=minor
gh run watch "$(gh run list --workflow=release-plugin.yml --limit 1 --json databaseId --jq '.[0].databaseId')" --exit-status

gh workflow run release-plugin.yml --ref main -f plugin=xrpl-signer -f bump=minor
```

Каждый запуск чекаутит SHA на момент диспатча и пушит в `main` без rebase, поэтому одновременный диспатч приводит к non-fast-forward падениям, а общая группа `concurrency: release-plugin` отменяет средний запуск.

## Что релизный workflow **не** делает

| Задача | Где делать |
|---|---|
| Передеплой cloud-сервера | Actions → **deploy-build** (`gh workflow run deploy-build.yml`). Собирает из исходников на VPS под non-root `mcpdeploy`; см. `docs/DEPLOY.md`. С релизами плагинов не связано |
| Пуш Docker-образа | Пушить нечего — cloud-деплой собирает образ **из исходников на хосте**. Образа в GHCR, который надо публиковать или пуллить, нет |
| Bump gitlink в `mcp-fleet` | Задача суперпроекта: `git add staticbit-xrpl-mcp && git commit && git push` в `mcp-fleet` |
| Force-push | Намеренно не поддерживается. Если push отбился (non-fast-forward) — разбирайся руками: `git pull --rebase`, затем передиспатчить |

## После релиза

1. **Деплой cloud**, если затронут серверный код — Actions → `deploy-build`. Релизный workflow не деплоит.
2. **Bump gitlink** в суперпроекте `mcp-fleet`, чтобы он указывал на релизный коммит.
3. **Сообщить пользователям**, что обновление доступно. Они выполняют:
   ```
   /plugin marketplace update staticbit-xrpl-mcp
   /plugin update xrpl-signer@staticbit-xrpl-mcp
   ```
   Форма `<plugin>@<marketplace>` обязательна — короткий
   `claude plugin update xrpl-signer` падает с `Plugin not found`
   в текущем Claude Code CLI.

Сам GitHub Release (notes из CHANGELOG, тарболы, `.sha256`, SBOM, attestation) создаёт workflow — руками делать нечего.

## Флаги скрипта (полный список)

```
./release-plugin.sh --help
```

Полезные:

| Флаг | Назначение |
|---|---|
| `--no-build` | Пропустить пересборку (фикс только доков / манифеста) — вход workflow `no_build` |
| `--build-only` | Только сборка + копирование, без bump/commit/tag — локальная проверка, аналога в workflow нет |
| `--push` | После всех коммитов сделать fast-forward push обоих репо — **только для CI; см. предупреждение в TL;DR** |
| `--version X.Y.Z` | Явная версия вместо semver-bump (например, pre-release) |
| `--dry-run` | Показать, что произойдёт, ничего не меняя — вход workflow `dry_run` |

## Troubleshooting

| Симптом | Причина | Решение |
|---|---|---|
| Тег есть, а GitHub Release / SBOM / attestation нет | Релиз сделан локальным `release-plugin.sh --push` вместо диспатча workflow | `git revert` релизного коммита (он трогает только `bin/`, манифесты и CHANGELOG — код и прод не затронуты), `git push origin --delete <tag>`, затем диспатчить workflow. Он пересоздаст ту же версию начисто; force-push не нужен |
| `Repo … has uncommitted changes` | Скрипт требует чистых репо | `git status` + закоммить или stash |
| `Plugin … not found in marketplace.json` | Имя плагина не зарегистрировано в marketplace | Проверь `plugins[].name` в `.claude-plugin/marketplace.json` |
| `Artifacts not found at …` | Build-скрипт упал или не запускался | Запусти `bash build-signer-binaries.sh` отдельно и посмотри ошибки |
| `non-fast-forward` при push | Параллельный релизный запуск, либо кто-то запушил раньше | Диспатчь релизы последовательно (сценарий E); `git pull --rebase` → передиспатчить |
| `claude plugin tag` ругается на валидацию | Манифест плагина и запись в marketplace рассинхронизированы | Открой оба и проверь, что `version` в `plugin.json` совпадает с `marketplace.json/plugins[i]` (скрипт делает это автоматически — но ручная правка могла их рассинхронизировать) |

## Расширение на другие плагины

Если в этот marketplace приезжает плагин из **другого** репо-источника (например, `x-mcp-cloud` из `Platonenkov/XMcp`) — нужен такой же `release-plugin.sh` в том репо-источнике. Он будет знать про свои бинари (если они есть) и копировать их в `staticbit-xrpl-mcp/plugins/x-mcp-cloud/`. Логика JSON-хелперов / changelog / commit-tag-push один в один — можно скопировать и подставить свои значения `PLUGIN_KIND`.
