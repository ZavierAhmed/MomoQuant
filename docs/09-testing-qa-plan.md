# MOMO Quant

## Part 9 — Testing & Quality Assurance Plan

**Version:** 1.0 Draft

---

## 1. Testing Principle

MOMO Quant is not good because it compiles. It is good only if it calculates correctly, stores decisions correctly, rejects unsafe trades, simulates execution realistically, explains decisions, handles failure safely, and prevents accidental live trading.

Highest-risk areas: risk engine, execution simulation, backtesting, strategies, indicators, mode safety, emergency stop, AI failure handling.

---

## 2. Testing Layers

```text
Unit Tests
Integration Tests
API Tests
UI Tests
End-to-End Workflow Tests
Manual Replay Validation
Paper Trading Validation
```

Backend tests: MomoQuant.UnitTests and MomoQuant.IntegrationTests. Python tests in momo-ai/app/tests. Frontend tests later.

---

## 3. Unit Tests

Test indicators, strategy signals, risk rules, position sizing, PnL, order fills, backtest performance, AI scoring, regime detection, timeframe helpers, decimal precision.

---

## 4. Indicator Tests

Test EMA, VWAP, RSI, ATR, Volume SMA, swing high/low, market structure.

Examples:

- EMA returns expected value for known series.
- ATR increases when ranges expand.
- VWAP matches manual calculation.
- RSI stays 0-100.
- Swing high needs surrounding confirmation.

---

## 5. Strategy Tests

For Liquidity Sweep, VWAP Mean Reversion, and EMA Pullback, verify correct signal, no signal in invalid conditions, direction, explanation, supported regimes/timeframes, and that strategies never place orders or approve risk.

Example: EMA Pullback should generate Long when EMA20 > EMA50 > EMA200, price pulls back near EMA20, bullish confirmation closes, and volume is acceptable.

---

## 6. AI Tests

Test regime detection, confidence scoring, anomaly detection, explanation, invalid input, missing candles, extreme values, schema validation, timeout/fallback.

Examples:

- Trending candles return Trending.
- Overlapping candles return Ranging/Choppy.
- High spread returns anomaly.
- Strong aligned signal returns high confidence.
- Invalid payload returns validation error.

---

## 7. Risk Engine Tests

Mandatory tests for max risk per trade, daily/weekly loss, max open positions, exposure, correlated exposure, consecutive losses, min confidence, max spread, max volatility, emergency stop.

Cases:

- Approved when all rules pass.
- Rejected when confidence too low.
- Rejected when daily loss hit.
- Rejected when max positions reached.
- Rejected when emergency stop active.
- Position size adjusted when needed.
- Rejected rule key and reason stored.

If risk fails, system is unsafe.

---

## 8. Execution Simulation Tests

Test limit order creation, post-only behavior, fills when price touches, no fill when price moves away, timeout, partial fill, fee calculation, missed order logging, cancellation, trade/position update after fill.

Maker-only scalping can look fake-profitable if fills are simulated badly.

---

## 9. Backtesting Tests

Test candle ordering, indicators before strategies, strategy per candle, AI decision storage, risk decision storage, order simulation, trade creation, PnL, fees, missed orders, result metrics.

Also test no candles, duplicates, missing indicators, AI unavailable, disabled strategy, missing risk profile, invalid date range.

---

## 10. Replay Tests

Test session creation, start from correct candle, step forward, pause/resume, speed change, timeline events, decision state including signal/AI/risk/execution, and no-trade reasons.

Replay is the debugging microscope.

---

## 11. Paper Trading Tests

Test paper account creation, start/stop, live market data consumption, signal generation, AI call, risk evaluation, simulated orders, position update, account balance update, and no real exchange orders.

Also test exchange data stops, AI fails, Redis unavailable, emergency stop, daily loss limit.

---

## 12. API and Database Tests

API integration tests: login, me, users, exchange, symbols, candle import, indicator recalc, strategy enable/disable, risk profile, backtest, replay, paper account, reports, health, audit.

Database tests: migrations apply, indexes exist, candle uniqueness, decimal precision, relationships, delete restrictions, audit insert, UTC timestamps.

---

## 13. Security and Mode Safety Tests

Security tests: unauthenticated blocked, Viewer cannot start bot, Trader cannot enable live, only Admin manages users/API keys, JWT expiration, secrets hidden, dangerous actions audited.

Mode safety: default not Live, Live blocked without readiness, admin required, confirmation required, paper validation required, health required, emergency stop blocks trades, clear emergency stop admin-only.

---

## 14. Reporting Tests

Test PnL, net PnL, fees, win rate, profit factor, expectancy, average win/loss, drawdown, strategy/symbol/regime performance, rejected counts, missed counts, AI confidence buckets.

Reports must be based on stored facts.

---

## 15. End-to-End Local Test

1. Start MySQL and Redis.
2. Run migrations.
3. Start .NET API.
4. Start Python AI.
5. Start React dashboard.
6. Login as Admin.
7. Create/seed exchange and symbols.
8. Import BTCUSDT 3m candles.
9. Recalculate indicators.
10. Enable EMA Pullback.
11. Create conservative risk profile.
12. Run backtest.
13. View signals, AI, risk, trades, reports.
14. Create replay and step through decisions.
15. Create paper account.
16. Start paper trading.
17. Trigger emergency stop.
18. Confirm paper trading stops safely.

---

## 16. Smoke Test Checklist

Backend builds, frontend builds, AI starts, migrations work, login works, dashboard loads, health healthy, symbols/strategies/risk load, backtest starts, replay starts, paper account loads, SignalR connects, emergency stop works.

---

## 17. Test Data

Create datasets for trending, ranging, choppy, high-volatility, liquidity sweep, VWAP reversion, EMA pullback, missing candles, duplicate candles, extreme spread. Repeatable datasets beat random live data.

---

## 18. Backtest and Paper Validation

Before trusting a strategy, test multiple symbols/months/regimes, include fees/spread/maker assumptions, track missed orders, avoid optimizing on one period, compare in-sample/out-of-sample.

Before live, require weeks of stable paper trading, multiple backtests, replay review of wins/losses, review of risk rejections and missed orders.

---

## 19. Failure Testing

Test AI down, Redis down, DB down, exchange API down, stale WebSocket, invalid candles, simulation failure, report failure, expired JWT, permission denial, emergency stop during active paper trade. Expected behavior: fail clearly, log error, protect trading, never continue unsafe silently.

---

## 20. Definition of Done

Feature is done when it builds, passes tests, handles bad input, logs important actions, uses standard response format, respects permissions, follows architecture, is visible in dashboard if needed, fails clearly, and can be manually verified.

For trading logic: it stores decisions, explains decisions, respects risk authority, and does not bypass safety controls.

Most important rule: never trust a trade result unless you can trace Candle -> Indicator -> Strategy Signal -> AI Decision -> Risk Decision -> Order -> Fill -> Trade -> Report.
