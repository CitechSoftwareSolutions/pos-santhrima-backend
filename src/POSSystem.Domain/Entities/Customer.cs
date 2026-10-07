using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

public class Customer : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    public decimal LoyaltyPoints { get; set; } = 0;
    public decimal TotalPurchases { get; set; } = 0;
    public decimal LoyaltyPointsRedeemed { get; set; } = 0;
    public int MilestoneGiftsClaimed { get; set; } = 0;
    public decimal CreditBalance { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<LoyaltyTransaction> LoyaltyTransactions { get; set; } = new List<LoyaltyTransaction>();
}
