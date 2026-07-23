using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MomoQuant.Domain.Sessions;

namespace MomoQuant.Persistence.Configurations;

internal sealed class TradingSessionSymbolConfiguration : IEntityTypeConfiguration<TradingSessionSymbol>
{
    public void Configure(EntityTypeBuilder<TradingSessionSymbol> builder)
    {
        builder.ToTable("TradingSessionSymbols");

        builder.Property(sessionSymbol => sessionSymbol.Timeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(sessionSymbol => sessionSymbol.HigherTimeframe)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(sessionSymbol => sessionSymbol.CreatedAtUtc).HasColumnName("CreatedAt");

        builder.HasOne<TradingSession>()
            .WithMany()
            .HasForeignKey(sessionSymbol => sessionSymbol.TradingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Domain.Exchanges.Symbol>()
            .WithMany()
            .HasForeignKey(sessionSymbol => sessionSymbol.SymbolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
