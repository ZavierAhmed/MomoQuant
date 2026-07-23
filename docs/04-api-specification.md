# MOMO Quant

## Part 4 — API Specification

**Version:** 1.0 Draft  
**Backend:** .NET 8 Web API  
**Frontend:** React Dashboard  
**Realtime:** SignalR  
**API Style:** REST + SignalR events

---

## 1. API Principles

APIs must be predictable, versioned, consistent, safe by default, auditable, expandable, and easy for the React dashboard to consume. The frontend sends commands and reads state. Trading decisions remain inside the backend.

Base path:

```text
/api/v1
```

Standard success response:

```json
{ "success": true, "message": "Request completed successfully.", "data": {} }
```

Standard error response:

```json
{ "success": false, "message": "Validation failed.", "errors": [{ "field": "symbol", "message": "Symbol is required." }] }
```

List endpoints support page, pageSize, sortBy, sortDirection, and search.

---

## 2. Authentication APIs

```http
POST /api/v1/auth/login
GET  /api/v1/auth/me
POST /api/v1/auth/logout
```

Login returns accessToken, expiresAtUtc, and user profile.

---

## 3. User Management APIs

Admin-only:

```http
GET  /api/v1/users
GET  /api/v1/users/{id}
POST /api/v1/users
PUT  /api/v1/users/{id}
POST /api/v1/users/{id}/disable
```

---

## 4. Exchange, API Keys, Symbols

Exchange APIs:

```http
GET  /api/v1/exchanges
POST /api/v1/exchanges
PUT  /api/v1/exchanges/{id}
POST /api/v1/exchanges/{id}/test-connection
```

API key vault, admin-only and never returns raw secrets:

```http
GET  /api/v1/api-keys
POST /api/v1/api-keys
POST /api/v1/api-keys/{id}/disable
```

Symbol APIs:

```http
GET  /api/v1/symbols
POST /api/v1/symbols/sync
PUT  /api/v1/symbols/{id}/status
```

---

## 5. Market Data and Indicators

```http
GET  /api/v1/market-data/candles
POST /api/v1/market-data/candles/import
GET  /api/v1/market-data/imports/{importId}
GET  /api/v1/market-data/snapshot

GET  /api/v1/indicators/snapshot
POST /api/v1/indicators/recalculate
```

Candle queries filter by symbolId, timeframe, fromUtc, toUtc, and limit.

---

## 6. Strategy APIs

```http
GET  /api/v1/strategies
GET  /api/v1/strategies/{id}
POST /api/v1/strategies/{id}/enable
POST /api/v1/strategies/{id}/disable
GET  /api/v1/strategies/{id}/parameters
PUT  /api/v1/strategies/{id}/parameters
```

Strategy parameter updates must be audited.

---

## 7. Trading Sessions and Bot Control

Trading sessions:

```http
GET  /api/v1/trading-sessions
GET  /api/v1/trading-sessions/{id}
POST /api/v1/trading-sessions/{id}/stop
POST /api/v1/trading-sessions/{id}/pause
POST /api/v1/trading-sessions/{id}/resume
```

Bot control:

```http
GET  /api/v1/bot/status
POST /api/v1/bot/change-mode
POST /api/v1/bot/start
POST /api/v1/bot/stop
POST /api/v1/bot/emergency-stop
POST /api/v1/bot/clear-emergency-stop
```

Live mode requires stricter confirmation and admin role. Emergency stop immediately blocks new entries and attempts to cancel open orders.

---

## 8. Backtesting APIs

```http
POST /api/v1/backtests/run
GET  /api/v1/backtests/{id}
GET  /api/v1/backtests/{id}/results
GET  /api/v1/backtests/{id}/trades
GET  /api/v1/backtests/{id}/signals
POST /api/v1/backtests/{id}/cancel
```

Backtest run request includes exchangeId, symbolIds, timeframe, higherTimeframe, fromUtc, toUtc, initialBalance, strategyIds, riskProfileId, maker/taker fees, slippage, simulateMakerFills, and orderTimeoutSeconds.

---

## 9. Replay APIs

```http
POST /api/v1/replay/create
GET  /api/v1/replay/{id}
POST /api/v1/replay/{id}/start
POST /api/v1/replay/{id}/pause
POST /api/v1/replay/{id}/resume
POST /api/v1/replay/{id}/speed
POST /api/v1/replay/{id}/step
POST /api/v1/replay/{id}/stop
```

