# MOMO Quant

## Part 7 — DevOps & Deployment Guide

**Version:** 1.0 Draft  
**Deployment Style:** Docker Compose first  
**Production Target:** Linux VPS or cloud VM

---

## 1. DevOps Principle

Start with Docker Compose + Linux VPS + MySQL + Redis + .NET API + Python AI + React Dashboard. Do not start with Kubernetes, Kafka, complex CI/CD, or distributed infrastructure.

Move to heavier infrastructure only after the system is stable, profitable, and too large for one host.

---

## 2. Runtime Components

```text
momo-api
momo-dashboard
momo-ai
momo-mysql
momo-redis
momo-nginx
```

Optional later: momo-worker, Prometheus, Grafana, Loki, backup container. For MVP, API and worker can live together.

---

## 3. Project and Deployment Structure

```text
momo-quant/
  docker/
    nginx/nginx.conf
    mysql/init/
    redis/
    api/
    ai/
    dashboard/
  src/
    backend/
    frontend/
    ai/
  deploy/
    docker-compose.yml
    docker-compose.override.yml
    .env.example
  scripts/
    backup-db.sh
    restore-db.sh
    deploy.sh
    logs.sh
```

Never commit real `.env` files. Commit only `.env.example`.

---

## 4. Environment Variables

Example `.env.example`:

```text
APP_ENV=Production
APP_URL=https://momoquant.yourdomain.com
MYSQL_DATABASE=momo_quant
MYSQL_USER=momo_user
MYSQL_PASSWORD=change-this
MYSQL_ROOT_PASSWORD=change-this
REDIS_PASSWORD=change-this
JWT_SECRET=change-this-long-secret
JWT_ISSUER=MOMOQuant
JWT_AUDIENCE=MOMOQuantDashboard
AI_SERVICE_URL=http://momo-ai:8000
ENCRYPTION_KEY=change-this-32-byte-key
BINANCE_BASE_URL=https://fapi.binance.com
BINANCE_WS_URL=wss://fstream.binance.com
```

---

## 5. Service Responsibilities

- momo-api: REST API, SignalR, authentication, orchestration, backtesting, replay, paper trading, reports, monitoring.
- momo-dashboard: React build served by Nginx. Do not run Vite dev server in production.
- momo-ai: internal FastAPI AI service. Not public.
- momo-mysql: persistent database with Docker volume.
- momo-redis: cache/runtime state/queues later. Password protected.
- momo-nginx: public entry point, reverse proxy, TLS termination.

Only Nginx exposes 80/443. MySQL, Redis, AI, API internals are private.

---

## 6. HTTPS and Security

Use Nginx + Let's Encrypt/Certbot for HTTPS. HTTPS/TLS protects data in transit. Database/API key encryption protects secrets at rest. JWT protects authenticated access. Role permissions protect dangerous actions. Audit logs track sensitive operations.

Do not invent custom encryption for every API payload; it often creates bugs and false security.

---

## 7. Secrets Management

Secrets: JWT secret, DB password, Redis password, exchange API keys/secrets, encryption key, TLS private keys.

Rules:

1. Never commit secrets.
2. Use `.env` locally and server env vars in production.
3. Encrypt exchange API keys before DB storage.
4. Never display API secrets after saving.
5. Use testnet and live keys separately.
6. Disable withdrawals on exchange keys.
7. Use IP whitelist if supported.

Tracked `appsettings*.json` files must use empty strings or `CHANGE_ME` placeholders only. For local Development after rotation, prefer `dotnet user-secrets` (API `UserSecretsId`) or environment variables — see `docs/11-local-secrets-and-hosting-security.md` and root `.env.example`.

API startup rejects missing/weak JWT and DB/seed placeholders outside the `Testing` host (`StartupSecretsValidator`).

---

## 8. Database Migrations

Use EF Core migrations. Commit migrations. Backup before production migration. Never run destructive migrations casually.

