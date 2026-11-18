using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using POSSystem.Domain.Entities;

namespace POSSystem.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Barcode).HasMaxLength(100);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.NameSi).HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.Brand).HasMaxLength(100);
        builder.Property(p => p.Unit).HasMaxLength(20);

        builder.Property(p => p.CostPrice).HasColumnType("numeric(18,2)");
        builder.Property(p => p.RetailPrice).HasColumnType("numeric(18,2)");
        builder.Property(p => p.Mrp).HasColumnType("numeric(18,2)");
        builder.Property(p => p.WholesalePrice).HasColumnType("numeric(18,2)");
        builder.Property(p => p.SpecialPrice).HasColumnType("numeric(18,2)");
        builder.Property(p => p.TaxRate).HasColumnType("numeric(5,2)");
        builder.Property(p => p.StockQuantity).HasColumnType("numeric(18,3)");
        builder.Property(p => p.ReorderLevel).HasColumnType("numeric(18,3)");

        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Barcode);
        builder.HasIndex(p => p.Name);
        builder.HasIndex(p => p.NameSi);
        builder.HasIndex(p => p.Brand);
        builder.HasIndex(p => p.SupplierId);

        builder.HasOne(p => p.Supplier)
            .WithMany()
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
