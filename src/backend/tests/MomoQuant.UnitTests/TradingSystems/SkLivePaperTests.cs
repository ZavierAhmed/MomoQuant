using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.TradingSystems;

namespace MomoQuant.UnitTests.TradingSystems;

public class SkLivePaperTests
{
    private static SkLivePaperSession DefaultSession() => new()
    {
        Id = 1,
        SymbolId = 10,
        Symbol = "BTCUSDT",
        PrimaryTimeframe = "1h",
        HigherTimeframe = "4h",
        CurrentBalance = 10_000m,
        RiskPerPaperTradePercent = 0.5m,
        MaxPaperTradesPerDay = 3,
        MaxOpenPaperPositions = 1,
        AllowLong = true,
        AllowShort = true,
        RequireHtfAgreement = true,
        MinClarityScore = 60m,
        MinUsefulnessScore = 60m,
        RequireReactionConfirmation = false,
        ConfirmationMode = "CloseBackInDirection",
        SimulatedLeverage = 3m
    };

    private static SkSequenceDto UpwardSequence(
        decimal zoneLow = 62000m,
        decimal zoneHigh = 62500m,
        decimal invalidation = 61000m,
        decimal target1 = 64000m,
        decimal target2 = 65000m,
        string validationStatus = SkScenarioValidator.Valid,
        decimal clarityScore = 70m,
        decimal usefulnessScore = 70m) => new()
    {
        Id = "up-1",
        Direction = "Upward",
        Timeframe = "1h",
        Symbol = "BTCUSDT",
        CorrectionZoneLow = zoneLow,
        CorrectionZoneHigh = zoneHigh,
        StrongCorrectionZoneLow = zoneLow,
        StrongCorrectionZoneHigh = zoneHigh,
        InvalidationLevel = invalidation,
        Target1 = target1,
        Target2 = target2,
        ClarityScore = clarityScore,
        UsefulnessScore = usefulnessScore,
        ValidityStatus = "Valid",
        UsefulnessStatus = "Fresh",
        ValidationStatus = validationStatus,
        EligibleForBestIdea = validationStatus is SkScenarioValidator.Valid or SkScenarioValidator.LowClarity
    };

    private static SkSystemAnalysisResultDto AnalysisWithAgreement() => new()
    {
        AnalysisId = 1,
        Symbol = "BTCUSDT",
        PrimaryTimeframe = "1h",
        HigherTimeframe = "4h",
        ConceptAudit = new SkConceptAuditDto { HtfLtfAgreement = true }
    };

    private static Candle ClosedCandle(decimal close, decimal low, decimal high) => new()
    {
        Id = 1,
        SymbolId = 10,
        Open = close,
        Close = close,
        Low = low,
        High = high,
        OpenTimeUtc = DateTime.UtcNow.AddHours(-1),
        CloseTimeUtc = DateTime.UtcNow
    };

    [Fact]
    public void Evaluate_DirectionMismatch_IsRejected()
    {
        var sequence = UpwardSequence(
            target1: 61000m,
            target2: 60500m,
            validationStatus: SkScenarioValidator.DirectionMismatch);

        var result = SkLivePaperCandidateEvaluator.Evaluate(
            DefaultSession(),
            sequence,
            AnalysisWithAgreement(),
            ClosedCandle(62200m, 62100m, 62300m),
            openTradeCount: 0,
            tradesOpenedToday: 0);

        Assert.False(result.CanOpenTrade);
        Assert.Equal(SkLivePaperRejectionReasons.DirectionMismatch, result.RejectionReason);
    }

    [Fact]
    public void Evaluate_LowClarity_IsRejected()
    {
        var sequence = UpwardSequence(clarityScore: 40m);

        var result = SkLivePaperCandidateEvaluator.Evaluate(
            DefaultSession(),
            sequence,
            AnalysisWithAgreement(),
            ClosedCandle(62200m, 62100m, 62300m),
            0,
            0);

        Assert.False(result.CanOpenTrade);
        Assert.Equal(SkLivePaperRejectionReasons.LowClarity, result.RejectionReason);
    }

    [Fact]
    public void Evaluate_LowUsefulness_IsRejected()
    {
        var sequence = UpwardSequence(usefulnessScore: 40m);

        var result = SkLivePaperCandidateEvaluator.Evaluate(
            DefaultSession(),
            sequence,
            AnalysisWithAgreement(),
            ClosedCandle(62200m, 62100m, 62300m),
            0,
            0);

        Assert.False(result.CanOpenTrade);
        Assert.Equal(SkLivePaperRejectionReasons.LowUsefulness, result.RejectionReason);
    }

    [Fact]
    public void Evaluate_HtfDisagreement_IsRejectedWhenRequired()
    {
        var analysis = AnalysisWithAgreement() with
        {
            ConceptAudit = new SkConceptAuditDto { HtfLtfAgreement = false }
        };

        var result = SkLivePaperCandidateEvaluator.Evaluate(
            DefaultSession(),
            UpwardSequence(),
            analysis,
            ClosedCandle(62200m, 62100m, 62300m),
            0,
            0);

        Assert.False(result.CanOpenTrade);
        Assert.Equal(SkLivePaperRejectionReasons.HtfDisagreement, result.RejectionReason);
    }

    [Fact]
    public void Evaluate_MaxOpenPositions_IsEnforced()
    {
        var result = SkLivePaperCandidateEvaluator.Evaluate(
            DefaultSession(),
            UpwardSequence(),
            AnalysisWithAgreement(),
            ClosedCandle(62200m, 62100m, 62300m),
            openTradeCount: 1,
            tradesOpenedToday: 0);

        Assert.False(result.CanOpenTrade);
        Assert.Equal(SkLivePaperRejectionReasons.MaxOpenPositionsReached, result.RejectionReason);
    }

