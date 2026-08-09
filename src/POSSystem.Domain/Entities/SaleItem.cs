using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

public class SaleItem : BaseEntity
{
    public Guid SaleId { get; set; }
    public Sale Sale { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }

    public Guid? PromotionId { get; set; }
    public Promotion? Promotion { get; set; }
    public string? PromotionName { get; set; }
    public decimal PromotionDiscountAmount { get; set; } = 0;
}