Flow: create locally -> test locally -> test/staging DB -> backup production -> apply production -> verify health.

---

## 9. Backup and Restore

Backups are mandatory. Back up MySQL, deployment secrets, historical data if external, and Docker config.

Recommended: daily DB backup, weekly full backup, backup before every deployment and migration. Store backups outside the server if possible.

Create `scripts/backup-db.sh` and `scripts/restore-db.sh`. Test restore before trusting backups.

Suggested retention: daily 14 days, weekly 8 weeks, monthly 12 months.

---

## 10. Logging and Monitoring

Use structured logging, recommended Serilog for .NET. Logs include timestamp, level, module, tradingSessionId, symbol, strategy, message, exception, correlationId.

Monitor API, DB, Redis, AI, exchange REST/WebSocket, workers, bot state, last market update, last AI response, last order update, errors, latency.

Expose `GET /api/v1/monitoring/health`.

---

## 11. Health Checks and Alerts

Each container needs health checks: API `/health`, AI `/health`, MySQL ping, Redis ping, dashboard static response.

Alerts: emergency stop, exchange disconnected, AI/DB/Redis unavailable, stale WebSocket, daily loss, backtest complete, paper trading stopped unexpectedly, critical error. MVP uses in-app alerts; email/Telegram/Discord later.

---

## 12. CI/CD and Deployment Flow

Keep CI simple: backend build/tests, AI tests, frontend build, Docker build. Git workflow: main, develop, feature/*.

Manual deployment flow first:

```text
Pull code
Backup DB
Build images
Run migrations
Restart containers
Check health
Check dashboard
Check logs
Run smoke tests
```

---

## 13. Local Development

Recommended local setup:

```text
MySQL + Redis in Docker
.NET API local
React dashboard local via Vite
Python AI local via uvicorn
```

Later run full Docker Compose locally.

---

## 14. Production Environment

Use Linux VPS, Docker, Docker Compose, Nginx, HTTPS, MySQL volume, Redis volume, backups, firewall.

Minimum MVP VPS: 2 CPU, 4GB RAM, 80GB SSD. Better: 4 CPU, 8GB RAM, 160GB SSD.

Only expose ports 80/443 and restricted SSH. Do not expose MySQL, Redis, AI, or internal API ports.

---

## 15. Nginx Routing

```text
/      -> React dashboard
/api/  -> .NET API
/hubs/ -> SignalR
```

Do not expose `/ai/`. Python AI stays internal. SignalR over Nginx requires WebSocket upgrade headers and long timeouts.

---

## 16. Persistent Data

Use Docker volumes: MySQL data, Redis data if persistence enabled, logs, backups. Containers are disposable; volumes must survive.

Historical candles start in MySQL with proper indexes. Later, consider Parquet/object storage/analytics DB if needed.

---

## 17. Performance Risks

Risks: large candle queries, heavy backtests, too many SignalR events, AI latency, DB write volume, inefficient reports.

Mitigations: pagination, indexes, batch inserts, cache summaries, top 5 symbols initially, limited timeframes, background jobs, useful realtime events only.

---

## 18. Deployment Safety

Before paper/live engine: DB healthy, Redis healthy, AI healthy or fallback configured, exchange healthy, symbols active, risk profile active, emergency stop available, mode confirmed.

For live later: readiness check, API key test, paper validation, typed confirmation.

---

## 19. Rollback and Versioning

Keep previous Docker image, previous `.env`, pre-deployment DB backup, and migration rollback notes. Version backend, dashboard, AI service, migrations, AI models, strategy logic, and strategy parameters.

---

## 20. Critical DevOps Rules

1. Do not expose MySQL, Redis, or AI publicly.
2. Use HTTPS in production.
3. Do not commit secrets.
4. Backup before migrations.
5. Test restore.
6. Keep live disabled by default.
7. Use volumes for persistent data.
8. Monitor stale market data.
9. Do not add Kubernetes early.
10. Do not trust deployment until smoke tests pass.
