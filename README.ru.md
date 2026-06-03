> 🇬🇧 [Read in English](README.md)

# staticbit-xrpl-mcp

XRPL toolkit for Claude Code — **исходники + три плагина в одном репо**. Содержит:

- **C# / .NET 10 server** для XRP Ledger (cloud deploy + local stdio в одном бинаре).
- **Offline stdio signer** с encrypted keystore (PBKDF2 + AES-256-GCM).
- **Три плагина** для Claude Code в виде marketplace в этом же репо.

> 📖 **[INSTALL.ru.md](docs/ru/INSTALL.md)** — пошаговая инструкция для конечного пользователя плагинов: от чистой Claude Code до первой подписанной XRPL-транзакции.
> 📖 **[DEPLOY.ru.md](docs/ru/DEPLOY.md)** — для админа, разворачивающего cloud-сервер на VPS.
> 📖 **[OPERATIONS.ru.md](docs/ru/OPERATIONS.md)** — day-two runbook для cloud-сервера: деплой, откат, health, секреты.
> 📖 **[RELEASE.ru.md](RELEASE.ru.md)** — для меня (релизера), как публиковать новые версии плагинов.
> 📖 **[docs/glossary.ru.md](docs/ru/glossary.md)** — XRPL-термины, которые встречаются в описаниях tools.
> 📖 **[docs/supply-chain.ru.md](docs/ru/supply-chain.md)** — что прикладывается к каждому release (SBOM, SLSA, опц. notarization) и как пользователю это верифицировать.
> 📖 **[docs/tools-schema.json](docs/tools-schema.json)** — машинно-читаемый JSON-Schema каталог всех MCP-tools (<!-- toolcount:total -->131<!-- /toolcount:total --> шт), для third-party агентов.
> 📖 **[docs/examples/](docs/ru/examples/)** — рецепты кросс-плагинных Cowork-агентов.
> 📖 **[docs/bilingual-convention.ru.md](docs/ru/bilingual-convention.md)** — соглашение о двуязычной документации (зеркало `docs/ru/` + суффикс `.ru.md` вне `docs/`).

## Плагины marketplace

| Plugin | What it does | Size |
|---|---|---|
| [`xrpl-cloud`](plugins/xrpl-cloud/) | HTTP MCP to the StaticBit cloud server at `xrpl.mcp.staticbit.ai`. Read / prepare / submit через OAuth 2.1-authed HTTPS. | ~10 KB |
| [`xrpl-local`](plugins/xrpl-local/) | Local stdio MCP — те же <!-- toolcount:xrpl -->116<!-- /toolcount:xrpl --> tools, но полностью на твоей машине, WebSocket напрямую к публичным XRPL нодам. | ~260 MB (5 RIDs) |
| [`xrpl-signer`](plugins/xrpl-signer/) | Offline stdio MCP для управления кошельками и подписания — encrypted keystore, zero network code. Парится с cloud либо local. | ~200 MB (5 RIDs) |

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

Signer одинаков в обоих flow — он чистая криптография, ничего не знает о prepare/submit стороне.

## Install (для пользователей плагина)

```
/plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
```

Marketplace приватный; Claude Code запросит GitHub PAT (read access) при первом `marketplace add`.

Дальше — какие плагины ставить:

```
# Cloud + signer (легковесный, зависит от StaticBit VPS):
/plugin install xrpl-cloud@staticbit-xrpl-mcp
/plugin install xrpl-signer@staticbit-xrpl-mcp

# Local + signer (offline-first, без cloud dependency):
/plugin install xrpl-local@staticbit-xrpl-mcp
/plugin install xrpl-signer@staticbit-xrpl-mcp

# Read-only через cloud (без подписи — Cowork-агенты, дашборды):
/plugin install xrpl-cloud@staticbit-xrpl-mcp
```

Подробная инструкция со всеми ENV-переменными — в [INSTALL.ru.md](docs/ru/INSTALL.md).

## Структура репо

```
staticbit-xrpl-mcp/
├── .claude-plugin/marketplace.json        ← реестр плагинов
├── .github/workflows/                     ← CI: docker, dotnet-test, release-plugin
├── plugins/
│   ├── xrpl-cloud/      (manifest + skill, без бинарей)
│   ├── xrpl-local/      (+ bin/<rid>/ для 5 RIDs)
│   └── xrpl-signer/     (+ bin/<rid>/ для 5 RIDs)
├── src/
│   ├── StaticBit.Xrpl.Mcp.Abstractions/   ← shared models
│   ├── StaticBit.Xrpl.Mcp.Core/           ← <!-- toolcount:xrpl -->116<!-- /toolcount:xrpl --> read/prepare/submit tools
│   ├── StaticBit.Xrpl.Mcp.Server/         ← HTTP+stdio host
│   └── StaticBit.Xrpl.Mcp.Signer/         ← offline signer
├── tests/                                  ← Core + Server + Signer unit tests
├── Dockerfile, docker-compose.yml          ← cloud deploy
├── build-server-binaries.sh                ← publish server для 5 RIDs
├── build-signer-binaries.sh                ← publish signer для 5 RIDs
├── release-plugin.sh                       ← релизер
├── INSTALL.md, DEPLOY.md, RELEASE.md
└── StaticBitXrplMcp.sln, Directory.Build.props
```

## Разработка

```bash
# Сборка + тесты
dotnet restore
dotnet build
dotnet test --filter TestU

# Локальный запуск cloud-сервера в stdio для отладки
dotnet run --project src/StaticBit.Xrpl.Mcp.Server -- --transport stdio

# Локальный запуск HTTP-сервера
dotnet run --project src/StaticBit.Xrpl.Mcp.Server -- --transport http --urls http://localhost:5500

# Self-contained publish для одного плагина (или всех)
bash build-signer-binaries.sh           # 5 RIDs
bash build-server-binaries.sh win-x64   # одна платформа

# Release плагина (см. RELEASE.md)
./release-plugin.sh xrpl-signer patch --push

# Регенерация docs/tools-schema.json (после добавления/изменения [McpServerTool])
dotnet run --project tools/SchemaGen -- docs/tools-schema.json
```

### Конвенция тестов

Суффикс `U` = **Unit** (test). Применяется в двух местах:

- **Файлы** — `*TestsU.cs` (например `CurrencyParserTestsU.cs`).
- **Имена тест-методов** — префикс `TestU_` (например `TestU_Parse_Empty_Throws`).

Это позволяет одной командой запустить только быстрые юнит-тесты:

```bash
dotnet test --filter TestU
```

`Directory.Build.props` задаёт `TargetFramework=net10.0` по умолчанию; единственное исключение — `StaticBit.Xrpl.Mcp.Abstractions`, которая пинится на `netstandard2.1` чтобы её можно было встраивать в более старые хосты (Staticbit Wallet и т.п.).

Интеграционные тесты против реального rippled testnet (когда появятся) будут жить как `*TestsI.cs` / `TestI_*` и крутиться отдельно (по расписанию в CI, не на каждый PR).

## История

Этот репо объединяет то, что раньше жило в двух приватных репо:
- `StaticBit-io/StaticBitXrplMcp` (server source, signer source, tests, Docker, cloud deploy)
- `StaticBit-io/staticbit-plugins` (marketplace, plugin manifests, бинари)

С момента миграции код и marketplace живут atomic'но: PR может одновременно править `src/` и `plugins/<name>/bin/`. CI авто-релиза работает в одном workflow. Старые репо архивированы для истории.
