# MOMO Quant

## Part 2 — System Architecture Document

**Version:** 1.0 Draft  
**Architecture Style:** Modular Monolith + Python AI Service

---

## 1. Architecture Decision

MOMO Quant starts as a modular monolith, not microservices. The backend is one .NET application internally separated into clean modules. This is the right tradeoff for a solo developer because it avoids networking, deployment, debugging, and distributed logging complexity.

```text
React Dashboard
        |
        v
.NET 8 Backend API
        |
        +-- Market Data
        +-- Indicators
        +-- Strategies
        +-- AI Decision
        +-- Risk
        +-- Execution
        +-- Backtesting
        +-- Replay
        +-- Paper Trading
        +-- Reporting
        +-- Monitoring
        |
        +-- MySQL
        +-- Redis
        |
        v
Python AI Service
```

---

## 2. Runtime Components

1. React Dashboard: control, monitoring, replay, reports, settings.
2. .NET Backend: authentication, APIs, orchestration, strategies, risk, execution, reporting, audit.
3. Python AI Service: regime detection, confidence scoring, anomaly detection, explanations, optimization later.
4. MySQL + Redis: persistence, runtime cache/state, queues/pub-sub later.

---

## 3. Backend Solution Structure

```text
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
```

Responsibilities:

- Api: controllers, SignalR hubs, middleware, auth endpoints. No trading logic.
- Application: use cases and orchestration.
- Domain: entities, enums, interfaces, rules. No DB/API dependencies.
- Infrastructure: exchange clients, WebSockets, Redis, AI HTTP client, notifications.
- Persistence: EF Core DbContext, mappings, repositories, migrations.
- Worker: market collection, candle builder, paper loop, backtests, health checks.
- Shared: DTO primitives, result models, pagination, constants.

---

## 4. Application Modules

```text
Auth, Users, Exchanges, MarketData, Indicators, Strategies, AI, Risk,
Execution, Backtesting, Replay, PaperTrading, LiveTrading, Reporting,
Monitoring, AuditLogs, Settings
```

Each module should contain Commands, Queries, Services, DTOs, Validators, and Mappings as needed.

---

## 5. Strategy Architecture

Every strategy implements a common interface.

```csharp
public interface ITradingStrategy
{
    string Name { get; }
    StrategySignal Evaluate(StrategyContext context);
    IReadOnlyCollection<MarketRegime> SupportedRegimes { get; }
    IReadOnlyCollection<Timeframe> SupportedTimeframes { get; }
}
```

Initial strategies:

- LiquiditySweepStrategy
- VwapMeanReversionStrategy
- EmaPullbackStrategy

Strategies produce signals only. They never execute orders or approve risk.

---

## 6. Trading Decision Flow

```text
Market Data
   -> Indicator Engine
   -> Market Regime Detector
   -> Strategy Engine
   -> AI Confidence Engine
   -> Risk Engine
   -> Execution Engine
   -> Trade Logger
```

Execution happens only after risk approval.

---

## 7. Trading Modes and Execution Providers

Backtest, Paper, and future Live modes share strategy, AI, risk, and execution abstractions. Only the execution provider changes.

```csharp
public interface IExecutionProvider
{
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken cancellationToken);
    Task<OrderStatus> GetOrderStatusAsync(string orderId, CancellationToken cancellationToken);
    Task<CancelOrderResult> CancelOrderAsync(string orderId, CancellationToken cancellationToken);
}
```

Default mode is Backtest or Paper, never Live. Live requires admin, explicit confirmation, credentials, risk rules, emergency stop, and paper validation.

---

## 8. AI Service Architecture

Internal endpoints:

```text
POST /ai/regime/detect
POST /ai/confidence/score
POST /ai/parameters/optimize
POST /ai/anomaly/detect
POST /ai/trade/explain
```

AI output is stored in AiDecisions. AI is advisory; risk can reject AI-approved trades.

---

## 9. Risk Engine

Risk engine receives signal, AI decision, account state, open positions, volatility, spread, PnL, and rules. It returns Approved, Rejected, Adjusted, or EmergencyBlocked with reasons and adjusted sizing/SL/TP.

Risk is final gate before execution.

---

## 10. Market Data and Indicators

MVP starts with candles first. Later order book, funding rates, and open interest can be added. Indicators include EMA, VWAP, RSI, ATR, Volume SMA, swing highs/lows, and market structure. Strategies must consume indicator snapshots rather than calculating internally.

---

## 11. Backtesting and Replay

Backtesting replays historical candles internally and runs the same decision chain. It must include fees, spread, slippage, maker fill assumptions, missed orders, and later partial fills.

Replay uses backtesting logic but exposes timeline state to UI: candle, indicators, signals, AI, risk, execution, trade, and no-trade reason.

---

## 12. Paper Trading

Paper trading uses live market data with simulated execution and paper portfolio state. It should behave as close to live as possible.

---

## 13. Realtime Updates

Use SignalR hubs for bot status, market updates, trading events, replay ticks, and monitoring. Events include SignalGenerated, AiDecisionCreated, RiskDecisionCreated, OrderUpdated, TradeOpened, TradeClosed, MissedOrderCreated, and HealthChanged.

---

## 14. Configuration

Use appsettings.json for defaults and database settings for runtime configurable values. Secrets must not be stored plain text.

---

## 15. Critical Rules

1. Strategies never place orders.
2. AI never places orders.
3. Risk engine has final authority.
4. Modes share core logic.
5. Execution provider changes by mode.
6. Every decision is logged.
7. Live trading disabled by default.
8. Domain does not depend on database or infrastructure.
9. Dashboard does not contain trading decisions.
10. Every trade must be explainable.
