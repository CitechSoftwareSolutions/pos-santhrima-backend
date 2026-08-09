using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POSSystem.Domain.Entities;

namespace POSSystem.Infrastructure.Persistence.Configurations;

public class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("Promotions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(500);

        builder.Property(p => p.BuyQuantity).HasColumnType("numeric(18,3)");
        builder.Property(p => p.FreeQuantity).HasColumnType("numeric(18,3)");
        builder.Property(p => p.MinQuantity).HasColumnType("numeric(18,3)");
        builder.Property(p => p.DiscountPercent).HasColumnType("numeric(5,2)");
        builder.Property(p => p.BundleQuantity).HasColumnType("numeric(18,3)");
        builder.Property(p => p.BundlePrice).HasColumnType("numeric(18,2)");

        builder.HasOne(p => p.Product)
            .WithMany(pr => pr.Promotions)
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.ProductId, p.IsActive });
    }
}
