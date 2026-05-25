# Deploying StaticBitXrplMcp

This document is **self-contained**. Hand a Claude (or any operator) a link to
this repository and tell them "deploy according to DEPLOY.md" — everything needed
is here. No prior knowledge of the codebase is assumed.

StaticBitXrplMcp is designed to live behind a **shared Traefik platform** on a
single VPS — the same model as TelegramMCP. Multiple MCPs sit behind one Traefik
that handles TLS, Let's Encrypt and host-based routing. Each MCP owns its own
directory under `/opt/<service-name>/` with its own docker-compose.yml and `.env`s.

```
/opt/traefik/                ← platform (shared by all MCPs)
  docker-compose.yml
  .env                       ← LETSENCRYPT_EMAIL

/opt/TelegramMCP/            ← one MCP
  docker-compose.yml
  .env, .env.telegram-mcp

/opt/staticbit-xrpl-mcp/       ← another MCP (this repo)
  docker-compose.yml
  .env, .env.xrpl-mcp
```

All services join an **external Docker network `mcp-net`**. Traefik scans it and
picks up new containers automatically via labels — no Traefik config changes
are required when adding a new MCP.

The container image is auto-published to
`ghcr.io/staticbit-io/staticbit-xrpl-mcp:latest` on every push to `main`
([`.github/workflows/docker.yml`](.github/workflows/docker.yml)).

---

## Prerequisites on the host

Before deploying this MCP:

| Need | Owner | How to verify |
|---|---|---|
| Ubuntu 22.04+ / Debian 12+ with Docker engine ≥ 24 | host admin | `docker version` |
| External Docker network `mcp-net` exists | host admin | `docker network ls \| grep mcp-net` |
| Traefik platform running at `/opt/traefik/` | host admin | `docker ps \| grep traefik` |
| DNS A-record `xrpl-mcp.staticbit.io` → host IP propagated | DNS owner | `dig +short xrpl-mcp.staticbit.io @1.1.1.1` |
| Optional GHCR pull access (org is private) | repo owner | `docker login ghcr.io` |

If any of the first three are missing, follow `TelegramMCP/OPERATIONS.md`
sections 3–5 to set up the host and the Traefik platform once. Those steps are
generic and not repeated here.

---

## Step 1 — get the code onto the server

```bash
ssh root@<HOST_IP>
mkdir -p /opt/staticbit-xrpl-mcp
cd /opt/staticbit-xrpl-mcp

# Option A — public clone (anonymous)
git clone https://github.com/StaticBit-io/staticbit-xrpl-mcp.git .

# Option B — private repo via PAT
git clone https://<GITHUB_USER>:<PAT>@github.com/StaticBit-io/staticbit-xrpl-mcp.git .

# Option C — no git access, push from local workstation
# (run from your dev box, NOT on the server)
#   tar -cz --exclude=bin --exclude=obj --exclude=.git --exclude=publish \
#     --exclude='.env' --exclude='.env.xrpl-mcp' -f - . \
#     | ssh root@<HOST_IP> 'mkdir -p /opt/staticbit-xrpl-mcp && tar -xz -C /opt/staticbit-xrpl-mcp'
```

---

## Step 2 — generate bearer tokens

One bearer per consumer (you, a teammate, an automated routine). Each at least 32
characters. The Label appears in audit logs so you can see who did what.

```bash
cd /opt/staticbit-xrpl-mcp
OWNER_BEARER=$(openssl rand -base64 48 | tr -d '\n' | tr '/+' '_-')
echo "owner: $OWNER_BEARER"

# More if needed:
ALICE_BEARER=$(openssl rand -base64 48 | tr -d '\n' | tr '/+' '_-')
echo "alice: $ALICE_BEARER"
```

Save these somewhere safe (1Password / Bitwarden / age) and hand the right one
to each consumer. They go on the server in `.env.xrpl-mcp`, and on each client
in their MCP config (see Step 5).

---

## Step 3 — configure the service

```bash
cd /opt/staticbit-xrpl-mcp
cp .env.example          .env
cp .env.xrpl-mcp.example .env.xrpl-mcp
```

Edit `.env`:
```
XRPL_MCP_HOST=xrpl-mcp.staticbit.io
```

Edit `.env.xrpl-mcp` — replace the placeholder token with your generated `$OWNER_BEARER`:
```
Server__HttpAuth__Tokens__0__Token=<OWNER_BEARER>
Server__HttpAuth__Tokens__0__Label=owner

# Add more if needed:
# Server__HttpAuth__Tokens__1__Token=<ALICE_BEARER>
# Server__HttpAuth__Tokens__1__Label=alice
```

