using System.Reflection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Backtesting;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.StrategyLab;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.ValidationLab;

namespace MomoQuant.UnitTests.ValidationLab;

/// <summary>
/// Milestone 23.0C Part 4 — validation-training components must not take unrestricted
/// candle loaders as constructor dependencies (beyond documented intentional exceptions).
/// </summary>
public sealed class ValidationLab230CArchitectureTests
{
    private static readonly Type[] ProhibitedCtorDeps =
    [
        typeof(IBacktestDataLoader),
        typeof(ICandleRepository),
        typeof(IHistoricalCandleCoverageService),
        typeof(IUnscopedCandleReader)
    ];

    /// <summary>
    /// Intentional exceptions (documented):
    /// - <see cref="ValidationTrainingCandleScopeFactory"/> may take <see cref="IUnscopedCandleReader"/>
    ///   solely to materialize the immutable training scope at create-time (never exposed to trials).
    /// - <see cref="ValidationLabService"/> may take <see cref="ICandleRepository"/> /
    ///   <see cref="IHistoricalCandleCoverageService"/> for prepare-data and holdout validation paths;
    ///   training trials must use <see cref="IValidationTrainingCandleScope"/> /
    ///   <see cref="IStrategyLabCandleDataSource"/> via <see cref="StrategyLabExecutionContext"/>.
    /// - <see cref="StrategyLabRunner"/> and <see cref="StandardStrategyLabCandleDataSource"/> may take
    ///   unrestricted loaders for <see cref="ExecutionPurpose.GeneralResearch"/> only; ValidationTraining
    ///   must supply an explicit scoped <see cref="IStrategyLabCandleDataSource"/>.
    /// - <see cref="BacktestDataLoader"/> is the unrestricted loader itself (not a validation-training worker).
    /// </summary>
    private static readonly HashSet<Type> IntentionalExceptions =
    [
        typeof(ValidationTrainingCandleScopeFactory),
        typeof(ValidationLabService),
        typeof(StrategyLabRunner),
        typeof(StandardStrategyLabCandleDataSource),
        typeof(BacktestDataLoader)
    ];

    [Fact]
    public void ValidationTraining_RelatedTypes_DoNotTakeProhibitedCandleCtorDeps()
    {
        var applicationAssembly = typeof(IValidationTrainingCandleScopeFactory).Assembly;
        var targets = applicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(IsValidationTrainingRelated)
            .Where(t => !IntentionalExceptions.Contains(t))
            .ToList();

        Assert.True(targets.Count >= 8, $"Expected several training-related types; got {targets.Count}.");

        var offenders = new List<string>();
        foreach (var type in targets)
        {
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var param in ctor.GetParameters())
                {
                    if (ProhibitedCtorDeps.Any(p => p.IsAssignableFrom(param.ParameterType)))
                    {
                        offenders.Add($"{type.FullName}({DescribeCtor(ctor)})");
                    }
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Prohibited candle loader ctor deps on validation-training types:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void ApplicationTypes_TakingIUnscopedCandleReader_AreOnlyDocumentedExceptions()
    {
        var applicationAssembly = typeof(IValidationTrainingCandleScopeFactory).Assembly;
        var unscoped = typeof(IUnscopedCandleReader);
        var offenders = applicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Select(c => (Type: t, Ctor: c)))
            .Where(x => x.Ctor.GetParameters().Any(p => p.ParameterType == unscoped))
            .Select(x => x.Type)
            .Distinct()
            .ToList();

        Assert.Equal(
            [typeof(ValidationTrainingCandleScopeFactory)],
            offenders);
    }

    [Fact]
    public void ApplicationTypes_TakingIBacktestDataLoader_AreOnlyDocumentedExceptions()
    {
        var applicationAssembly = typeof(IValidationTrainingCandleScopeFactory).Assembly;
        var loader = typeof(IBacktestDataLoader);
        var offenders = applicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => !IntentionalExceptions.Contains(t))
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Where(c => c.GetParameters().Any(p => p.ParameterType == loader || loader.IsAssignableFrom(p.ParameterType)))
                .Select(c => t))
            .Distinct()
            .Where(IsValidationTrainingRelated)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ApplicationAssembly_DoesNotReferenceMomoQuantDbContextByName()
    {
        // Application must not construct Persistence DbContext; repositories stay behind abstractions.
        var applicationAssembly = typeof(IValidationTrainingCandleScopeFactory).Assembly;
        var hits = applicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(IsValidationTrainingRelated)
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters().Select(p => (Type: t, Param: p.ParameterType))))
            .Where(x => x.Param.Name.Contains("DbContext", StringComparison.Ordinal)
                        || (x.Param.FullName?.Contains("MomoQuantDbContext", StringComparison.Ordinal) ?? false))
            .Select(x => $"{x.Type.Name}:{x.Param.Name}")
            .ToList();

        Assert.Empty(hits);
    }

    [Fact]
    public void TrainingScope_And_Selection_And_FailureHandler_Ctors_AreScopedOnly()
    {
        AssertNoProhibited(typeof(ValidationTrainingScopeExecution));
        AssertNoProhibited(typeof(ValidationTrainingFailureHandler));
        AssertNoProhibited(typeof(ValidationCandleAccessRecorder));
        AssertNoProhibited(typeof(ValidationTrainingSelectionService));
        AssertNoProhibited(typeof(ValidationSelectionIntegrityService));
        AssertNoProhibited(typeof(ValidationTrainingStrategyLabCandleDataSource));
        AssertNoProhibited(typeof(ValidationTrainingPreflightService));
        AssertNoProhibited(typeof(ValidationTrainingExecutionLeaseService));
        AssertNoProhibited(typeof(ValidationTrialRecoveryService));
        AssertNoProhibited(typeof(StrategyLabRiskObserver));
    }

    private static void AssertNoProhibited(Type type)
    {
        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var param in ctor.GetParameters())
            {
                Assert.False(
                    ProhibitedCtorDeps.Any(p => p.IsAssignableFrom(param.ParameterType)),
                    $"{type.Name} must not take {param.ParameterType.Name}");
            }
        }
    }

    private static bool IsValidationTrainingRelated(Type type)
    {
        var ns = type.Namespace ?? string.Empty;
        var name = type.Name;

        if (ns.Contains("ValidationLab", StringComparison.Ordinal))
        {
            return name.Contains("Training", StringComparison.Ordinal)
                   || name.Contains("Trial", StringComparison.Ordinal)
                   || name.Contains("Selection", StringComparison.Ordinal)
                   || name.Contains("Leakage", StringComparison.Ordinal)
                   || name.Contains("CandleAccess", StringComparison.Ordinal)
                   || name.Contains("Scope", StringComparison.Ordinal)
                   || name.Contains("Freeze", StringComparison.Ordinal)
                   || name.Contains("Rank", StringComparison.Ordinal)
                   || name == nameof(ValidationLabService)
                   || name == nameof(ValidationSegmentResultWriter)
                   || name == nameof(ValidationLifecycleGate);
        }

        if (ns.Contains("StrategyLab", StringComparison.Ordinal))
        {
            return name.Contains("ValidationTraining", StringComparison.Ordinal)
                   || name == nameof(StrategyLabRunner)
                   || name == nameof(StrategyLabRiskObserver)
                   || name == nameof(StandardStrategyLabCandleDataSource)
                   || name == nameof(ValidationTrainingStrategyLabCandleDataSource)
                   || name == "StrategySetupQualityScorer";
        }

        return false;
    }

    private static string DescribeCtor(ConstructorInfo ctor) =>
        string.Join(", ", ctor.GetParameters().Select(p => p.ParameterType.Name));
}