Allowed speeds: 1, 2, 10, 50, 100.

---

## 10. Paper Trading APIs

```http
GET  /api/v1/paper/accounts
POST /api/v1/paper/accounts
POST /api/v1/paper/start
POST /api/v1/paper/stop
GET  /api/v1/paper/accounts/{id}/snapshot
GET  /api/v1/paper/accounts/{id}/trades
```

Paper trading uses simulated execution and must never place real exchange orders.

---

## 11. Live Trading APIs

Live trading is later. MVP implements readiness only.

```http
GET  /api/v1/live/readiness
POST /api/v1/live/enable
POST /api/v1/live/disable
```

Enable live requires admin role, confirmation text `ENABLE LIVE TRADING`, API key, risk profile, system health, paper validation, emergency stop availability, and audit logging.

---

## 12. Signals, AI, Risk

Signals:

```http
GET /api/v1/signals
GET /api/v1/signals/{id}
```

AI dashboard APIs:

```http
GET  /api/v1/ai/decisions
GET  /api/v1/ai/decisions/{id}
POST /api/v1/ai/regime/test
GET  /api/v1/ai/performance
```

Risk APIs:

```http
GET  /api/v1/risk/profiles
POST /api/v1/risk/profiles
GET  /api/v1/risk/profiles/{id}/rules
PUT  /api/v1/risk/profiles/{id}/rules
GET  /api/v1/risk/decisions
```

---

## 13. Orders, Trades, Positions

```http
GET  /api/v1/orders
GET  /api/v1/orders/{id}
POST /api/v1/orders/{id}/cancel

GET  /api/v1/trades
GET  /api/v1/trades/{id}
POST /api/v1/trades/{id}/close

GET  /api/v1/positions
GET  /api/v1/positions/{id}
```

Trade details should include signal, AI decision, risk decision, entry/exit orders, fills, and PnL breakdown.

---

## 14. Reporting APIs

```http
GET /api/v1/reports/dashboard-summary
GET /api/v1/reports/performance
GET /api/v1/reports/strategies
GET /api/v1/reports/symbols
GET /api/v1/reports/market-regimes
GET /api/v1/reports/rejections
GET /api/v1/reports/missed-trades
```

Reports must come from stored facts.

---

## 15. Monitoring, Audit, Settings

```http
GET /api/v1/monitoring/health
GET /api/v1/monitoring/exchange-health
GET /api/v1/monitoring/workers
GET /api/v1/monitoring/errors

GET /api/v1/audit-logs
GET /api/v1/audit-logs/{id}

GET /api/v1/settings
PUT /api/v1/settings/{key}
```

---

## 16. SignalR Hubs

```text
/hubs/bot        -> BotStatusChanged, TradingModeChanged, EmergencyStopTriggered
/hubs/market     -> CandleUpdated, IndicatorUpdated, MarketSnapshotUpdated
/hubs/trading    -> SignalGenerated, AiDecisionCreated, RiskDecisionCreated, OrderUpdated, TradeOpened, TradeClosed, PositionUpdated
/hubs/replay     -> ReplayStarted, ReplayPaused, ReplayTick, ReplayDecisionUpdated
/hubs/monitoring -> HealthStatusChanged, WorkerStatusChanged, ErrorLogged
```

---

## 17. Internal Python AI Service API

Base URL inside Docker network:

```text
http://momo-ai:8000
```

Endpoints:

```http
POST /ai/regime/detect
POST /ai/confidence/score
POST /ai/parameters/optimize
POST /ai/anomaly/detect
POST /ai/trade/explain
```

---

## 18. Authorization Rules

Admin can access everything. Trader can run backtests, replay, paper trading, view reports/trades/orders/monitoring, and update strategy parameters if allowed. Viewer can only read dashboard, reports, trades, positions, and monitoring.

---

## 19. Critical API Rules

1. No frontend endpoint places trades directly.
2. All trading actions go through backend command handlers.
3. Live APIs disabled until explicitly implemented.
4. Every state-changing endpoint creates an audit log.
5. Timestamps are UTC.
6. Financial values use decimal.
7. Every API uses the standard response format.
8. Lists support pagination.
9. Bot control actions are permission checked.
10. Emergency stop is fast and reliable.
11. API keys never return raw.
12. Backtest, replay, paper, live share core abstractions.
