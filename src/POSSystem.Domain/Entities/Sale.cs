using POSSystem.Domain.Common;
using POSSystem.Domain.Enums;

namespace POSSystem.Domain.Entities;

public class Sale : BaseEntity
{
    public string SaleNumber { get; set; } = string.Empty;

    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid CashierId { get; set; }

    public DateTime SaleDate { get; set; } = DateTime.UtcNow;

    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal ChangeDue { get; set; }
    public decimal LoyaltyPointsRedeemed { get; set; } = 0;
    public decimal LoyaltyPointsEarned { get; set; } = 0;

    public SaleStatus Status { get; set; } = SaleStatus.Completed;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;

    public string? Notes { get; set; }

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
