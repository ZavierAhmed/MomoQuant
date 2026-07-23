using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

public class ValidationLab230LeakageBoundaryTests
{
    private static readonly DateTime TrainingStart = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ValidationStart = new(2024, 1, 8, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TrainingScope_AllowsCandlesStrictlyBeforeBoundary()
    {
        var scope = CreateScope();
        var slice = scope.GetRange(TrainingStart, ValidationStart, "Test");
        Assert.All(slice, c => Assert.True(c.OpenTimeUtc < ValidationStart));
        Assert.DoesNotContain(scope.AccessLog, a => a.WasDenied);
    }

    [Fact]
    public void TrainingScope_DeniesExactValidationStartCandle()
    {
        var scope = CreateScope();
        var ex = Assert.Throws<ValidationDataLeakageException>(() =>
            scope.GetByOpenTimeUtc(ValidationStart, "AdversarialTrainer"));

        Assert.Equal(ValidationStart, ex.RequestedStartUtc);
        Assert.Contains(scope.AccessLog, a => a.WasDenied && a.RequestedStartUtc == ValidationStart);
        Assert.Contains("ValidationDataLeakageDetected", ex.Message);
    }

    [Fact]
    public void TrainingScope_DeniesRangeCrossingBoundary()
    {
        var scope = CreateScope();
        Assert.Throws<ValidationDataLeakageException>(() =>
            scope.GetRange(TrainingStart, ValidationStart.AddHours(1), "Optimizer"));
        Assert.Contains(scope.AccessLog, a => a.WasDenied);
    }

    [Fact]
    public void LeakageAudit_FromPersistedDeniedAccess_Fails()
    {
        var auditor = new ValidationLeakageAuditor();
        var audits = new List<ValidationCandleAccessAudit>
        {
            new()
            {
                ValidationExperimentId = 1,
                TrialNumber = 1,
                CallerComponent = "AdversarialTrainer",
                RequestedStartUtc = ValidationStart,
                RequestedEndUtc = ValidationStart,
                AccessedAtUtc = DateTime.UtcNow,
                WasDenied = true,
                DenialReason = "BoundaryCrossed"
            }
        };

        var report = auditor.EvaluateFromAccessEvidence(
            audits, ValidationStart, TrainingStart, ValidationStart.AddTicks(-1), "fp");

        Assert.Equal(ValidationLeakageAuditStatus.Failed, report.Status);
        Assert.True(report.BlocksFreezeOrPassed);
        Assert.Equal(1, report.DeniedAccessCount);
    }

    [Fact]
    public void LeakageAudit_FromPersistedAllowedAccess_Passes()
    {
        var auditor = new ValidationLeakageAuditor();
        var lastTraining = ValidationStart.AddHours(-1);
        var audits = new List<ValidationCandleAccessAudit>
        {
            new()
            {
                ValidationExperimentId = 1,
                TrialNumber = 1,
                CallerComponent = "GetCandlesChronologicalAsync",
                RequestedStartUtc = TrainingStart,
                RequestedEndUtc = ValidationStart,
                ReturnedCandleCount = 2,
                MinimumReturnedTimestampUtc = TrainingStart,
                MaximumReturnedTimestampUtc = lastTraining,
                AccessedAtUtc = DateTime.UtcNow,
                WasDenied = false
            }
        };

        var report = auditor.EvaluateFromAccessEvidence(
            audits, ValidationStart, TrainingStart, lastTraining, "fp");

        Assert.Equal(ValidationLeakageAuditStatus.Passed, report.Status);
        Assert.False(report.BlocksFreezeOrPassed);
        Assert.Equal(lastTraining, report.MaximumTimestampAccessedByOptimizer);
    }

    [Fact]
    public void LeakageAudit_DoesNotPassFromExpectedTrainingEndAlone()
    {
        var auditor = new ValidationLeakageAuditor();
        var report = auditor.EvaluateFromAccessEvidence(
            [], ValidationStart, TrainingStart, ValidationStart.AddTicks(-1), "fp");

        Assert.Equal(ValidationLeakageAuditStatus.NotAvailable, report.Status);
        Assert.DoesNotContain("MaximumTimestampAccessedByOptimizer < ValidationStartUtc", report.Reason ?? "");
    }

    private static ValidationTrainingCandleScope CreateScope()
    {
        var candles = new List<Candle>
        {
            CandleAt(TrainingStart, 100m),
            CandleAt(TrainingStart.AddHours(1), 101m),
            CandleAt(ValidationStart.AddHours(-1), 102m),
            CandleAt(ValidationStart, 999m),
            CandleAt(ValidationStart.AddHours(1), 1000m)
        };

        return new ValidationTrainingCandleScope(42, TrainingStart, ValidationStart, candles);
    }

    private static Candle CandleAt(DateTime open, decimal close) => new()
    {
        ExchangeId = 1,
        SymbolId = 1,
        Timeframe = Timeframe.H1,
        OpenTimeUtc = open,
        CloseTimeUtc = open.AddHours(1),
        Open = close,
        High = close,
        Low = close,
        Close = close,
        Volume = 1m,
        CreatedAtUtc = DateTime.UtcNow
    };
}