    [Fact]
    public void Evaluate_MaxTradesPerDay_IsEnforced()
    {
        var result = SkLivePaperCandidateEvaluator.Evaluate(
            DefaultSession(),
            UpwardSequence(),
            AnalysisWithAgreement(),
            ClosedCandle(62200m, 62100m, 62300m),
            0,
            tradesOpenedToday: 3);

        Assert.False(result.CanOpenTrade);
        Assert.Equal(SkLivePaperRejectionReasons.MaxTradesPerDayReached, result.RejectionReason);
    }

    [Fact]
    public void Evaluate_ValidUpwardCandidate_CanOpenTrade()
    {
        var result = SkLivePaperCandidateEvaluator.Evaluate(
            DefaultSession(),
            UpwardSequence(),
            AnalysisWithAgreement(),
            ClosedCandle(62200m, 62100m, 62300m),
            0,
            0);

        Assert.True(result.CanOpenTrade);
    }

    [Fact]
    public void PositionSizer_UsesRiskPercentAndStopDistance()
    {
        var sizing = SkLivePaperPositionSizer.TrySize(
            currentBalance: 10_000m,
            riskPercent: 0.5m,
            entryPrice: 62_000m,
            stopLoss: 61_000m,
            leverage: 3m);

        Assert.True(sizing.IsValid);
        Assert.Equal(50m, sizing.RiskAmount);
        Assert.Equal(0.05m, sizing.Quantity);
    }

    [Fact]
    public void TradeCloser_LongStopLoss_ClosesFirstOnSameCandle()
    {
        var trade = new SkLivePaperTrade
        {
            SessionId = 1,
            SymbolId = 10,
            Symbol = "BTCUSDT",
            Direction = "Bullish",
            Status = SkLivePaperTradeStatus.Open,
            EntryPrice = 62_000m,
            Quantity = 0.1m,
            StopLoss = 61_000m,
            TakeProfit1 = 64_000m,
            MarginUsed = 2_000m
        };

        var candle = ClosedCandle(close: 61_500m, low: 60_900m, high: 64_100m);
        var close = SkLivePaperTradeCloser.Evaluate(trade, candle);

        Assert.NotNull(close);
        Assert.Equal(SkLivePaperTradeExitReason.StopLoss, close!.Value.Reason);
        Assert.Equal(61_000m, close.Value.ExitPrice);
    }

    [Fact]
    public void TradeCloser_LongTarget1_ClosesWhenStopNotHit()
    {
        var trade = new SkLivePaperTrade
        {
            SessionId = 1,
            SymbolId = 10,
            Symbol = "BTCUSDT",
            Direction = "Bullish",
            Status = SkLivePaperTradeStatus.Open,
            EntryPrice = 62_000m,
            Quantity = 0.1m,
            StopLoss = 61_000m,
            TakeProfit1 = 64_000m,
            MarginUsed = 2_000m
        };

        var candle = ClosedCandle(close: 63_500m, low: 62_500m, high: 64_100m);
        var close = SkLivePaperTradeCloser.Evaluate(trade, candle);

        Assert.NotNull(close);
        Assert.Equal(SkLivePaperTradeExitReason.Target1, close!.Value.Reason);
    }

    [Fact]
    public void TradeCloser_ShortStopLoss_ClosesTrade()
    {
        var trade = new SkLivePaperTrade
        {
            SessionId = 1,
            SymbolId = 10,
            Symbol = "BTCUSDT",
            Direction = "Bearish",
            Status = SkLivePaperTradeStatus.Open,
            EntryPrice = 62_000m,
            Quantity = 0.1m,
            StopLoss = 63_000m,
            TakeProfit1 = 60_000m,
            MarginUsed = 2_000m
        };

        var candle = ClosedCandle(close: 62_500m, low: 59_900m, high: 63_100m);
        var close = SkLivePaperTradeCloser.Evaluate(trade, candle);

        Assert.NotNull(close);
        Assert.Equal(SkLivePaperTradeExitReason.StopLoss, close!.Value.Reason);
    }

    [Fact]
    public void TradeCloser_ShortTarget1_ClosesTrade()
    {
        var trade = new SkLivePaperTrade
        {
            SessionId = 1,
            SymbolId = 10,
            Symbol = "BTCUSDT",
            Direction = "Bearish",
            Status = SkLivePaperTradeStatus.Open,
            EntryPrice = 62_000m,
            Quantity = 0.1m,
            StopLoss = 63_000m,
            TakeProfit1 = 60_000m,
            MarginUsed = 2_000m
        };

        var candle = ClosedCandle(close: 60_500m, low: 59_900m, high: 61_500m);
        var close = SkLivePaperTradeCloser.Evaluate(trade, candle);

        Assert.NotNull(close);
        Assert.Equal(SkLivePaperTradeExitReason.Target1, close!.Value.Reason);
    }

    [Fact]
    public void ComputePnl_UpdatesNetAfterFees()
    {
        var trade = new SkLivePaperTrade
        {
            Direction = "Bullish",
            EntryPrice = 62_000m,
            Quantity = 0.1m,
            MarginUsed = 2_000m
        };

        var pnl = SkLivePaperTradeCloser.ComputePnl(trade, 64_000m);
        Assert.True(pnl.NetPnl < pnl.GrossPnl);
        Assert.Equal(200m, pnl.GrossPnl);
    }
}
