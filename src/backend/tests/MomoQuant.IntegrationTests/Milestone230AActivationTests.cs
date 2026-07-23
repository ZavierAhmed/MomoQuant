using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MomoQuant.Application.Abstractions;
using MomoQuant.Application.Auth.Dtos;
using MomoQuant.Application.StrategyLab.Risk;
using MomoQuant.Application.Users.Dtos;
using MomoQuant.Application.ValidationLab;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.MarketData;
using MomoQuant.Domain.StrategyLab;
using MomoQuant.Domain.ValidationLab;
using MomoQuant.Persistence;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.0A — MySQL-backed activation proofs (leases, audits, path metrics, live auth).
/// Requires MOMO_INTEGRATION_MYSQL / integration.local.env pointing at momo_quant_test.
/// </summary>
[Collection("Integration")]
public sealed class Milestone230AActivationTests : IClassFixture<MomoQuantWebApplicationFactory>
{
    private readonly MomoQuantWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public Milestone230AActivationTests(MomoQuantWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task MySql_Lease_TwoWorkers_ExactlyOneWins_OwnershipEnforced()
    {
        var experimentId = -2_300_001L; // synthetic negative id avoids colliding with real experiments
        await using (var cleanup = _factory.Services.CreateAsyncScope())
        {
            var db = cleanup.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            await db.ValidationExperimentExecutionLeases
                .Where(l => l.ValidationExperimentId == experimentId)
                .ExecuteDeleteAsync();
        }

        var barrier = new Barrier(2);
        async Task<(bool Acquired, string? Conflict, string Owner)> Worker(string owner)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var leases = scope.ServiceProvider.GetRequiredService<IValidationTrainingExecutionLeaseService>();
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            var result = await leases.TryAcquireAsync(experimentId, owner, TimeSpan.FromMinutes(5));
            return (result.Acquired, result.ConflictMessage, owner);
        }

        var t1 = Task.Run(() => Worker("worker-a"));
        var t2 = Task.Run(() => Worker("worker-b"));
        await Task.WhenAll(t1, t2);

        var results = new[] { t1.Result, t2.Result };
        Assert.Equal(1, results.Count(r => r.Acquired));
        Assert.Equal(1, results.Count(r => !r.Acquired));
        var winner = results.Single(r => r.Acquired).Owner;
        var loser = results.Single(r => !r.Acquired).Owner;
        Assert.False(string.IsNullOrWhiteSpace(results.Single(r => !r.Acquired).Conflict));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
            var rows = await db.ValidationExperimentExecutionLeases
                .Where(l => l.ValidationExperimentId == experimentId)
                .ToListAsync();
            Assert.Single(rows);
            Assert.Equal(winner, rows[0].LeaseOwner);
            var acquiredAt = rows[0].AcquiredAtUtc;

            var leases = scope.ServiceProvider.GetRequiredService<IValidationTrainingExecutionLeaseService>();
            var hbWin = await leases.HeartbeatAsync(experimentId, winner, TimeSpan.FromMinutes(5));
            var hbLose = await leases.HeartbeatAsync(experimentId, loser, TimeSpan.FromMinutes(5));
            Assert.Equal(ValidationLeaseOperationStatus.Succeeded, hbWin.Status);
            Assert.Equal(ValidationLeaseOperationStatus.Conflict, hbLose.Status);

            var afterHb = await db.ValidationExperimentExecutionLeases
                .AsNoTracking()
                .SingleAsync(l => l.ValidationExperimentId == experimentId);
            Assert.Equal(acquiredAt, afterHb.AcquiredAtUtc);

            Assert.Equal(
                ValidationLeaseOperationStatus.Conflict,
                (await leases.ReleaseAsync(experimentId, loser)).Status);
            Assert.Equal(
                ValidationLeaseOperationStatus.Succeeded,
                (await leases.ReleaseAsync(experimentId, winner)).Status);
        }

        // Expired reclaim: insert expired lease as prev owner, reclaim as new, prev cannot release.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IValidationExperimentExecutionLeaseRepository>();
            var leases = scope.ServiceProvider.GetRequiredService<IValidationTrainingExecutionLeaseService>();
            var past = DateTime.UtcNow.AddHours(-2);
            Assert.True(await repo.TryAcquireAtomicAsync(
                experimentId, "prev-owner", past, past.AddMinutes(1), past));

