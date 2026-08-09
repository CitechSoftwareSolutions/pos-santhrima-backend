using POSSystem.Domain.Entities;

namespace POSSystem.Application.Features.Products;

public static class PriceTierResolver
{
    /// <summary>
    /// Resolves the unit price for a given quantity: a tier only applies when the
    /// quantity exactly equals its Quantity (not a threshold); if none match, the
    /// product's base RetailPrice is used. Defaults to each tier's Retail price -
    /// callers that need a Wholesale/Special price send it explicitly via the sale
    /// item's UnitPrice override instead of relying on this fallback.
    /// </summary>
    public static decimal ResolveUnitPrice(decimal basePrice, IEnumerable<ProductPriceTier> tiers, decimal quantity)
    {
        var matchingTier = tiers.FirstOrDefault(t => t.Quantity == quantity);
        return matchingTier?.RetailPrice ?? basePrice;
    }
}
