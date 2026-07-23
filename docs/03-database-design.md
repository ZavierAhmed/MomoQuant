# MOMO Quant

## Part 3 — Database Design Document

**Version:** 1.0 Draft  
**Database:** MySQL  
**ORM:** Entity Framework Core

---

## 1. Database Principle

The database must store not only trades, but also market data, indicators, signals, AI decisions, risk decisions, orders, fills, positions, PnL, rejected trades, missed trades, mode changes, audit logs, and monitoring events.

A trading system that only stores final trades cannot be improved intelligently.

Use UTC timestamps. Use decimal for all trading values. Never use float/double for price, quantity, PnL, or fees. Recommended precision: `decimal(28,12)`.

---

## 2. Logical Areas

```text
Identity & Security
Exchange & Symbols
Market Data
Indicators
Strategies
AI Decisions
Risk Management
Trading Sessions
Signals
Orders
Trades
Positions
Backtesting
Replay
Paper Trading
Reporting
Monitoring
Audit Logs
Settings
```

---

## 3. Core Tables

### Users

Fields: Id, FullName, Email, PasswordHash, RoleId, IsActive, LastLoginAt, CreatedAt, UpdatedAt.

### Roles

Fields: Id, Name, Description, CreatedAt, UpdatedAt. Initial roles: Admin, Trader, Viewer.

### ApiKeyVault

Fields: Id, ExchangeId, Name, ApiKeyEncrypted, ApiSecretEncrypted, PassphraseEncrypted, IsTestnet, IsActive, CreatedAt, UpdatedAt. Never store raw keys.

### Exchanges

Fields: Id, Name, Code, BaseUrl, WebSocketUrl, IsActive, CreatedAt, UpdatedAt. MVP supports one exchange first.

### Symbols

Fields: Id, ExchangeId, Symbol, BaseAsset, QuoteAsset, ContractType, PricePrecision, QuantityPrecision, MinQty, MinNotional, TickSize, StepSize, MakerFeeRate, TakerFeeRate, IsActive, CreatedAt, UpdatedAt.

Initial examples: BTCUSDT, ETHUSDT, SOLUSDT, BNBUSDT, XRPUSDT.

---

## 4. Market Data

### Candles

Fields: Id, ExchangeId, SymbolId, Timeframe, OpenTimeUtc, CloseTimeUtc, Open, High, Low, Close, Volume, QuoteVolume, TradeCount, IsClosed, CreatedAt.

Indexes:

```text
ExchangeId + SymbolId + Timeframe + OpenTimeUtc unique
SymbolId + Timeframe + OpenTimeUtc
OpenTimeUtc
```

### OrderBookSnapshots

Optional later: BestBid/Ask, spread, mid price, captured time.

### FundingRates

Optional later: SymbolId, FundingTimeUtc, FundingRate, MarkPrice.

---

## 5. Indicators

### IndicatorSnapshots

Fields: Id, SymbolId, Timeframe, CandleId, CalculatedAtUtc, Ema20, Ema50, Ema200, Vwap, Rsi14, Atr14, VolumeSma20, SwingHigh, SwingLow, MarketStructure, CreatedAt.

Unique index: SymbolId + Timeframe + CandleId.

---

## 6. Strategies

### Strategies

Fields: Id, Code, Name, Description, IsEnabled, Version, CreatedAt, UpdatedAt.

Codes: LIQUIDITY_SWEEP, VWAP_MEAN_REVERSION, EMA_PULLBACK.

### StrategyParameters

Fields: Id, StrategyId, ParameterKey, ParameterValue, ValueType, Timeframe, SymbolId nullable, IsActive, CreatedAt, UpdatedAt. Symbol-specific overrides allowed.

---

## 7. AI and Risk

### AiDecisions

Fields: Id, TradingSessionId, SymbolId, Timeframe, CandleId nullable, SignalId nullable, MarketRegime, ConfidenceScore, PreferredStrategyCode, RiskAdjustment, TradeAllowed, Explanation, RawRequestJson, RawResponseJson, CreatedAt.

### AiModelVersions

Optional later: ModelName, ModelVersion, Purpose, TrainingDataFrom/To, MetricsJson, IsActive.

### RiskProfiles

Fields: Id, Name, Description, IsDefault, CreatedAt, UpdatedAt.

### RiskRules

Fields: Id, RiskProfileId, RuleKey, RuleValue, ValueType, IsEnabled, CreatedAt, UpdatedAt.

### RiskDecisions

Fields: Id, TradingSessionId, SignalId nullable, AiDecisionId nullable, SymbolId, Decision, Reason, ApprovedRiskPercent, PositionSize, StopLoss, TakeProfit, RejectedRuleKey, CreatedAt.

Decision values: Approved, Rejected, Adjusted, EmergencyBlocked.

---

## 8. Trading Sessions and Signals

### TradingSessions

Fields: Id, Name, Mode, Status, ExchangeId, StartedByUserId, StartedAtUtc, StoppedAtUtc, InitialBalance, FinalBalance, Notes, CreatedAt, UpdatedAt.

Modes: Backtest, Replay, Paper, Live. Statuses: Created, Running, Paused, Stopped, Completed, Failed.

### TradingSessionSymbols

