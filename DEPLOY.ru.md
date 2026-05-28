> 🇬🇧 [Read in English](DEPLOY.md)

# Развёртывание StaticBitXrplMcp

> ⏳ **Перевод в процессе.** Полная EN-версия — [DEPLOY.md](DEPLOY.md). Содержит инструкции по разворачиванию cloud-сервера на VPS через Docker, настройке reverse-proxy (nginx), systemd-юнитов, secrets, мониторинга и backup. Документ self-contained — оператор может развернуть с нуля без знания кодовой базы.

## Краткое содержание EN-версии

- **VPS prerequisites** — Ubuntu 22.04+ / Debian 12+, Docker, nginx, certbot, минимум 2 vCPU / 4 GB RAM.
- **Initial setup** — клонирование репо, конфигурация ENV-переменных (OAuth: `Server__OAuth__Issuer`, `Server__OAuth__Resource`, `Server__OAuth__RequiredScope`, плюс `XRPL_NETWORK_MAINNET_URL`, etc.).
- **Docker build & run** — `docker-compose up -d` на основе [docker-compose.yml](docker-compose.yml).
- **TLS termination** — nginx с Let's Encrypt сертификатом, HTTP→HTTPS redirect, X-Forwarded-Proto.
- **Hardening** — systemd unit, OAuth 2.1 для MCP-вызовов (валидация короткоживущих RS256 JWT через JWKS authorization-сервера, gating `/mcp` по scope `xrpl`), rate-limit per IP (см. [features.md §5](docs/features.ru.md#5-server-инфраструктура)). Требуется отдельный authorization-сервер.
- **Monitoring** — Prometheus scrape endpoint `/metrics`, AdminAlerter для security events.
- **Backup / rollback** — что бэкапить (volumes), как rollback'нуть на предыдущую версию.

См. полную инструкцию в [DEPLOY.md](DEPLOY.md) (на английском).
