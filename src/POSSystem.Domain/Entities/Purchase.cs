using POSSystem.Domain.Common;
using POSSystem.Domain.Enums;

namespace POSSystem.Domain.Entities;

/// <summary>
/// A supplier invoice. Any of Admin/Manager/Cashier can create one, entering
/// line items one by one - it starts as PendingApproval and has no effect on
/// stock. Only Admin/Manager can Approve it. Once Approved, any of
/// Admin/Manager/Cashier can commit it to stock exactly once (IsAddedToStock
/// guards against double-adding). Payment is tracked independently and can
/// only be marked by Admin/Manager.
/// </summary>
public class Purchase : BaseEntity
{
    public string PurchaseNumber { get; set; } = string.Empty;

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public Guid CreatedByUserId { get; set; }

    public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedDeliveryDate { get; set; }

    public decimal SubTotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public PurchaseApprovalStatus ApprovalStatus { get; set; } = PurchaseApprovalStatus.PendingApproval;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public bool IsAddedToStock { get; set; } = false;
    public Guid? StockAddedByUserId { get; set; }
    public DateTime? StockAddedAt { get; set; }

    public bool IsPaid { get; set; } = false;
    public Guid? PaidByUserId { get; set; }
    public DateTime? PaidAt { get; set; }

    public bool IsCancelled { get; set; } = false;

    public string? Notes { get; set; }

    public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
}
