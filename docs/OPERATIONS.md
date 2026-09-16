# Operations — StaticBitXrplMcp cloud server

>  🌐 **Language**: **English** | [Русский](ru/OPERATIONS.md)

Day-two runbook for the **xrpl-cloud** server (`xrpl.mcp.staticbit.ai`). The `xrpl-signer` plugin is a local stdio process on the user's machine — it has no server-side
operations. First-time bring-up from a clean VPS is [DEPLOY.md](DEPLOY.md).

## Deploy / redeploy

Deploys run as the **non-root** `mcpdeploy` user via **Actions → deploy-build**
(`deploy-build.yml`) — the image is built from source on the host, nothing is pulled:

- **Deploy (build from source)**: the runner transfers the repo source to the VPS
  (`git archive` piped over `ssh tar`), reconstructs `.env` (plus a minimal `.env.xrpl-mcp`)
  from **GitHub Secrets / Variables**, builds the image on the host
  (`docker compose up -d --build`, no GHCR auth), and smoke-tests `/healthz`.
- **Register**: a downstream `register` job pushes `.mcp-registry.json` to the authorization
  server (`PUT /api/admin/mcps`, `X-Service-Token`) so the AS knows the `xrpl` scope/resource.

The cloud server is OAuth-only — no static bearer to provision. The shared Traefik platform
(wildcard TLS for `*.mcp.staticbit.ai`, the `mcp-net` network) comes from the
[**mcp-infra**](https://github.com/StaticBit-io/mcp-infra) repo.

## Rollback

Re-run **Actions → deploy-build** pointing at the previous commit/tag — it re-transfers that
source and rebuilds. By hand on the host: `cd /opt/staticbit-xrpl-mcp && git checkout <ref> &&
docker compose up -d --build xrpl-mcp`.

## Health & logs

```bash
curl -fsS https://xrpl.mcp.staticbit.ai/healthz        # expect 200 {"status":"ok"}
docker compose -f /opt/staticbit-xrpl-mcp/docker-compose.yml ps
docker compose -f /opt/staticbit-xrpl-mcp/docker-compose.yml logs --tail=200 xrpl-mcp
```

The Dockerfile defines a `HEALTHCHECK` (curl `/healthz`); Traefik also probes it from the network
side. `docker inspect --format '{{.State.Health.Status}}' xrpl-mcp` reports container health.

## Configuration & secrets

Lives on the VPS under `/opt/staticbit-xrpl-mcp/`, never in git:

- `.env` — `XRPL_MCP_HOST` and other compose build/runtime vars. The `deploy-build` workflow
  reconstructs this file from GitHub Secrets / Variables on each deploy.
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
| `/healthz` not 200 | container health (`docker inspect`), Traefik routing, DNS record for `XRPL_MCP_HOST` |
| 401 on `/mcp` | expected without a token — clients log in via `/mcp` (OAuth); check the AS is up |
| Deploy fails at smoke | the freshly built image is unhealthy — check the build logs in `deploy-build`, then roll back to the previous commit/tag |
