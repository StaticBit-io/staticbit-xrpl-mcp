> 🇷🇺 [Прочесть на русском](DEPLOY.ru.md)

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

## Step 2 — provision the OAuth authorization server

The MCP server no longer issues or stores per-consumer bearer tokens. Instead it
is an **OAuth 2.1 resource server**: it validates short-lived RS256 JWTs (fetching
the signing keys from the authorization server's JWKS) and gates `/mcp` on the
`xrpl` scope. There are no secrets to generate here and no token list to hand out.

What you need before continuing:

- A reachable **authorization server** (the StaticBit deployment uses
  `https://auth.mcp.staticbit.io`). It must publish a JWKS endpoint, support
  dynamic client registration, and maintain the user **allow-list** — only
  allow-listed accounts can obtain a token, and disabling an account immediately
  revokes its refresh tokens.
- Its **issuer** URL, the **resource** identifier for this MCP
  (`https://xrpl-mcp.staticbit.io/mcp`), and the required **scope** (`xrpl`).

Standing up the authorization server itself is out of scope for this document —
point the MCP at an existing one. You'll wire these three values into the service
config in Step 3.

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

Edit `.env.xrpl-mcp` — point the server at your authorization server (values from Step 2):
```
Server__OAuth__Issuer=https://auth.mcp.staticbit.io
Server__OAuth__Resource=https://xrpl-mcp.staticbit.io/mcp
Server__OAuth__RequiredScope=xrpl
```

The server fetches the JWKS from the issuer's well-known metadata at startup and
on a refresh interval — no signing keys live in this file. Access is managed on
the authorization server's allow-list, not here.

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
StaticBitXrplMcp HTTP listening on port 5500, RequireHttps=True, RateLimit=60/min, OAuth=https://auth.mcp.staticbit.io (scope=xrpl)
```

The `OAuth=<issuer> (scope=...)` line confirms the server resolved the
authorization server's metadata and loaded its JWKS.

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

### 5.1 — no token → 401

```bash
curl -sS -o /dev/null -w "HTTP %{http_code}\n" \
  -X POST https://xrpl-mcp.staticbit.io/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
# Expected: HTTP 401 (plus a WWW-Authenticate header pointing at the resource metadata)
```

### 5.2 — invalid token → 401

```bash
curl -sS -o /dev/null -w "HTTP %{http_code}\n" \
  -X POST https://xrpl-mcp.staticbit.io/mcp \
  -H "Authorization: Bearer not-a-real-jwt" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
# Expected: HTTP 401 (signature/issuer validation fails)
```

### 5.3 — valid access token + initialize → 200 + SSE

Obtain a real access token for the `xrpl` scope from the authorization server
first (e.g. log in once via Claude Code's `/mcp` flow and copy the bearer it
stored, or use your AS's token endpoint). Then:

```bash
TOKEN=<XRPL_SCOPED_ACCESS_TOKEN>
curl -sS -X POST https://xrpl-mcp.staticbit.io/mcp \
  -H "Authorization: Bearer $TOKEN" \
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
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"health","version":"0.0.1"}}}'
SESSION=$(grep -i '^mcp-session-id' "$HDR" | awk '{print $2}' | tr -d '\r\n')

# Step 2: notifications/initialized
curl -sS -o /dev/null -X POST "$URL" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Mcp-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","method":"notifications/initialized"}'

# Step 3: server_info on mainnet
curl -sS -X POST "$URL" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Mcp-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"xrpl_server_info","arguments":{"network":"mainnet"}}}'
# Expected: event: message with current build_version, validated_ledger.seq, etc.
```

---

## Step 6 — register in clients (Claude Code / Cursor / Claude Desktop)

The deployed URL is `https://xrpl-mcp.staticbit.io/mcp`. There are no per-consumer
bearer tokens to distribute, and no `Authorization` headers to bake into config
files. Each consumer instead **logs in via OAuth** the first time they connect —
the client performs dynamic client registration against the authorization server,
the user signs in in the browser, and the client stores and auto-refreshes the
resulting token. The consumer's account must be on the authorization server's
allow-list (Step 2) or login is refused.

> Prefer installing the **`xrpl-cloud` plugin** rather than registering the
> server by hand — its `.mcp.json` already carries the OAuth block, so the
> consumer just runs `/mcp` and logs in. See the plugin README and INSTALL.md.

### 6.1 Claude Code (user scope — available across all projects)

```powershell
claude mcp add xrpl-cloud https://xrpl-mcp.staticbit.io/mcp `
  --scope user `
  --transport http
```

Then run `/mcp`, select `xrpl-cloud`, and complete the browser login. Verify:
```powershell
claude mcp list
# xrpl-cloud   https://xrpl-mcp.staticbit.io/mcp (HTTP) - ✓ Connected
```

