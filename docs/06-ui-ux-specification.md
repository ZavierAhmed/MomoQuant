# MOMO Quant

## Part 6 — UI/UX Specification

**Version:** 1.0 Draft  
**Frontend:** React + Vite + Tailwind CSS  
**Realtime:** SignalR

---

## 1. UI Principle

The dashboard is a control panel and debugging surface, not a marketing site. It must make mode, bot status, live state, symbols, strategies, risk limits, trade reasons, rejected reasons, health, and emergency stop obvious.

The UI must reduce confusion, not create excitement. It should feel like infrastructure, not a casino.

---

## 2. Core UX Rules

1. Current mode always visible.
2. Live mode must look obviously different from paper mode.
3. Emergency stop always accessible.
4. Dangerous actions require confirmation.
5. Reports explain performance clearly.
6. Every trade is traceable to signal, AI, risk, and order.
7. Rejected trades show reasons.
8. Use tables for detail and cards for summaries.
9. Do not hide important warnings.

---

## 3. Layout

```text
Top Bar: product, mode badge, bot status, health, emergency stop, user
Left Sidebar: navigation
Main Content: page content
Right Drawer/Modal: trade details, logs, explanation
```

Global header example:

```text
MOMO Quant | PAPER MODE | Bot: Running | Health: Healthy | Emergency Stop
```

Live mode must use high-visibility warning style.

---

## 4. Navigation

```text
Dashboard
Bot Control
Market Watch
Strategies
Backtesting
Replay Mode
Paper Trading
Trades
Orders
Positions
Reports
AI Analytics
Risk Management
Monitoring
Logs
Settings
Admin
```

Admin includes Users, Roles, Exchanges, API Keys, Audit Logs.

---

## 5. Dashboard Home

Summary cards:

- Current Mode
- Bot Status
- System Health
- Active Symbols
- Active Strategies
- Open Positions
- Open Orders
- Daily PnL
- Total PnL
- Win Rate
- Max Drawdown
- Emergency Stop Status

Main sections: bot status, active session, PnL summary, latest signals, latest trades, rejected reasons, system health, AI summary.

---

## 6. Bot Control

Controls: mode selector, start, stop, pause, resume, emergency stop, clear emergency stop, symbol selector, timeframe selector, strategy selector, risk profile selector.

Modes: Backtest, Replay, Paper, Live. Live is locked until readiness checks pass, requires admin role, typed confirmation, and warning panel.

Live confirmation text: `ENABLE LIVE TRADING`. Checkbox-only confirmation is too weak.

---

## 7. Market Watch and Chart

Market watch shows symbol list, price, 24h volume, spread, current candle, timeframe, market regime, AI confidence, active strategies, latest signal. Initial symbols: BTCUSDT, ETHUSDT, SOLUSDT, BNBUSDT, XRPUSDT.

Chart shows candles, EMA20/50/200, VWAP, entries, exits, SL, TP, signals, and rejected markers. Keep the default chart readable.

---

## 8. Strategies

Strategy page shows strategy list, enabled status, supported regimes/timeframes, parameter editor, performance summary, and recent signals.

Each detail page shows description, entry/exit summary, supported regimes, current parameters, backtest/paper performance, recent trades, and rejected signals.

Parameter changes must create audit logs. Risky parameters require warning before saving.

---

## 9. Backtesting

Inputs: name, exchange, symbols, timeframe, higher timeframe, date range, initial balance, strategies, risk profile, fees, slippage, maker simulation, order timeout.

Results show PnL, fees, win rate, profit factor, expectancy, max drawdown, total trades, average win/loss, missed orders, rejected signals, performance by symbol/strategy/regime, equity curve, drawdown curve, trade list, signal list.

---

## 10. Replay Mode

Replay is critical. Layout includes chart, controls, timeline, candle details, indicators, signals, AI panel, risk panel, execution panel, and trade log.

Controls: play, pause, step forward, optional step back later, speed 1x/2x/10x/50x/100x, restart, stop.

Timeline shows time, regime, signal, confidence, risk decision, execution decision, trade result, and no-trade reason.

---

## 11. Paper Trading

Shows balance, equity, open positions, open orders, daily/total PnL, symbols, strategies, trades, signals, rejected trades. Must clearly display `PAPER MODE — SIMULATED EXECUTION`.

---

## 12. Trades, Orders, Positions

Trades table: ID, time, session, mode, symbol, strategy, direction, entry/exit, quantity, net PnL, fees, R multiple, status, close reason. Detail drawer includes signal, AI, risk, orders, fills, fees, PnL, explanation.

Orders table: order ID, mode, symbol, side, type, price, quantity, status, post-only, reduce-only, submitted/filled time, failure reason.

Positions table: symbol, direction, quantity, average entry, mark price, unrealized/realized PnL, leverage, margin, status.

---

## 13. Reports

Report categories: performance, strategy, symbol, market regime, risk, fee, missed trade, rejected trade, AI confidence.

Rejected report shows confidence too low, risk limit exceeded, spread too high, choppy regime, max positions, daily loss, anomaly.

Missed trade report shows maker not filled, price moved away, timeout, spread too wide, partial fill.

---

## 14. AI Analytics

Sections: AI decisions, confidence buckets, market regime analysis, AI approved vs rejected outcomes, AI errors, parameter recommendations, model versions.

Confidence buckets: 70-79, 80-89, 90-100. If higher confidence does not outperform lower confidence, AI scoring is weak.

---

## 15. Risk, Monitoring, Logs, Settings

Risk page shows profiles, rules, current state, daily loss, consecutive losses, exposure, correlation, emergency stop.

Monitoring shows API, DB, Redis, AI, exchange REST/WebSocket, workers, latency, errors, last market update, last AI response, last order update. Status: Healthy, Warning, Critical, Stopped.

Logs include system, trading, AI, risk, execution, exchange, audit, error logs with filters.

Settings categories: general, trading, market data, strategies, AI, risk, execution, reporting, monitoring, security.

---

## 16. Frontend State and Structure

Use React Query/TanStack Query for server state, Zustand or Context for lightweight UI state, and SignalR hooks for realtime updates. Avoid Redux unless necessary.

Feature-based structure:

```text
src/
  app/
  components/
    common, layout, charts, tables, forms, modals, status, trading, reports
  features/
    auth, dashboard, bot-control, market-watch, strategies, backtesting, replay, paper-trading, trades, orders, positions, reports, ai-analytics, risk, monitoring, logs, settings, admin
  services/api
  services/signalr
  hooks
  types
  utils
```

Reusable components: ModeBadge, BotStatusBadge, HealthBadge, EmergencyStopButton, MetricCard, DataTable, DateRangePicker, SymbolSelector, StrategySelector, RiskProfileSelector, ConfirmationModal, TradeDetailDrawer, AiDecisionPanel, RiskDecisionPanel, OrderStatusBadge, PnLBadge.

---

## 17. MVP UI Scope

Build first: login, dashboard, bot control, market watch, strategies, backtesting, replay, paper trading, trades, orders, reports, AI analytics, risk, monitoring, settings, audit logs.

Delay: advanced notifications, mobile layout, theme customization, multi-exchange comparison, dashboard builder, advanced chart annotations, public SaaS/subscription UI.

---

## 18. Most Important UI Rule

Make dangerous actions difficult and important information obvious.
