using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MomoQuant.Application.ValidationLab.Dtos;
using MomoQuant.Domain.Enums;

namespace MomoQuant.Application.ValidationLab;

public interface IValidationExportContentVerifier
{
    ExportVerificationResult Verify(
        ValidationExperimentDetailDto detail,
        string? jsonContent,
        string? csvBundle,
        string? pdfSummaryText);
}

public sealed class ExportManifest
{
    public string ManifestVersion { get; init; } = "ValidationExportManifest/v2";
    public long? ExportJobId { get; init; }
    public long? ExperimentId { get; init; }
    public string? Format { get; init; }
    public string? FileName { get; init; }
    public string? RelativePath { get; init; }
    public long? FileSizeBytes { get; init; }
    public string ContentSha256 { get; init; } = string.Empty;
    public string? DatasetName { get; init; }
    public int ExpectedRecordCount { get; init; }
    public int ActualRecordCount { get; init; }
    public IReadOnlyList<string> RequiredSections { get; init; } = [];
    public IReadOnlyList<string> PresentSections { get; init; } = [];
    public int SegmentResultCount { get; init; }
    public int OverlapCandidateCount { get; init; }
    public int QualificationRuleCount { get; init; }
    public bool HasOverlapCandidatesCsv { get; init; }
    public bool HasExclusivityReport { get; init; }
    public bool HasPopulationCounts { get; init; }
    public ValidationExportVerificationStatus VerificationStatus { get; init; } =
        ValidationExportVerificationStatus.NotRun;
    public IReadOnlyList<string> Issues { get; init; } = [];
    public DateTime VerifiedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class ExportVerificationResult
{
    public ValidationExportVerificationStatus Status { get; init; }
    public ExportManifest Manifest { get; init; } = new();
    public IReadOnlyList<string> Issues { get; init; } = [];
}

public sealed class ValidationExportContentVerifier : IValidationExportContentVerifier
{
    private static readonly string[] RequiredJsonSections =
    [
        "experiment",
        "split",
        "candidateReconciliation",
        "frozenConfiguration",
        "trainingResults",
        "validationResults",
        "qualification",
        "holdoutExclusivity",
        "exportManifest"
    ];

    private static readonly string[] RequiredCsvMarkers =
    [
        "training-trials.csv",
        "validation-experiment-segment-results.csv",
        "validation-experiment-candidate-reconciliation.csv",
        "training-candidates.csv",
        "validation-candidates.csv",
        "validation-experiment-qualification-rules.csv",
        "diagnostics.csv"
    ];

    private static readonly string[] RequiredPdfHeadings =
    [
        "Experiment Overview",
        "Data Quality",
        "Chronological Split",
        "Candidate Reconciliation",
        "Training Search",
        "Top Trials",
        "Frozen Configuration",
        "Training Metrics",
        "Holdout Metrics",
        "Gross versus Net Results",
        "Layer Comparison",
        "Parameter Stability",
        "Leakage Audit",
        "Qualification Rules",
        "Final Verdict",
        "Limitations"
    ];

    public ExportVerificationResult Verify(
        ValidationExperimentDetailDto detail,
        string? jsonContent,
        string? csvBundle,
        string? pdfSummaryText)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            issues.Add("JSON export content is empty.");
        }
        else
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;
                // Envelope may wrap under results.complete or be the complete object itself.
                var subject = root;
                if (root.TryGetProperty("results", out var results)
                    && results.TryGetProperty("complete", out var complete))
                {
                    subject = complete;
                }

                foreach (var section in RequiredJsonSections)
                {
                    if (!HasPropertyIgnoreCase(subject, section))
                    {
                        issues.Add($"JSON missing required section: {section}");
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"JSON parse failed: {ex.Message}");
            }
        }

        var csv = csvBundle ?? string.Empty;
        foreach (var marker in RequiredCsvMarkers)
        {
            if (!csv.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"CSV bundle missing required file marker: {marker}");
            }
        }

        var pdf = pdfSummaryText ?? string.Empty;
        foreach (var heading in RequiredPdfHeadings)
        {
            if (!pdf.Contains(heading, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"PDF summary missing required heading: {heading}");
            }
        }

        if (detail.CrossSegmentOverlapCount > 0
            && !csv.Contains("overlap-candidates.csv", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("CSV bundle missing overlap-candidates.csv for cross-segment overlaps.");
        }

        if (pdf.Length < 200)
        {
            issues.Add("PDF summary is too short — likely a placeholder.");
        }

        var presentSections = RequiredJsonSections
            .Where(s => jsonContent?.Contains($"\"{s}\"", StringComparison.OrdinalIgnoreCase) == true
                || jsonContent?.Contains(s, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var overlapCount = 0;
        if (!string.IsNullOrWhiteSpace(detail.HoldoutExclusivityJson))
        {
            try
            {
                using var excl = JsonDocument.Parse(detail.HoldoutExclusivityJson);
                if (excl.RootElement.TryGetProperty("crossSegmentOverlapCount", out var c)
                    || excl.RootElement.TryGetProperty("CrossSegmentOverlapCount", out c))
                {
                    overlapCount = c.GetInt32();
                }
            }
            catch
            {
                issues.Add("HoldoutExclusivityJson could not be parsed.");
            }
        }

        var hasPopulation = (detail.SegmentResults ?? []).Any(s =>
            s.PersistedCandidateRowCount > 0 || s.MetricIncludedCandidateCount > 0);

        var payloadForHash = string.Join('\n',
            jsonContent ?? string.Empty,
            csv,
            pdf);
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadForHash)));

        var status = issues.Count == 0
            ? ValidationExportVerificationStatus.Passed
            : ValidationExportVerificationStatus.Failed;

        var manifest = new ExportManifest
        {
            ExperimentId = detail.Id,
            Format = "bundle",
            DatasetName = detail.Name,
            ContentSha256 = sha,
            ExpectedRecordCount = detail.SegmentResults?.Count ?? 0,
            ActualRecordCount = detail.SegmentResults?.Count ?? 0,
            RequiredSections = RequiredJsonSections,
            PresentSections = presentSections,
            SegmentResultCount = detail.SegmentResults?.Count ?? 0,
            OverlapCandidateCount = overlapCount,
            QualificationRuleCount = CountQualificationRules(detail.QualificationRuleResultsJson),
            HasOverlapCandidatesCsv = csv.Contains("overlap-candidates.csv", StringComparison.OrdinalIgnoreCase),
            HasExclusivityReport = !string.IsNullOrWhiteSpace(detail.HoldoutExclusivityJson)
                || (jsonContent?.Contains("holdoutExclusivity", StringComparison.OrdinalIgnoreCase) ?? false),
            HasPopulationCounts = hasPopulation,
            VerificationStatus = status,
            Issues = issues,
            VerifiedAtUtc = DateTime.UtcNow
        };

        return new ExportVerificationResult
        {
            Status = status,
            Manifest = manifest,
            Issues = issues
        };
    }

    private static int CountQualificationRules(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.GetArrayLength()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static bool HasPropertyIgnoreCase(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return false;
        if (el.TryGetProperty(name, out _)) return true;
        foreach (var p in el.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
