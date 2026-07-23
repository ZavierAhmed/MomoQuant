using MomoQuant.Domain.Exports;

namespace MomoQuant.Application.Abstractions;

public interface IExportJobRepository
{
    Task AddAsync(ExportJob job, CancellationToken cancellationToken = default);
    Task<ExportJob?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task UpdateAsync(ExportJob job, CancellationToken cancellationToken = default);
}