If it shows `needs login`, finish the `/mcp` flow; if login is refused, the
account is not on the allow-list.

### 6.2 Claude Code (project scope — `.mcp.json` in git)

The config is commit-safe by design — it holds no secret, only an `oauth` block.
Each developer logs in with their own browser session; access is per-account on
the authorization server.

`.mcp.json` (committed to the repo):
```json
{
  "mcpServers": {
    "xrpl-cloud": {
      "type": "http",
      "url": "https://xrpl-mcp.staticbit.io/mcp",
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
      "url": "https://xrpl-mcp.staticbit.io/mcp",
      "oauth": {}
    }
  }
}
```
Restart Cursor after editing, then complete the browser login when prompted.

### 6.4 Claude Desktop — `claude_desktop_config.json`

```json
{
  "mcpServers": {
    "xrpl-cloud": {
      "url": "https://xrpl-mcp.staticbit.io/mcp",
      "oauth": {}
    }
  }
}
```

Restart Claude Desktop after editing and complete the browser login.

### 6.5 Revoking and re-granting access

There's no shared secret to rotate. To cut a consumer off, disable their account
on the authorization server's allow-list — that revokes their refresh tokens
immediately, on every device, with no config edits anywhere. To grant access,
add the account back; the consumer logs in again via `/mcp`. A consumer can also
clear their own stored token locally: `/mcp` → select `xrpl-cloud` → clear
authentication.

---

## Step 7 — admin alerts in Telegram (optional but recommended)

When enabled, the server posts operational events to a **separate** Telegram chat
through its own **dedicated** admin bot. Use this to learn fast about:

- **AuthFailure** — a request hit `/mcp` with no token, an invalid/expired JWT, or one missing the `xrpl` scope (probes or a misconfigured client)
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
• oauth: https://auth.mcp.staticbit.io (scope=xrpl)

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
Missing token from <YOUR_IP>

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

### Revoke or rotate access

There is no server-side bearer to rotate. Access is controlled entirely on the
**authorization server's allow-list**:

- **Revoke a consumer** — disable their account on the authorization server. Its
  refresh tokens are invalidated immediately and every device drops to
  `needs login`. No change to `.env.xrpl-mcp` and no container restart.
- **Compromise response** — same: disable the account, then re-enable it and have
  the user log in again via `/mcp`. Access tokens are short-lived, so even an
  already-issued one expires on its own within minutes.

### Add a new user

Add the account to the authorization server's allow-list. The new user then
installs the `xrpl-cloud` plugin (or registers the URL per Step 6), runs `/mcp`,
and completes the browser login. Nothing changes on this server.

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
| `Server__OAuth__Issuer` | — | authorization server issuer URL (JWKS resolved from its metadata; required) |
| `Server__OAuth__Resource` | — | resource identifier for this MCP, e.g. `https://xrpl-mcp.staticbit.io/mcp` (required) |
| `Server__OAuth__RequiredScope` | `xrpl` | scope a token must carry to reach `/mcp` |
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
| Container starts then exits with `OAuth:Issuer is empty` | No authorization server configured | Set `Server__OAuth__Issuer` / `Resource` in `.env.xrpl-mcp` (Step 3) |
| Startup fails fetching JWKS / metadata | Issuer unreachable or wrong URL | Verify `Server__OAuth__Issuer` and that `<issuer>/.well-known/...` is reachable from the container |
| TLS cert not issued | DNS not propagated / port 80 not reachable | `dig +short xrpl-mcp.staticbit.io @1.1.1.1`; `curl -I http://xrpl-mcp.staticbit.io/` |
| `401` even with a token | Expired/invalid JWT, wrong issuer/audience, or missing `xrpl` scope | Re-login via `/mcp`; confirm the token's `iss`/`aud`/`scope` match the server config |
| `401` for an allow-listed user | Account disabled on the authorization server | Re-enable the account on the allow-list, then re-login |
| `400 HTTPS required` | `RequireHttps=true` and no `X-Forwarded-Proto` header | Verify Traefik labels are correct and request reaches via HTTPS |
| `404` on `/` | Expected — MCP is at `/mcp` | Use `/mcp` path |
| `429 Too Many Requests` | Hit rate limit | Increase `Server__RateLimit__PermitsPerMinute` or wait one minute |
| Traefik logs show `client version 1.24 is too old` | Traefik ≤ 3.5 vs Docker ≥ 29 | Upgrade Traefik to v3.6+ in `/opt/traefik/docker-compose.yml` |

Live inspection inside the container:
```bash
docker compose exec xrpl-mcp curl -sS http://127.0.0.1:5500/healthz
docker compose logs --tail=200 -f xrpl-mcp
```
