using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

public enum LoyaltyTransactionType
{
    Earned = 0,
    Redeemed = 1,
    Expired = 2,
    MilestoneGiftClaimed = 3,
    Adjustment = 4
}

public class LoyaltyTransaction : BaseEntity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid? SaleId { get; set; }
    public Sale? Sale { get; set; }

    public LoyaltyTransactionType Type { get; set; }

    public decimal Points { get; set; }
    public decimal PointsRemaining { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? Notes { get; set; }
}
