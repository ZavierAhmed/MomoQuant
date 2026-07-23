using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MomoQuant.Application;
using MomoQuant.Application.Common;
using MomoQuant.Application.Exports;
using MomoQuant.Application.Exports.Dtos;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Api.Controllers;

[ApiController]
[Route("api/v1/exports")]
[Authorize(Policy = AuthorizationPolicies.ResearchRead)]
public sealed class ExportsController : ControllerBase
{
    private readonly IExportService _exportService;

    public ExportsController(IExportService exportService) => _exportService = exportService;

    [HttpGet("scopes")]
    public ActionResult<ApiResponse<IReadOnlyList<ExportScopeDto>>> GetScopes()
    {
        var scopes = _exportService.GetScopes();
        return Ok(ApiResponse<IReadOnlyList<ExportScopeDto>>.Ok(scopes));
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.ResearchExecute)]
    public async Task<ActionResult<ApiResponse<ExportJobDto>>> Create(
        [FromBody] CreateExportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _exportService.CreateAsync(request, userId: null, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ExportJobDto>.Ok(result.Data!))
            : BadRequest(ApiResponse<ExportJobDto>.Fail(result.ErrorMessage ?? "Export failed."));
    }

    [HttpGet("{exportId:long}")]
    public async Task<ActionResult<ApiResponse<ExportJobDto>>> Get(long exportId, CancellationToken cancellationToken)
    {
        var result = await _exportService.GetAsync(exportId, cancellationToken);
        return result.Succeeded
            ? Ok(ApiResponse<ExportJobDto>.Ok(result.Data!))
            : NotFound(ApiResponse<ExportJobDto>.Fail(result.ErrorMessage ?? "Export not found."));
    }

    [HttpGet("{exportId:long}/download")]
    public async Task<IActionResult> Download(long exportId, CancellationToken cancellationToken)
    {
        var result = await _exportService.DownloadAsync(exportId, cancellationToken);
        if (!result.Succeeded || result.Data is null)
        {
            return NotFound(ApiResponse<object>.Fail(result.ErrorMessage ?? "Export file not found."));
        }

        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }
}
