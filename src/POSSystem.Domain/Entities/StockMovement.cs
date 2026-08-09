using POSSystem.Domain.Common;
using POSSystem.Domain.Enums;

namespace POSSystem.Domain.Entities;

public class StockMovement : BaseEntity
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public StockMovementType Type { get; set; }
    public decimal QuantityChange { get; set; }
    public decimal StockAfter { get; set; }

    public string? Reference { get; set; }
    public string? Notes { get; set; }

    public Guid? PerformedByUserId { get; set; }
}