Lock down the files:
```bash
chmod 600 .env .env.xrpl-mcp
```

---

## Step 4 — bring up the container

Either pull the prebuilt image from GHCR (faster, recommended for prod):

```bash
# If the GHCR package is private, log in first with a PAT that has read:packages.
echo "$GHCR_PAT" | docker login ghcr.io -u <GITHUB_USERNAME> --password-stdin

docker compose pull
docker compose up -d
```

Or build locally from the source you just cloned (no GHCR access needed):

```bash
docker compose up -d --build
```

Watch it come up:
```bash
docker compose logs -f xrpl-mcp
```

On startup you should see:
```
StaticBitXrplMcp HTTP listening on port 5500, RequireHttps=True, RateLimit=60/min, BearerTokens=1
```

`BearerTokens=N` confirms the server picked up all configured tokens.

Traefik picks up the new container by its labels and starts the Let's Encrypt
HTTP-01 challenge automatically:
```bash
cd /opt/traefik
docker compose logs traefik 2>&1 | grep "xrpl-mcp.staticbit.io" | tail -3
# Look for: INF Server responded with a certificate ...  domain=xrpl-mcp.staticbit.io
```

Usually 10–60 seconds after `up -d`.

---

## Step 5 — health checks

### 5.1 — no bearer → 401

```bash
curl -sS -o /dev/null -w "HTTP %{http_code}\n" \
  -X POST https://xrpl-mcp.staticbit.io/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
# Expected: HTTP 401
```

### 5.2 — wrong bearer → 401

```bash
curl -sS -o /dev/null -w "HTTP %{http_code}\n" \
  -X POST https://xrpl-mcp.staticbit.io/mcp \
  -H "Authorization: Bearer wrong-token-xxxxxxxxxxxxxxxxxxxxxxxxxxxxx" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
# Expected: HTTP 401
```

### 5.3 — correct bearer + initialize → 200 + SSE

```bash
BEARER=$(grep '^Server__HttpAuth__Tokens__0__Token=' /opt/staticbit-xrpl-mcp/.env.xrpl-mcp | cut -d= -f2-)
curl -sS -X POST https://xrpl-mcp.staticbit.io/mcp \
  -H "Authorization: Bearer $BEARER" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"health","version":"0.0.1"}}}'
# Expected: event: message + serverInfo StaticBit.Xrpl.Mcp.Server 0.1.0.0
```

### 5.4 — real XRPL call through the deployed server

```bash
URL=https://xrpl-mcp.staticbit.io/mcp
HDR=$(mktemp)

# Step 1: initialize, capture session id
curl -sS -D "$HDR" -o /dev/null -X POST "$URL" \
  -H "Authorization: Bearer $BEARER" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"health","version":"0.0.1"}}}'
SESSION=$(grep -i '^mcp-session-id' "$HDR" | awk '{print $2}' | tr -d '\r\n')

# Step 2: notifications/initialized
curl -sS -o /dev/null -X POST "$URL" \
  -H "Authorization: Bearer $BEARER" \
  -H "Mcp-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","method":"notifications/initialized"}'

# Step 3: server_info on mainnet
curl -sS -X POST "$URL" \
  -H "Authorization: Bearer $BEARER" \
  -H "Mcp-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"xrpl_server_info","arguments":{"network":"mainnet"}}}'
# Expected: event: message with current build_version, validated_ledger.seq, etc.
```

---

## Step 6 — register in clients (Claude Code / Cursor / Claude Desktop)

The deployed URL is `https://xrpl-mcp.staticbit.io/mcp`. Each consumer registers
this once with their personal bearer token.

### 6.1 Store the bearer in an environment variable (recommended)

Putting the bearer literally into config files (`.mcp.json`, `claude_desktop_config.json`)
is fine for personal user-scope, but breaks the moment you want to commit a
project-scope `.mcp.json` to git. The portable pattern is to keep an ENV
variable `XRPL_MCP_BEARER` and reference it via `${...}` substitution.

**Windows (PowerShell — persistent for the current user):**
```powershell
[Environment]::SetEnvironmentVariable("XRPL_MCP_BEARER", "<YOUR_BEARER>", "User")
# Verify after opening a NEW PowerShell window:
[Environment]::GetEnvironmentVariable("XRPL_MCP_BEARER", "User")
```

