> 🇷🇺 [Прочесть на русском](monitor-balance-telegram.ru.md)

# Example: Cowork agent "monitor balance, notify in Telegram"

A cross-plugin recipe. The agent runs in the background, polls for new transactions on an address, and posts notifications to Telegram.

## What is used

| Plugin | What the recipe needs |
|---|---|
| **xrpl-cloud** (or `xrpl-local`) | `xrpl_account_tx_since` for polling, `xrpl_tx_explain` for formatting |
| **telegram** (separate plugin) | `mcp__telegram-cloud__send_message` for notifications |

`xrpl-signer` is not needed — read-only.

## Architecture

No subscribe / WebSocket inside MCP — the protocol has no bidirectional channel (see [features.md §2](../features.md#2-read-api-and-streaming)). We use **polling** through `xrpl_account_tx_since`: the caller keeps the max `ledger_index` from the previous response and passes it as `sinceLedger` on the next iteration. Idempotent, stateless, works in any deployment mode.

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

## Agent prompt

Save into `~/.claude/agents/xrpl-watch.md` or `.claude/agents/xrpl-watch.md` of your project:

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

## Running via `/schedule` (recommended)

`/schedule` manages cron jobs for a remote agent. Survives Claude Code restarts and works even when the CLI is closed.

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

> The first invocation with `since_ledger=0` will pull the entire history. For an active address, do the initial bootstrap manually with `since_ledger=<current ledger from xrpl_ledger>` minus 1 so the first iteration is empty.

The scheduler feeds the stdout of the previous iteration into the prompt of the next one via template variables. Exact syntax — see `/schedule help`.

## Running via `/loop` (for debugging)

`/loop` runs the job in the current session — handy for tuning parameters. Does not survive restarts.

```
/loop 5m xrpl-watch account=r... network=mainnet since_ledger=0 telegram_chat_id=...
```

## Extensions

- **Multiple addresses** — make the `account` parameter an array; the agent walks each separately (per-address `since_ledger` — store in a file via the bash tool, or parse from prompt as JSON).
- **Filter by type** — skip tx when `TransactionType` is not in the whitelist. Add `types_filter=Payment,TrustSet` to the prompt.
- **Incoming only** — skip when `Account == watched_account` (those are outgoing).
- **Amount threshold** — for Payment, parse `Amount` (drops for XRP, object for tokens) and compare against a threshold; send only the big ones.
- **Auto-respond scenario** — add `xrpl-signer` + auto-reply Payment to incoming (e.g. for a refund bot). **Never do this without user-approval on every outgoing tx** — `RequiresUserApproval=true` in `PreparedTransaction` exists exactly for this.

## Verification

Before prod deployment, verify:

1. **Idempotency** — run the agent twice in a row with the same `since_ledger`. The second run must not send anything new. If it does — a bug in max-ledger extraction.
2. **Lag** — `xrpl_account_tx_since` returns `validated` ledgers only; between the on-chain event and the notification you may see `interval + ~10 seconds` of consensus delay.
3. **Telegram rate limit** — bot API: 30 messages/sec across chats, 1/sec to a single chat. For large histories, add throttling.

## Cost

- **xrpl-cloud bearer**: one invocation ≈ one request to the cloud server (cheap). For a 5-minute interval: 12/hour, 288/day. Default rate limit 60/min is not impacted.
- **rippled load** — `account_tx` paginates by indexes on the validated-only ledger range. No node strain.
- **Telegram** — bot API is free.
