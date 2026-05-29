# Operations — StaticBitXrplMcp cloud server

>  🌐 **Language**: **English** | [Русский](ru/OPERATIONS.md)

Day-two runbook for the **xrpl-cloud** server (`xrpl-mcp.staticbit.io`). The `xrpl-local` and
`xrpl-signer` plugins are local stdio processes on the user's machine — they have no server-side
operations. First-time bring-up from a clean VPS is [DEPLOY.md](DEPLOY.md).

## Deploy / redeploy

Image build + deploy are automated via the shared reusable workflows in
[`mcp-tooling`](https://github.com/Platonenkov/mcp-tooling):

- **Build/publish**: an `xrpl-cloud` release (`release-plugin.yml`) builds and pushes
  `ghcr.io/staticbit-io/staticbit-xrpl-mcp` via the reusable `docker-build-push`. Ad-hoc:
  **Actions → docker** (workflow_dispatch, `version`).
- **Deploy**: **Actions → deploy** (`deploy.yml`, `tag` = `latest` or a semver). The runner ships
  the image over SSH into the forced-command `deploy/deploy.sh` on the VPS (no GHCR login on the
  host), which pins `XRPL_MCP_IMAGE` + `XRPL_PULL_POLICY=never` in `/opt/staticbit-xrpl-mcp/.env`,
  recreates the container, and the runner smoke-tests `/healthz`.

## Rollback

Re-run **Actions → deploy** with a previous tag (the older image is shipped and pinned). As a
host-side fallback the compose still has a `build:` block: `cd /opt/staticbit-xrpl-mcp && docker
compose up -d --build xrpl-mcp` rebuilds from source.

## Health & logs

```bash
curl -fsS https://xrpl-mcp.staticbit.io/healthz        # expect 200 {"status":"ok"}
docker compose -f /opt/staticbit-xrpl-mcp/docker-compose.yml ps
docker compose -f /opt/staticbit-xrpl-mcp/docker-compose.yml logs --tail=200 xrpl-mcp
```

The Dockerfile defines a `HEALTHCHECK` (curl `/healthz`); Traefik also probes it from the network
side. `docker inspect --format '{{.State.Health.Status}}' xrpl-mcp` reports container health.

## Configuration & secrets

Lives on the VPS under `/opt/staticbit-xrpl-mcp/`, never in git:

- `.env` — `XRPL_MCP_HOST`, `XRPL_MCP_IMAGE`, `XRPL_PULL_POLICY` (deploy.sh manages the last two).
- `.env.xrpl-mcp` — per-service secrets: OAuth (`Server__OAuth__Issuer/Resource/RequiredScope`),
  optional custom XRPL endpoints, admin-alert bot token/chat. `chmod 600`.

Rotate a secret → edit `.env.xrpl-mcp` → `docker compose up -d xrpl-mcp`.

## Admin alerts (optional)

`Server__AdminAlerts__{Enabled,BotToken,ChatId}` posts lifecycle/security events to a Telegram
chat via a dedicated bot. Keep `System.Net.Http.HttpClient` logging at `Warning` so the bot token
in API URLs never reaches logs.

## Troubleshooting

| Symptom | Check |
|---------|-------|
| Container won't start | `docker compose logs xrpl-mcp`; verify `.env.xrpl-mcp` and OAuth config |
| `/healthz` not 200 | container health (`docker inspect`), Traefik routing, DNS A-record for `XRPL_MCP_HOST` |
| 401 on `/mcp` | expected without a token — clients log in via `/mcp` (OAuth); check the AS is up |
| Deploy fails at smoke | the new image is unhealthy — roll back to the previous tag |
