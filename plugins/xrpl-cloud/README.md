# xrpl-cloud plugin

Лёгкий HTTP MCP-клиент к нашему cloud XRPL-серверу на `xrpl-mcp.staticbit.io`. Никаких бинарей, никаких локальных процессов — Claude Code просто бьёт по HTTPS с твоим bearer-токеном.

## Когда выбирать этот плагин

- **Cowork / 24/7 routines** — серверный агент должен работать без зависимости от твоей машины.
- **Mobile / lightweight** — не хочешь скачивать ~100 MB local-сервера.
- **Multi-device** — один bearer, все твои устройства видят одну XRPL-конфигурацию.

Если ты privacy-sensitive (не хочешь чтобы админ cloud-сервера видел traffic к XRPL нодам), смотри в сторону `xrpl-local`.

## Установка

```
/plugin marketplace add https://github.com/StaticBit-io/staticbit-xrpl-mcp
/plugin install xrpl-cloud@staticbit-xrpl-mcp
```

### Bearer от админа cloud-сервера

Запроси `XRPL_MCP_BEARER` у админа `xrpl-mcp.staticbit.io`. Один токен — один тебе.

```powershell
[Environment]::SetEnvironmentVariable("XRPL_MCP_BEARER", "<bearer-от-админа>", "User")
```

```bash
echo 'export XRPL_MCP_BEARER="<bearer>"' >> ~/.bashrc
source ~/.bashrc
```

После установки ENV — **полный рестарт Claude Code** (`/exit`, потом снова запустить).

## Проверка

```
/mcp
```
```
xrpl-cloud  https://xrpl-mcp.staticbit.io/mcp (HTTP)  ✓ Connected
```

Все 21 tool доступны как `mcp__plugin_xrpl-cloud_xrpl-cloud__*`:
- read: server_info, fee, ledger, tx_lookup, account_{info,lines,tx,offers,objects}, xrp_balance, book_offers, amm_info
- prepare: payment, trustset, offer_create, offer_cancel, amm_deposit, amm_withdraw
- submit/utils: tx_submit_signed, tx_decode_blob, tx_prepare_generic

## Подписание транзакций

Этот плагин **только** проксирует к серверу, **не** имеет ключей. Чтобы реально отправлять транзакции, поставь рядом `xrpl-signer`:

```
/plugin install xrpl-signer@staticbit-xrpl-mcp
```

Cloud делает `prepare` → signer (локально) делает `sign` → cloud делает `submit_signed`. Подробности — в README каждого плагина и в их skills.

## Безопасность

- Cloud-сервер **никогда** не получает seed/private key — все write-tools принимают только подписанный blob.
- Все попытки подобрать bearer летят в Telegram админу VPS.
- HTTPS-only, бэрер передаётся в Authorization header.
