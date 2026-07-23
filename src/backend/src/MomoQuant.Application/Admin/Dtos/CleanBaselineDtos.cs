namespace MomoQuant.Application.Admin.Dtos;

public sealed class CleanBaselineRequest
{
    public const string RequiredConfirmation = "CLEAN_MOMO_QUANT_BASELINE";

    public string Confirmation { get; set; } = string.Empty;

    public bool PreserveAdminUser { get; set; } = true;

    public bool PreserveBinanceFuturesExchange { get; set; } = true;

    public bool RemoveStrategies { get; set; } = true;

    public bool RemoveSymbols { get; set; } = true;

    public bool RemoveSimulationData { get; set; } = true;

    public bool RemoveReports { get; set; } = true;

    public bool RemoveMarketData { get; set; } = true;
}

public sealed class CleanBaselinePreviewDto
{
    public IReadOnlyList<CleanBaselinePreviewItemDto> Items { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> Preserved { get; init; } = [];

    public DateTime GeneratedAtUtc { get; init; }
}

public sealed class CleanBaselinePreviewItemDto
{
    public string EntityName { get; init; } = string.Empty;

    public int Count { get; init; }

    public bool WillDelete { get; init; }
}

public sealed class CleanBaselineResultDto
{
    public IReadOnlyList<CleanBaselineResultItemDto> Items { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public string BinanceFuturesExchangeAction { get; init; } = string.Empty;

    public DateTime CompletedAtUtc { get; init; }
}

public sealed class CleanBaselineResultItemDto
{
    public string EntityName { get; init; } = string.Empty;

    public int CountBefore { get; init; }

    public int CountDeleted { get; init; }

    public int CountAfter { get; init; }
}
