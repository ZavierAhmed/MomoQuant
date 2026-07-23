using MomoQuant.Application.Common;
using MomoQuant.Application.TradingSystems.Dtos;

namespace MomoQuant.Application.TradingSystems;

public interface ISkSystemPdfExportService
{
    /// <summary>
    /// Builds a beginner-friendly, analysis-only PDF report for a saved SK analysis.
    /// Loads the stored analysis, embeds the optional chart image, and returns the PDF
    /// bytes and a descriptive file name. Never creates trades, orders, or executions.
    /// </summary>
    Task<ServiceResult<SkPdfDocumentDto>> ExportAsync(
        long analysisId,
        SkExportPdfRequest request,
        CancellationToken cancellationToken = default);
}
