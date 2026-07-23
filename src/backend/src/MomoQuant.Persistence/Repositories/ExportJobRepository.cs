using Microsoft.EntityFrameworkCore;
using MomoQuant.Application.Abstractions;
using MomoQuant.Domain.Exports;

namespace MomoQuant.Persistence.Repositories;

public sealed class ExportJobRepository : IExportJobRepository
{
    private readonly MomoQuantDbContext _dbContext;

    public ExportJobRepository(MomoQuantDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(ExportJob job, CancellationToken cancellationToken = default)
    {
        _dbContext.ExportJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<ExportJob?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _dbContext.ExportJobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken);

    public async Task UpdateAsync(ExportJob job, CancellationToken cancellationToken = default)
    {
        _dbContext.ExportJobs.Update(job);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
