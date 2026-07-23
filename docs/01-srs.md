# MOMO Quant

## Part 1 — Software Requirements Specification

**Version:** 1.0 Draft  
**Product Type:** AI-assisted quantitative crypto futures trading platform  
**Primary Developer:** Solo developer  
**Initial Build Style:** Modular monolith with future service-splitting support

---

## 1. Purpose

MOMO Quant is an AI-assisted trading platform for crypto futures markets. It supports historical backtesting, replay mode, paper trading, and later live trading. The system uses deterministic trading strategies supported by AI-based market classification, confidence scoring, parameter optimization, anomaly detection, trade explanation, and performance analysis.

AI must not directly place trades. The risk engine and execution engine control final trade approval.

---

## 2. Product Vision

MOMO Quant is a trading research and automation platform, not just a bot. It must let the developer test strategies, replay decisions, run paper trading, monitor performance, understand why trades were taken/rejected, improve with data, and only move to live trading after validation.

---

## 3. Initial Trading Scope

MVP focuses on:

- Crypto futures/derivatives.
- Top 5 high-liquidity coins.
- 3-minute and 5-minute charts.
- 15-minute higher timeframe confirmation.
- Maker-style limit orders in simulation and later live mode.
- Strategies: Liquidity Sweep, VWAP Mean Reversion, EMA Pullback.

The system must not require all strategies to agree. It should use market regime detection and weighted confidence scoring.

---

## 4. Trading Modes

### Backtesting

Historical testing with selected symbols, date range, timeframe, strategies, fees, spread assumptions, maker-fill assumptions, slippage model, and full performance report.

### Replay Mode

Replay historical candles with play/pause/speed controls and visible candles, indicators, signals, AI confidence, risk decisions, executions, and no-trade reasons. Replay is mandatory for debugging.

### Paper Trading

Uses live market data with simulated execution. Must share strategy, AI, risk, and execution abstractions with live trading. Only the execution provider changes.

### Live Trading

Added only after backtesting and paper trading are stable. Disabled by default and protected by readiness checks, admin confirmation, audit logging, and emergency stop.

---

## 5. Core Modules

- Authentication
- User Management
- Exchange Management
- Market Data
- Candle Storage
- Indicator Engine
- Strategy Engine
- AI Decision Engine
- Risk Management
- Execution
- Backtesting
- Replay
- Paper Trading
- Live Trading Readiness
- Reporting
- Monitoring
- Notifications
- Audit Logging
- Settings

---

## 6. Strategy Requirements

Every strategy must expose name, description, supported timeframes, supported regimes, required indicators, entry/exit rules, stop-loss/take-profit rules, confidence contribution, explanation, and backtest compatibility.

Strategies generate signals only. They must never place orders or approve risk.

---

## 7. AI Requirements

AI responsibilities:

- Market regime classification.
- Confidence scoring.
- Strategy weighting.
- Parameter optimization.
- Trade explanation.
- Anomaly detection.
- Performance pattern discovery.

AI output is advisory. AI cannot bypass risk rules.

---

## 8. Risk Management

Risk engine has final authority. Required rules include max risk per trade, daily/weekly loss limits, max open positions, exposure limits, correlated exposure limits, max consecutive losses, minimum confidence, max spread, max volatility, session filters, and emergency stop.

Stop losses should be ATR-based or structure-based, not random fixed percentages.

---

## 9. Execution Requirements

Execution supports maker-style limit orders, post-only flag, reduce-only flag, order timeout, cancel/replace, partial fills, fill tracking, order reconciliation, simulated execution for backtest/paper, and live exchange execution later. Missed maker orders must be logged separately.

---

## 10. Reporting and Monitoring

Reports must include PnL, win rate, profit factor, expectancy, average win/loss, max drawdown, fees, funding, best/worst symbol, best/worst strategy, performance by regime/timeframe/session, rejected reasons, and missed trade reasons.

Monitoring must track bot status, mode, active symbols/strategies, positions, orders, WebSocket/API health, database, Redis, AI service, errors, latency, and last market/execution update.

---

## 11. Security

Use JWT, role-based authorization, encrypted exchange API keys, audit logs, environment secrets, HTTPS in production, live disabled by default, confirmation before live mode, and dashboard-accessible emergency stop.

---

## 12. MVP Boundary

MVP includes one exchange integration, top 5 symbols, 3m/5m candles, 15m confirmation, three strategies, backtesting, replay, paper trading, reporting, monitoring, AI confidence scoring, risk engine, dashboard, and live readiness checks.

MVP excludes real money trading, multi-exchange routing, mobile app, advanced portfolio optimization, reinforcement learning, marketplace, and public SaaS features.

---

## 13. Success Criteria

MOMO Quant v1 is successful if it can import historical candles, calculate indicators, run backtests, replay decisions, paper trade realistically, log decisions, generate reports, monitor health, support the three strategies, use AI for confidence/regime detection, and prevent accidental live trading.
