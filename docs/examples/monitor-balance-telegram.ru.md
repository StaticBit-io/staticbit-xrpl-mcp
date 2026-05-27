> 🇬🇧 [Read in English](monitor-balance-telegram.md)

# Пример: Cowork-агент «monitor balance, notify in Telegram»

Кросс-плагинный рецепт. Агент крутится в фоне, polling-ом проверяет новые транзакции на адрес и шлёт уведомления в Telegram.

## Что используется

| Плагин | Что нужно для рецепта |
|---|---|
| **xrpl-cloud** (или `xrpl-local`) | `xrpl_account_tx_since` для polling, `xrpl_tx_explain` для форматирования |
| **telegram** (отдельный плагин) | `mcp__telegram-cloud__send_message` для уведомлений |

`xrpl-signer` не нужен — только чтение.

## Архитектура

Никаких subscribe / WebSocket в MCP — у протокола нет двунаправленного канала (см. [features.md §2](../features.md#2-read-api-и-streaming)). Используем **polling** через `xrpl_account_tx_since`: caller хранит max `ledger_index` из прошлого ответа и передаёт его как `sinceLedger` на следующей итерации. Идемпотентно, stateless, работает в любом deployment-режиме.

```
┌────────────┐  every N min   ┌────────────────────┐   new txs    ┌──────────────┐
│ scheduler  │ ─────────────► │ xrpl_account_tx_   │ ───────────► │ xrpl_tx_     │
│ (/schedule │                │ since(sinceLedger) │              │ explain      │
│  or /loop) │ ◄─────────────  │                    │              │ (one-liner)  │
└────────────┘  max ledger_idx └────────────────────┘              └──────┬───────┘
                                                                          │
                                                                          ▼
                                                              ┌────────────────────┐
                                                              │ mcp__telegram_*__  │
                                                              │ send_message       │
                                                              └────────────────────┘
```

## Промт агента

Сохрани в `~/.claude/agents/xrpl-watch.md` либо в `.claude/agents/xrpl-watch.md` проекта:

```markdown
---
name: xrpl-watch
description: Polls XRPL account for new transactions and posts a human-readable
  summary to Telegram. Stateless between invocations — receives `since_ledger`
  in the prompt, returns the new max `ledger_index` in the last line for the
  scheduler to feed back.
tools:
  - mcp__xrpl-cloud__xrpl_account_tx_since
  - mcp__xrpl-cloud__xrpl_tx_explain
  - mcp__telegram-cloud__send_message
---

You are a passive XRPL watcher. Each invocation:

1. Parse the prompt for these inputs:
   - `account` — the r-address to watch
   - `network` — usually `mainnet`
   - `since_ledger` — integer, last ledger index already reported (0 on first run)
   - `telegram_chat_id`

2. Call `xrpl_account_tx_since(network, account, sinceLedger=since_ledger,
   forward=true, limit=50)`.

3. For each new transaction (skip those with `ledger_index <= since_ledger`):
   - Pass `tx.tx_json` to `xrpl_tx_explain` to get the one-line summary.
   - Compose a Telegram message:
     `<emoji> <summary> — https://livenet.xrpl.org/transactions/<hash>`
     Use 💰 for incoming Payment to the watched account, 💸 for outgoing,
     🔧 for everything else.
   - Send via `send_message(chat_id=telegram_chat_id, text=...)`.

4. Compute `new_max_ledger = max(ledger_index of all returned txs, since_ledger)`.

5. **Output exactly one line as the final response**:
   `NEW_SINCE_LEDGER=<new_max_ledger>`

   Nothing else. The scheduler parses this to feed back next time.

If `xrpl_account_tx_since` returns an empty list, just output
`NEW_SINCE_LEDGER=<since_ledger>` (unchanged).
```

## Запуск через `/schedule` (рекомендуется)

`/schedule` — управляет cron-задачами remote-агента. Выживает после рестарта Claude Code, работает даже когда CLI не открыт.

```
/schedule create
  name: xrpl-watch-rN7n
  cron: */5 * * * *
  agent: xrpl-watch
  prompt: |
    account=rN7n7otQDd6FczFgLdSqtcsAUxDkw6fzRH
    network=mainnet
    since_ledger=0
    telegram_chat_id=123456789
```

> Первый запуск с `since_ledger=0` поднимет всю историю. Если адрес активный — лучше начальный bootstrap сделать руками с `since_ledger=<current ledger from xrpl_ledger>` минус 1, чтобы первая итерация была пустой.

Scheduler сам прокидывает stdout последней итерации в prompt следующей через template-переменные. Точный синтаксис — в `/schedule help`.

## Запуск через `/loop` (для отладки)

`/loop` крутит задачу в текущей сессии — удобно для подбора параметров. Не выживает рестарт.

```
/loop 5m xrpl-watch account=r... network=mainnet since_ledger=0 telegram_chat_id=...
```

## Расширения

- **Несколько адресов** — в prompt'е переданный `account` сделай массивом, агент пройдёт по каждому отдельно (отдельный `since_ledger` на адрес — сохраняй в файле через bash-tool, либо распарси из prompt'а как JSON).
- **Фильтрация по типу** — пропускай tx если `TransactionType` не входит в whitelist. В prompt'е добавь `types_filter=Payment,TrustSet`.
- **Только incoming** — пропускай если `Account == watched_account` (это outgoing).
- **Threshold по сумме** — для Payment'ов парсь `Amount` (drops для XRP, объект для token'ов), сравнивай с порогом, шли только большие.
- **Сценарий с подписью** — добавь `xrpl-signer` + автоматический ответ-payment'ом на incoming (например, для refund-бота). **Не делай это без user-approval на каждую исходящую tx** — `RequiresUserApproval=true` в `PreparedTransaction` существует именно для этого.

## Verification

Перед prod-deployment проверь:

1. **Idempotency** — запусти агента два раза подряд с одним `since_ledger`. Второй раз не должен отправить ничего нового. Если шлёт — баг в извлечении max ledger.
2. **Lag** — `xrpl_account_tx_since` возвращает `validated` ledger'ы, между прошедшим on-chain событием и notification может пройти interval + ~10 секунд на consensus.
3. **Telegram rate limit** — bot API: 30 messages/sec в разные chat'ы, 1/sec в один chat. При больших историях добавь throttle.

## Затраты

- **xrpl-cloud bearer**: одна invocation = ~1 запрос к cloud-серверу (cheap). Для 5-минутного интервала: 12/час, 288/день. Default rate limit 60/min не задевает.
- **rippled load** — `account_tx` paginates по indexes, на validated-only ledger range. Не нагружает ноду.
- **Telegram** — bot API бесплатен.
