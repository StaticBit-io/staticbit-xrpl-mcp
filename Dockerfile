# syntax=docker/dockerfile:1.7

ARG DOTNET_VERSION=10.0

# ─────────────────────────────────────────────────────────────────────────────
# Stage 1 — build
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore as a separate layer for better cache hits when only source changes.
COPY Directory.Build.props ./
COPY ["src/StaticBit.Xrpl.Mcp.Abstractions/StaticBit.Xrpl.Mcp.Abstractions.csproj", "src/StaticBit.Xrpl.Mcp.Abstractions/"]
COPY ["src/StaticBit.Xrpl.Mcp.Core/StaticBit.Xrpl.Mcp.Core.csproj",                 "src/StaticBit.Xrpl.Mcp.Core/"]
COPY ["src/StaticBit.Xrpl.Mcp.Server/StaticBit.Xrpl.Mcp.Server.csproj",             "src/StaticBit.Xrpl.Mcp.Server/"]
RUN dotnet restore "src/StaticBit.Xrpl.Mcp.Server/StaticBit.Xrpl.Mcp.Server.csproj" --runtime linux-x64

COPY src/ src/

# Stamped into the assembly so /healthz can report it. CI passes the released
# tag (leading v stripped) via the shared docker-build-push workflow.
ARG APP_VERSION=0.0.0-dev

RUN dotnet publish "src/StaticBit.Xrpl.Mcp.Server/StaticBit.Xrpl.Mcp.Server.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --runtime linux-x64 \
    --self-contained false \
    -p:InformationalVersion=$APP_VERSION \
    /p:UseAppHost=false

# ─────────────────────────────────────────────────────────────────────────────
# Stage 2 — runtime
# ─────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# curl is used by HEALTHCHECK to probe /healthz. Debian slim doesn't include it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Microsoft's aspnet image already defines APP_UID (64198) — a non-root user.
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./

# Sensible defaults; can be overridden via env_file / docker-compose.
ENV Server__Transport=http \
    Server__HttpPort=5500 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=true \
    ASPNETCORE_HTTP_PORTS=5500

EXPOSE 5500
USER $APP_UID

# In-container healthcheck. Traefik also probes from the network side, but
# this lets `docker compose ps` and `docker inspect` report container health.
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:5500/healthz || exit 1

ENTRYPOINT ["dotnet", "StaticBit.Xrpl.Mcp.Server.dll"]
CMD ["--transport", "http"]
