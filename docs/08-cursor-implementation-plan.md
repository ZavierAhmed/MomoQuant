# MOMO Quant

## Part 8 — Cursor Implementation Plan

**Version:** 1.0 Draft

---

## 1. Implementation Philosophy

Do not ask Cursor to generate the whole app. Build milestone by milestone with docs, acceptance criteria, build verification, tests, manual review, and commits.

Global Cursor rules:

```text
Follow /docs.
Use .NET 8 backend, React + Vite + Tailwind frontend, MySQL EF Core, Redis, Python FastAPI AI.
Build modular monolith plus Python AI service.
Do not implement live trading in MVP.
AI never places orders.
Strategies never place orders.
Risk engine approves every trade before execution.
Backtest, replay, paper, future live share abstractions.
Log every important decision.
Use UTC timestamps.
Use decimal for financial values.
Return full updated files when modifying code.
Add tests for important logic.
```

---

## 2. Repository Structure

```text
momo-quant/
  docs/
  src/
    backend/
      MomoQuant.sln
      src/
        MomoQuant.Api/
        MomoQuant.Application/
        MomoQuant.Domain/
        MomoQuant.Infrastructure/
        MomoQuant.Persistence/
        MomoQuant.Worker/
        MomoQuant.Shared/
      tests/
        MomoQuant.UnitTests/
        MomoQuant.IntegrationTests/
    frontend/momo-dashboard/
    ai/momo-ai/
  deploy/
    docker-compose.yml
    docker-compose.override.yml
    .env.example
  scripts/
    backup-db.sh
    restore-db.sh
    deploy.sh
    logs.sh
  README.md
```

---

## 3. Milestones

```text
1  Repository and solution skeleton
2  Backend domain models and shared contracts
3  Database entities and EF Core setup
4  Authentication and authorization
5  Exchange and symbol management
6  Historical candle import and storage
7  Indicator engine
8  Strategy framework
9  Risk engine
10 Python AI service skeleton
11 .NET AI integration
12 Backtesting engine
13 Replay engine
14 Paper trading engine
15 Reporting engine
16 Monitoring and audit logging
17 React dashboard shell
18 Dashboard feature pages
19 Docker local environment
20 Deployment preparation
21 Live trading readiness only
```

---

## 4. Milestone Prompt Template

```text
Context:
You are working on MOMO Quant. Read the relevant docs in /docs.

Relevant docs:
- /docs/...

Task:
Implement [specific milestone].

Requirements:
[list exact requirements]

Architecture rules:
[list safety rules]

Acceptance criteria:
[list what must work]

Output:
Return full updated files.
```

---

## 5. Milestone 1 — Skeleton

Create repository structure, .NET solution, projects, references, React app folder, Python AI folder, deploy/scripts/docs folders. No trading logic.

Acceptance: solution builds, project references correct, repository matches docs.

---

## 6. Milestone 2 — Domain

Create domain models and enums for exchanges, symbols, candles, indicators, strategies, sessions, signals, AI decisions, risk decisions, orders, fills, missed orders, trades, positions, backtests, replay, paper accounts, audit logs, health logs, settings.

Rules: decimal for financial values, UTC timestamps, no EF dependency in Domain.

---

## 7. Milestone 3 — Persistence

Implement EF Core + MySQL with Pomelo, MomoQuantDbContext, MVP tables, relationships, decimal(28,12), indexes, unique candle index, delete restrictions, design-time factory, initial migration.

---

## 8. Milestone 4 — Auth

Implement JWT auth, roles Admin/Trader/Viewer, AuthController, UsersController, password hashing, current user service, seed dev admin only for development.

---

## 9. Milestone 5 — Exchange and Symbols

Implement exchanges, symbols, fake/local symbol provider, test connection interface, audit logs. Do not hardwire real exchange yet unless behind interface.

---

## 10. Milestone 6 — Candles

Implement candle import/query/status/snapshot. Support local JSON/CSV or fake provider first. Enforce UTC, unique index, batch inserts, no duplicates.

