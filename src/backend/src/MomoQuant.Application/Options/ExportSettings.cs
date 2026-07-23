namespace MomoQuant.Application.Options;

public sealed class ExportSettings
{
    public const string SectionName = "MomoQuant:Exports";

    public string RootPath { get; set; } = @"C:\momo_quants_exports";
    public int MaxRowsInPdfTables { get; set; } = 500;
    public bool IncludeCandlesByDefault { get; set; }
    public bool IncludeIndicatorSeriesByDefault { get; set; }
    public bool IncludeDiagnosticsByDefault { get; set; } = true;
    public bool IncludeRawJsonByDefault { get; set; } = true;
}
