>  🌐 **Язык**: [English](README.md) | **Русский**

# xrpl-local plugin

Локальный stdio MCP с тем же набором из 21 XRPL-tool, что и `xrpl-cloud`, но запущенный **полностью у тебя на машине**. WebSocket к публичным XRPL нодам (`xrplcluster.com`, `s.altnet.rippletest.net` и т.д.) идёт из твоего процесса напрямую — никакого посредника.

## Когда выбирать этот плагин

- **Privacy-sensitive** — не хочешь чтобы админ cloud-сервера видел traffic к XRPL нодам.
- **No-server-dependency** — наш VPS упал? Local продолжает работать.
- **Кастомные ноды** — хочешь ходить в свой rippled, в Hooks testnet v3, в Sidechain — задаёшь URL через ENV.
- **Air-gapped (almost)** — нужны только публичные XRPL-ноды; никакой централизованной точки в нашей инфраструктуре.

Если тебе нужны Cowork-агенты, мобильный доступ или ты не хочешь скачивать ~110 MB бинарь — смотри `xrpl-cloud`.

## Установка

```
/plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
/plugin install xrpl-local@staticbit-xrpl-mcp
```

Без bearer-токенов, без cloud-зависимости. Просто работает.

### Опциональная конфигурация через ENV

| Переменная | Default | Что |
|---|---|---|
| `XRPL_LOCAL_DEFAULT_NETWORK` | `mainnet` | сеть когда caller не указал |
| `XRPL_LOCAL_MAINNET_URL` | `wss://xrplcluster.com` | mainnet WS endpoint |
| `XRPL_LOCAL_TESTNET_URL` | `wss://s.altnet.rippletest.net:51233` | testnet WS endpoint |
| `XRPL_LOCAL_DEVNET_URL` | `wss://s.devnet.rippletest.net:51233` | devnet WS endpoint |
| `XRPL_LOCAL_REQUEST_TIMEOUT` | `30` | таймаут одного rippled-запроса (сек) |

Например, переключить mainnet на Ripple-провайдера:

```powershell
[Environment]::SetEnvironmentVariable("XRPL_LOCAL_MAINNET_URL", "wss://s1.ripple.com", "User")
```

После изменения — рестарт Claude Code.

## Проверка

```
/mcp
```
```
xrpl-local  node bin/server.js  ✓ Connected
```

Tools зарегистрированы как `mcp__plugin_xrpl-local_xrpl-local__*` — то же имя `xrpl_*`, просто другой префикс. Если у тебя одновременно установлены `xrpl-cloud` и `xrpl-local`, агент видит оба набора и может вызвать любой; различение через namespace плагинов.

## Подписание транзакций

Этот плагин **не** имеет ключей. Чтобы подписывать — рядом ставь `xrpl-signer`:

```
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

Local делает `prepare` → signer (offline, локально) делает `sign` → local делает `submit_signed`. Никакого внешнего сервиса в цепочке.

## Платформы

В плагине лежат self-contained .NET бинарники для:
- `win-x64` (~111 MB)
- `linux-x64` (~108 MB)
- `linux-arm64` (~119 MB)
- `osx-x64` (~108 MB)
- `osx-arm64` (~118 MB)

Node.js launcher `bin/server.js` выбирает нужный по `os.platform()/os.arch()`.

## Безопасность

- Никакой сетевой коммуникации с нашим VPS — Claude Code запускает локальный subprocess.
- WebSocket идёт только к публичным XRPL нодам, чьи URL ты контролируешь через ENV.
- Никаких ключей — write-tools принимают только подписанный blob (тот же контракт что у cloud).