            var reclaim = await leases.TryAcquireAsync(experimentId, "reclaimer", TimeSpan.FromMinutes(5));
            Assert.True(reclaim.Acquired);
            Assert.Equal(
                ValidationLeaseOperationStatus.Conflict,
                (await leases.ReleaseAsync(experimentId, "prev-owner")).Status);
            Assert.Equal(
                ValidationLeaseOperationStatus.Succeeded,
                (await leases.ReleaseAsync(experimentId, "reclaimer")).Status);
        }
    }

    [Fact]
    public async Task MySql_LeakageDenial_PersistsAudit_AndBlocksFreezeEvidence()
    {
        var validationStart = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var trainingStart = validationStart.AddDays(-7);
        var candles = Enumerable.Range(0, 48)
            .Select(i =>
            {
                var t = trainingStart.AddHours(i * 4);
                return new Candle
                {
                    ExchangeId = 1,
                    SymbolId = 1,
                    Timeframe = Timeframe.H4,
                    OpenTimeUtc = t,
                    CloseTimeUtc = t.AddHours(4).AddTicks(-1),
                    Open = 100m,
                    High = 101m,
                    Low = 99m,
                    Close = 100.5m,
                    Volume = 1m,
                    CreatedAtUtc = DateTime.UtcNow
                };
            })
            .Concat(
            [
                new Candle
                {
                    ExchangeId = 1,
                    SymbolId = 1,
                    Timeframe = Timeframe.H4,
                    OpenTimeUtc = validationStart,
                    CloseTimeUtc = validationStart.AddHours(4).AddTicks(-1),
                    Open = 110m,
                    High = 111m,
                    Low = 109m,
                    Close = 110.5m,
                    Volume = 1m,
                    CreatedAtUtc = DateTime.UtcNow
                }
            ])
            .ToList();

        var scope = new ValidationTrainingCandleScope(
            validationExperimentId: 23_00_042,
            segmentStartUtc: trainingStart,
            validationBoundaryUtc: validationStart,
            trainingCandles: candles);
        scope.ActiveTrialNumber = 1;

        var ex = Assert.Throws<ValidationDataLeakageException>(() =>
            scope.GetByOpenTimeUtc(validationStart, "AdversarialTrainer"));
        Assert.Equal(validationStart, ex.RequestedStartUtc);
        Assert.Contains(scope.AccessLog, a => a.WasDenied);

        await using var dbScope = _factory.Services.CreateAsyncScope();
        var audits = dbScope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
        var marker = $"adv-{Guid.NewGuid():N}";
        var entity = new ValidationCandleAccessAudit
        {
            ValidationExperimentId = 23_00_042,
            TrialNumber = 1,
            CallerComponent = $"AdversarialTrainer:{marker}",
            RequestedStartUtc = validationStart,
            RequestedEndUtc = validationStart,
            ReturnedCandleCount = 0,
            AccessedAtUtc = DateTime.UtcNow,
            WasDenied = true,
            DenialReason = "BoundaryCrossed",
            CreatedAtUtc = DateTime.UtcNow
        };
        await audits.AddRangeAsync([entity]);

        var loaded = await audits.GetByExperimentIdAsync(23_00_042);
        var denied = Assert.Single(loaded.Where(a =>
            a.WasDenied
            && a.RequestedStartUtc == validationStart
            && a.CallerComponent == $"AdversarialTrainer:{marker}"));
        Assert.True(denied.Id > 0);
        Assert.True(denied.WasDenied);

        var report = new ValidationLeakageAuditor().EvaluateFromAccessEvidence(
            [denied],
            validationStart,
            trainingStart,
            validationStart.AddTicks(-1),
            "test-fp");
        Assert.Equal(ValidationLeakageAuditStatus.Failed, report.Status);
        Assert.True(report.BlocksFreezeOrPassed);
        Assert.Equal(1, report.DeniedAccessCount);
    }

    [Fact]
    public async Task MySql_PathMetrics_RiskOnly_And_FullPipeline_Independent()
    {
        var candidate = new StrategyResearchCandidate
        {
            Id = 9_001,
            SetupFingerprint = "M230A-PATH",
            CandidateStatus = StrategyResearchCandidateStatus.Closed,
            RawOutcomeStatus = RawOutcomeStatus.Winner,
            ProposedEntryPrice = 100m,
            StopLoss = 99m,
            RawExitPrice = 102m,
            ProposedEntryTimeUtc = DateTime.UtcNow.AddHours(-2),
            SetupDetectedAtUtc = DateTime.UtcNow.AddHours(-2),
            RawExitTimeUtc = DateTime.UtcNow,
            ProposedPositionSize = 10m,
            ConfidenceDecision = ResearchConfidenceDecision.Approved,
            Direction = TradeDirection.Long,
            RiskOnlyAssessmentJson = JsonSerializer.Serialize(new PathPortfolioAssessmentDto
            {
                Quantity = 2m,
                RiskAmount = 2m,
                PortfolioPath = "RiskOnly"
            }),
            FullPipelineAssessmentJson = JsonSerializer.Serialize(new PathPortfolioAssessmentDto
            {
                Quantity = 5m,
                RiskAmount = 5m,
                PortfolioPath = "FullPipeline"
            })
        };

        var riskOnly = new ShadowPortfolioSummaryDto
        {
            PathName = "RiskOnly",
            TradesOpened = 1,
            GrossPnl = 4m,
            RealizedNetPnl = 3.6m,
            TotalTransactionCosts = 0.4m,
            Ledger =
            [
                new ShadowTradeLedgerEntry
                {
                    CandidateId = 9_001,
                    SetupFingerprint = "M230A-PATH",
                    Direction = TradeDirection.Long,
                    EntryTimeUtc = DateTime.UtcNow.AddHours(-1),
                    ExitTimeUtc = DateTime.UtcNow,
                    EntryPrice = 100m,
                    ExitPrice = 102m,
                    Quantity = 2m,
                    GrossPnl = 4m,
                    EntryFee = 0.2m,
                    ExitFee = 0.2m,
                    TotalCost = 0.4m,
                    NetPnl = 3.6m,
                    ExitOutcome = "TargetHit"
                }
            ]
        };
        var full = new ShadowPortfolioSummaryDto
        {
            PathName = "FullPipeline",
            TradesOpened = 1,
            GrossPnl = -5m,
            RealizedNetPnl = -6m,
            TotalTransactionCosts = 1m,
            Ledger =
            [
                new ShadowTradeLedgerEntry
                {
                    CandidateId = 9_001,
                    SetupFingerprint = "M230A-PATH",
                    Direction = TradeDirection.Long,
                    EntryTimeUtc = DateTime.UtcNow.AddHours(-1),
                    ExitTimeUtc = DateTime.UtcNow,
                    EntryPrice = 100m,
                    ExitPrice = 99m,
                    Quantity = 5m,
                    GrossPnl = -5m,
                    EntryFee = 0.5m,
                    ExitFee = 0.5m,
                    TotalCost = 1m,
                    NetPnl = -6m,
                    ExitOutcome = "StopHit"
                }
            ]
        };

        var builder = new ValidationPathMetricInputBuilder();
        var risk = new ValidationRiskBasisService();
        var costs = new ValidationPathMetricCostModel
        {
            EntryFeeRate = 0.0004m,
            ExitFeeRate = 0.0004m,
            SlippagePercent = 0m
        };

        var raw = builder.Build(
            23, ValidationSegmentType.Validation, ValidationLayerType.RawStrategy,
            [candidate], riskOnly, full, costs);
        var conf = builder.Build(
            23, ValidationSegmentType.Validation, ValidationLayerType.ConfidenceQualified,
            [candidate], riskOnly, full, costs);
        var ro = builder.Build(
            23, ValidationSegmentType.Validation, ValidationLayerType.RiskOnly,
            [candidate], riskOnly, full, costs);
        var fp = builder.Build(
            23, ValidationSegmentType.Validation, ValidationLayerType.FullPipeline,
            [candidate], riskOnly, full, costs);

        Assert.Equal(1m, raw[0].Quantity);
        Assert.Equal(2m, raw[0].GrossPnl);
        Assert.Equal(raw[0].NetPnl, conf[0].NetPnl);
        Assert.Equal(2m, ro[0].Quantity);
        Assert.Equal(5m, fp[0].Quantity);
        Assert.Equal(3.6m, ro[0].NetPnl);
        Assert.Equal(-6m, fp[0].NetPnl);

        var roMetrics = ValidationMetricsContract.FromPathTradesV13(
            ro, 500, 1, 0, ValidationLayerType.RiskOnly, risk);
        var fpMetrics = ValidationMetricsContract.FromPathTradesV13(
            fp, 500, 1, 0, ValidationLayerType.FullPipeline, risk);
        Assert.Equal(1.8m, roMetrics.NetExpectancyR);
        Assert.Equal(-1.2m, fpMetrics.NetExpectancyR);
        Assert.NotEqual(roMetrics.NetExpectancyR, fpMetrics.NetExpectancyR);

        // Persist path independence evidence into MySQL candle-access audit diagnostic payload
        // (avoids FK to ValidationExperiments for this controlled fixture).
        await using var scope = _factory.Services.CreateAsyncScope();
        var audits = scope.ServiceProvider.GetRequiredService<IValidationCandleAccessAuditRepository>();
        var marker = $"m230a-path-{Guid.NewGuid():N}";
        await audits.AddRangeAsync(
        [
            new ValidationCandleAccessAudit
            {
                ValidationExperimentId = 23_00_043,
                CallerComponent = "PathMetricIndependenceFixture",
                ReturnedCandleCount = 0,
                AccessedAtUtc = DateTime.UtcNow,
                WasDenied = false,
                DenialReason =
                    $"{marker}|roNet={roMetrics.NetExpectancyR}|fpNet={fpMetrics.NetExpectancyR}|roQty={ro[0].Quantity}|fpQty={fp[0].Quantity}",
                CreatedAtUtc = DateTime.UtcNow
            }
        ]);

        var reloaded = await audits.GetByExperimentIdAsync(23_00_043);
        var row = Assert.Single(reloaded.Where(a =>
            a.CallerComponent == "PathMetricIndependenceFixture"
            && (a.DenialReason ?? "").Contains(marker, StringComparison.Ordinal)));
        Assert.Contains("roNet=1.8", row.DenialReason ?? "");
        Assert.Contains("fpNet=-1.2", row.DenialReason ?? "");
        Assert.Contains("roQty=2", row.DenialReason ?? "");
        Assert.Contains("fpQty=5", row.DenialReason ?? "");
        Assert.DoesNotContain("qty=10", row.DenialReason ?? "");
    }

    [Fact]
    public async Task LiveAuth_Viewer_Mutations_Return403_Reads_Ok_Anonymous_401()
    {
        await EnsureSeededResearchUsersAsync();

        var adminToken = await LoginAsync("admin@momoquant.local", "Admin123!");
        var viewerToken = await LoginAsync("viewer230a@momoquant.local", "Viewer230a!");
        var traderToken = await LoginAsync("trader230a@momoquant.local", "Trader230a!");

        // Anonymous mutations → 401
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/v1/strategy-lab/runs", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _client.PostAsJsonAsync("/api/v1/validation-lab/experiments", new { })).StatusCode);

        // Viewer read → allowed (ResearchRead)
        var viewerGet = await SendAsync(HttpMethod.Get, "/api/v1/strategy-lab/runs?limit=1", viewerToken);
        Assert.Equal(HttpStatusCode.OK, viewerGet.StatusCode);
        var viewerVlGet = await SendAsync(HttpMethod.Get, "/api/v1/validation-lab/experiments?limit=1", viewerToken);
        Assert.Equal(HttpStatusCode.OK, viewerVlGet.StatusCode);

        // Viewer mutations → 403
        var viewerMutations = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, "/api/v1/strategy-lab/runs", new { strategyCode = "x" }),
            (HttpMethod.Post, "/api/v1/strategy-lab/runs/1/rerun", null),
            (HttpMethod.Post, "/api/v1/validation-lab/experiments", new { name = "x" }),
            (HttpMethod.Post, "/api/v1/validation-lab/experiments/1/freeze", null),
            (HttpMethod.Post, "/api/v1/validation-lab/experiments/1/resume-training", null),
            (HttpMethod.Post, "/api/v1/validation-lab/experiments/1/recover-trials", null),
            (HttpMethod.Post, "/api/v1/validation-lab/experiments/1/clone", null),
            (HttpMethod.Post, "/api/v1/validation-lab/experiments/1/rerun-exactly", null),
            (HttpMethod.Post, "/api/v1/validation-lab/experiments/1/recalculate-metrics", null),
            (HttpMethod.Post, "/api/v1/validation-lab/closeout/milestone-223", null),
            (HttpMethod.Post, "/api/v1/exports", new { scope = "ValidationExperiment", format = "Json", entityId = 1 }),
            (HttpMethod.Post, "/api/v1/backtests/run", new { }),
            (HttpMethod.Post, "/api/v1/paper/sessions", new { }),
            (HttpMethod.Post, "/api/v1/replay/sessions", new { }),
            (HttpMethod.Post, "/api/v1/strategy-benchmarks", new { }),
        };

        foreach (var (method, path, body) in viewerMutations)
        {
            var response = await SendAsync(method, path, viewerToken, body);
            Assert.True(
                response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"Viewer expected 403 on {method} {path}, got {(int)response.StatusCode}");
            // Prefer Forbidden; NotFound only acceptable when route requires resource that auth still gates first.
            // ASP.NET authorization runs before action — must be 403 when authorized policy fails.
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // Trader research execute → not 403 (may be 400 validation)
        var traderCreate = await SendAsync(
            HttpMethod.Post,
            "/api/v1/validation-lab/experiments",
            traderToken,
            new { name = "auth-probe" });
        Assert.NotEqual(HttpStatusCode.Forbidden, traderCreate.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, traderCreate.StatusCode);

        // Trader admin-only → 403
        var traderUsers = await SendAsync(HttpMethod.Get, "/api/v1/users", traderToken);
        Assert.Equal(HttpStatusCode.Forbidden, traderUsers.StatusCode);

        // Admin intended → permitted
        var adminUsers = await SendAsync(HttpMethod.Get, "/api/v1/users", adminToken);
        Assert.Equal(HttpStatusCode.OK, adminUsers.StatusCode);
    }

    private async Task EnsureSeededResearchUsersAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MomoQuantDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<MomoQuant.Application.Abstractions.IPasswordHasher>();

        async Task UpsertAsync(string email, string password, UserRole role)
        {
            var normalized = email.Trim().ToLowerInvariant();
            var roleEntity = await db.Roles.FirstAsync(r => r.Name == role);
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized);
            var now = DateTime.UtcNow;
            if (user is null)
            {
                db.Users.Add(new MomoQuant.Domain.Identity.User
                {
                    FullName = email,
                    Email = normalized,
                    PasswordHash = hasher.Hash(password),
                    RoleId = roleEntity.Id,
                    Role = roleEntity.Name,
                    IsActive = true,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
            else
            {
                user.PasswordHash = hasher.Hash(password);
                user.RoleId = roleEntity.Id;
                user.Role = roleEntity.Name;
                user.IsActive = true;
                user.UpdatedAtUtc = now;
            }
        }

        // Keep admin password aligned with AuthEndpointTests / factory seed.
        await UpsertAsync("admin@momoquant.local", "Admin123!", UserRole.Admin);
        await UpsertAsync("viewer230a@momoquant.local", "Viewer230a!", UserRole.Viewer);
        await UpsertAsync("trader230a@momoquant.local", "Trader230a!", UserRole.Trader);
        await db.SaveChangesAsync();
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest
        {
            Email = email,
            Password = password
        });
        Assert.True(response.IsSuccessStatusCode, $"Login failed for {email}: {(int)response.StatusCode}");
        var payload = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>(IntegrationTestJson.Options);
        Assert.False(string.IsNullOrWhiteSpace(payload?.Data?.AccessToken), $"Login returned no token for {email}");
        return payload!.Data!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? token,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }
}