**macOS / Linux (bash or zsh):**
```bash
# Append to ~/.bashrc or ~/.zshrc:
echo 'export XRPL_MCP_BEARER="<YOUR_BEARER>"' >> ~/.bashrc
source ~/.bashrc
```

> After setting the ENV variable you MUST restart Claude Code / Cursor /
> Claude Desktop completely — they read environment only at process start, so
> `${XRPL_MCP_BEARER}` substitution only kicks in for a freshly launched client.

If you also use TelegramMCP, set both at once and never mix them up:
```powershell
[Environment]::SetEnvironmentVariable("TELEGRAM_MCP_BEARER", "<TELEGRAM_BEARER>", "User")
[Environment]::SetEnvironmentVariable("TELEGRAM_BOT_TOKEN",  "<BOT_TOKEN>",       "User")
[Environment]::SetEnvironmentVariable("XRPL_MCP_BEARER",     "<XRPL_BEARER>",     "User")
```

### 6.2 Claude Code (user scope — available across all projects)

User-scope keeps the bearer inline in `~/.claude.json` (no ENV substitution
needed — the file is gitignored by Claude Code anyway):

```powershell
claude mcp add xrpl-cloud https://xrpl-mcp.staticbit.io/mcp `
  --scope user `
  --transport http `
  -H "Authorization: Bearer <YOUR_BEARER>"
```

Verify:
```powershell
claude mcp list
# xrpl-cloud   https://xrpl-mcp.staticbit.io/mcp (HTTP) - ✓ Connected
```

### 6.3 Claude Code (project scope — `.mcp.json` in git)

For a project shared between several developers — each puts their own bearer
in their own ENV variable, the config file stays clean and commit-safe:

`.mcp.json` (committed to the repo, no secrets):
```json
{
  "mcpServers": {
    "xrpl-cloud": {
      "type": "http",
      "url": "https://xrpl-mcp.staticbit.io/mcp",
      "headers": {
        "Authorization": "Bearer ${XRPL_MCP_BEARER}"
      }
    }
  }
}
```

If you also have TelegramMCP in the same project, two servers side by side:
```json
{
  "mcpServers": {
    "telegram-cloud": {
      "type": "http",
      "url": "https://telegram-mcp.staticbit.io/mcp",
      "headers": {
        "Authorization":        "Bearer ${TELEGRAM_MCP_BEARER}",
        "X-Telegram-Bot-Token": "${TELEGRAM_BOT_TOKEN}"
      }
    },
    "xrpl-cloud": {
      "type": "http",
      "url": "https://xrpl-mcp.staticbit.io/mcp",
      "headers": {
        "Authorization": "Bearer ${XRPL_MCP_BEARER}"
      }
    }
  }
}
```

### 6.4 Cursor — `.cursor/mcp.json`

```json
{
  "mcpServers": {
    "xrpl-cloud": {
      "url": "https://xrpl-mcp.staticbit.io/mcp",
      "headers": {
        "Authorization": "Bearer ${XRPL_MCP_BEARER}"
      }
    }
  }
}
```
Restart Cursor after editing.

### 6.5 Claude Desktop — `claude_desktop_config.json`

Claude Desktop reads the file on launch. ENV substitution support varies by
version — if `${XRPL_MCP_BEARER}` is not expanded, paste the bearer literally
(user-scope, not committed):

```json
{
  "mcpServers": {
    "xrpl-cloud": {
      "url": "https://xrpl-mcp.staticbit.io/mcp",
      "headers": {
        "Authorization": "Bearer <YOUR_BEARER>"
      }
    }
  }
}
```

Restart Claude Desktop after editing.

### 6.6 Rotating without editing every config

If you rotate the server-side bearer (Step "Rotate a bearer token" below),
all consumers just overwrite their ENV variable once:

```powershell
[Environment]::SetEnvironmentVariable("XRPL_MCP_BEARER", "<NEW_BEARER>", "User")
```
…and restart their client. No `.mcp.json` / `claude_desktop_config.json` /
`.cursor/mcp.json` edits required, because all of them reference `${XRPL_MCP_BEARER}`.

---

## Step 7 — admin alerts in Telegram (optional but recommended)

When enabled, the server posts operational events to a **separate** Telegram chat
through its own **dedicated** admin bot. Use this to learn fast about:

- **AuthFailure** — someone is bruteforcing your bearer (`/mcp` with no/wrong token)
- **RateLimit** — a client tripped the per-IP limit (legit spike or a buggy script)
- **StartUp / ShutDown** — every container restart (planned or crash-loop)
- **ToolError** — XRPL tool calls failing (future iteration; not wired yet)

Isolation: the admin bot is **NOT** shared with anything else on the VPS, and
the alert pipeline lives entirely in the StaticBitXrplMcp container — no
dependency on TelegramMCP or its bot.

### 7.1 Create the admin bot

1. [@BotFather](https://t.me/BotFather) → `/newbot` → name it e.g. `@StaticBitXrplAdminBot`.
   Save the bot token.
2. Create a private chat (or use [@userinfobot](https://t.me/userinfobot) on
   yourself to get your personal `chat_id`).
3. Add the new bot to that chat **as admin** (so it can post).

### 7.2 Wire it up on the server

```bash
ssh root@<HOST_IP>
cd /opt/staticbit-xrpl-mcp

