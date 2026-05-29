# Эксплуатация — cloud-сервер StaticBitXrplMcp

>  🌐 **Язык**: [English](../OPERATIONS.md) | **Русский**

Day-two runbook для **xrpl-cloud** сервера (`xrpl-mcp.staticbit.io`). Плагины `xrpl-local` и
`xrpl-signer` — локальные stdio-процессы на машине пользователя, серверной эксплуатации у них нет.
Первичное развёртывание с чистого VPS — [DEPLOY.md](DEPLOY.md).

## Деплой / передеплой

Сборка образа и деплой автоматизированы через общие reusable-workflow в
[`mcp-tooling`](https://github.com/Platonenkov/mcp-tooling):

- **Сборка/публикация**: релиз `xrpl-cloud` (`release-plugin.yml`) собирает и пушит
  `ghcr.io/staticbit-io/staticbit-xrpl-mcp` через reusable `docker-build-push`. Ad-hoc:
  **Actions → docker** (workflow_dispatch, `version`).
- **Деплой**: **Actions → deploy** (`deploy.yml`, `tag` = `latest` или semver). Runner шлёт образ
  по SSH в forced-command `deploy/deploy.sh` на VPS (без логина в GHCR на хосте), который пинит
  `XRPL_MCP_IMAGE` + `XRPL_PULL_POLICY=never` в `/opt/staticbit-xrpl-mcp/.env`, пересоздаёт
  контейнер, после чего runner смоук-тестит `/healthz`.

## Откат

Перезапусти **Actions → deploy** с предыдущим тегом (старый образ доставляется и пинится). Как
fallback на хосте в compose остался блок `build:`: `cd /opt/staticbit-xrpl-mcp && docker compose
up -d --build xrpl-mcp` пересоберёт из исходников.

## Health и логи

```bash
curl -fsS https://xrpl-mcp.staticbit.io/healthz        # ожидаем 200 {"status":"ok"}
docker compose -f /opt/staticbit-xrpl-mcp/docker-compose.yml ps
docker compose -f /opt/staticbit-xrpl-mcp/docker-compose.yml logs --tail=200 xrpl-mcp
```

В Dockerfile задан `HEALTHCHECK` (curl `/healthz`); Traefik также проверяет его со стороны сети.
`docker inspect --format '{{.State.Health.Status}}' xrpl-mcp` показывает health контейнера.

## Конфигурация и секреты

Лежат на VPS под `/opt/staticbit-xrpl-mcp/`, никогда не в git:

- `.env` — `XRPL_MCP_HOST`, `XRPL_MCP_IMAGE`, `XRPL_PULL_POLICY` (последние два ведёт deploy.sh).
- `.env.xrpl-mcp` — секреты сервиса: OAuth (`Server__OAuth__Issuer/Resource/RequiredScope`),
  опциональные кастомные XRPL-эндпоинты, токен/чат admin-алертов. `chmod 600`.

Ротация секрета → правишь `.env.xrpl-mcp` → `docker compose up -d xrpl-mcp`.

## Admin-алерты (опционально)

`Server__AdminAlerts__{Enabled,BotToken,ChatId}` шлёт lifecycle/security-события в Telegram-чат
через отдельного бота. Держи логирование `System.Net.Http.HttpClient` на `Warning`, чтобы токен
бота в URL API не попадал в логи.

## Troubleshooting

| Симптом | Что проверить |
|---------|---------------|
| Контейнер не стартует | `docker compose logs xrpl-mcp`; проверь `.env.xrpl-mcp` и OAuth-конфиг |
| `/healthz` не 200 | health контейнера (`docker inspect`), маршрутизацию Traefik, DNS A-запись для `XRPL_MCP_HOST` |
| 401 на `/mcp` | ожидаемо без токена — клиенты логинятся через `/mcp` (OAuth); проверь, что AS поднят |
| Деплой падает на smoke | новый образ нездоров — откатись на предыдущий тег |
