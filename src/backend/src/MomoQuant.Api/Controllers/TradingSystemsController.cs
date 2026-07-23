using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Common;
using MomoQuant.Application.TradingSystems;
using MomoQuant.Application.TradingSystems.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

/// <summary>
/// Trading Systems are analytical frameworks only. They never execute trades, create
/// backtests, paper sessions, benchmarks, orders, candidate trades, or shadow trades.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/trading-systems")]
public sealed class TradingSystemsController : ApiControllerBase
{
    private readonly ITradingSystemService _tradingSystemService;
    private readonly ISkSystemAnalysisService _skSystemAnalysisService;
    private readonly ISkSystemPdfExportService _skSystemPdfExportService;

    public TradingSystemsController(
        ITradingSystemService tradingSystemService,
        ISkSystemAnalysisService skSystemAnalysisService,
        ISkSystemPdfExportService skSystemPdfExportService)
    {
        _tradingSystemService = tradingSystemService;
        _skSystemAnalysisService = skSystemAnalysisService;
        _skSystemPdfExportService = skSystemPdfExportService;
    }

    [HttpGet]
    public IActionResult GetSystems() => OkResponse(_tradingSystemService.GetSystems());

    [HttpGet("sk/defaults")]
    [HttpGet("sk-system/defaults")]
    public IActionResult GetSkDefaults() => OkResponse(new SkAnalysisDefaultsDto());

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("sk/analyze")]
    [HttpPost("sk-system/analyze")]
    public async Task<IActionResult> Analyze(
        [FromBody] SkSystemAnalyzeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _skSystemAnalysisService.AnalyzeAsync(request, cancellationToken);
        return FromResult(result);
    }

    [HttpGet("sk/analyses")]
    [HttpGet("sk-system/analyses")]
    public async Task<IActionResult> GetAnalyses(
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _skSystemAnalysisService.GetRecentAnalysesAsync(limit, cancellationToken);
        return OkResponse(result.Data!);
    }

    [HttpGet("sk/analyses/{id:long}")]
    [HttpGet("sk-system/analyses/{id:long}")]
    public async Task<IActionResult> GetAnalysis(long id, CancellationToken cancellationToken)
    {
        var result = await _skSystemAnalysisService.GetAnalysisAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Analysis was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(result.Data);
    }

    [HttpGet("sk/analyses/{id:long}/chart")]
    [HttpGet("sk-system/analyses/{id:long}/chart")]
    public async Task<IActionResult> GetAnalysisChart(long id, CancellationToken cancellationToken)
    {
        var result = await _skSystemAnalysisService.GetAnalysisAsync(id, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(result.ErrorMessage ?? "Analysis was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(new
        {
            result.Data.AnalysisId,
            result.Data.Symbol,
            result.Data.PrimaryTimeframe,
            result.Data.HigherTimeframe,
            result.Data.CurrentPrice,
            result.Data.Candles,
            result.Data.ChartOverlays,
            result.Data.AnalysisOnlyDisclaimer
        });
    }

    [HttpGet("sk/analyses/{id:long}/report")]
    [HttpGet("sk-system/analyses/{id:long}/report")]
    public Task<IActionResult> GetAnalysisReport(long id, CancellationToken cancellationToken) =>
        GetAnalysis(id, cancellationToken);

    /// <summary>
    /// Exports a saved SK analysis as a PDF report. Analysis only — this never creates
    /// trades, orders, backtests, paper sessions, or benchmarks. Same authorization as
    /// viewing an analysis.
    /// </summary>
    [HttpPost("sk/analyses/{id:long}/export-pdf")]
    [HttpPost("sk-system/analyses/{id:long}/export-pdf")]
    public async Task<IActionResult> ExportAnalysisPdf(
        long id,
        [FromBody] SkExportPdfRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _skSystemPdfExportService.ExportAsync(
            id,
            request ?? new SkExportPdfRequest(),
            cancellationToken);

        if (!result.Succeeded || result.Data is null)
        {
            return FailResponse(
                result.ErrorMessage ?? "Analysis was not found.",
                StatusCodes.Status404NotFound);
        }

        return File(result.Data.Content, "application/pdf", result.Data.FileName);
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpDelete("sk/analyses/{id:long}")]
    [HttpDelete("sk-system/analyses/{id:long}")]
    public async Task<IActionResult> DeleteAnalysis(long id, CancellationToken cancellationToken)
    {
        var result = await _skSystemAnalysisService.DeleteAnalysisAsync(id, cancellationToken);
        if (!result.Succeeded)
        {
            return FailResponse(result.ErrorMessage ?? "Analysis was not found.", StatusCodes.Status404NotFound);
        }

        return OkResponse(true, "Analysis deleted.");
    }

    [Authorize(Policy = AuthorizationPolicies.AdminOrTrader)]
    [HttpPost("sk/import-required-data")]
    [HttpPost("sk-system/import-required-data")]
    public async Task<IActionResult> ImportRequiredData(
        [FromBody] SkImportRequiredDataRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _skSystemAnalysisService.ImportRequiredDataAsync(request, cancellationToken);
        return FromResult(result);
    }

    private IActionResult FromResult<T>(ServiceResult<T> result)
    {
        if (!result.Succeeded || result.Data is null)
        {
            var errors = result.ErrorField is null
                ? null
                : new List<ApiError>
                {
                    new() { Field = result.ErrorField, Message = result.ErrorMessage ?? "Invalid value." }
                };

            return FailResponse(result.ErrorMessage ?? "Request failed.", StatusCodes.Status400BadRequest, errors);
        }

        return OkResponse(result.Data);
    }
}
