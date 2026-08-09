using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POSSystem.Domain.Entities;

namespace POSSystem.Infrastructure.Persistence.Configurations;

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.ToTable("Purchases");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PurchaseNumber).IsRequired().HasMaxLength(50);
        builder.HasIndex(p => p.PurchaseNumber).IsUnique();

        builder.Property(p => p.SubTotal).HasColumnType("numeric(18,2)");
        builder.Property(p => p.TaxAmount).HasColumnType("numeric(18,2)");
        builder.Property(p => p.TotalAmount).HasColumnType("numeric(18,2)");

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.Purchases)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Items)
            .WithOne(i => i.Purchase)
            .HasForeignKey(i => i.PurchaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PurchaseItemConfiguration : IEntityTypeConfiguration<PurchaseItem>
{
    public void Configure(EntityTypeBuilder<PurchaseItem> builder)
    {
        builder.ToTable("PurchaseItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.QuantityOrdered).HasColumnType("numeric(18,3)");
        builder.Property(i => i.FreeQuantity).HasColumnType("numeric(18,3)");
        builder.Property(i => i.UnitCost).HasColumnType("numeric(18,2)");
        builder.Property(i => i.LineTotal).HasColumnType("numeric(18,2)");

        builder.HasOne(i => i.Product)
            .WithMany(p => p.PurchaseItems)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
