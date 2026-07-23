using MomoQuant.Domain.Benchmarks;
using MomoQuant.Shared.Contracts;

namespace MomoQuant.Application.Abstractions;

public interface IStrategyBenchmarkRunRepository
{
    Task<StrategyBenchmarkRun?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<StrategyBenchmarkRun> Items, int TotalCount)> GetPagedAsync(
        PagedRequest request,
        CancellationToken cancellationToken = default);

    Task AddAsync(StrategyBenchmarkRun run, CancellationToken cancellationToken = default);

    Task UpdateAsync(StrategyBenchmarkRun run, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IStrategyBenchmarkResultRepository
{
    Task<IReadOnlyList<StrategyBenchmarkResult>> GetByBenchmarkRunIdAsync(
        long benchmarkRunId,
        CancellationToken cancellationToken = default);

    Task AddAsync(StrategyBenchmarkResult result, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<StrategyBenchmarkResult> results, CancellationToken cancellationToken = default);

    Task DeleteByBenchmarkRunIdAsync(long benchmarkRunId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IStrategyBenchmarkRunItemRepository
{
    Task<IReadOnlyList<StrategyBenchmarkRunItem>> GetByBenchmarkRunIdAsync(
        long benchmarkRunId,
        CancellationToken cancellationToken = default);

    Task<StrategyBenchmarkRunItem?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task AddRangeAsync(IReadOnlyCollection<StrategyBenchmarkRunItem> items, CancellationToken cancellationToken = default);

    Task UpdateAsync(StrategyBenchmarkRunItem item, CancellationToken cancellationToken = default);

    Task DeleteByBenchmarkRunIdAsync(long benchmarkRunId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
