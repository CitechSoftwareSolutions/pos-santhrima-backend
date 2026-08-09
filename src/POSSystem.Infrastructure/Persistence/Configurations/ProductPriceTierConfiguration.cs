using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POSSystem.Domain.Entities;

namespace POSSystem.Infrastructure.Persistence.Configurations;

public class ProductPriceTierConfiguration : IEntityTypeConfiguration<ProductPriceTier>
{
    public void Configure(EntityTypeBuilder<ProductPriceTier> builder)
    {
        builder.ToTable("ProductPriceTiers");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Quantity).HasColumnType("numeric(18,3)");
        builder.Property(t => t.RetailPrice).HasColumnType("numeric(18,2)");
        builder.Property(t => t.WholesalePrice).HasColumnType("numeric(18,2)");
        builder.Property(t => t.SpecialPrice).HasColumnType("numeric(18,2)");

        builder.HasOne(t => t.Product)
            .WithMany(p => p.PriceTiers)
            .HasForeignKey(t => t.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.ProductId, t.Quantity }).IsUnique();
    }
}
