using MomoQuant.Application.Abstractions;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.Strategies;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationTrainingCandleScopeFactory
{
    /// <summary>
    /// v2: builds an immutable training scope with exact warm-up + evaluation partitions.
    /// Throws <see cref="ValidationTrainingInsufficientWarmupException"/> when available &lt; required.
    /// </summary>
    Task<IValidationTrainingCandleScope> CreateAsync(
        ValidationTrainingCandleScopeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obsolete wrapper — prefer <see cref="CreateAsync"/>. Uses experiment.RequiredWarmupCandles
    /// when a full requirements resolution was not supplied.
    /// </summary>
    [Obsolete("Use CreateAsync(ValidationTrainingCandleScopeRequest) after resolving StrategyExecutionRequirements.")]
    Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
        ValidationExperiment experiment,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds an immutable training candle scope from DB candles strictly before ValidationStartUtc.
/// Loads exact warm-up (latest N closed bars before evaluation start) plus evaluation bars.
/// When Adaptive requires HTF, binds mapped HTF into the scope at construction (Milestone 23.1B1A/B1B).
/// Uses the inner (unscoped) candle repository to avoid recursive boundary checks during bootstrap.
/// </summary>
public sealed class ValidationTrainingCandleScopeFactory : IValidationTrainingCandleScopeFactory
{
    private readonly IUnscopedCandleReader _candles;
    private readonly IValidationCandleAccessRecorder? _recorder;
    private readonly ValidationScopeFactoryCapability _capability = ValidationScopeFactoryCapability.Create();

    public ValidationTrainingCandleScopeFactory(IUnscopedCandleReader candles) => _candles = candles;

    public ValidationTrainingCandleScopeFactory(
        IUnscopedCandleReader candles,
        IValidationCandleAccessRecorder recorder)
    {
        _candles = candles;
        _recorder = recorder;
    }

    /// <summary>Bootstrap access evidence from the most recent <see cref="CreateAsync"/> attempt.</summary>
    public IReadOnlyList<ValidationCandleAccessRecord> LastBootstrapAccessEvidence { get; private set; } =
        Array.Empty<ValidationCandleAccessRecord>();

    public async Task<IValidationTrainingCandleScope> CreateAsync(
        ValidationTrainingCandleScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (request.BoundAuditExecutionId is not null)
        {
            request.ValidateCanonical();
        }

        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            throw new InvalidOperationException($"Unknown timeframe '{request.Timeframe}'.");
        }

        var evalStart = request.TrainingEvaluationStartUtc;
        var evalEndExclusive = request.TrainingEvaluationEndExclusiveUtc;
        var boundary = request.ValidationBoundaryUtc;
        var loadEndExclusive = evalEndExclusive <= boundary ? evalEndExclusive : boundary;
        var requiredWarmup = request.RequiredWarmupCandleCount;
        var scopeExecutionId = request.BoundScopeExecutionId ?? Guid.NewGuid();
        var bootstrapRecords = new List<ValidationCandleAccessRecord>();

        using (_capability.Activate())
        {
            IReadOnlyList<Candle> warmup;
            if (requiredWarmup > 0)
            {
                warmup = await _candles.GetClosedCandlesBeforeUnscopedAsync(
                    request.SymbolId,
                    timeframe,
                    beforeOpenTimeUtc: evalStart,
                    count: requiredWarmup,
                    cancellationToken);
            }
            else
            {
                warmup = Array.Empty<Candle>();
            }

            var evaluation = await _candles.GetCandlesChronologicalUnscopedAsync(
                request.SymbolId,
                timeframe,
                fromUtc: evalStart,
                toUtc: loadEndExclusive,
                warmUpCount: 0,
                cancellationToken);

            evaluation = evaluation
                .Where(c =>
                {
                    var open = DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc);
                    return open >= evalStart
                           && open < loadEndExclusive
                           && open < boundary
                           && c.SymbolId == request.SymbolId;
                })
                .OrderBy(c => c.OpenTimeUtc)
                .ToList();

            warmup = warmup
                .Where(c =>
                {
                    var open = DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc);
                    return open < evalStart
                           && open < boundary
                           && c.IsClosed
                           && c.SymbolId == request.SymbolId;
                })
                .OrderBy(c => c.OpenTimeUtc)
                .ToList();

            var availableWarmup = warmup.Count;
            ValidationWarmupStatus status;
            if (requiredWarmup <= 0)
            {
                status = ValidationWarmupStatus.NotRequired;
            }
            else if (availableWarmup >= requiredWarmup)
            {
                status = ValidationWarmupStatus.Complete;
                if (availableWarmup > requiredWarmup)
                {
                    warmup = warmup.TakeLast(requiredWarmup).ToList();
                    availableWarmup = warmup.Count;
                }
            }
            else
            {
                status = ValidationWarmupStatus.Insufficient;
                throw new ValidationTrainingInsufficientWarmupException(
                    request.ValidationExperimentId,
                    requiredWarmup,
                    availableWarmup,
                    $"Insufficient warm-up candles for validation training experiment {request.ValidationExperimentId}: " +
                    $"available={availableWarmup}, required={requiredWarmup}, status={status}, " +
                    $"requirementsVersion={request.RequirementsVersion}.");
            }

            var exchangeId = request.ExchangeId > 0 ? request.ExchangeId : (long?)null;
            var mappedHtf = TryResolveMappedHtf(request.StrategyCode, timeframe);
            if (request.BoundAuditExecutionId is not null
                && !string.IsNullOrWhiteSpace(request.StrategyCode)
                && StrategyCodeExtensions.FromCode(request.StrategyCode)
                    == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout
                && !mappedHtf.HasValue)
            {
                throw new InvalidOperationException(
                    $"Canonical Adaptive validation requires mapped HTF for execution timeframe '{request.Timeframe}'.");
            }
            IReadOnlyDictionary<Timeframe, IReadOnlyList<Candle>>? htfPartition = null;
            string? htfFingerprint = null;

            if (mappedHtf.HasValue)
            {
                if (exchangeId is not > 0)
                {
                    throw new InvalidOperationException(
                        "Canonical validation training requires ExchangeId when mapped HTF is required.");
                }

                var htfWarmup = Math.Max(requiredWarmup, 200);
                var raw = await _candles.GetCandlesChronologicalUnscopedAsync(
                    request.SymbolId,
                    mappedHtf.Value,
                    fromUtc: evalStart,
                    toUtc: loadEndExclusive,
                    warmUpCount: htfWarmup,
                    cancellationToken);

                var validation = ValidationHtfPartitionValidator.ValidateRawHtfPartitionFailClosed(
                    raw,
                    request.SymbolId,
                    exchangeId.Value,
                    mappedHtf.Value,
                    loadEndExclusive,
                    boundary);

                if (!validation.Succeeded)
                {
                    var denied = CreateBootstrapAccessRecord(
                        request,
                        scopeExecutionId,
                        sequenceNumber: 1,
                        mappedHtf.Value,
                        evalStart,
                        loadEndExclusive,
                        htfWarmup,
                        returned: raw,
                        denied: true,
                        denialCode: validation.DenialCode!,
                        denialReason: validation.DenialReason!);
                    bootstrapRecords.Add(denied);
                    LastBootstrapAccessEvidence = bootstrapRecords;

                    await PersistDeniedBootstrapEvidenceAsync(
                        request,
                        scopeExecutionId,
                        warmup,
                        evaluation,
                        availableWarmup,
                        status,
                        evalStart,
                        loadEndExclusive,
                        boundary,
                        requiredWarmup,
                        exchangeId,
                        mappedHtf,
                        bootstrapRecords,
                        cancellationToken);

                    throw new ValidationCandlePartitionViolationException(
                        request.ValidationExperimentId,
                        scopeExecutionId,
                        boundary,
                        evalStart,
                        loadEndExclusive,
                        htfWarmup,
                        evalStart,
                        loadEndExclusive,
                        validation.DenialCode!,
                        validation.DenialReason!,
                        "ValidationTrainingCandleScopeFactory");
                }

                htfPartition = new Dictionary<Timeframe, IReadOnlyList<Candle>>
                {
                    [mappedHtf.Value] = validation.Authorized
                };
                htfFingerprint = ValidationTrainingCandleScope.ComputeContentFingerprint(validation.Authorized);

                bootstrapRecords.Add(CreateBootstrapAccessRecord(
                    request,
                    scopeExecutionId,
                    sequenceNumber: 1,
                    mappedHtf.Value,
                    evalStart,
                    loadEndExclusive,
                    htfWarmup,
                    validation.Authorized,
                    denied: false,
                    denialCode: null,
                    denialReason: null));
            }

            LastBootstrapAccessEvidence = bootstrapRecords;

            var combined = warmup.Concat(evaluation).ToList();
            var partition = ValidationTrainingCandleScope.BuildPartition(
                request.ValidationExperimentId,
                request.SymbolId,
                request.SymbolName,
                request.Timeframe,
                requiredWarmup,
                availableWarmup,
                evaluation.Count,
                status,
                evalStart,
                loadEndExclusive,
                boundary,
                request.RequirementsVersion,
                warmup,
                evaluation,
                combined,
                strategyCode: request.StrategyCode,
                strategyVersion: request.StrategyVersion,
                exchangeId: exchangeId,
                mappedHigherTimeframe: mappedHtf is { } htf
                    ? TimeframeParser.ToApiString(htf)
                    : null,
                higherTimeframeContentFingerprint: htfFingerprint);

            return new ValidationTrainingCandleScope(
                partition,
                warmup,
                evaluation,
                scopeExecutionId: scopeExecutionId,
                boundAuditExecutionId: request.BoundAuditExecutionId,
                higherTimeframePartition: htfPartition,
                strategyCode: request.StrategyCode,
                strategyVersion: request.StrategyVersion,
                exchangeId: exchangeId,
                mappedHigherTimeframe: mappedHtf,
                bootstrapAccessRecords: bootstrapRecords);
        }
    }

    private async Task PersistDeniedBootstrapEvidenceAsync(
        ValidationTrainingCandleScopeRequest request,
        Guid scopeExecutionId,
        IReadOnlyList<Candle> warmup,
        IReadOnlyList<Candle> evaluation,
        int availableWarmup,
        ValidationWarmupStatus status,
        DateTime evalStart,
        DateTime loadEndExclusive,
        DateTime boundary,
        int requiredWarmup,
        long? exchangeId,
        Timeframe? mappedHtf,
        IReadOnlyList<ValidationCandleAccessRecord> bootstrapRecords,
        CancellationToken cancellationToken)
    {
        if (_recorder is null
            || request.BoundAuditExecutionId is not Guid auditId
            || request.BoundScopeExecutionId is not Guid boundScopeId
            || string.IsNullOrWhiteSpace(request.BoundExecutionToken))
        {
            return;
        }

        var combined = warmup.Concat(evaluation).ToList();
        var partition = ValidationTrainingCandleScope.BuildPartition(
            request.ValidationExperimentId,
            request.SymbolId,
            request.SymbolName,
            request.Timeframe,
            requiredWarmup,
            availableWarmup,
            evaluation.Count,
            status,
            evalStart,
            loadEndExclusive,
            boundary,
            request.RequirementsVersion,
            warmup,
            evaluation,
            combined,
            strategyCode: request.StrategyCode,
            strategyVersion: request.StrategyVersion,
            exchangeId: exchangeId,
            mappedHigherTimeframe: mappedHtf is { } htf
                ? TimeframeParser.ToApiString(htf)
                : null,
            higherTimeframeContentFingerprint: null);

        var shell = new ValidationTrainingCandleScope(
            partition,
            warmup,
            evaluation,
            scopeExecutionId: scopeExecutionId,
            boundAuditExecutionId: auditId,
            higherTimeframePartition: null,
            strategyCode: request.StrategyCode,
            strategyVersion: request.StrategyVersion,
            exchangeId: exchangeId,
            mappedHigherTimeframe: mappedHtf,
            bootstrapAccessRecords: bootstrapRecords);

        using var auditAmbient = ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
        {
            AuditExecutionId = auditId,
            ScopeExecutionId = boundScopeId,
            ExecutionToken = request.BoundExecutionToken!,
            AttemptNumber = request.BoundAttemptNumber ?? 0,
            ValidationExperimentId = request.ValidationExperimentId
        });

        await _recorder.FlushAsync(shell, cancellationToken).ConfigureAwait(false);
        await shell.DisposeAsync().ConfigureAwait(false);
    }

    private static ValidationCandleAccessRecord CreateBootstrapAccessRecord(
        ValidationTrainingCandleScopeRequest request,
        Guid scopeExecutionId,
        long sequenceNumber,
        Timeframe mappedHtf,
        DateTime evalStart,
        DateTime loadEndExclusive,
        int htfWarmup,
        IReadOnlyList<Candle>? returned,
        bool denied,
        string? denialCode,
        string? denialReason)
    {
        var htfApi = TimeframeParser.ToApiString(mappedHtf);
        var context = ValidationCandleAccessContext.Create(
            "ValidationTrainingCandleScopeFactory",
            ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad);

        return new ValidationCandleAccessRecord
        {
            AccessEventId = Guid.NewGuid(),
            ScopeExecutionId = scopeExecutionId,
            ScopeSequenceNumber = sequenceNumber,
            ValidationExperimentId = request.ValidationExperimentId,
            CallerComponent = context.ToCallerAuditLabel(),
            AccessPurpose = ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad,
            RequestedStartUtc = evalStart,
            RequestedEndUtc = loadEndExclusive,
            RequestedCandleCount = htfWarmup,
            ReturnedStartUtc = returned is { Count: > 0 }
                ? DateTime.SpecifyKind(returned[0].OpenTimeUtc, DateTimeKind.Utc)
                : null,
            ReturnedEndUtc = returned is { Count: > 0 }
                ? DateTime.SpecifyKind(returned[^1].CloseTimeUtc, DateTimeKind.Utc)
                : null,
            ReturnedCandleCount = returned?.Count ?? 0,
            MinimumReturnedTimestampUtc = returned is { Count: > 0 }
                ? returned.Min(c => DateTime.SpecifyKind(c.OpenTimeUtc, DateTimeKind.Utc))
                : null,
            MaximumReturnedTimestampUtc = returned is { Count: > 0 }
                ? returned.Max(c => DateTime.SpecifyKind(c.CloseTimeUtc, DateTimeKind.Utc))
                : null,
            CandleContentFingerprint = returned is { Count: > 0 }
                ? ValidationTrainingCandleScope.ComputeContentFingerprint(returned)
                : null,
            AccessedAtUtc = DateTime.UtcNow,
            WasDenied = denied,
            DenialCode = denialCode,
            DenialReason = denialReason,
            DatasetPartition = $"BootstrapHTF:{htfApi}:S{request.SymbolId}:E{request.ExchangeId}",
            RecorderVersion = ValidationCandleAccessRecorder.RecorderVersion,
            AuditExecutionId = request.BoundAuditExecutionId,
            RequestSymbolId = request.SymbolId,
            RequestExchangeId = request.ExchangeId,
            RequestTimeframeApi = htfApi,
            RequestStrategyCode = request.StrategyCode,
            RequestStrategyVersion = request.StrategyVersion
        };
    }

    private static Timeframe? TryResolveMappedHtf(string? strategyCode, Timeframe executionTimeframe)
    {
        if (string.IsNullOrWhiteSpace(strategyCode))
        {
            return null;
        }

        try
        {
            var code = StrategyCodeExtensions.FromCode(strategyCode);
            if (code != StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout)
            {
                return null;
            }

            if (executionTimeframe is not (Timeframe.M5 or Timeframe.M15 or Timeframe.H1 or Timeframe.H4))
            {
                return null;
            }

            return MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(executionTimeframe);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

#pragma warning disable CS0618
    public Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
        ValidationExperiment experiment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        if (experiment.TrainingEndUtc is null)
        {
            throw new InvalidOperationException("Training candle scope requires TrainingEndUtc.");
        }

        if (!TimeframeParser.TryGetDurationMinutes(experiment.Timeframe, out var minutes) || minutes <= 0)
        {
            throw new InvalidOperationException($"Unable to resolve timeframe duration for '{experiment.Timeframe}'.");
        }

        var endExclusive = DateTime.SpecifyKind(experiment.TrainingEndUtc.Value, DateTimeKind.Utc)
            .AddMinutes(minutes);
        var request = ValidationTrainingCandleScopeRequest.FromExperimentLegacy(experiment, endExclusive);
        return CreateAsync(request, cancellationToken);
    }
#pragma warning restore CS0618
}

/// <summary>
/// Escape hatch for bootstrap loads that must bypass the ambient training boundary decorator.
/// </summary>
public interface IUnscopedCandleReader
{
    Task<IReadOnlyList<Candle>> GetCandlesChronologicalUnscopedAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime? fromUtc,
        DateTime? toUtc,
        int warmUpCount = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest <paramref name="count"/> closed candles with OpenTimeUtc &lt; <paramref name="beforeOpenTimeUtc"/>
    /// (ORDER BY OpenTimeUtc DESC TAKE N, returned ascending).
    /// </summary>
    Task<IReadOnlyList<Candle>> GetClosedCandlesBeforeUnscopedAsync(
        long symbolId,
        Timeframe timeframe,
        DateTime beforeOpenTimeUtc,
        int count,
        CancellationToken cancellationToken = default);
}
