using MomoQuant.Domain.Enums;
using MomoQuant.Domain.Simulation;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Abstractions;

public interface ISimulationRunSummaryRepository
{
    Task<SimulationRunSummary?> GetBySourceAsync(
        SimulationRunSourceType sourceType,
        long sourceId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SimulationRunSummary> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        SimulationRunSourceType? sourceType = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(SimulationRunSummary summary, CancellationToken cancellationToken = default);
    Task UpdateAsync(SimulationRunSummary summary, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
