using POSSystem.Domain.Entities;
using POSSystem.Domain.Enums;

namespace POSSystem.Application.Features.Promotions;

public static class PromotionCalculator
{
    /// <summary>
    /// Picks the currently-active promotion (if any) from a product's promotion
    /// collection, honoring IsActive and the optional Start/End date window.
    /// </summary>
    public static PromotionSummaryDto? GetActivePromotion(IEnumerable<Promotion> promotions)
    {
        var now = DateTime.UtcNow;

        var promotion = promotions
            .Where(p => p.IsActive && (p.StartDate == null || p.StartDate <= now) && (p.EndDate == null || p.EndDate >= now))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();

        return promotion is null
            ? null
            : new PromotionSummaryDto(
                promotion.Id,
                promotion.Name,
                promotion.Type,
                promotion.BuyQuantity,
                promotion.FreeQuantity,
                promotion.MinQuantity,
                promotion.DiscountPercent,
                promotion.BundleQuantity,
                promotion.BundlePrice);
    }

    /// <summary>
    /// Computes the automatic discount amount a promotion contributes for a given
    /// line quantity and unit price. Returns 0 if the promotion doesn't apply yet
    /// (e.g. quantity below its threshold).
    /// </summary>
    public static decimal CalculateDiscount(PromotionSummaryDto promotion, decimal quantity, decimal unitPrice)
    {
        return promotion.Type switch
        {
            PromotionType.BuyXGetYFree => CalculateBuyXGetYFree(promotion, quantity, unitPrice),
            PromotionType.BulkPercentOff => CalculateBulkPercentOff(promotion, quantity, unitPrice),
            PromotionType.FixedPriceBundle => CalculateFixedPriceBundle(promotion, quantity, unitPrice),
            _ => 0,
        };
    }

    private static decimal CalculateBuyXGetYFree(PromotionSummaryDto promotion, decimal quantity, decimal unitPrice)
    {
        var buyQuantity = promotion.BuyQuantity ?? 0;
        var freeQuantity = promotion.FreeQuantity ?? 0;
        if (buyQuantity <= 0 || freeQuantity <= 0) return 0;

        var groupSize = buyQuantity + freeQuantity;
        var numGroups = Math.Floor(quantity / groupSize);
        var freeUnits = numGroups * freeQuantity;

        return freeUnits * unitPrice;
    }

    private static decimal CalculateBulkPercentOff(PromotionSummaryDto promotion, decimal quantity, decimal unitPrice)
    {
        var minQuantity = promotion.MinQuantity ?? 0;
        var discountPercent = promotion.DiscountPercent ?? 0;
        if (minQuantity <= 0 || discountPercent <= 0 || quantity < minQuantity) return 0;

        return quantity * unitPrice * (discountPercent / 100m);
    }

    private static decimal CalculateFixedPriceBundle(PromotionSummaryDto promotion, decimal quantity, decimal unitPrice)
    {
        var bundleQuantity = promotion.BundleQuantity ?? 0;
        var bundlePrice = promotion.BundlePrice ?? 0;
        if (bundleQuantity <= 0) return 0;

        var numBundles = Math.Floor(quantity / bundleQuantity);
        if (numBundles <= 0) return 0;

        var normalPrice = numBundles * bundleQuantity * unitPrice;
        var discount = normalPrice - (numBundles * bundlePrice);

        return Math.Max(0, discount);
    }
}
