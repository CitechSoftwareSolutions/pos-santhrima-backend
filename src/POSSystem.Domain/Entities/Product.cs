using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

public class Product : BaseEntity
{
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public decimal CostPrice { get; set; }

    // Base prices used when no exact-quantity ProductPriceTier matches the sale
    // quantity. RetailPrice is required; Wholesale/Special are optional overrides
    // that fall back to RetailPrice when not set.
    public decimal RetailPrice { get; set; }
    public decimal? WholesalePrice { get; set; }
    public decimal? SpecialPrice { get; set; }

    public decimal TaxRate { get; set; } = 0;

    public string Unit { get; set; } = "pcs";
    public bool AllowDecimalQuantity { get; set; } = false;
    public decimal StockQuantity { get; set; } = 0;
    public decimal ReorderLevel { get; set; } = 5;

    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
    public ICollection<PurchaseItem> PurchaseItems { get; set; } = new List<PurchaseItem>();
    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
    public ICollection<Promotion> Promotions { get; set; } = new List<Promotion>();
    public ICollection<ProductPriceTier> PriceTiers { get; set; } = new List<ProductPriceTier>();
}
