using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POSSystem.Domain.Entities;

namespace POSSystem.Infrastructure.Persistence.Configurations;

public class LoyaltyTransactionConfiguration : IEntityTypeConfiguration<LoyaltyTransaction>
{
    public void Configure(EntityTypeBuilder<LoyaltyTransaction> builder)
    {
        builder.ToTable("LoyaltyTransactions");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Points).HasColumnType("numeric(18,2)");
        builder.Property(t => t.PointsRemaining).HasColumnType("numeric(18,2)");
        builder.Property(t => t.Notes).HasMaxLength(500);

        builder.HasOne(t => t.Customer)
            .WithMany(c => c.LoyaltyTransactions)
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Sale)
            .WithMany()
            .HasForeignKey(t => t.SaleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => t.CustomerId);
        builder.HasIndex(t => t.ExpiresAt);
    }
}
