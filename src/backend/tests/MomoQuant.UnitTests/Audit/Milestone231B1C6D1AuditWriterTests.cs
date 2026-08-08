using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MomoQuant.Application.Audit;
using MomoQuant.Domain.Audit;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.PaperTrading;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Services;

namespace MomoQuant.UnitTests.Audit;

public sealed class Milestone231B1C6D1AuditWriterTests
{
    [Theory]
    [InlineData("CleanBaselinePreviewed")]
    [InlineData("CleanBaselineFailed")]
    [InlineData("CleanBaselineExecuted")]
    [InlineData("FakeMarketDataCleanupPreviewed")]
    [InlineData("FakeMarketDataCleanupFailed")]
    [InlineData("FakeMarketDataCleanupExecuted")]
    public void TelemetryWriter_AcceptsAndPreservesPascalCaseProductionActions(string action)
    {
        var payload = AuditWritePayloadProtection.PrepareTelemetry(new AuditTelemetryRequest(
            action,
            "Cleanup",
            41,
            null,
            null,
            "{\"state\":\"Observed\"}",
            null,
            null));

        Assert.Equal(action, payload.Action);
    }

    [Fact]
    public void TelemetryWriter_AcceptsAndPreservesUppercaseUnderscoreActions()
    {
        const string action = "PAPER_SESSION_STARTED";

        var payload = AuditWritePayloadProtection.PrepareTelemetry(new AuditTelemetryRequest(
            action,
            "PaperTradingSession",
            41,
            null,
            null,
            null,
            null,
            null));

        Assert.Equal(action, payload.Action);
    }

    [Theory]
    [InlineData("clean baseline")]
    [InlineData("Clean/Baseline")]
    [InlineData("Clean\\Baseline")]
    [InlineData("Clean-Baseline")]
    [InlineData("1CleanBaseline")]
    public void TelemetryWriter_RejectsUnsafeActionStrings(string action)
    {
        var exception = Assert.Throws<AuditEvidenceException>(() =>
            AuditWritePayloadProtection.PrepareTelemetry(new AuditTelemetryRequest(
                action,
                "Cleanup",
                41,
                null,
                null,
                null,
                null,
                null)));

        Assert.Equal(AuditEvidenceCodes.Invalid, exception.Code);
    }

    [Fact]
    public void RequiredWriter_RetainsCanonicalActionAllowlist()
    {
        var request = PublicationRequest() with { Action = "OtherRequiredAction" };

        var exception = Assert.Throws<AuditEvidenceException>(() =>
            AuditWritePayloadProtection.PrepareRequired(request));

        Assert.Equal(AuditEvidenceCodes.Invalid, exception.Code);
    }

