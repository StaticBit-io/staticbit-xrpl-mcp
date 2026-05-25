# RELEASE — выпуск новой версии плагинов

Этот документ для **тебя**, когда нужно опубликовать обновление одного из плагинов в marketplace. Скрипт-оркестратор: [`release-plugin.sh`](release-plugin.sh).

## TL;DR

```bash
cd /e/GIT/XRPL/StaticBitXrplMcp

# 1. Закоммитил код, запушил, проверил тесты — стандартный flow.
git status            # должно быть clean
git push

# 2. Релизишь:
./release-plugin.sh xrpl-signer patch --push
```

Скрипт сам пересоберёт бинари, скопирует их в marketplace, bumpнет версию в `plugin.json` + `marketplace.json`, припишет CHANGELOG из git log, создаст коммит и git tag в marketplace, и сделает fast-forward push.

---

## Когда какой bump

Сейчас все плагины на `0.1.0` — публичных пользователей нет, можно крутить смело. Когда появятся реальные подписчики на ваш marketplace — соблюдай [semver](https://semver.org/lang/ru/):

| Что изменилось | Bump |
|---|---|
| Фикс бага без изменения API (имя tool'а, его параметры, поведение) | `patch` |
| Новый tool, новый опциональный параметр существующего tool'а, новая опц. ENV | `minor` |
| Удалён tool, переименован параметр, изменена семантика (breaking) | `major` |
| Только текст SKILL.md / README — нет изменения в API | `patch` |
| Обновили self-contained .NET бинарь без изменения API | `patch` (или `minor` если performance/storage заметно поменялись) |

Точную версию можно задать вручную через `--version`:
```bash
./release-plugin.sh xrpl-signer --version 1.0.0-rc.1
```

## Какой плагин зависит от какого исходника

| Плагин | Источник кода | При изменении чего bumpаем |
|---|---|---|
| `xrpl-cloud` | манифест плагина + skill + .mcp.json (URL/headers) | только manifest/skill — `--no-build` |
| `xrpl-local` | `src/StaticBit.Xrpl.Mcp.{Abstractions,Core,Server}` | весь server-проект |
| `xrpl-signer` | `src/StaticBit.Xrpl.Mcp.Signer` | только signer-проект (independent) |

Если правишь `StaticBit.Xrpl.Mcp.Core` — затронут только `xrpl-local` (signer не зависит от Core). Если правишь `StaticBit.Xrpl.Mcp.Server` — только `xrpl-local`. Если правишь `StaticBit.Xrpl.Mcp.Signer` — только `xrpl-signer`. `xrpl-cloud` зависит только от URL endpoint'а и текста в манифесте.

## Типичные сценарии

### Сценарий A — мелкий фикс в signer-коде

```bash
# Правишь src/StaticBit.Xrpl.Mcp.Signer/..., тестируешь:
dotnet test --filter TestU

# Коммитишь в основной репо как обычно:
git add -A
git commit -m "fix(signer): correct error message on missing wallet"
git push

# Релизишь — скрипт сам всё сделает:
./release-plugin.sh xrpl-signer patch --push
```

### Сценарий B — обновил skill / README плагина (без пересборки)

```bash
# Правишь в marketplace репо:
cd /e/GIT/staticbit-plugins
vim plugins/xrpl-cloud/skills/xrpl-cloud-operations/SKILL.md
git add -A
git commit -m "docs(xrpl-cloud): clarify two-phase signing flow in skill"
git push

# Релизишь без билда:
cd /e/GIT/XRPL/StaticBitXrplMcp
./release-plugin.sh xrpl-cloud patch --no-build --push
```

### Сценарий C — большая фича в server-коде, затрагивает cloud + local

```bash
# 1. Закоммитил branch в основной репо, мерджнул в main, запушил.
# 2. ДЕПЛОЙ CLOUD-сервера — отдельная процедура (DEPLOY.md):
ssh root@195.26.227.83 'cd /opt/StaticBitXrplMcp && git pull && docker compose up -d --build'
# Тестируешь cloud endpoint вручную, убеждаешься что работает.

# 3. Релизишь local-плагин с новым self-contained бинарём:
./release-plugin.sh xrpl-local minor --push

# 4. cloud-плагин в большинстве случаев bumpить НЕ надо —
#    это просто HTTP wrapper, новый функционал доступен через тот же URL
#    автоматически. Bumpи если изменился URL/headers в .mcp.json.
```

### Сценарий D — sanity-check без публикации

```bash
# Хочешь убедиться что свежий код собирается и проходит тесты,
# и проверить плагин локально перед релизом:
./release-plugin.sh xrpl-signer --build-only

# Это пересобрало бинари + скопировало в marketplace, но не делало
# version bump / коммит / тэг. У тебя в marketplace появится diff,
# который можно реверснуть (git checkout) или закоммитить как
# отдельный prep-commit перед нормальным релизом.

# Локально проверить через переустановку плагина:
claude plugin marketplace update staticbit-plugins
claude plugin update xrpl-signer
# Перезапустить Claude Code, протестировать вживую.

# Если всё ок — нормальный релиз:
./release-plugin.sh xrpl-signer patch --push
```

### Сценарий E — Multi-plugin release

```bash
# Поменял что-то в Core, и оно затронуло и server, и есть резон
# актуализировать manifest у обоих:
./release-plugin.sh xrpl-local,xrpl-signer minor --push
```
Версии обоих плагинов bumpятся одновременно, бинари каждого собираются, коммит один общий.

## Чего скрипт **не** делает

| Что | Где сделать |
|---|---|
| Передеплоить cloud-сервер на VPS | `ssh root@<vps>` + процедура в `DEPLOY.md` (отдельный шаг — это никак не привязано к плагинам) |
| Запушить Docker-образ в GHCR | Делает GitHub Actions автоматически при push в main основного репо |
| Создать GitHub Release (с release notes UI на GitHub) | Только если хочешь. Скрипт создаёт **git tag** — этого обычно достаточно. Можешь руками потом `gh release create <tag>` |
| Force-push | Намеренно не поддерживается. Если push отклонён (non-fast-forward) — разруливай руками: `git pull --rebase` в нужном репо, затем повтори с `--push` |

## Что лежит на тебе после релиза

1. **Cloud deployment**, если затронут server-код и есть live VPS. Скрипт не SSH'ится сам.
2. **Уведомить пользователей** что обновление доступно (если их больше чем ты сам). Они сделают:
   ```
   /plugin marketplace update staticbit-plugins
   /plugin update xrpl-signer
   ```
3. **GitHub Release с UI-нотами**, если хочешь — `gh release create xrpl-signer--v0.1.1 --notes-from-tag`. CHANGELOG.md плагина — отличная база для нот.

## Опции скрипта (полный список)

```
./release-plugin.sh --help
```

Полезные:

| Флаг | Назначение |
|---|---|
| `--no-build` | Пропустить пересборку бинарей (docs-only / манифест-only фикс) |
| `--build-only` | Только сборка + копирование, без bump/commit/tag |
| `--push` | После всех коммитов сделать fast-forward push в оба репо |
| `--version X.Y.Z` | Точная версия вместо semver bump |
| `--plugins-path P` | Указать нестандартное расположение marketplace репо |
| `--dry-run` | Показать что будет сделано, ничего не менять |

## Troubleshooting

| Симптом | Причина | Решение |
|---|---|---|
| `Repo … has uncommitted changes` | Скрипт требует чистые репо | `git status` + закоммитить или stash |
| `Marketplace path not found` | Marketplace не найден автоматически | `STATICBIT_PLUGINS_PATH=/path/to/staticbit-plugins ./release-plugin.sh ...` или `--plugins-path` |
| `Plugin … not found in marketplace.json` | Имя плагина не зарегистрировано в marketplace | Проверь `plugins[].name` в `.claude-plugin/marketplace.json` |
| `Artifacts not found at …` | Build-скрипт упал или не запускался | Запусти отдельно `bash build-signer-binaries.sh` чтобы видеть ошибки |
| `non-fast-forward` при push | Кто-то (или ты с другого устройства) запушил раньше | `git pull --rebase` в нужном репо → повтори с `--push` |
| `claude plugin tag` ругается на validation | Манифест плагина или marketplace entry рассинхронизированы | Открой и проверь что `version` в `plugin.json` и в `marketplace.json/plugins[i]` совпадают (скрипт делает это автоматически — но может быть ручной правки) |

## Расширение для других плагинов

Если в этом marketplace появится плагин из **другого** source-репо (например `x-mcp-cloud` из репо `Platonenkov/XMcp`) — нужно такой же `release-plugin.sh` в том source-репо. Он будет знать про свои бинари (если есть) и копировать их в `staticbit-plugins/plugins/x-mcp-cloud/`. JSON-helpers / changelog / commit-tag-push логика повторяется один-к-одному — можно скопировать оттуда сюда и подставить свои значения в `PLUGIN_KIND`.
