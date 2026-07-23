using MomoQuant.Application.Common;
using MomoQuant.Application.Simulation.Dtos;
using MomoQuant.Domain.Enums;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Simulation;

public interface ISimulationRunSummaryService
{
    /// <summary>
    /// Creates or updates the unified summary for a simulation run/session.
    /// Safe to call from completion hooks; failures are swallowed so they never break a run.
    /// </summary>
    Task RecordAsync(SimulationRunSummaryInput input, CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResult<SimulationRunSummaryDto>>> GetSummariesAsync(
        PagedRequest request,
        SimulationRunSourceType? sourceType = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SimulationRunSummaryDto>> GetSummaryAsync(
        SimulationRunSourceType sourceType,
        long sourceId,
        CancellationToken cancellationToken = default);
}
