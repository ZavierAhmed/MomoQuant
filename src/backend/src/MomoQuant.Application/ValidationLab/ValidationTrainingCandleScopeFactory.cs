using System.Security.Cryptography;
using System.Text;
using MomoQuant.Application.MarketData;
using MomoQuant.Application.Strategies;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.ValidationLab;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationTrainingCandleScopeFactory
{
    Task<IValidationTrainingCandleScope> CreateLtfWarmupBootstrapAsync(
        ValidationLtfWarmupBootstrapRequest request,
        CancellationToken cancellationToken = default);

    Task<IValidationTrainingCandleScope> CreateCanonicalAsync(
        ValidationCanonicalTrainingCandleScopeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Quarantined — always throws before candle access.</summary>
    [Obsolete("CreateAsync is quarantined. Use CreateLtfWarmupBootstrapAsync or CreateCanonicalAsync.")]
    Task<IValidationTrainingCandleScope> CreateAsync(
        ValidationTrainingCandleScopeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Quarantined — always throws before candle access.</summary>
    [Obsolete("CreateForExperimentAsync is quarantined. Use CreateLtfWarmupBootstrapAsync or CreateCanonicalAsync.")]
    Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
        ValidationExperiment experiment,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds an immutable training candle scope from DB candles strictly before ValidationStartUtc.
/// Loads exact warm-up (latest N closed bars before evaluation start) plus evaluation bars.
/// When Adaptive requires HTF, binds mapped HTF into the scope at construction (Milestone 23.1B1A/B1B/C1).
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

    /// <summary>Bootstrap access evidence from the most recent canonical scope construction attempt.</summary>
    public IReadOnlyList<ValidationCandleAccessRecord> LastBootstrapAccessEvidence { get; private set; } =
        Array.Empty<ValidationCandleAccessRecord>();

    public Task<IValidationTrainingCandleScope> CreateAsync(
        ValidationTrainingCandleScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        throw new InvalidOperationException(
            "CreateAsync is quarantined. Use CreateLtfWarmupBootstrapAsync or CreateCanonicalAsync.");
    }

#pragma warning disable CS0618
    public Task<IValidationTrainingCandleScope> CreateForExperimentAsync(
        ValidationExperiment experiment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        throw new InvalidOperationException(
            "CreateForExperimentAsync is quarantined. Use CreateLtfWarmupBootstrapAsync or CreateCanonicalAsync.");
    }
#pragma warning restore CS0618

    public async Task<IValidationTrainingCandleScope> CreateLtfWarmupBootstrapAsync(
        ValidationLtfWarmupBootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (!TimeframeParser.TryParse(request.Timeframe, out var timeframe))
        {
            throw new InvalidOperationException($"Unknown timeframe '{request.Timeframe}'.");
        }

        return await CreateLtfOnlyScopeAsync(request, timeframe, cancellationToken);
    }

    public async Task<IValidationTrainingCandleScope> CreateCanonicalAsync(
        ValidationCanonicalTrainingCandleScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_recorder is null)
        {
            throw new InvalidOperationException("recorder required for canonical validation training scope construction.");
        }

        request.ValidateAuthoritativeBindings();

        var experiment = request.Experiment;
        var requirements = request.Requirements;
        var audit = request.AuditExecution;

        if (!TimeframeParser.TryParse(experiment.Timeframe, out var timeframe))
        {
            throw new InvalidOperationException($"Unknown timeframe '{experiment.Timeframe}'.");
        }

        var evalStart = DateTime.SpecifyKind(experiment.TrainingStartUtc!.Value, DateTimeKind.Utc);
        var evalEndExclusive = DateTime.SpecifyKind(request.TrainingEvaluationEndExclusiveUtc, DateTimeKind.Utc);
        var boundary = DateTime.SpecifyKind(experiment.ValidationStartUtc!.Value, DateTimeKind.Utc);
        var loadEndExclusive = evalEndExclusive <= boundary ? evalEndExclusive : boundary;
        var requiredWarmup = requirements.RequiredWarmupCandleCount;
        var scopeExecutionId = audit.ScopeExecutionId;
        var bootstrapRecords = new List<ValidationCandleAccessRecord>();

        using (_capability.Activate())
        {
            var (warmup, evaluation, availableWarmup, status) = await LoadLtfPartitionAsync(
                experiment.Id,
                experiment.SymbolId,
                timeframe,
                evalStart,
                loadEndExclusive,
                boundary,
                requiredWarmup,
                requirements.RequirementsVersion,
                cancellationToken);

            var exchangeId = experiment.ExchangeId;
            Timeframe? mappedHtf = null;

            if (requirements.RequiresHigherTimeframePartition)
            {
                if (string.IsNullOrWhiteSpace(requirements.RequiredHigherTimeframeApi)
                    || !TimeframeParser.TryParse(requirements.RequiredHigherTimeframeApi, out var requiredHtf))
                {
                    throw new InvalidOperationException(
                        $"Canonical validation requires a mapped HTF partition for strategy '{requirements.StrategyCode}'.");
                }

                var resolvedHtf = MomoAdaptiveMtfTrendBreakoutEvaluator.ResolveHigherTimeframe(timeframe);
                if (resolvedHtf != requiredHtf)
                {
                    throw new InvalidOperationException(
                        $"Requirements HTF '{requirements.RequiredHigherTimeframeApi}' does not match internal mapping " +
                        $"'{TimeframeParser.ToApiString(resolvedHtf)}' for execution timeframe '{experiment.Timeframe}'.");
                }

                mappedHtf = requiredHtf;

                var htfWarmup = Math.Max(requiredWarmup, 200);
                var raw = await _candles.GetCandlesChronologicalUnscopedAsync(
                    experiment.SymbolId,
                    mappedHtf.Value,
                    fromUtc: evalStart,
                    toUtc: loadEndExclusive,
                    warmUpCount: htfWarmup,
                    cancellationToken);

                var validation = ValidationHtfPartitionValidator.ValidateRawHtfPartitionFailClosed(
                    raw,
                    experiment.SymbolId,
                    exchangeId,
                    mappedHtf.Value,
                    loadEndExclusive,
                    boundary);

                if (!validation.Succeeded)
                {
                    var denied = CreateBootstrapAccessRecord(
                        experiment.Id,
                        experiment.SymbolId,
                        exchangeId,
                        requirements.StrategyCode,
                        requirements.StrategyVersion,
                        audit.AuditExecutionId,
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
                        experiment.Id,
                        experiment.SymbolId,
                        experiment.Symbol,
                        experiment.Timeframe,
                        requirements,
                        audit,
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
                        experiment.Id,
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

                var htfPartition = new Dictionary<Timeframe, IReadOnlyList<Candle>>
                {
                    [mappedHtf.Value] = validation.Authorized
                };
                var htfFingerprint = ValidationTrainingCandleScope.ComputeContentFingerprint(validation.Authorized);

                bootstrapRecords.Add(CreateBootstrapAccessRecord(
                    experiment.Id,
                    experiment.SymbolId,
                    exchangeId,
                    requirements.StrategyCode,
                    requirements.StrategyVersion,
                    audit.AuditExecutionId,
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

                LastBootstrapAccessEvidence = bootstrapRecords;

                var combined = warmup.Concat(evaluation).ToList();
                var partition = ValidationTrainingCandleScope.BuildPartition(
                    experiment.Id,
                    experiment.SymbolId,
                    experiment.Symbol,
                    experiment.Timeframe,
                    requiredWarmup,
                    availableWarmup,
                    evaluation.Count,
                    status,
                    evalStart,
                    loadEndExclusive,
                    boundary,
                    requirements.RequirementsVersion,
                    warmup,
                    evaluation,
                    combined,
                    strategyCode: requirements.StrategyCode,
                    strategyVersion: requirements.StrategyVersion,
                    exchangeId: exchangeId,
                    mappedHigherTimeframe: TimeframeParser.ToApiString(mappedHtf.Value),
                    higherTimeframeContentFingerprint: htfFingerprint);

                return new ValidationTrainingCandleScope(
                    partition,
                    warmup,
                    evaluation,
                    scopeExecutionId: scopeExecutionId,
                    boundAuditExecutionId: audit.AuditExecutionId,
                    higherTimeframePartition: htfPartition,
                    strategyCode: requirements.StrategyCode,
                    strategyVersion: requirements.StrategyVersion,
                    exchangeId: exchangeId,
                    mappedHigherTimeframe: mappedHtf,
                    bootstrapAccessRecords: bootstrapRecords);
            }

            LastBootstrapAccessEvidence = bootstrapRecords;

            var combinedNoHtf = warmup.Concat(evaluation).ToList();
            var partitionNoHtf = ValidationTrainingCandleScope.BuildPartition(
                experiment.Id,
                experiment.SymbolId,
                experiment.Symbol,
                experiment.Timeframe,
                requiredWarmup,
                availableWarmup,
                evaluation.Count,
                status,
                evalStart,
                loadEndExclusive,
                boundary,
                requirements.RequirementsVersion,
                warmup,
                evaluation,
                combinedNoHtf,
                strategyCode: requirements.StrategyCode,
                strategyVersion: requirements.StrategyVersion,
                exchangeId: exchangeId,
                mappedHigherTimeframe: null,
                higherTimeframeContentFingerprint: null);

            return new ValidationTrainingCandleScope(
                partitionNoHtf,
                warmup,
                evaluation,
                scopeExecutionId: scopeExecutionId,
                boundAuditExecutionId: audit.AuditExecutionId,
                higherTimeframePartition: null,
                strategyCode: requirements.StrategyCode,
                strategyVersion: requirements.StrategyVersion,
                exchangeId: exchangeId,
                mappedHigherTimeframe: mappedHtf,
                bootstrapAccessRecords: bootstrapRecords);
        }
    }

    private async Task<IValidationTrainingCandleScope> CreateLtfOnlyScopeAsync(
        ValidationLtfWarmupBootstrapRequest request,
        Timeframe timeframe,
        CancellationToken cancellationToken)
    {
        var evalStart = request.TrainingEvaluationStartUtc;
        var evalEndExclusive = request.TrainingEvaluationEndExclusiveUtc;
        var boundary = request.ValidationBoundaryUtc;
        var loadEndExclusive = evalEndExclusive <= boundary ? evalEndExclusive : boundary;
        var requiredWarmup = request.RequiredWarmupCandleCount;
        var scopeExecutionId = Guid.NewGuid();

        using (_capability.Activate())
        {
            var (warmup, evaluation, availableWarmup, status) = await LoadLtfPartitionAsync(
                request.ValidationExperimentId,
                request.SymbolId,
                timeframe,
                evalStart,
                loadEndExclusive,
                boundary,
                requiredWarmup,
                request.RequirementsVersion,
                cancellationToken);

            LastBootstrapAccessEvidence = Array.Empty<ValidationCandleAccessRecord>();
            var combined = warmup.Concat(evaluation).ToList();
            var exchangeId = request.ExchangeId is > 0 ? request.ExchangeId : null;
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
                mappedHigherTimeframe: null,
                higherTimeframeContentFingerprint: null);

            return new ValidationTrainingCandleScope(
                partition,
                warmup,
                evaluation,
                scopeExecutionId: scopeExecutionId,
                boundAuditExecutionId: null,
                higherTimeframePartition: null,
                strategyCode: request.StrategyCode,
                strategyVersion: request.StrategyVersion,
                exchangeId: exchangeId,
                mappedHigherTimeframe: null,
                bootstrapAccessRecords: Array.Empty<ValidationCandleAccessRecord>());
        }
    }

    private async Task<(IReadOnlyList<Candle> Warmup, IReadOnlyList<Candle> Evaluation, int AvailableWarmup, ValidationWarmupStatus Status)>
        LoadLtfPartitionAsync(
            long validationExperimentId,
            long symbolId,
            Timeframe timeframe,
            DateTime evalStart,
            DateTime loadEndExclusive,
            DateTime boundary,
            int requiredWarmup,
            string requirementsVersion,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<Candle> warmup;
        if (requiredWarmup > 0)
        {
            warmup = await _candles.GetClosedCandlesBeforeUnscopedAsync(
                symbolId,
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
            symbolId,
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
                       && c.SymbolId == symbolId;
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
                       && c.SymbolId == symbolId;
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
                validationExperimentId,
                requiredWarmup,
                availableWarmup,
                $"Insufficient warm-up candles for validation training experiment {validationExperimentId}: " +
                $"available={availableWarmup}, required={requiredWarmup}, status={status}, " +
                $"requirementsVersion={requirementsVersion}.");
        }

        return (warmup, evaluation, availableWarmup, status);
    }

    private async Task PersistDeniedBootstrapEvidenceAsync(
        long validationExperimentId,
        long symbolId,
        string symbolName,
        string timeframeApi,
        StrategyExecutionRequirements requirements,
        ValidationAuditExecution audit,
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
            || audit.AuditExecutionId == Guid.Empty
            || audit.ScopeExecutionId == Guid.Empty
            || string.IsNullOrWhiteSpace(audit.ExecutionToken)
            || audit.AttemptNumber <= 0)
        {
            return;
        }

        var combined = warmup.Concat(evaluation).ToList();
        var partition = ValidationTrainingCandleScope.BuildPartition(
            validationExperimentId,
            symbolId,
            symbolName,
            timeframeApi,
            requiredWarmup,
            availableWarmup,
            evaluation.Count,
            status,
            evalStart,
            loadEndExclusive,
            boundary,
            requirements.RequirementsVersion,
            warmup,
            evaluation,
            combined,
            strategyCode: requirements.StrategyCode,
            strategyVersion: requirements.StrategyVersion,
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
            boundAuditExecutionId: audit.AuditExecutionId,
            higherTimeframePartition: null,
            strategyCode: requirements.StrategyCode,
            strategyVersion: requirements.StrategyVersion,
            exchangeId: exchangeId,
            mappedHigherTimeframe: mappedHtf,
            bootstrapAccessRecords: bootstrapRecords);

        using var auditAmbient = ValidationAuditExecutionAmbient.Enter(new ValidationAuditExecutionAmbientContext
        {
            AuditExecutionId = audit.AuditExecutionId,
            ScopeExecutionId = audit.ScopeExecutionId,
            ExecutionToken = audit.ExecutionToken,
            AttemptNumber = audit.AttemptNumber,
            ValidationExperimentId = validationExperimentId
        });

        await _recorder.FlushAsync(shell, cancellationToken).ConfigureAwait(false);
        await shell.DisposeAsync().ConfigureAwait(false);
    }

    private static ValidationCandleAccessRecord CreateBootstrapAccessRecord(
        long validationExperimentId,
        long symbolId,
        long exchangeId,
        string? strategyCode,
        string? strategyVersion,
        Guid auditExecutionId,
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
            AccessEventId = CreateStableBootstrapAccessEventId(scopeExecutionId, sequenceNumber),
            ScopeExecutionId = scopeExecutionId,
            ScopeSequenceNumber = sequenceNumber,
            ValidationExperimentId = validationExperimentId,
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
            DatasetPartition = $"BootstrapHTF:{htfApi}:S{symbolId}:E{exchangeId}",
            RecorderVersion = ValidationCandleAccessRecorder.RecorderVersion,
            AuditExecutionId = auditExecutionId,
            RequestSymbolId = symbolId,
            RequestExchangeId = exchangeId,
            RequestTimeframeApi = htfApi,
            RequestStrategyCode = strategyCode,
            RequestStrategyVersion = strategyVersion
        };
    }

    private static Guid CreateStableBootstrapAccessEventId(Guid scopeExecutionId, long sequenceNumber)
    {
        var payload = $"{scopeExecutionId:N}|{sequenceNumber}|{ValidationCandleAccessPurpose.FactoryBootstrapHtfLoad}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return new Guid(hash.AsSpan(0, 16));
    }
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
