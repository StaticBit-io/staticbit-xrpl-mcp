# Эксплуатация — cloud-сервер StaticBitXrplMcp

>  🌐 **Язык**: [English](../OPERATIONS.md) | **Русский**

Day-two runbook для **xrpl-cloud** сервера (`xrpl.mcp.staticbit.ai`). Плагины `xrpl-local` и
`xrpl-signer` — локальные stdio-процессы на машине пользователя, серверной эксплуатации у них нет.
Первичное развёртывание с чистого VPS — [DEPLOY.md](DEPLOY.md).

## Деплой / передеплой

Деплои выполняются от **непривилегированного** пользователя `mcpdeploy` через
**Actions → deploy-build** (`deploy-build.yml`) — образ собирается из исходников на хосте, ничего
не тянется:

- **Деплой (сборка из исходников)**: runner переносит исходники репозитория на VPS
  (`git archive` стримится через `ssh tar`), реконструирует `.env` (плюс минимальный
  `.env.xrpl-mcp`) из **GitHub Secrets / Variables**, собирает образ на хосте
  (`docker compose up -d --build`, без аутентификации в GHCR) и смоук-тестит `/healthz`.
- **Регистрация**: downstream-джоб `register` пушит `.mcp-registry.json` на сервер авторизации
  (`PUT /api/admin/mcps`, `X-Service-Token`), чтобы AS знал scope/resource для `xrpl`.

Cloud-сервер только OAuth — статического bearer для провижининга нет. Общая платформа Traefik
(wildcard TLS для `*.mcp.staticbit.ai`, сеть `mcp-net`) приходит из репозитория
[**mcp-infra**](https://github.com/StaticBit-io/mcp-infra).

## Откат

Перезапусти **Actions → deploy-build**, указав предыдущий commit/тег — он заново переносит эти
исходники и пересобирает. Вручную на хосте: `cd /opt/staticbit-xrpl-mcp && git checkout <ref> &&
docker compose up -d --build xrpl-mcp`.

## Health и логи

```bash
curl -fsS https://xrpl.mcp.staticbit.ai/healthz        # ожидаем 200 {"status":"ok"}
docker compose -f /opt/staticbit-xrpl-mcp/docker-compose.yml ps
docker compose -f /opt/staticbit-xrpl-mcp/docker-compose.yml logs --tail=200 xrpl-mcp
```

В Dockerfile задан `HEALTHCHECK` (curl `/healthz`); Traefik также проверяет его со стороны сети.
`docker inspect --format '{{.State.Health.Status}}' xrpl-mcp` показывает health контейнера.

## Конфигурация и секреты

Лежат на VPS под `/opt/staticbit-xrpl-mcp/`, никогда не в git:

- `.env` — `XRPL_MCP_HOST` и прочие build/runtime-переменные compose. Воркфлоу `deploy-build`
  реконструирует этот файл из GitHub Secrets / Variables при каждом деплое.
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
| `/healthz` не 200 | health контейнера (`docker inspect`), маршрутизацию Traefik, DNS-запись для `XRPL_MCP_HOST` |
| 401 на `/mcp` | ожидаемо без токена — клиенты логинятся через `/mcp` (OAuth); проверь, что AS поднят |
| Деплой падает на smoke | свежесобранный образ нездоров — проверь build-логи в `deploy-build`, затем откатись на предыдущий commit/тег |
