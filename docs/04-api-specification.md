# MOMO Quant — Part 4: Repository-Aligned API Specification

**Status:** As-built contract plus explicitly separated future scope
**Authoritative snapshot:** `c8ba9e87a83b5c19ad574ef7f98f3e5340bd56a2`
**Repository:** `ZavierAhmed/MomoQuant`

## 1. Purpose and authority

This document describes the API that is implemented at the pinned SHA. Production route attributes, DTOs, shared contracts, authorization policies, frontend clients, tests, and the internal Python service outrank older design drafts. The exhaustive route table is in `docs/04-api-route-inventory.md`.

## 2. Contract status terminology

**Implemented** means executable code and a route attribute exist. **Implemented alias** means a second route reaches the same action. **Partial**, **missing**, **deferred**, and **decision required** are future/gap classifications only; they are not current contracts.

## 3. Base paths and exceptions

Most .NET routes are under `/api/v1` . Exceptions are public `/api/health` and SignalR `/hubs/live-market`. The internal FastAPI service has its own `/health` and `/api/v1/ai/*` paths. The dashboard base URL defaults to `https://localhost:7295/api/v1` .

## 4. Authentication

Login is `POST /api/v1/auth/login` and returns a flat login DTO containing `accessToken` , `expiresAtUtc` , `userId` , `fullName` , `email` , and `role` . `GET /api/v1/auth/me` requires the current JWT. Logout is JWT-only client/session acknowledgement at `POST /api/v1/auth/logout`; no server-side token revocation store is claimed.

## 5. Authorization policies

The implemented policies are `AdminOnly` , `AdminOrTrader` , `ResearchRead` , and `ResearchExecute` . Ordinary `[Authorize]` remains distinct from policy checks. Anonymous actions are explicit (login/logout, public health, development hosting-security, and Strategy Lab health); see the inventory for action-level detail.

## 6. Standard REST envelope

Successful .NET responses normally serialize `{ success, message, data }` via `ApiResponse<T>` . Failures use `{ success: false, message, errors[] }` . There is no universal top-level `code` or `traceId` in the shared contract. Export downloads and public health are non-envelope exceptions; SignalR and Python have their own payloads.

## 7. Error representation and stable codes

Controllers map service failures to HTTP results and `ApiError` entries where applicable. Stable safety codes used by the accepted audit/qualification architecture are `AUDIT_EVIDENCE_INVALID` , `AUDIT_EVIDENCE_UNAVAILABLE` , and `PAPER_RUNTIME_ACTIVATION_FAILED` .

## 8. Pagination

The shared contract is `PagedRequest` (page, pageSize, sortBy, sortDirection, search) and `PagedResult<T>` (items, page, pageSize, totalCount, totalPages). Only endpoints that actually accept those types are paginated. Some endpoints use a bounded `limit`; many detail/list endpoints are unpaged. The count property is `totalCount`.

## 9. UTC, financial values, and enum serialization

API time values are UTC where named `*Utc` . Timeframes use canonical strings `1m`, `3m`, `5m`, `15m`, `30m`, `1h`, `4h`, `1d`, `1w` . Decimal/financial values remain DTO-defined; clients must not infer precision from documentation.

## 10. Public health

NaN is anonymous and returns the health controller payload directly. It is not versioned and is distinct from authenticated monitoring endpoints.

## 11. Exchanges and Binance Futures symbol management

Exchange CRUD, connection testing, and exchange-symbol reads are under `/api/v1/exchanges` . Binance discovery and add-symbol operations are under `/api/v1/exchanges/binance-futures` and are Admin-only.

## 12. Symbols

Symbol list/detail, sync, and active-status update are under `/api/v1/symbols` . The list uses `PagedRequest`; sync/status are mutation operations.

## 13. Market data and indicators

Market data candles, imports, settings, quality and snapshots are under `/api/v1/market-data` . Indicator snapshot/recalculation is under `/api/v1/indicators` . Market-situation analysis is `/api/v1/market-situation/current` .

## 14. Strategies and canonical portfolio

Strategy catalog, data requirements, parameters and evaluation are under `/api/v1/strategies` . New operational and research work may use only:

- `MOMO_ADAPTIVE_MTF_TREND_BREAKOUT`
- `PRICE_STRUCTURE_BREAKOUT_RETEST`
- `MOMO_VOLATILITY_RANGE_REVERSION`

Archived records may remain in storage/catalog history but are not active choices.

## 15. Backtesting

Backtest execution and read-side result families are under `/api/v1/backtests` . Trades, orders, missed orders, curves and breakdowns are nested under a backtest id; there is no generic order/trade route.

## 16. Replay

Replay resources are under `/api/v1/replay/sessions` . Controls include separate `step-forward` , `step-backward` , and `PUT .../speed` operations. Diagnostics, chart/frame and nested result families are listed in the inventory.

## 17. Paper trading

Paper accounts and sessions are resource-oriented at `/api/v1/paper/accounts` and `/api/v1/paper/sessions` . Deployment-simulation creation binds a verified qualification result transactionally; start/resume revalidate the durable binding. The route surface does not use generic start/stop resources.

## 18. Live-market subscriptions and snapshots