---

## 11. Milestone 7 — Indicators

Implement EMA20/50/200, VWAP, RSI14, ATR14, VolumeSMA20, swing high/low, basic market structure, snapshots, APIs, tests.

---

## 12. Milestone 8 — Strategies

Implement ITradingStrategy, StrategyContext, StrategySignalResult, StrategyEngine, registry, parameter provider, and MVP strategies: Liquidity Sweep, VWAP Mean Reversion, EMA Pullback. Strategies only produce signals and explanations.

---

## 13. Milestone 9 — Risk

Implement risk engine, risk context/result, risk profiles/rules/decisions, emergency stop, and rules for max risk, daily/weekly loss, positions, exposure, correlation, confidence, spread, volatility. Risk is final authority.

---

## 14. Milestone 10 — Python AI

Create FastAPI service with /health and AI endpoints. Implement rule-based regime detection, weighted confidence, simple anomaly detection, explanation generation, tests. No deep learning.

---

## 15. Milestone 11 — .NET AI Integration

Implement AI client, DTOs, timeout handling, fallback, AiDecision persistence, AI dashboard APIs. AI failures must not crash the app.

---

## 16. Milestone 12 — Backtesting

Implement backtest runner, execution provider, portfolio, performance calculator, controller. Flow: load candles, indicators, regime, strategies, AI, risk, simulated execution, store all decisions/orders/fills/trades, calculate results. Include fees, slippage, order timeout, missed maker orders.

---

## 17. Milestone 13 — Replay

Reuse backtest logic but expose step-by-step state through APIs and SignalR. Replay tick includes candle, indicators, signal, AI, risk, execution, trade updates, no-trade reason.

---

## 18. Milestone 14 — Paper Trading

Implement paper account, simulated execution provider, paper portfolio, start/stop, snapshots, trades. Must never place real orders.

---

## 19. Milestone 15 — Reporting

Implement dashboard summary, performance, strategy, symbol, regime, rejection, missed-trade, AI performance reports from stored facts.

---

## 20. Milestone 16 — Monitoring and Audit

Implement health checks, monitoring endpoints, audit logging for state changes, monitoring SignalR events.

---

## 21. Milestone 17 — Dashboard Shell

Create React layout, top bar, sidebar, protected routes, auth provider, API client, SignalR setup, reusable components, placeholder pages.

---

## 22. Milestone 18 — Dashboard Features

Connect pages to APIs with React Query and SignalR. Dangerous actions require confirmation. Live UI remains locked. Paper mode clearly labeled.

---

## 23. Milestone 19 — Local Docker

Create Docker Compose for MySQL and Redis, optional API/AI/dashboard, .env.example, local README.

---

## 24. Milestone 20 — Deployment Preparation

Create production Dockerfiles, Nginx config, production compose profile, backup/restore/deploy/log scripts, health checks, deployment README.

---

## 25. Milestone 21 — Live Readiness Only

Implement readiness checks and live enable/disable endpoints while keeping real order placement blocked. Enabling live must be audited and admin-confirmed.

---

## 26. Branch Strategy and Definition of Done

Use main, develop, feature/milestone-xx branches. Commit after each working milestone.

Done means: builds, tests pass, migrations work if needed, APIs/UI work if applicable, errors handled, audit logs for state changes, no secrets committed, docs updated, smoke tested.

---

## 27. First End-to-End Local Test

Login as admin, create exchange, seed/sync symbols, import BTCUSDT candles, recalc indicators, enable EMA Pullback, create conservative risk profile, run backtest, view signals/AI/risk/trades/report, open replay, step decisions, create paper account, start paper trading, trigger emergency stop.

---

## 28. What Not to Build Early

Do not build real live execution, Kubernetes, multi-exchange routing, advanced AI, reinforcement learning, mobile app, subscription billing, public SaaS, advanced notifications, order book strategies, or news sentiment early.
