namespace MomoQuant.Application.Options;

public sealed class StrategyCatalogSettings
{
    public const string SectionName = "StrategyCatalog";

    /// <summary>
    /// When false (default) the default strategy catalog is not automatically seeded.
    /// Strategies must be researched and added manually before running benchmarks or simulations.
    /// </summary>
    public bool SeedDefaultStrategies { get; set; }
}