Live-market status, diagnostics, snapshots and subscribe/unsubscribe/reconnect are under `/api/v1/live-market` . These are market-data subscriptions and snapshots, not live trading.

## 19. SK System analysis

SK System is a diagnostic analysis system, not a trading strategy. Routes are under `/api/v1/trading-systems` with both `/sk/...` and `/sk-system/...` aliases where the controller declares them. The inventory records each alias separately.

## 20. SK LivePaper

SK LivePaper is a separate simulation/diagnostic module under `/api/v1/trading-systems/sk/livepaper` . It does not authorize real orders or live trading.

## 21. Market situation and strategy recommendations

Market situation is `/api/v1/market-situation/current` . Strategy recommendations are `/api/v1/strategy-recommendations/current` .

## 22. Strategy benchmarks

Benchmark create, preflight, paged list, progress, reports, diagnostics and lifecycle controls are under `/api/v1/strategy-benchmarks` .

## 23. Strategy Research

Validation runs, parameter optimization, parameter sets, approval, definitions and target optimization are under `/api/v1/strategy-research` . This is research workflow, not deployment qualification by itself.

## 24. Strategy Laboratory

Strategy Lab runs, reruns, candidates, risk/gate analysis and synthetic checks are under `/api/v1/strategy-lab` . New runs are limited to the canonical three-strategy portfolio.

## 25. Validation Laboratory

Validation experiments, training, holdout/closeout, leakage/exclusivity, reconciliation, selection integrity, metric audits and publication are under `/api/v1/validation-lab` . Research read/execute policy separation is enforced by controller attributes.

## 26. Risk

Risk profiles/rules, decisions and evaluation are under `/api/v1/risk` . These endpoints do not create a generic order or position API.

## 27. AI-facing .NET endpoints

Authenticated .NET AI health/decision endpoints and AdminOrTrader advisory operations are under `/api/v1/ai` . The .NET client calls the internal Python service documented in section 36.

## 28. Reports and simulation summaries

Reports are under `/api/v1/reports` beginning with `/overview` and nested backtest/paper families. Simulation summaries are under `/api/v1/simulation-summaries` .

## 29. Monitoring

Monitoring health, subsystem checks, status, system-health logs, recent errors/events, safety events and trading-pipeline status are under `/api/v1/monitoring` .

## 30. Audit queries

Audit queries are Admin-only under `/api/v1/audit/logs` , `/api/v1/audit/logs/{id}` , and `/api/v1/audit/summary` .

## 31. Exports

Export scopes, job creation/status and raw file download are under `/api/v1/exports` . Download is a file response rather than `ApiResponse<T>` .

## 32. Trading settings

Trading settings read/update/reset are under `/api/v1/settings/trading` . Reset defaults is Admin-only.

## 33. Admin cleanup

Fake-market-data cleanup is under `/api/v1/admin/data-cleanup` . Clean-baseline preview/execute is under `/api/v1/admin/system-cleanup` . Both are Admin-only.

## 34. Users

Admin-only user list/detail/create/update/disable routes are under `/api/v1/users` .

## 35. SignalR

Exactly one hub is registered: `/hubs/live-market` . Production event names and payload sources are documented in the route inventory. The frontend currently has no verified connection consumer.

## 36. Internal Python service

FastAPI exposes `GET /health` plus four advisory POST routes: regime detect, confidence score, anomaly detect, and trade explanation. It does not place orders or approve risk. Python responses are not wrapped in the .NET `ApiResponse<T>` contract.

## 37. Explicitly missing or deferred public areas

The gap register is the only place future/missing scope is recorded. At this SHA there is no implemented public API-key vault, generic trading-session resource, generic bot control, live trading/order placement, top-level signals/orders/trades/positions, notifications, or additional SignalR hubs. These examples are historical/deferred, not current contracts. Live trading remains blocked.

## 38. Current implementation order and continuation boundary

Documentation reconciliation is complete at the pinned SHA. Future coding requires an authoritative narrow milestone prompt based on the gap register; do not invent B1C6D2, B1C6D3, or B1C7. The next agent must inspect remote `main` and the latest diff before acting.

## 39. Critical API safety rules

Required authoritative evidence uses typed allowlisted metadata, the owning scoped `MomoQuantDbContext` , attach-only writes, one transaction, fail-closed behavior, and cancellation propagation. Legacy operational telemetry uses an isolated context, sanitizes before persistence, is best-effort, logs safe failures, does not commit caller state, and also propagates cancellation.

Required actions are `PARAMETER_SET_DEPLOYMENT_QUALIFIED`, `PAPER_DEPLOYMENT_QUALIFICATION_VERIFIED`, `PAPER_SESSION_CREATED`, `PAPER_SESSION_STARTED`, `PAPER_SESSION_RESUMED`, `PAPER_SESSION_FAILED` . New safety-critical or authoritative state changes require an explicit durability classification and transactional required-evidence design before adoption; detailed action-catalog reconciliation is deferred to B1C6D3.

## 40. References to inventory and gap register

Use `docs/04-api-route-inventory.md` for the 288-row implemented route surface and `docs/04-api-gap-register.md` for explicit mismatches, missing candidates, future dependencies, and unresolved product decisions.
