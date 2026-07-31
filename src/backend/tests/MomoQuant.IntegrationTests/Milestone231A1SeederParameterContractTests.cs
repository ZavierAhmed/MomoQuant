using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using MomoQuant.Application.Strategies.MomoAdaptive;
using MomoQuant.Application.Strategies.MomoRange;
using MomoQuant.Domain.Exchanges;
using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Strategies;
using MomoQuant.Persistence;
using MomoQuant.Persistence.Seeding;

namespace MomoQuant.IntegrationTests;

/// <summary>
/// Milestone 23.1A1C — MySQL seeder reconciliation for canonical parameter contracts.
/// </summary>
[Collection("Integration")]
public sealed class Milestone231A1SeederParameterContractTests : IClassFixture<DisposableIntegrationDatabaseFixture>
{
    private readonly DisposableIntegrationDatabaseFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Milestone231A1SeederParameterContractTests(
        DisposableIntegrationDatabaseFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Theory]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.H1)]
    [InlineData(Timeframe.H4)]
    public async Task Seeder_Adaptive_ActiveKeysMatchContract(Timeframe timeframe)
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var contract = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();
        var active = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == timeframe && p.SymbolId == null && p.IsActive)
            .ToListAsync();

        AssertCanonicalContract(contract, active, strategy.Id, timeframe);

        var obsolete = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == timeframe && p.SymbolId == null)
            .Where(p =>
                p.ParameterKey == "htfTrendTimeframe"
                || p.ParameterKey == "htfStructureTimeframe"
                || p.ParameterKey == "requireHtfTrendAlignment"
                || p.ParameterKey == "requireHtfStructureBreak"
                || p.ParameterKey == "minBreakoutStrength"
                || p.ParameterKey == "stopBufferPercent")
            .ToListAsync();

        Assert.All(obsolete, p => Assert.False(p.IsActive));
    }

    [Theory]
    [InlineData(Timeframe.M5)]
    [InlineData(Timeframe.M15)]
    [InlineData(Timeframe.M30)]
    [InlineData(Timeframe.H1)]
    public async Task Seeder_Range_ActiveKeysMatchContract(Timeframe timeframe)
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoVolatilityRangeReversion);
        var contract = MomoVolatilityRangeReversionParameters.GetDefaultParameterContract();
        var active = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == timeframe && p.SymbolId == null && p.IsActive)
            .ToListAsync();

        AssertCanonicalContract(contract, active, strategy.Id, timeframe);

        var obsolete = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == timeframe && p.SymbolId == null)
            .Where(p =>
                p.ParameterKey == "rangeLookbackBars"
                || p.ParameterKey == "maxVolatilityAtrPercent"
                || p.ParameterKey == "meanReversionZonePercent"
                || p.ParameterKey == "requireRangeConfirmation"
                || p.ParameterKey == "fixedRewardRisk"
                || p.ParameterKey == "stopBufferPercent")
            .ToListAsync();

        Assert.All(obsolete, p => Assert.False(p.IsActive));
    }

    [Fact]
    public async Task Seeder_ObsoleteKeys_AreDeactivatedOnReseed()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var obsolete = new StrategyParameter
        {
            StrategyId = strategy.Id,
            ParameterKey = "htfTrendTimeframe",
            ParameterValue = "1h",
            ValueType = SettingValueType.String,
            Timeframe = Timeframe.M5,
            SymbolId = null,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.StrategyParameters.Add(obsolete);
        await db.SaveChangesAsync();

        await seeder.SeedAsync();
        await db.Entry(obsolete).ReloadAsync();
        Assert.False(obsolete.IsActive);
    }

    [Fact]
    public async Task Seeder_IsIdempotent_ActiveKeyCountsStable()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var adaptive = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var range = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoVolatilityRangeReversion);

        var adaptiveBefore = await db.StrategyParameters.CountAsync(p => p.StrategyId == adaptive.Id && p.SymbolId == null && p.IsActive);
        var rangeBefore = await db.StrategyParameters.CountAsync(p => p.StrategyId == range.Id && p.SymbolId == null && p.IsActive);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var adaptiveAfter = await db.StrategyParameters.CountAsync(p => p.StrategyId == adaptive.Id && p.SymbolId == null && p.IsActive);
        var rangeAfter = await db.StrategyParameters.CountAsync(p => p.StrategyId == range.Id && p.SymbolId == null && p.IsActive);

        Assert.Equal(adaptiveBefore, adaptiveAfter);
        Assert.Equal(rangeBefore, rangeAfter);
        Assert.Equal(1, await db.Strategies.CountAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout));
        Assert.Equal(1, await db.Strategies.CountAsync(s => s.Code == StrategyCode.MomoVolatilityRangeReversion));
    }

    [Fact]
    public async Task Seeder_CaseInsensitiveKeys_DoNotCreateDuplicates()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var row = await db.StrategyParameters.SingleAsync(p =>
            p.StrategyId == strategy.Id
            && p.Timeframe == Timeframe.M5
            && p.SymbolId == null
            && p.ParameterKey == "fixedRewardRisk");

        row.ParameterKey = "FixedRewardRisk";
        row.ParameterValue = "2.50";
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await seeder.SeedAsync();

        var matches = await db.StrategyParameters
            .Where(p =>
                p.StrategyId == strategy.Id
                && p.Timeframe == Timeframe.M5
                && p.SymbolId == null)
            .ToListAsync();

        var fixedRr = matches
            .Where(p => string.Equals(p.ParameterKey, "fixedRewardRisk", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(fixedRr);
        Assert.Equal("fixedRewardRisk", fixedRr[0].ParameterKey);
        Assert.True(fixedRr[0].IsActive);
    }

    [Fact]
    public async Task Seeder_BoolAndDecimal_NormalizeInContractComparisons()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var contract = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();

        Assert.Equal("true", NormalizeDecimalish(contract["requireHistogramExpansion"]));
        Assert.Equal("2.5", NormalizeDecimalish(contract["fixedRewardRisk"]));

        await RestoreAdaptiveSymbolNullContractAsync(db, strategy.Id, Timeframe.M5);

        var boolRow = await db.StrategyParameters.SingleAsync(p =>
            p.StrategyId == strategy.Id
            && p.Timeframe == Timeframe.M5
            && p.SymbolId == null
            && p.ParameterKey == "requireHistogramExpansion");
        var rrRow = await db.StrategyParameters.SingleAsync(p =>
            p.StrategyId == strategy.Id
            && p.Timeframe == Timeframe.M5
            && p.SymbolId == null
            && p.ParameterKey == "fixedRewardRisk");

        Assert.Equal(NormalizeDecimalish(contract["requireHistogramExpansion"]), NormalizeDecimalish(boolRow.ParameterValue));
        Assert.Equal(NormalizeDecimalish(contract["fixedRewardRisk"]), NormalizeDecimalish(rrRow.ParameterValue));
        Assert.Equal(NormalizeDecimalish("True"), NormalizeDecimalish(boolRow.ParameterValue));
        Assert.Equal(NormalizeDecimalish("2.50"), NormalizeDecimalish(rrRow.ParameterValue));
    }

    [Fact]
    public async Task Seeder_PreservesUnprovenancedAdaptiveRewardRiskOf20()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        await DeleteAnySeedProvenanceRowsAsync(db, strategy.Id);

        var row = await db.StrategyParameters.SingleAsync(p =>
            p.StrategyId == strategy.Id
            && p.Timeframe == Timeframe.M5
            && p.SymbolId == null
            && p.ParameterKey == "fixedRewardRisk");

        row.ParameterValue = "2.0";
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await seeder.SeedAsync();
        await db.Entry(row).ReloadAsync();
        Assert.Equal(NormalizeDecimalish("2.0"), NormalizeDecimalish(row.ParameterValue));

        await RestoreAdaptiveSymbolNullContractAsync(db, strategy.Id, Timeframe.M5);
    }

    [Fact]
    public async Task Seeder_DoesNotBlindlyOverwriteNonSeedAdaptiveRewardRisk()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var row = await db.StrategyParameters.SingleAsync(p =>
            p.StrategyId == strategy.Id
            && p.Timeframe == Timeframe.M5
            && p.SymbolId == null
            && p.ParameterKey == "fixedRewardRisk");

        row.ParameterValue = "3.25";
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await seeder.SeedAsync();
        await db.Entry(row).ReloadAsync();
        Assert.Equal("3.25", NormalizeDecimalish(row.ParameterValue));

        await RestoreAdaptiveSymbolNullContractAsync(db, strategy.Id, Timeframe.M5);
    }

    [Fact]
    public async Task Seeder_AdaptiveRewardRiskReseed_IsIdempotent()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await RestoreAdaptiveSymbolNullContractAsync(
            db,
            (await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout)).Id,
            Timeframe.M5);
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var contract = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();
        var active = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == Timeframe.M5 && p.SymbolId == null && p.IsActive)
            .ToListAsync();

        Assert.Equal(contract.Count, active.Count);
        Assert.Equal(active.Count, active.Select(p => p.ParameterKey.ToLowerInvariant()).Distinct().Count());
        var row = Assert.Single(active, p => string.Equals(p.ParameterKey, "fixedRewardRisk", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(NormalizeDecimalish("2.50"), NormalizeDecimalish(row.ParameterValue));

        var provenanceRows = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.ParameterKey == "seedProvenance")
            .ToListAsync();
        Assert.Empty(provenanceRows);
    }

    [Fact]
    public async Task Seeder_BlankAdaptiveRewardRisk_BackfillsTo250()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var row = await db.StrategyParameters.SingleAsync(p =>
            p.StrategyId == strategy.Id
            && p.Timeframe == Timeframe.M5
            && p.SymbolId == null
            && p.ParameterKey == "fixedRewardRisk");

        row.ParameterValue = "   ";
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await seeder.SeedAsync();
        await db.Entry(row).ReloadAsync();
        Assert.Equal(NormalizeDecimalish("2.50"), NormalizeDecimalish(row.ParameterValue));
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task Seeder_MissingAdaptiveDefaults_Receive250AndExactContractKeys()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var contract = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();
        var existing = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == Timeframe.M5 && p.SymbolId == null)
            .ToListAsync();
        _output.WriteLine(BuildContractDiagnostic(contract, existing.Where(p => p.IsActive).ToList(), strategy.Id, Timeframe.M5));
        db.StrategyParameters.RemoveRange(existing);
        await DeleteAnySeedProvenanceRowsAsync(db, strategy.Id);
        await db.SaveChangesAsync();

        await seeder.SeedAsync();

        var active = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == Timeframe.M5 && p.SymbolId == null && p.IsActive)
            .ToListAsync();
        AssertCanonicalContract(contract, active, strategy.Id, Timeframe.M5);

        Assert.Equal(NormalizeDecimalish("2.50"), NormalizeDecimalish(Assert.Single(active, p =>
            string.Equals(p.ParameterKey, "fixedRewardRisk", StringComparison.OrdinalIgnoreCase)).ParameterValue));

        var provenance = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.ParameterKey == "seedProvenance")
            .ToListAsync();
        Assert.Empty(provenance);
    }

    [Fact]
    public async Task Seeder_DoesNotInsertHiddenSeedProvenanceParameter()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        await DeleteAnySeedProvenanceRowsAsync(db, strategy.Id);
        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var provenance = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.ParameterKey == "seedProvenance")
            .ToListAsync();
        Assert.Empty(provenance);
    }

    [Fact]
    public async Task Seeder_PreservesSyntheticUnknownAdaptiveGlobalParameters()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var contract = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();
        var syntheticKeys = new[]
        {
            "testOnlyUnknownAdaptiveA",
            "testOnlyUnknownAdaptiveB",
            "testOnlyUnknownAdaptiveC"
        };
        db.StrategyParameters.AddRange(syntheticKeys.Select(key => new StrategyParameter
        {
            StrategyId = strategy.Id,
            ParameterKey = key,
            ParameterValue = "test-only",
            ValueType = SettingValueType.String,
            Timeframe = Timeframe.M5,
            SymbolId = null,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        }));
        await db.SaveChangesAsync();

        await seeder.SeedAsync();
        var active = await db.StrategyParameters
            .Where(p => p.StrategyId == strategy.Id && p.Timeframe == Timeframe.M5 && p.SymbolId == null && p.IsActive)
            .ToListAsync();

        Assert.Equal(27, active.Count);
        Assert.All(syntheticKeys, key => Assert.Contains(active, parameter =>
            string.Equals(parameter.ParameterKey, key, StringComparison.Ordinal)
            && parameter.IsActive));
        var canonical = active.Where(parameter => contract.ContainsKey(parameter.ParameterKey)).ToList();
        AssertCanonicalContract(contract, canonical, strategy.Id, Timeframe.M5);
    }

    [Fact]
    public async Task Seeder_PreservesSymbolSpecificOverrides()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var strategy = await db.Strategies.SingleAsync(s => s.Code == StrategyCode.MomoAdaptiveMultiTimeframeTrendBreakout);
        var exchange = await db.Exchanges.AsNoTracking().OrderBy(item => item.Id).FirstAsync();
        var symbol = new Symbol
        {
            ExchangeId = exchange.Id,
            SymbolName = "SEEDERCONTRACT",
            BaseAsset = "SEEDER",
            QuoteAsset = "USDT",
            ContractType = ContractType.Perpetual,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.Symbols.Add(symbol);
        await db.SaveChangesAsync();

        var overrideRow = new StrategyParameter
        {
            StrategyId = strategy.Id,
            ParameterKey = "fixedRewardRisk",
            ParameterValue = "4.75",
            ValueType = SettingValueType.Decimal,
            Timeframe = Timeframe.M5,
            SymbolId = symbol.Id,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.StrategyParameters.Add(overrideRow);
        await db.SaveChangesAsync();

        await seeder.SeedAsync();
        await db.Entry(overrideRow).ReloadAsync();

        Assert.Equal("4.75", overrideRow.ParameterValue);
        Assert.Equal(symbol.Id, overrideRow.SymbolId);
        Assert.True(overrideRow.IsActive);

        db.StrategyParameters.Remove(overrideRow);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Seeder_PreservesArchivedHistoricalParameters()
    {
        await using var testScope = await _fixture.CreateTestScopeAsync();
        var db = testScope.Db;
        var seeder = testScope.Seeder;
        await seeder.SeedAsync();

        var archived = await db.Strategies.FirstOrDefaultAsync(s => s.Code == StrategyCode.EmaPullback);
        if (archived is null)
        {
            archived = new Strategy
            {
                Code = StrategyCode.EmaPullback,
                Name = "EMA Pullback",
                Description = "Archived historical",
                IsEnabled = false,
                Version = "1.0.0",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Strategies.Add(archived);
            await db.SaveChangesAsync();
        }

        var historical = new StrategyParameter
        {
            StrategyId = archived.Id,
            ParameterKey = "PullbackTolerancePercent",
            ParameterValue = "0.42",
            ValueType = SettingValueType.Decimal,
            Timeframe = Timeframe.M5,
            SymbolId = null,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var existing = await db.StrategyParameters.FirstOrDefaultAsync(p =>
            p.StrategyId == archived.Id
            && p.ParameterKey == "PullbackTolerancePercent"
            && p.Timeframe == Timeframe.M5
            && p.SymbolId == null);

        if (existing is not null)
        {
            existing.ParameterValue = "0.42";
            existing.IsActive = true;
            existing.UpdatedAtUtc = DateTime.UtcNow;
            historical = existing;
        }
        else
        {
            db.StrategyParameters.Add(historical);
        }

        await db.SaveChangesAsync();
        var historicalId = historical.Id;

        await seeder.SeedAsync();

        var reloaded = await db.StrategyParameters.SingleAsync(p => p.Id == historicalId);
        Assert.Equal("0.42", reloaded.ParameterValue);
        Assert.True(reloaded.IsActive);
        Assert.Equal(archived.Id, reloaded.StrategyId);
    }

    /// <summary>
    /// Restores Adaptive symbol-null fixedRewardRisk to the contract default so shared MySQL
    /// integration state does not leak user-edit mutation fixtures into later contract proofs.
    /// </summary>
    private static async Task RestoreAdaptiveSymbolNullContractAsync(
        MomoQuantDbContext db,
        long strategyId,
        Timeframe timeframe)
    {
        await DeleteAnySeedProvenanceRowsAsync(db, strategyId);

        var contract = MomoAdaptiveMtfTrendBreakoutEvaluator.GetDefaultParameterContract();
        var rr = await db.StrategyParameters.SingleAsync(p =>
            p.StrategyId == strategyId
            && p.Timeframe == timeframe
            && p.SymbolId == null
            && p.ParameterKey == "fixedRewardRisk");
        rr.ParameterValue = contract["fixedRewardRisk"];
        rr.IsActive = true;
        rr.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task DeleteAnySeedProvenanceRowsAsync(MomoQuantDbContext db, long strategyId)
    {
        var provenance = await db.StrategyParameters
            .Where(p => p.StrategyId == strategyId && p.ParameterKey == "seedProvenance")
            .ToListAsync();
        if (provenance.Count == 0)
        {
            return;
        }

        db.StrategyParameters.RemoveRange(provenance);
        await db.SaveChangesAsync();
    }

    private static string NormalizeDecimalish(string value)
    {
        if (bool.TryParse(value, out var boolean))
        {
            return boolean ? "true" : "false";
        }

        if (decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static void AssertCanonicalContract(
        IReadOnlyDictionary<string, string> contract,
        IReadOnlyList<StrategyParameter> active,
        long strategyId,
        Timeframe timeframe)
    {
        var diagnostic = BuildContractDiagnostic(contract, active, strategyId, timeframe);
        var canonicalKeys = new HashSet<string>(contract.Keys, StringComparer.OrdinalIgnoreCase);
        var missing = contract.Keys.Where(key => !active.Any(row =>
            string.Equals(row.ParameterKey, key, StringComparison.OrdinalIgnoreCase))).ToList();
        var unexpected = active.Where(row => !canonicalKeys.Contains(row.ParameterKey)).ToList();
        var duplicates = active.GroupBy(row => row.ParameterKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();
        var mismatches = contract.Where(pair => active.Where(row =>
                string.Equals(row.ParameterKey, pair.Key, StringComparison.OrdinalIgnoreCase))
            .Any(row => NormalizeDecimalish(row.ParameterValue) != NormalizeDecimalish(pair.Value)))
            .ToList();

        Assert.True(
            active.Count == contract.Count
            && missing.Count == 0
            && unexpected.Count == 0
            && duplicates.Count == 0
            && mismatches.Count == 0,
            diagnostic);
    }

    private static string BuildContractDiagnostic(
        IReadOnlyDictionary<string, string> contract,
        IReadOnlyList<StrategyParameter> active,
        long strategyId,
        Timeframe timeframe)
    {
        var canonicalKeys = new HashSet<string>(contract.Keys, StringComparer.OrdinalIgnoreCase);
        var missing = contract.Keys.Where(key => !active.Any(row =>
            string.Equals(row.ParameterKey, key, StringComparison.OrdinalIgnoreCase)));
        var unexpected = active.Where(row => !canonicalKeys.Contains(row.ParameterKey)).ToList();
        var duplicates = active.GroupBy(row => row.ParameterKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: [{string.Join(", ", group.Select(row => row.Id))}]");
        var mismatches = contract.Select(pair => new
            {
                pair.Key,
                Expected = pair.Value,
                Actual = active.Where(row => string.Equals(row.ParameterKey, pair.Key, StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.ParameterValue)
                    .ToList()
            })
            .Where(item => item.Actual.Any(value => NormalizeDecimalish(value) != NormalizeDecimalish(item.Expected)))
            .Select(item => $"{item.Key}: expected {item.Expected}; actual [{string.Join(", ", item.Actual)}]");

        var rows = active.OrderBy(row => row.Id).Select(row =>
            $"Id={row.Id}; ParameterKey={row.ParameterKey}; ParameterValue={row.ParameterValue}; ValueType={row.ValueType}; Timeframe={row.Timeframe}; SymbolId={row.SymbolId?.ToString() ?? "null"}; IsActive={row.IsActive}; CreatedAtUtc={row.CreatedAtUtc:O}; UpdatedAtUtc={row.UpdatedAtUtc:O}");
        return $"""
            Adaptive parameter contract diagnostic
            StrategyId: {strategyId}
            Timeframe: {timeframe}
            Expected canonical count: {contract.Count}
            Actual active count: {active.Count}
            Missing canonical keys: [{string.Join(", ", missing)}]
            Unexpected active keys: [{string.Join(", ", unexpected.Select(row => row.ParameterKey))}]
            Case-insensitive duplicates: [{string.Join("; ", duplicates)}]
            Duplicate row IDs: [{string.Join(", ", active.GroupBy(row => row.ParameterKey, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).SelectMany(group => group.Select(row => row.Id)))}]
            Value mismatches: [{string.Join("; ", mismatches)}]
            Full active rows:
            {string.Join(Environment.NewLine, rows)}
            """;
    }
}
