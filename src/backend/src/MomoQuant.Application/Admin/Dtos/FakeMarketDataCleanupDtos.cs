namespace MomoQuant.Application.Admin.Dtos;

public sealed class FakeMarketDataCleanupRequest
{
    public const string RequiredConfirmation = "DELETE_FAKE_MARKET_DATA";

    public string Confirmation { get; set; } = string.Empty;

    public bool IncludeBacktests { get; set; } = true;

    public bool IncludeReplay { get; set; } = true;

    public bool IncludePaperTrading { get; set; } = true;

    public bool IncludeAiDecisions { get; set; } = true;

    public bool IncludeRiskDecisions { get; set; } = true;

    public bool IncludeAuditLogs { get; set; }

    public bool ResetPaperAccounts { get; set; }
}

public sealed class FakeMarketDataCleanupPreviewDto
{
    public IReadOnlyList<FakeMarketDataCleanupPreviewItemDto> Items { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public DateTime GeneratedAtUtc { get; init; }
}

public sealed class FakeMarketDataCleanupPreviewItemDto
{
    public string EntityName { get; init; } = string.Empty;

    public int Count { get; init; }

    public bool WillDelete { get; init; }
}

public sealed class FakeMarketDataCleanupResultDto
{
    public IReadOnlyList<FakeMarketDataCleanupResultItemDto> Items { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public DateTime CompletedAtUtc { get; init; }
}

public sealed class FakeMarketDataCleanupResultItemDto
{
    public string EntityName { get; init; } = string.Empty;

    public int CountBefore { get; init; }

    public int CountDeleted { get; init; }

    public int CountAfter { get; init; }
}
