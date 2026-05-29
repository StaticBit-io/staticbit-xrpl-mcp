#!/usr/bin/env bash
#
# Forced-command deploy entrypoint for the xrpl-cloud MCP VPS.
#
# Wired as the *only* thing a CI deploy key may run, via ~/.ssh/authorized_keys:
#
#   command="/opt/xrpl-mcp/deploy.sh",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty ssh-ed25519 AAAA... ci-deploy
#
# The CI job (deploy.yml -> mcp-tooling/deploy-vps.yml) ships the image as a
# tarball on stdin and passes the tag as the original SSH command, e.g.:
#
#   docker save ghcr.io/staticbit-io/staticbit-xrpl-mcp:0.2.0 \
#     | ssh -i id_deploy deploy@host "0.2.0"
#
# Because of the forced command, the requested command ("0.2.0") never runs as
# a shell — it only arrives here in $SSH_ORIGINAL_COMMAND, and the image tar
# arrives on this script's stdin. The tag is whitelisted before use.
#
# Host-specific constants — match these to your VPS layout (and to the path in
# authorized_keys above):
set -euo pipefail

APP_DIR="/opt/xrpl-mcp"                                 # dir holding docker-compose.yml + .env
CONTAINER="xrpl-mcp"                                    # container_name in docker-compose.yml
IMAGE_REPO="ghcr.io/staticbit-io/staticbit-xrpl-mcp"   # must match the saved image name
HEALTH_RETRIES=20
HEALTH_INTERVAL=3

TAG="${SSH_ORIGINAL_COMMAND:-}"

# Whitelist: semver (optionally v-prefixed) or the literal "latest". Anything
# else is rejected before it can touch the shell, the .env, or docker.
if ! [[ "$TAG" =~ ^(v?[0-9]+\.[0-9]+\.[0-9]+|latest)$ ]]; then
  echo "deploy: refusing invalid tag '${TAG}'" >&2
  exit 2
fi

cd "$APP_DIR"

echo "deploy: loading image tar from stdin…"
docker load

# Pin the compose image to the tar-loaded ref and force it (never pull from a
# registry the VPS isn't logged into). xrpl's compose reads ${XRPL_MCP_IMAGE}.
IMAGE_REF="${IMAGE_REPO}:${TAG}"
if grep -q '^XRPL_MCP_IMAGE=' .env; then
  sed -i "s|^XRPL_MCP_IMAGE=.*|XRPL_MCP_IMAGE=${IMAGE_REF}|" .env
else
  echo "XRPL_MCP_IMAGE=${IMAGE_REF}" >> .env
fi
if grep -q '^XRPL_PULL_POLICY=' .env; then
  sed -i "s|^XRPL_PULL_POLICY=.*|XRPL_PULL_POLICY=never|" .env
else
  echo "XRPL_PULL_POLICY=never" >> .env
fi

echo "deploy: bringing up ${CONTAINER} on tag ${TAG}…"
docker compose up -d --force-recreate

echo "deploy: waiting for health…"
for _ in $(seq 1 "$HEALTH_RETRIES"); do
  status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$CONTAINER" 2>/dev/null || echo none)"
  if [ "$status" = "healthy" ] || [ "$status" = "running" ]; then
    echo "deploy: ${CONTAINER} up (${status}) on ${TAG}"
    docker image prune -f >/dev/null 2>&1 || true
    exit 0
  fi
  sleep "$HEALTH_INTERVAL"
done

echo "deploy: ${CONTAINER} did not become healthy on ${TAG}; check 'docker compose logs'" >&2
exit 1
