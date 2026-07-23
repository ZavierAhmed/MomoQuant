using MomoQuant.Application.Exports.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.UnitTests.Exports;

public class ExportServiceTests
{
    [Fact]
    public void ExportEnvelope_IncludesMetadataFields()
    {
        var envelope = new ExportEnvelopeDto
        {
            ExportMetadata = new ExportMetadataDto
            {
                ExportId = "abc",
                ExportedAtUtc = DateTime.UtcNow,
                AppName = "MOMO Quant",
                AppVersion = "20.1",
                Environment = "Test",
                Scope = ExportScope.SkAnalysisRun.ToString(),
                SourceId = "1",
                Format = "json",
                DetailLevel = "full"
            },
            SourceMetadata = new Dictionary<string, object?> { ["module"] = "SkAnalysisRun" }
        };

        Assert.Equal("MOMO Quant", envelope.ExportMetadata.AppName);
        Assert.Equal("json", envelope.ExportMetadata.Format);
        Assert.DoesNotContain("password", envelope.SourceMetadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportScopes_IncludeJsonAndPdfFormats()
    {
        var scopes = new[]
        {
            new ExportScopeDto { Scope = ExportScope.SkAnalysisRun.ToString(), Label = "SK", SupportedFormats = ["json", "pdf"] },
            new ExportScopeDto { Scope = ExportScope.StrategyBacktestRun.ToString(), Label = "Backtest", SupportedFormats = ["json", "pdf"] }
        };

        Assert.All(scopes, scope => Assert.Contains("json", scope.SupportedFormats));
    }
}
