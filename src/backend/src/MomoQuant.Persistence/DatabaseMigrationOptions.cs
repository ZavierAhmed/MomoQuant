namespace MomoQuant.Persistence;

public sealed class DatabaseMigrationOptions
{
    public const string SectionName = "DatabaseMigrations";

    public bool ApplyOnStartup { get; set; } = false;
    public bool RequireTestSuffixWhenApplying { get; set; } = false;
    public bool LogPendingMigrationsWhenDisabled { get; set; } = true;
}