Fields: Id, TradingSessionId, SymbolId, Timeframe, HigherTimeframe, IsActive, CreatedAt.

### StrategySignals

Fields: Id, TradingSessionId, StrategyId, SymbolId, Timeframe, CandleId nullable, SignalType, Direction, Strength, ConfidenceContribution, EntryPrice, SuggestedStopLoss, SuggestedTakeProfit, Reason, RawDataJson, CreatedAt.

Store NoTrade signals when useful for debugging.

---

## 9. Orders, Fills, Trades, Positions

### Orders

Fields: Id, TradingSessionId, SymbolId, TradeId nullable, ExternalOrderId nullable, Mode, Side, OrderType, PositionSide, Price, Quantity, Status, IsPostOnly, IsReduceOnly, TimeInForce, RequestedAtUtc, SubmittedAtUtc, CancelledAtUtc, FilledAtUtc, FailureReason, CreatedAt, UpdatedAt.

### OrderFills

Fields: Id, OrderId, ExternalFillId nullable, FillPrice, FillQuantity, Fee, FeeAsset, LiquidityType, FilledAtUtc, CreatedAt.

LiquidityType: Maker, Taker, SimulatedMaker, SimulatedTaker.

### MissedOrders

Fields: Id, TradingSessionId, SignalId, SymbolId, RequestedPrice, BestBid, BestAsk, Reason, ExpiredAtUtc, CreatedAt.

Reasons include maker not filled, price moved away, spread too wide, timeout.

### Trades

Fields: Id, TradingSessionId, SymbolId, StrategyId nullable, SignalId nullable, AiDecisionId nullable, RiskDecisionId nullable, Direction, EntryOrderId nullable, ExitOrderId nullable, EntryPrice, ExitPrice nullable, Quantity, StopLoss, TakeProfit, Status, OpenedAtUtc, ClosedAtUtc nullable, GrossPnl, Fees, FundingFees, NetPnl, RMultiple, CloseReason, CreatedAt, UpdatedAt.

### Positions

Fields: Id, TradingSessionId, SymbolId, Direction, Quantity, AverageEntryPrice, MarkPrice, UnrealizedPnl, RealizedPnl, Leverage, MarginUsed, Status, OpenedAtUtc, UpdatedAtUtc, ClosedAtUtc nullable.

---

## 10. Backtesting, Replay, Paper

### BacktestRuns

Fields: Id, TradingSessionId, Name, SymbolId, Timeframe, HigherTimeframe, StartDateUtc, EndDateUtc, InitialBalance, FinalBalance, StrategySetJson, SettingsJson, Status, StartedAtUtc, CompletedAtUtc, CreatedAt.

### BacktestResults

Fields: Id, BacktestRunId, TotalTrades, WinningTrades, LosingTrades, WinRate, GrossPnl, NetPnl, TotalFees, MaxDrawdown, ProfitFactor, Expectancy, AverageWin, AverageLoss, LargestWin, LargestLoss, SharpeRatio, SortinoRatio, CreatedAt.

### ReplaySessions

Fields: Id, TradingSessionId, SymbolId, Timeframe, StartTimeUtc, EndTimeUtc, CurrentReplayTimeUtc, ReplaySpeed, Status, CreatedAt, UpdatedAt.

### PaperAccounts

Fields: Id, Name, InitialBalance, CurrentBalance, Currency, IsActive, CreatedAt, UpdatedAt.

### PaperAccountSnapshots

Fields: Id, PaperAccountId, TradingSessionId, Balance, Equity, UnrealizedPnl, RealizedPnl, MarginUsed, CapturedAtUtc, CreatedAt.

---

## 11. Monitoring, Audit, Settings

### SystemHealthLogs

Fields: Id, ServiceName, Status, Message, LatencyMs, CheckedAtUtc, CreatedAt.

### AuditLogs

Fields: Id, UserId nullable, TradingSessionId nullable, Action, EntityType, EntityId nullable, OldValueJson, NewValueJson, IpAddress, UserAgent, CreatedAt.

Audit mode changes, live enabled, risk/strategy/API key changes, bot start/stop, emergency stop.

### AppSettings

Fields: Id, SettingKey, SettingValue, ValueType, Category, IsSensitive, CreatedAt, UpdatedAt. Sensitive settings must be encrypted or externalized.

---

## 12. MVP Database Scope

Create first: Users, Roles, Exchanges, Symbols, Candles, Strategies, StrategyParameters, TradingSessions, IndicatorSnapshots, StrategySignals, AiDecisions, RiskRules, RiskDecisions, Orders, OrderFills, MissedOrders, Trades, Positions, BacktestRuns, BacktestResults, ReplaySessions, PaperAccounts, PaperAccountSnapshots, AuditLogs, SystemHealthLogs, AppSettings.

Delay: OrderBookSnapshots, FundingRates, AiModelVersions, materialized report snapshots, ExchangeConnectionLogs.

---

## 13. Critical Rules

1. Use UTC for timestamps.
2. Use decimal, never float/double, for trading values.
3. Store every decision, not only executed trades.
4. Keep raw AI request/response JSON.
5. Store missed orders separately.
6. Store rejected risk decisions.
7. Never store plain exchange API keys.
8. Reports must come from stored facts.
9. Do not delete trading records casually.
10. Every live-mode action must be auditable.
