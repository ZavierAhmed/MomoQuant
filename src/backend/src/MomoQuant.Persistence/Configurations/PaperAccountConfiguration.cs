using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.PaperTrading;

namespace MomoQuant.Persistence.Configurations;

internal sealed class PaperAccountConfiguration : IEntityTypeConfiguration<PaperAccount>
{
    public void Configure(EntityTypeBuilder<PaperAccount> builder)
    {
        builder.ToTable("PaperAccounts");

        builder.Property(account => account.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(account => account.InitialBalance).HasTradingDecimal();
        builder.Property(account => account.CurrentBalance).HasTradingDecimal();
        builder.Property(account => account.CurrentEquity).HasTradingDecimal();
        builder.Property(account => account.TotalRealizedPnl).HasTradingDecimal();
        builder.Property(account => account.TotalUnrealizedPnl).HasTradingDecimal();
        builder.Property(account => account.TotalFees).HasTradingDecimal();
        builder.Property(account => account.MaxDrawdown).HasTradingDecimal();
        builder.Property(account => account.MaxDrawdownPercent).HasTradingDecimal();

        builder.Property(account => account.Currency)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(account => account.CreatedAtUtc).HasColumnName("CreatedAt");
        builder.Property(account => account.UpdatedAtUtc).HasColumnName("UpdatedAt");

        builder.HasIndex(account => account.Name).IsUnique();
    }
}
