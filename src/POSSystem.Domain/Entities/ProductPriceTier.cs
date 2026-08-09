using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

/// <summary>
/// An exact-quantity price rule for a product, e.g. "exactly 6 units cost 80 each".
/// A product can have any number of tiers set by whoever manages pricing. A tier
/// only applies when the sale quantity exactly equals its Quantity - it is not a
/// threshold, so a tier for 6 does not apply to a sale of 5 or 7. When no tier
/// matches exactly, Product.RetailPrice is used as the base price.
/// Each tier carries up to three price variants for the same exact quantity -
/// Retail is required, Wholesale/Special are optional overrides a cashier can
/// pick at checkout (falling back to Retail when not set).
/// </summary>
public class ProductPriceTier : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal Quantity { get; set; }
    public decimal RetailPrice { get; set; }
    public decimal? WholesalePrice { get; set; }
    public decimal? SpecialPrice { get; set; }
}
