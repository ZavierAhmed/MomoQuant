# MOMO Quant

## Part 5 — AI & Machine Learning Design Document

**Version:** 1.0 Draft  
**AI Service:** Python FastAPI

---

## 1. AI Principle

MOMO Quant is not a black-box AI trading bot. The correct design is deterministic strategy logic + AI assistance + strict risk management. AI advises; risk controls; execution obeys.

AI must not directly place orders, bypass risk, enable live trading, change risk rules automatically, or rewrite strategy rules.

---

## 2. AI Responsibilities

1. Market Regime Detection
2. Trade Confidence Scoring
3. Parameter Optimization
4. Anomaly Detection
5. Trade Explanation

Each responsibility is a separate module. MVP starts with explainable rules and weighted scoring. Do not start with deep learning.

---

## 3. Python Service Structure

```text
momo-ai/
  app/
    main.py
    api/
      regime_routes.py
      confidence_routes.py
      optimization_routes.py
      anomaly_routes.py
      explanation_routes.py
    core/
      config.py
      logging.py
      schemas.py
    services/
      regime_service.py
      confidence_service.py
      optimization_service.py
      anomaly_service.py
      explanation_service.py
    features/
      feature_builder.py
      candle_features.py
      indicator_features.py
      trade_features.py
    models/
    storage/
    tests/
```

---

## 4. Integration Flow

```text
Market Data -> Indicator Engine -> Strategy Engine -> .NET builds AI request ->
Python AI Service -> AI response saved to AiDecisions -> Risk Engine -> Execution
```

The AI result must always be stored.

---

## 5. Market Regime Detection

Supported regimes:

```text
Trending
Ranging
Breakout
Reversal
HighVolatility
LowVolatility
Choppy
Abnormal
Unknown
```

Input includes symbol, timeframe, higher timeframe, candles, EMA, VWAP, RSI, ATR, volume SMA, and market structure.

Initial rule examples:

- EMA20 > EMA50 > EMA200 + price above EMA50 + higher highs/lows -> Trending.
- Repeated VWAP crosses + overlapping candles + low/moderate ATR -> Ranging.
- ATR expansion + high volume + large candle body -> HighVolatility/Breakout.

Output includes marketRegime, confidence, and explanation.

---

## 6. Confidence Scoring

MVP uses a transparent weighted model:

```text
Strategy signal strength       0–40
Market regime alignment        0–20
Higher timeframe confirmation  0–15
Volume confirmation            0–10
Volatility quality             0–10
Spread/execution quality       0–5
```

Default thresholds:

```text
>= 80  -> trade allowed
70-79  -> watchlist / no trade
< 70   -> reject
```

These values must be configurable.

---

## 7. Strategy Weighting

Weights depend on market regime. Example:

```text
Trending:
  EMA_PULLBACK 45%
  LIQUIDITY_SWEEP 25%
  VWAP_MEAN_REVERSION 10%

Ranging:
  VWAP_MEAN_REVERSION 45%
  LIQUIDITY_SWEEP 30%
  EMA_PULLBACK 10%

Choppy:
  all strategies reduced or disabled
```

Weights are configurable, not permanently hardcoded.

---

## 8. Parameter Optimization

Optimizable parameters include EMA periods, ATR multiplier, minimum confidence, VWAP deviation, liquidity sweep size, stop-loss multiplier, take-profit multiplier, and order timeout.

Avoid overfitting. Use train/validate/test and walk-forward validation. Do not optimize only for net profit; evaluate profit factor, max drawdown, expectancy, trade count, Sharpe/Sortino, fee impact, and missed order rate.

MVP recommendations require manual approval before applying.

---

## 9. Anomaly Detection

Detect exchange latency spikes, WebSocket gaps, spread expansion, abnormal candle size, volume shock, API failures, and extreme volatility.

Severity:

```text
Low      -> Continue
Medium   -> Reduce confidence
High     -> Block new trades
Critical -> Emergency stop
```

---

## 10. Trade Explanation

Every trade and rejected setup must be explainable. Explanation input includes signal, AI decision, risk decision, and execution decision. Output is human-readable.

Example:

```text
Long trade approved because market regime was Trending, EMA Pullback produced a strong bullish signal, confidence was 86, higher timeframe confirmed direction, and risk limits were valid.
```

Rejected setups are as important as executed trades.

---

## 11. Feature Engineering

Feature categories:

- Price features
- Volume features
- Volatility features
- Trend features
- Market structure features
- Strategy signal features
- Risk context features
- Execution quality features
- Historical performance features

Examples: price_above_ema20, ema20_slope, atr_percent, volume_vs_sma, distance_from_vwap, candle body/wick percentages, spread_percent, consecutive_losses, daily_pnl_percent.

---

## 12. AI Data Storage

Store candles, indicators, strategy signals, AI decisions, risk decisions, orders, fills, trades, missed orders, rejected trades, regime labels, and performance reports. Do not train only on executed trades or learning becomes biased.

---

## 13. Model Versioning

Track model name, version, training date, training range, feature set version, metrics, active status, and notes. Model changes must be auditable.

---

## 14. AI Failure Handling

Handle timeouts, unavailable service, invalid response, low confidence, response conflict, missing model version, and feature generation failure.

Recommended behavior:

```text
Backtesting -> fallback allowed
Replay      -> fallback allowed
Paper       -> stop new trades or configured fallback
Live        -> block new trades
```

---

## 15. AI Performance Evaluation

Track average confidence, win rate by confidence bucket, PnL by bucket, confidence vs outcome accuracy, regime performance, false approvals, false rejections, AI-approved losses, and AI-rejected winners. If high confidence does not perform better, the AI model is weak.

---

## 16. MVP AI Scope

Build first: rule-based regime detection, weighted confidence scoring, simple anomaly detection, explanation generation, decision logging, AI dashboard views, and manual parameter recommendation review.

Delay: deep learning, reinforcement learning, autonomous risk adjustment, autonomous deployment, autonomous live trading.

---

## 17. Recommended Libraries

Initial: FastAPI, Pydantic, pandas, numpy, scikit-learn, joblib, uvicorn, pytest.

Later optional: xgboost, lightgbm, optuna, statsmodels, mlflow.

---

## 18. AI Development Order

1. FastAPI skeleton
2. Health endpoint
3. Pydantic models
4. Feature builder
5. Rule-based regime detector
6. Weighted confidence scorer
7. Simple anomaly detector
8. Explanation generator
9. .NET AI client
10. Database logging
11. Dashboard AI pages
12. Parameter recommendations later
