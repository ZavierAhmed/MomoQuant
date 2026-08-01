using System.Text.RegularExpressions;
using MomoQuant.Application.Optimization;
using MomoQuant.Application.Optimization.Dtos;
using MomoQuant.Application.Validation.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;

namespace MomoQuant.UnitTests.Strategies;

public sealed class Milestone231B1C6AQualificationSemanticsTests
{
    [Fact]
    public async Task ManualUnapprovedSet_PersistsAsResearchOnly()
    {
        var (service, repository) = CreateService();

        var result = await service.SaveAsync(Request());

        Assert.True(result.Succeeded);
        Assert.False(result.Data!.IsApproved);
        AssertResearchOnly(result.Data);
        Assert.Equal(ParameterSetQualificationStatus.ResearchOnly, (await repository.GetByIdAsync(result.Data.Id))!.QualificationStatus);
    }

    [Fact]
    public async Task ManualApprovedSet_PersistsResearchApprovalWithoutDeploymentQualification()
    {
        var (service, repository) = CreateService();

        var result = await service.SaveAsync(Request(approve: true));

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.IsApproved);
        AssertResearchOnly(result.Data);
        Assert.Equal(ParameterSetQualificationStatus.ResearchOnly, (await repository.GetByIdAsync(result.Data.Id))!.QualificationStatus);
    }

    [Fact]
    public async Task DirectApproval_WithNullMetrics_RemainsResearchOnly()
    {
        var (service, repository) = CreateService();
        var saved = await service.SaveAsync(Request());
        var entity = await repository.GetByIdAsync(saved.Data!.Id);
        Assert.Null(entity!.ValidationMetricsJson);

        var approved = await service.ApproveAsync(saved.Data.Id);

        Assert.True(approved.Succeeded);
        Assert.True(approved.Data!.IsApproved);
        AssertResearchOnly(approved.Data);
        Assert.Equal(ParameterSetQualificationStatus.ResearchOnly, entity.QualificationStatus);
    }

    [Fact]
    public async Task CallerPassedStatus_DoesNotCreateDeploymentQualification()
    {
        var (service, _) = CreateService();

        var result = await service.SaveAsync(Request(
            approve: true,
            validationStatus: ValidationStatus.Passed.ToString(),
            validationTradeCount: 12));

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.IsApproved);
        AssertResearchOnly(result.Data);
    }

    [Fact]
    public async Task FabricatedNonZeroMetrics_DoNotCreateDeploymentQualification()
    {
        var (service, _) = CreateService();
        var request = Request(approve: true, validationStatus: ValidationStatus.Passed.ToString());
        request = new SaveStrategyParameterSetRequest
        {
            Name = request.Name,
            StrategyCode = request.StrategyCode,
            Timeframe = request.Timeframe,
            Parameters = request.Parameters,
            Approve = true,
            ValidationStatus = ValidationStatus.Passed.ToString(),
            ValidationMetrics = Metrics(999)
        };

        var result = await service.SaveAsync(request);

        Assert.True(result.Succeeded);
        Assert.True(result.Data!.IsApproved);
        AssertResearchOnly(result.Data);
    }

    [Fact]
    public async Task ZeroTradeResearchApproval_RemainsRejected()
    {
        var (service, _) = CreateService();

        var result = await service.SaveAsync(Request(approve: true, validationTradeCount: 0));

        Assert.False(result.Succeeded);
        Assert.Contains("No trades", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ParameterSetQualificationStatus.HistoricalNotEvaluated, "PARAMETER_SET_HISTORICAL_NOT_EVALUATED")]
    [InlineData(ParameterSetQualificationStatus.ResearchOnly, "PARAMETER_SET_RESEARCH_ONLY")]
    public async Task DtoMapping_ExposesResearchScopeAndQualificationBlocker(
        ParameterSetQualificationStatus status,
        string expectedReason)
    {
        var (service, repository) = CreateService();
        var entity = new StrategyParameterSet
        {
            Name = "Existing",
            StrategyCode = "MOMO_ADAPTIVE_MTF_TREND_BREAKOUT",
            Timeframe = "15m",
            ParametersJson = "{}",
            QualificationStatus = status,
            IsApproved = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        await repository.AddAsync(entity);

        var result = await service.ListAsync(entity.StrategyCode, null, entity.Timeframe);
        var dto = Assert.Single(result.Data!);

        Assert.Equal(status.ToString(), dto.QualificationStatus);
        Assert.Equal("Research", dto.ApprovalScope);
        Assert.False(dto.IsDeploymentQualified);
        Assert.Equal([expectedReason], dto.QualificationBlockingReasons);
    }

    [Fact]
    public void ProductionServices_HaveExactlyOneBoundedDeploymentQualifiedWriter()
    {
        var applicationRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MomoQuant.Application"));
        Assert.True(Directory.Exists(applicationRoot), $"Expected application sources at {applicationRoot}");
        var assignment = new Regex(
            @"QualificationStatus\s*=\s*ParameterSetQualificationStatus\.DeploymentQualified",
            RegexOptions.CultureInvariant);

        var violations = Directory.EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => assignment.IsMatch(File.ReadAllText(path)))
            .ToList();

        var violation = Assert.Single(violations);
        Assert.EndsWith(
            Path.Combine("ValidationLab", "ValidationParameterSetPublicationService.cs"),
            violation,
            StringComparison.OrdinalIgnoreCase);
    }

    private static (StrategyParameterSetService Service, InMemoryStrategyParameterSetRepository Repository) CreateService()
    {
        var repository = new InMemoryStrategyParameterSetRepository();
        return (new StrategyParameterSetService(repository), repository);
    }

    private static SaveStrategyParameterSetRequest Request(
        bool approve = false,
        string? validationStatus = null,
        int? validationTradeCount = null) => new()
    {
        Name = "Research set",
        StrategyCode = "MOMO_ADAPTIVE_MTF_TREND_BREAKOUT",
        Timeframe = "15m",
        Parameters = new Dictionary<string, string> { ["minimumStrength"] = "0.5" },
        Approve = approve,
        ValidationStatus = validationStatus,
        ValidationTradeCount = validationTradeCount
    };

    private static StrategyPerformanceMetricsDto Metrics(int trades) => new()
    {
        NetPnlPercent = 5m,
        WinRate = 55m,
        ProfitFactor = 1.5m,
        MaxDrawdownPercent = 4m,
        TradeCount = trades,
        AverageR = 1m,
        Expectancy = 1m,
        RecoveryFactor = 1m,
        LargestLoss = -1m,
        ConsecutiveLosses = 1
    };

    private static void AssertResearchOnly(StrategyParameterSetDto dto)
    {
        Assert.Equal(ParameterSetQualificationStatus.ResearchOnly.ToString(), dto.QualificationStatus);
        Assert.Equal("Research", dto.ApprovalScope);
        Assert.False(dto.IsDeploymentQualified);
        Assert.Equal(["PARAMETER_SET_RESEARCH_ONLY"], dto.QualificationBlockingReasons);
    }
}
