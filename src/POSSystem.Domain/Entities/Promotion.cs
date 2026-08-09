using POSSystem.Domain.Common;
using POSSystem.Domain.Enums;

namespace POSSystem.Domain.Entities;

public class Promotion : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public PromotionType Type { get; set; }

    // BuyXGetYFree: for every (BuyQuantity + FreeQuantity) units, FreeQuantity are free.
    public decimal? BuyQuantity { get; set; }
    public decimal? FreeQuantity { get; set; }

    // BulkPercentOff: when quantity >= MinQuantity, DiscountPercent applies to the whole line.
    public decimal? MinQuantity { get; set; }
    public decimal? DiscountPercent { get; set; }

    // FixedPriceBundle: every BundleQuantity units cost a flat BundlePrice; remainder at normal price.
    public decimal? BundleQuantity { get; set; }
    public decimal? BundlePrice { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
