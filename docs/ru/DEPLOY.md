> 🇬🇧 [Read in English](../DEPLOY.md)

# Развёртывание StaticBitXrplMcp

Этот документ **самодостаточен**. Дай Claude (или любому оператору) ссылку на этот
репозиторий и скажи «разверни по DEPLOY.md» — всё необходимое здесь. Предварительного
знания кодовой базы не требуется.

StaticBitXrplMcp рассчитан на работу за **общей платформой Traefik** на одном VPS. Несколько MCP
сидят за одним Traefik, который занимается TLS, Let's Encrypt и host-based маршрутизацией. Общая
платформа — Traefik плюс **wildcard TLS-сертификат для `*.mcp.staticbit.ai`** и внешняя сеть
`mcp-net` — принадлежит репозиторию [**mcp-infra**](https://github.com/StaticBit-io/mcp-infra), а
не этому. У каждого MCP свой каталог под `/opt/<service-name>/` с собственными docker-compose.yml
и `.env`.

```
/opt/traefik/                ← платформа (из mcp-infra, общая для всех MCP)
  docker-compose.yml
  .env                       ← LETSENCRYPT_EMAIL

/opt/TelegramMCP/            ← один MCP
  docker-compose.yml
  .env, .env.telegram-mcp

/opt/staticbit-xrpl-mcp/       ← другой MCP (этот репозиторий)
  docker-compose.yml
  .env, .env.xrpl-mcp
```

Все сервисы подключаются к **внешней Docker-сети `mcp-net`**. Traefik сканирует её и
подхватывает новые контейнеры автоматически по labels — менять конфиг Traefik при
добавлении нового MCP не нужно.

Образ контейнера **собирается из исходников на хосте** деплой-воркфлоу
(`docker compose up -d --build`) — готового образа для pull нет, и аутентификация в GHCR на хосте
не нужна. Деплой-джоб переносит исходники репозитория на VPS и собирает там.

---

## CI/CD-деплой (быстрый путь)

После первичной настройки хоста (разделы ниже) рутинные деплои **автоматизированы** и работают
от **непривилегированного** пользователя `mcpdeploy` — docker руками запускать не нужно. Запуск
через **Actions → deploy-build** (`deploy-build.yml`):

- **Деплой (сборка из исходников)**: runner переносит исходники репозитория на VPS (`git archive`
  стримится через `ssh tar`), реконструирует `.env` (плюс минимальный `.env.xrpl-mcp`) на хосте из
  **GitHub Secrets / Variables**, затем собирает образ **из этих исходников на хосте**
  (`docker compose up -d --build`, **без аутентификации в GHCR**) и смоук-тестит `/healthz`.
- **Регистрация**: downstream-джоб `register` пушит дескриптор сам/регистрации этого MCP
  (`.mcp-registry.json`) на сервер авторизации (`PUT /api/admin/mcps`, аутентификация заголовком
  `X-Service-Token`), чтобы AS знал scope/resource для `xrpl`.

Cloud-сервер **только OAuth** — статического bearer для провижининга нет. Весь поток работает от
`mcpdeploy` по SSH; нет root forced-command, нет `deploy/deploy.sh`, нет `DEPLOY_*`
секретов доставки образа и нет тарбола `docker save | ssh`. Ручные разделы ниже остаются
референсом первичной настройки и fallback-путём.

> Общая платформа Traefik (wildcard TLS для `*.mcp.staticbit.ai`, сеть `mcp-net`) провижинится
> отдельно из репозитория [**mcp-infra**](https://github.com/StaticBit-io/mcp-infra).

---

## Prerequisites на хосте

Перед развёртыванием этого MCP:

| Нужно | Кто отвечает | Как проверить |
|---|---|---|
| Ubuntu 22.04+ / Debian 12+ с Docker engine ≥ 24 | админ хоста | `docker version` |
| Внешняя Docker-сеть `mcp-net` существует | админ хоста | `docker network ls \| grep mcp-net` |
| Платформа Traefik запущена в `/opt/traefik/` (из **mcp-infra**, wildcard TLS для `*.mcp.staticbit.ai`) | админ хоста | `docker ps \| grep traefik` |
| DNS-запись `xrpl.mcp.staticbit.ai` → IP хоста распространилась | владелец DNS | `dig +short xrpl.mcp.staticbit.ai @1.1.1.1` |
| Docker build-тулчейн на хосте (образ собирается из исходников, не тянется) | админ хоста | `docker buildx version` |

Если чего-то из первых трёх не хватает — выполни `TelegramMCP/OPERATIONS.md` разделы 3–5,
чтобы один раз настроить хост и платформу Traefik. Эти шаги общие и здесь не повторяются.

---

## Шаг 1 — выложить код на сервер

```bash
ssh root@<HOST_IP>
mkdir -p /opt/staticbit-xrpl-mcp
cd /opt/staticbit-xrpl-mcp

# Вариант A — публичный clone (анонимно)
git clone https://github.com/StaticBit-io/staticbit-xrpl-mcp.git .

# Вариант B — приватный репозиторий через PAT
git clone https://<GITHUB_USER>:<PAT>@github.com/StaticBit-io/staticbit-xrpl-mcp.git .

# Вариант C — нет git-доступа, push с локальной рабочей станции
# (запускать на своей dev-машине, НЕ на сервере)
#   tar -cz --exclude=bin --exclude=obj --exclude=.git --exclude=publish \
#     --exclude='.env' --exclude='.env.xrpl-mcp' -f - . \
#     | ssh root@<HOST_IP> 'mkdir -p /opt/staticbit-xrpl-mcp && tar -xz -C /opt/staticbit-xrpl-mcp'
```

---

## Шаг 2 — подготовить OAuth-сервер авторизации

MCP-сервер больше не выпускает и не хранит per-consumer bearer-токены. Вместо этого он —
**OAuth 2.1 resource server**: валидирует короткоживущие RS256 JWT (забирая ключи подписи из
JWKS сервера авторизации) и гейтит `/mcp` по scope `xrpl`. Здесь нет секретов для генерации и
нет списка токенов для раздачи.

Что нужно перед продолжением:

- Доступный **сервер авторизации** (деплой StaticBit использует `https://auth.mcp.staticbit.ai`).
  Он должен публиковать JWKS-эндпоинт, поддерживать dynamic client registration и вести
  **allow-list** пользователей — только аккаунты из allow-list могут получить токен, а
  отключение аккаунта немедленно отзывает его refresh-токены.
- Его URL **issuer**, идентификатор **resource** для этого MCP
  (`https://xrpl.mcp.staticbit.ai/mcp`) и требуемый **scope** (`xrpl`).

Поднятие самого сервера авторизации — вне рамок этого документа; направь MCP на уже
существующий. Эти три значения ты пропишешь в конфиг сервиса на Шаге 3.

---

## Шаг 3 — сконфигурировать сервис

```bash
cd /opt/staticbit-xrpl-mcp
cp .env.example          .env
cp .env.xrpl-mcp.example .env.xrpl-mcp
```

Отредактируй `.env`:
```
XRPL_MCP_HOST=xrpl.mcp.staticbit.ai
```

Отредактируй `.env.xrpl-mcp` — направь сервер на свой сервер авторизации (значения из Шага 2):
```
Server__OAuth__Issuer=https://auth.mcp.staticbit.ai
Server__OAuth__Resource=https://xrpl.mcp.staticbit.ai/mcp
Server__OAuth__RequiredScope=xrpl
```

Сервер забирает JWKS из well-known метаданных issuer при старте и по интервалу обновления —
ключей подписи в этом файле нет. Доступ управляется на allow-list сервера авторизации, не здесь.

Закрой файлы:
```bash
chmod 600 .env .env.xrpl-mcp
```

---

## Шаг 4 — поднять контейнер

Собери образ из исходников, которые ты только что выложил на хост, и запусти — доступ к GHCR и
`docker login` не нужны:

```bash
docker compose up -d --build
```

Это та же команда, что выполняет на хосте воркфлоу `deploy-build`. Смотри, как он поднимается:
```bash
docker compose logs -f xrpl-mcp
```

При старте ты должен увидеть:
```
StaticBitXrplMcp HTTP listening on port 5500, RequireHttps=True, RateLimit=60/min, OAuth=https://auth.mcp.staticbit.ai (scope=xrpl)
```

Строка `OAuth=<issuer> (scope=...)` подтверждает, что сервер разрешил метаданные сервера
авторизации и загрузил его JWKS.

Traefik подхватывает новый контейнер по его labels и автоматически запускает Let's Encrypt
HTTP-01 challenge:
```bash
cd /opt/traefik
docker compose logs traefik 2>&1 | grep "xrpl.mcp.staticbit.ai" | tail -3
# Ищи: INF Server responded with a certificate ...  domain=xrpl.mcp.staticbit.ai
```

Обычно через 10–60 секунд после `up -d`.

---

## Шаг 5 — health-проверки

### 5.1 — без токена → 401

```bash
curl -sS -o /dev/null -w "HTTP %{http_code}\n" \
  -X POST https://xrpl.mcp.staticbit.ai/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
# Ожидается: HTTP 401 (плюс заголовок WWW-Authenticate, указывающий на resource metadata)
```

### 5.2 — невалидный токен → 401

```bash
curl -sS -o /dev/null -w "HTTP %{http_code}\n" \
  -X POST https://xrpl.mcp.staticbit.ai/mcp \
  -H "Authorization: Bearer not-a-real-jwt" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
# Ожидается: HTTP 401 (валидация подписи/issuer не проходит)
```

### 5.3 — валидный access token + initialize → 200 + SSE

Сначала получи реальный access token со scope `xrpl` от сервера авторизации (например, залогинься
один раз через `/mcp` в Claude Code и скопируй сохранённый bearer, или используй token-эндпоинт
своего AS). Затем:

```bash
TOKEN=<XRPL_SCOPED_ACCESS_TOKEN>
curl -sS -X POST https://xrpl.mcp.staticbit.ai/mcp \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"health","version":"0.0.1"}}}'
# Ожидается: event: message + serverInfo StaticBit.Xrpl.Mcp.Server 0.1.0.0
```

### 5.4 — реальный XRPL-вызов через развёрнутый сервер

```bash
URL=https://xrpl.mcp.staticbit.ai/mcp
HDR=$(mktemp)

# Шаг 1: initialize, захватить session id
curl -sS -D "$HDR" -o /dev/null -X POST "$URL" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"health","version":"0.0.1"}}}'
SESSION=$(grep -i '^mcp-session-id' "$HDR" | awk '{print $2}' | tr -d '\r\n')

# Шаг 2: notifications/initialized
curl -sS -o /dev/null -X POST "$URL" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Mcp-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","method":"notifications/initialized"}'

# Шаг 3: server_info на mainnet
curl -sS -X POST "$URL" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Mcp-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"xrpl_server_info","arguments":{"network":"mainnet"}}}'
# Ожидается: event: message с текущими build_version, validated_ledger.seq, и т.д.
```

---

## Шаг 6 — регистрация в клиентах (Claude Code / Cursor / Claude Desktop)

Развёрнутый URL — `https://xrpl.mcp.staticbit.ai/mcp`. Нет per-consumer bearer-токенов для раздачи
и нет заголовков `Authorization` для зашивания в конфиги. Каждый потребитель вместо этого
**логинится через OAuth** при первом подключении — клиент выполняет dynamic client registration на
сервере авторизации, пользователь входит в браузере, и клиент сохраняет и авто-обновляет
полученный токен. Аккаунт потребителя должен быть в allow-list сервера авторизации (Шаг 2), иначе
вход отклоняется.

> Предпочтительно ставить **плагин `xrpl-cloud`**, а не регистрировать сервер вручную — его
> `.mcp.json` уже несёт OAuth-блок, так что потребителю достаточно запустить `/mcp` и залогиниться.
> См. README плагина и INSTALL.md.

### 6.1 Claude Code (user scope — доступно во всех проектах)

```powershell
claude mcp add xrpl-cloud https://xrpl.mcp.staticbit.ai/mcp `
  --scope user `
  --transport http
```

Затем запусти `/mcp`, выбери `xrpl-cloud` и заверши вход в браузере. Проверь:
```powershell
claude mcp list
# xrpl-cloud   https://xrpl.mcp.staticbit.ai/mcp (HTTP) - ✓ Connected
```

Если показывает `needs login` — заверши `/mcp` flow; если вход отклонён — аккаунт не в allow-list.

### 6.2 Claude Code (project scope — `.mcp.json` в git)

Конфиг безопасен для коммита by design — в нём нет секрета, только `oauth`-блок. Каждый
разработчик логинится своей браузерной сессией; доступ — per-account на сервере авторизации.

`.mcp.json` (коммитится в репозиторий):
```json
{
  "mcpServers": {
    "xrpl-cloud": {
      "type": "http",
      "url": "https://xrpl.mcp.staticbit.ai/mcp",
      "oauth": {}
    }
  }
}
```

### 6.3 Cursor — `.cursor/mcp.json`

```json
{
  "mcpServers": {
    "xrpl-cloud": {
      "url": "https://xrpl.mcp.staticbit.ai/mcp",
      "oauth": {}
    }
  }
}
```
Перезапусти Cursor после редактирования, затем заверши вход в браузере по запросу.

### 6.4 Claude Desktop — `claude_desktop_config.json`

```json
{
  "mcpServers": {
    "xrpl-cloud": {
      "url": "https://xrpl.mcp.staticbit.ai/mcp",
      "oauth": {}
    }
  }
}
```

Перезапусти Claude Desktop после редактирования и заверши вход в браузере.

### 6.5 Отзыв и повторная выдача доступа

Общего секрета для ротации нет. Чтобы отрезать потребителя — отключи его аккаунт в allow-list
сервера авторизации; это немедленно отзывает его refresh-токены на всех устройствах, без правок
конфигов. Чтобы выдать доступ — добавь аккаунт обратно; потребитель снова логинится через `/mcp`.
Потребитель также может сам очистить свой сохранённый токен локально: `/mcp` → выбрать
`xrpl-cloud` → clear authentication.

---

## Шаг 7 — admin-алерты в Telegram (опционально, но рекомендуется)

Когда включено, сервер постит операционные события в **отдельный** Telegram-чат через своего
**выделенного** admin-бота. Используй это, чтобы быстро узнавать о:

- **AuthFailure** — запрос пришёл в `/mcp` без токена, с невалидным/просроченным JWT или без scope `xrpl` (пробы или неверно настроенный клиент)
- **RateLimit** — клиент превысил per-IP лимит (легитимный всплеск или баг-скрипт)
- **StartUp / ShutDown** — каждый перезапуск контейнера (плановый или crash-loop)
- **ToolError** — падения вызовов XRPL-инструментов (будущая итерация; пока не подключено)

Изоляция: admin-бот **НЕ** разделяется ни с чем другим на VPS, а пайплайн алертов живёт целиком в
контейнере StaticBitXrplMcp — без зависимости от TelegramMCP или его бота.

### 7.1 Создать admin-бота

1. [@BotFather](https://t.me/BotFather) → `/newbot` → назови его, напр. `@StaticBitXrplAdminBot`.
   Сохрани токен бота.
2. Создай приватный чат (или используй [@userinfobot](https://t.me/userinfobot) на себе, чтобы
   получить свой персональный `chat_id`).
3. Добавь нового бота в этот чат **админом** (чтобы он мог постить).

### 7.2 Прописать на сервере

```bash
ssh root@<HOST_IP>
cd /opt/staticbit-xrpl-mcp

# Замени на свои реальные токен admin-бота и chat_id:
ADMIN_BOT='123456:YOUR_ADMIN_BOT_TOKEN'
ADMIN_CHAT='-1001234567890'

sed -i 's/^Server__AdminAlerts__Enabled=false$/Server__AdminAlerts__Enabled=true/' .env.xrpl-mcp
sed -i '/^# Server__AdminAlerts__BotToken/d' .env.xrpl-mcp
sed -i '/^# Server__AdminAlerts__ChatId/d'   .env.xrpl-mcp
cat >> .env.xrpl-mcp <<EOF
Server__AdminAlerts__BotToken=$ADMIN_BOT
Server__AdminAlerts__ChatId=$ADMIN_CHAT
EOF

docker compose up -d        # подхватывает изменения env_file и пересоздаёт контейнер
```

После перезапуска в admin-чате ты должен увидеть:

```
🟢 StartUp
StaticBitXrplMcp server started

• transport: http
• port: 5500
• oauth: https://auth.mcp.staticbit.ai (scope=xrpl)

2026-05-23T...  •  StaticBitXrplMcp admin-alerts
```

### 7.3 Live-тест (1 минута)

С любого внешнего хоста:
```bash
curl -s -o /dev/null -X POST https://xrpl.mcp.staticbit.ai/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

В течение ~3 секунд admin-чат должен получить:

```
🔒 AuthFailure
Missing token from <YOUR_IP>

• ip: <YOUR_IP>
• path: /mcp
• reason: missing
```

Последующие идентичные попытки в пределах 5-минутного окна дедупа **подавляются** — ты увидишь
алерт один раз, а не на каждую пробу.

### 7.4 Троттлинг и контроль шума

| Лимит | По умолчанию | Override |
|---|---|---|
| Окно дедупа — одинаковые kind+tags схлопываются | 5 мин | `Server__AdminAlerts__Throttling__DedupWindowMinutes` |
| Жёсткий cap в минуту | 10 алертов | `Server__AdminAlerts__Throttling__MaxAlertsPerMinute` |
| Размер фонового канала | 1000 | `Server__AdminAlerts__Throttling__QueueCapacity` |

Если срабатывает жёсткий cap, лишние алерты тихо дропаются в Telegram, но попадают в stderr-лог
контейнера:
```
warn: StaticBit.Xrpl.Mcp.Server.Services.AdminAlerter[0]
      AdminAlert dropped due to MaxAlertsPerMinute: kind=AuthFailure key=...
```

Чтобы выключить конкретный тип события без отключения всей фичи:
```
Server__AdminAlerts__Events__AuthFailure=false
```

### 7.5 Отключить

```bash
sed -i 's/^Server__AdminAlerts__Enabled=true$/Server__AdminAlerts__Enabled=false/' .env.xrpl-mcp
docker compose up -d
```

Фоновый воркер `AdminAlerter` вообще не запускается при `Enabled=false` — вместо него
инжектится no-op заглушка `NullAdminAlerter`, без затрат.

---

## Day-two операции

### Логи
```bash
cd /opt/staticbit-xrpl-mcp && docker compose logs -f xrpl-mcp
docker compose logs xrpl-mcp | grep -iE 'auth (failure|success)'   # аудит
```

### Обновление (пересборка из свежих исходников)

Штатный путь — перезапуск **Actions → deploy-build**, который заново переносит исходники и
пересобирает на хосте. Сделать вручную:
```bash
cd /opt/staticbit-xrpl-mcp
git pull --ff-only
docker compose up -d --build
```

### Отзыв или ротация доступа

Серверного bearer для ротации нет. Доступ управляется целиком на **allow-list сервера
авторизации**:

- **Отозвать потребителя** — отключи его аккаунт на сервере авторизации. Его refresh-токены
  инвалидируются немедленно, и каждое устройство падает в `needs login`. Без изменений в
  `.env.xrpl-mcp` и без перезапуска контейнера.
- **Реакция на компрометацию** — то же: отключи аккаунт, затем включи обратно и попроси
  пользователя залогиниться снова через `/mcp`. Access-токены короткоживущие, так что даже уже
  выпущенный истечёт сам в течение минут.

### Добавить нового пользователя

Добавь аккаунт в allow-list сервера авторизации. Новый пользователь затем ставит плагин
`xrpl-cloud` (или регистрирует URL по Шагу 6), запускает `/mcp` и завершает вход в браузере. На
этом сервере ничего не меняется.

### Снос

```bash
cd /opt/staticbit-xrpl-mcp
docker compose down
# Опционально удалить локально собранный образ:
docker image rm staticbit-xrpl-mcp-xrpl-mcp 2>/dev/null || true
rm -rf /opt/staticbit-xrpl-mcp
# Затем удали DNS-запись.
```

Платформа (Traefik) и другие MCP не затрагиваются.

---

## Справочник конфигурации

| Env var | По умолчанию | Назначение |
|---|---|---|
| `Server__Transport` | `http` (в контейнере) | `stdio` или `http` |
| `Server__HttpPort` | `5500` | порт прослушивания внутри контейнера |
| `Server__OAuth__Issuer` | — | URL issuer сервера авторизации (JWKS резолвится из его метаданных; обязательно) |
| `Server__OAuth__Resource` | — | идентификатор resource для этого MCP, напр. `https://xrpl.mcp.staticbit.ai/mcp` (обязательно) |
| `Server__OAuth__RequiredScope` | `xrpl` | scope, который токен обязан нести, чтобы достучаться до `/mcp` |
| `Server__HttpAuth__RequireHttps` | `true` | требовать `X-Forwarded-Proto: https` от прокси |
| `Server__RateLimit__Enabled` | `true` | включить per-IP rate limiter |
| `Server__RateLimit__PermitsPerMinute` | `60` | запросов в минуту на IP |
| `StaticBitXrplMcp__DefaultNetwork` | `mainnet` | используется, когда вызывающий опускает `network` |
| `StaticBitXrplMcp__RequestTimeoutSeconds` | `30` | таймаут запроса к rippled |
| `StaticBitXrplMcp__LastLedgerSequenceOffset` | `20` | добавляется к текущему ledger в `*_prepare` |
| `StaticBitXrplMcp__Networks__mainnet` | `wss://xrplcluster.com` | WS-эндпоинт mainnet |
| `StaticBitXrplMcp__Networks__testnet` | `wss://s.altnet.rippletest.net:51233` | WS-эндпоинт testnet |
| `StaticBitXrplMcp__Networks__devnet`  | `wss://s.devnet.rippletest.net:51233`  | WS-эндпоинт devnet |
| `Logging__LogLevel__Default` | `Information` | уровень логирования |

Определять сети на лету:
```yaml
environment:
  StaticBitXrplMcp__Networks__hooks_v3: wss://hooks-testnet-v3.xrpl-labs.com
```
Вызывающие затем передают `"network": "hooks_v3"`.

---

## Troubleshooting

| Симптом | Вероятная причина | Решение |
|---|---|---|
| `docker compose up` падает: `network mcp-net not found` | Платформа не развёрнута | `docker network create mcp-net && cd /opt/traefik && docker compose up -d` |
| Контейнер стартует и выходит с `OAuth:Issuer is empty` | Сервер авторизации не сконфигурирован | Задай `Server__OAuth__Issuer` / `Resource` в `.env.xrpl-mcp` (Шаг 3) |
| Старт падает при загрузке JWKS / metadata | Issuer недоступен или неверный URL | Проверь `Server__OAuth__Issuer` и что `<issuer>/.well-known/...` доступен из контейнера |
| TLS-сертификат не выписан | DNS не распространился / порт 80 недоступен | `dig +short xrpl.mcp.staticbit.ai @1.1.1.1`; `curl -I http://xrpl.mcp.staticbit.ai/` |
| `401` даже с токеном | Просроченный/невалидный JWT, неверный issuer/audience или нет scope `xrpl` | Перелогинься через `/mcp`; проверь, что `iss`/`aud`/`scope` токена совпадают с конфигом сервера |
| `401` для allow-listed пользователя | Аккаунт отключён на сервере авторизации | Включи аккаунт обратно в allow-list, затем перелогинься |
| `400 HTTPS required` | `RequireHttps=true` и нет заголовка `X-Forwarded-Proto` | Проверь корректность labels Traefik и что запрос приходит по HTTPS |
| `404` на `/` | Ожидаемо — MCP на `/mcp` | Используй путь `/mcp` |
| `429 Too Many Requests` | Достигнут rate limit | Подними `Server__RateLimit__PermitsPerMinute` или подожди минуту |
| Логи Traefik показывают `client version 1.24 is too old` | Traefik ≤ 3.5 против Docker ≥ 29 | Обнови Traefik до v3.6+ в `/opt/traefik/docker-compose.yml` |

Live-инспекция внутри контейнера:
```bash
docker compose exec xrpl-mcp curl -sS http://127.0.0.1:5500/healthz
docker compose logs --tail=200 -f xrpl-mcp
```