# Replace these with your real admin bot token and chat_id:
ADMIN_BOT='123456:YOUR_ADMIN_BOT_TOKEN'
ADMIN_CHAT='-1001234567890'

sed -i 's/^Server__AdminAlerts__Enabled=false$/Server__AdminAlerts__Enabled=true/' .env.xrpl-mcp
sed -i '/^# Server__AdminAlerts__BotToken/d' .env.xrpl-mcp
sed -i '/^# Server__AdminAlerts__ChatId/d'   .env.xrpl-mcp
cat >> .env.xrpl-mcp <<EOF
Server__AdminAlerts__BotToken=$ADMIN_BOT
Server__AdminAlerts__ChatId=$ADMIN_CHAT
EOF

docker compose up -d        # picks up env_file changes and recreates the container
```

After the restart you should see in the admin chat:

```
🟢 StartUp
StaticBitXrplMcp server started

• transport: http
• port: 5500
• bearerTokens: 1

2026-05-23T...  •  StaticBitXrplMcp admin-alerts
```

### 7.3 Live-test (1 minute)

From any external host:
```bash
curl -s -o /dev/null -X POST https://xrpl-mcp.staticbit.io/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

Within ~3 seconds the admin chat should receive:

```
🔒 AuthFailure
Missing bearer from <YOUR_IP>

• ip: <YOUR_IP>
• path: /mcp
• reason: missing
```

Subsequent identical attempts within the 5-minute dedup window get **suppressed**
— you'll see the alert once, not on every probe.

### 7.4 Throttling and noise control

| Limit | Default | Override |
|---|---|---|
| Dedup window — same kind+tags collapse | 5 min | `Server__AdminAlerts__Throttling__DedupWindowMinutes` |
| Hard cap per minute | 10 alerts | `Server__AdminAlerts__Throttling__MaxAlertsPerMinute` |
| Background channel size | 1000 | `Server__AdminAlerts__Throttling__QueueCapacity` |

If the hard cap kicks in, excess alerts are dropped silently to Telegram but
land in the container's stderr log:
```
warn: StaticBit.Xrpl.Mcp.Server.Services.AdminAlerter[0]
      AdminAlert dropped due to MaxAlertsPerMinute: kind=AuthFailure key=...
```

To turn off a specific event type without disabling the whole feature:
```
Server__AdminAlerts__Events__AuthFailure=false
```

### 7.5 Disable

```bash
sed -i 's/^Server__AdminAlerts__Enabled=true$/Server__AdminAlerts__Enabled=false/' .env.xrpl-mcp
docker compose up -d
```

The `AdminAlerter` background worker is not started at all when `Enabled=false`
— a `NullAdminAlerter` no-op stub is injected instead, zero cost.

---

## Day-two operations

### Logs
```bash
cd /opt/staticbit-xrpl-mcp && docker compose logs -f xrpl-mcp
docker compose logs xrpl-mcp | grep -iE 'auth (failure|success)'   # audit
```

### Update to latest image
```bash
cd /opt/staticbit-xrpl-mcp
docker compose pull
docker compose up -d
```

### Update from local source changes
```bash
cd /opt/staticbit-xrpl-mcp
git pull --ff-only
docker compose up -d --build
```

### Rotate a bearer token

```bash
cd /opt/staticbit-xrpl-mcp
NEW=$(openssl rand -base64 48 | tr -d '\n' | tr '/+' '_-')
# Replace by index (find the right index by Label):
sed -i "s|^Server__HttpAuth__Tokens__0__Token=.*|Server__HttpAuth__Tokens__0__Token=$NEW|" .env.xrpl-mcp
docker compose restart
echo "new owner bearer: $NEW"
```

