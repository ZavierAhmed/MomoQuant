namespace MomoQuant.Application.Abstractions;

public interface ITradingSettingsRepository
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);
}
