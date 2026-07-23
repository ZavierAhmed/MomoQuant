using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Exports;

namespace MomoQuant.Persistence.Configurations;

public sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("ExportJobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.SourceId).HasMaxLength(128).IsRequired();
        builder.Property(job => job.FileName).HasMaxLength(512).IsRequired();
        builder.Property(job => job.FilePath).HasMaxLength(1024).IsRequired();
        builder.Property(job => job.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(job => job.ErrorMessage).HasMaxLength(4000);
        builder.HasIndex(job => new { job.Scope, job.SourceId, job.RequestedAtUtc });
    }
}