The old token stops working ~2 seconds after restart. Hand the new one out over
a secure channel.

### Add a new user

```bash
cd /opt/staticbit-xrpl-mcp
NEW=$(openssl rand -base64 48 | tr -d '\n' | tr '/+' '_-')
cat >> .env.xrpl-mcp <<EOF
Server__HttpAuth__Tokens__2__Token=$NEW
Server__HttpAuth__Tokens__2__Label=bob
EOF
docker compose restart
echo "bearer for bob: $NEW"
```
(Use whatever the next free index is — count existing `__N__Token=` lines.)

### Tear down

```bash
cd /opt/staticbit-xrpl-mcp
docker compose down
# Optionally:
docker image rm ghcr.io/staticbit-io/staticbit-xrpl-mcp:latest
rm -rf /opt/staticbit-xrpl-mcp
# Then remove the DNS A-record.
```

The platform (Traefik) and other MCPs are unaffected.

---

## Configuration reference

| Env var | Default | Purpose |
|---|---|---|
| `Server__Transport` | `http` (in container) | `stdio` or `http` |
| `Server__HttpPort` | `5500` | listening port inside the container |
| `Server__HttpAuth__Tokens__N__Token` | — | bearer token (≥32 chars, required) |
| `Server__HttpAuth__Tokens__N__Label` | — | audit label, e.g. `owner`, `alice` |
| `Server__HttpAuth__RequireHttps` | `true` | demand `X-Forwarded-Proto: https` from the proxy |
| `Server__RateLimit__Enabled` | `true` | enable per-IP rate limiter |
| `Server__RateLimit__PermitsPerMinute` | `60` | requests per minute per IP |
| `StaticBitXrplMcp__DefaultNetwork` | `mainnet` | used when caller omits `network` |
| `StaticBitXrplMcp__RequestTimeoutSeconds` | `30` | rippled request timeout |
| `StaticBitXrplMcp__LastLedgerSequenceOffset` | `20` | added to current ledger in `*_prepare` |
| `StaticBitXrplMcp__Networks__mainnet` | `wss://xrplcluster.com` | mainnet WS endpoint |
| `StaticBitXrplMcp__Networks__testnet` | `wss://s.altnet.rippletest.net:51233` | testnet WS endpoint |
| `StaticBitXrplMcp__Networks__devnet`  | `wss://s.devnet.rippletest.net:51233`  | devnet WS endpoint |
| `Logging__LogLevel__Default` | `Information` | log verbosity |

Define ad-hoc networks dynamically:
```yaml
environment:
  StaticBitXrplMcp__Networks__hooks_v3: wss://hooks-testnet-v3.xrpl-labs.com
```
Callers then pass `"network": "hooks_v3"`.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `docker compose up` fails: `network mcp-net not found` | Platform not deployed | `docker network create mcp-net && cd /opt/traefik && docker compose up -d` |
| Container starts then exits with `HttpAuth:Tokens is empty` | No bearer configured | Add at least one `Tokens__0__Token` to `.env.xrpl-mcp` |
| Container exits with `... is shorter than 32 characters` | Bearer too short | Regenerate: `openssl rand -base64 48 \| tr '/+' '_-'` |
| TLS cert not issued | DNS not propagated / port 80 not reachable | `dig +short xrpl-mcp.staticbit.io @1.1.1.1`; `curl -I http://xrpl-mcp.staticbit.io/` |
| `401` even with correct bearer | Token mismatch (whitespace, wrong index) | `grep Token /opt/staticbit-xrpl-mcp/.env.xrpl-mcp`; verify length and exact value |
| `400 HTTPS required` | `RequireHttps=true` and no `X-Forwarded-Proto` header | Verify Traefik labels are correct and request reaches via HTTPS |
| `404` on `/` | Expected — MCP is at `/mcp` | Use `/mcp` path |
| `429 Too Many Requests` | Hit rate limit | Increase `Server__RateLimit__PermitsPerMinute` or wait one minute |
| Traefik logs show `client version 1.24 is too old` | Traefik ≤ 3.5 vs Docker ≥ 29 | Upgrade Traefik to v3.6+ in `/opt/traefik/docker-compose.yml` |

Live inspection inside the container:
```bash
docker compose exec xrpl-mcp curl -sS http://127.0.0.1:5500/healthz
docker compose logs --tail=200 -f xrpl-mcp
```
