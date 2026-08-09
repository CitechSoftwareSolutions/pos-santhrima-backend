using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

public class PurchaseItem : BaseEntity
{
    public Guid PurchaseId { get; set; }
    public Purchase Purchase { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal QuantityOrdered { get; set; }
    public decimal FreeQuantity { get; set; } = 0;
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}