    [Fact]
    public async Task RequiredWriter_AttachesExactlyOneAllowlistedAuditWithoutSaving()
    {
        await using var db = CreateContext();
        var writer = new RequiredAuditWriter(db);

        writer.AttachRequired(PublicationRequest());

        var entry = Assert.Single(db.ChangeTracker.Entries<AuditLog>());
        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal(0, await db.AuditLogs.CountAsync());
        Assert.Equal(RequiredAuditActions.ParameterSetDeploymentQualified, entry.Entity.Action);
        Assert.Contains("\"parameterSetId\":11", entry.Entity.NewValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("parametersJson", entry.Entity.NewValueJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredWriter_RejectsForbiddenMetadataBeforeAttachment()
    {
        using var db = CreateContext();
        var writer = new RequiredAuditWriter(db);
        var unsafeRequest = PublicationRequest() with
        {
            Metadata = new ParameterSetPublicationAuditMetadata(
                11, "TOKEN_STRATEGY", 21, 31, "ABCDEF0123456789", "v1", Timestamp)
        };

        var exception = Assert.Throws<AuditEvidenceException>(() => writer.AttachRequired(unsafeRequest));

        Assert.Equal(AuditEvidenceCodes.Invalid, exception.Code);
        Assert.Empty(db.ChangeTracker.Entries<AuditLog>());
    }

    [Fact]
    public void RequiredWriter_PropagatesCancellationWithoutAttachment()
    {
        using var db = CreateContext();
        var writer = new RequiredAuditWriter(db);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            writer.AttachRequired(PublicationRequest(), cancellation.Token));
        Assert.Empty(db.ChangeTracker.Entries<AuditLog>());
    }

    [Fact]
    public async Task TelemetryWriter_UsesIsolatedContextAndDoesNotPersistCallerChanges()
    {
        var services = new ServiceCollection();
        var databaseName = $"audit-isolation-{Guid.NewGuid():N}";
        services.AddDbContext<MomoQuantDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IAuditTelemetryWriter, AuditTelemetryWriter>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        await using var callerScope = provider.CreateAsyncScope();
        var caller = callerScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var account = new PaperAccount
        {
            Id = 41,
            Name = "Original",
            CurrentBalance = 100,
            CurrentEquity = 100,
            IsActive = true
        };
        caller.PaperAccounts.Add(account);
        await caller.SaveChangesAsync();
        account.Name = "Uncommitted caller mutation";

        var telemetry = provider.GetRequiredService<IAuditTelemetryWriter>();
        await telemetry.WriteTelemetryAsync(new AuditTelemetryRequest(
            "PAPER_TELEMETRY_TEST",
            nameof(PaperAccount),
            account.Id,
            null,
            null,
            "{\"password\":\"never-store\",\"state\":\"Observed\"}",
            null,
            null));

        await using var verificationScope = provider.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        Assert.Equal("Original", (await verification.PaperAccounts.SingleAsync()).Name);
        var row = await verification.AuditLogs.SingleAsync();
        Assert.Contains("[REDACTED]", row.NewValueJson, StringComparison.Ordinal);
        Assert.DoesNotContain("never-store", row.NewValueJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TelemetryWriter_LogsAndSuppressesOrdinaryValidationFailure()
    {
        var logger = new RecordingLogger<AuditTelemetryWriter>();
        var services = new ServiceCollection();
        services.AddDbContext<MomoQuantDbContext>(options =>
            options.UseInMemoryDatabase($"audit-failure-{Guid.NewGuid():N}"));
        await using var provider = services.BuildServiceProvider();
        var writer = new AuditTelemetryWriter(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger);

        await writer.WriteTelemetryAsync(new AuditTelemetryRequest(
            "PAPER_TELEMETRY_TEST", "PaperTradingSession", 1, null, null, "{malformed", null, null));

        Assert.Single(logger.Errors);
        Assert.DoesNotContain("malformed", logger.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TelemetryWriter_PropagatesCancellation()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MomoQuantDbContext>(options =>
            options.UseInMemoryDatabase($"audit-cancel-{Guid.NewGuid():N}"));
        await using var provider = services.BuildServiceProvider();
        var writer = new AuditTelemetryWriter(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AuditTelemetryWriter>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => writer.WriteTelemetryAsync(
            new AuditTelemetryRequest("PAPER_TELEMETRY_TEST", "PaperTradingSession", 1, null, null, null, null, null),
            cancellation.Token));
    }

    private static readonly DateTime Timestamp = new(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);

    private static RequiredAuditRequest PublicationRequest() => new(
        RequiredAuditActions.ParameterSetDeploymentQualified,
        "StrategyParameterSet",
        11,
        null,
        null,
        LogSeverity.Info,
        new ParameterSetPublicationAuditMetadata(
            11, "MOMO_ADAPTIVE_MTF_TREND_BREAKOUT", 21, 31, "ABCDEF0123456789", "v1", Timestamp),
        Timestamp);

    private static MomoQuantDbContext CreateContext() => new(
        new DbContextOptionsBuilder<MomoQuantDbContext>()
            .UseInMemoryDatabase($"required-audit-{Guid.NewGuid():N}")
            .Options);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}
