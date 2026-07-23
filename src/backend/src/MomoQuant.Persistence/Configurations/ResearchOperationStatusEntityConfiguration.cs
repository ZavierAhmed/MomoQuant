using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Research;

namespace MomoQuant.Persistence.Configurations;

internal sealed class ResearchOperationStatusEntityConfiguration : IEntityTypeConfiguration<ResearchOperationStatusEntity>
{
    public void Configure(EntityTypeBuilder<ResearchOperationStatusEntity> builder)
    {
        builder.ToTable("ResearchOperationStatuses");
        builder.Property(e => e.OperationId).HasMaxLength(128).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(e => e.OperationType).HasMaxLength(128).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Stage).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Status).HasMaxLength(64).IsRequired();
        builder.Property(e => e.PercentComplete).HasTradingDecimal();
        builder.Property(e => e.ActiveWorkItem).HasMaxLength(256);
        builder.Property(e => e.ErrorCode).HasMaxLength(64);
        builder.Property(e => e.UserSafeErrorMessage).HasMaxLength(512);
        builder.Property(e => e.DiagnosticReference).HasMaxLength(256);
        builder.Property(e => e.LeaseOwner).HasMaxLength(128);
        builder.HasIndex(e => e.OperationId).IsUnique();
        builder.HasIndex(e => new { e.OperationType, e.EntityId });
        builder.HasIndex(e => e.CorrelationId);
    }
}
