using POSSystem.Application.Common.Models;
using POSSystem.Domain.Enums;

namespace POSSystem.Application.Features.Purchases;

public record CreatePurchaseItemRequest(Guid ProductId, decimal QuantityOrdered, decimal FreeQuantity, decimal UnitCost);

public record CreatePurchaseRequest(
    Guid SupplierId,
    List<CreatePurchaseItemRequest> Items,
    DateTime? ExpectedDeliveryDate,
    string? Notes);

public record PurchaseItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal QuantityOrdered,
    decimal FreeQuantity,
    decimal UnitCost,
    decimal LineTotal);

public record PurchaseDto(
    Guid Id,
    string PurchaseNumber,
    Guid SupplierId,
    string SupplierName,
    DateTime PurchaseDate,
    DateTime? ExpectedDeliveryDate,
    decimal SubTotal,
    decimal TaxAmount,
    decimal TotalAmount,
    string? Notes,
    string CreatedByName,
    PurchaseApprovalStatus ApprovalStatus,
    string? ApprovedByName,
    DateTime? ApprovedAt,
    bool IsAddedToStock,
    string? StockAddedByName,
    DateTime? StockAddedAt,
    bool IsPaid,
    string? PaidByName,
    DateTime? PaidAt,
    bool IsCancelled,
    List<PurchaseItemDto> Items);

public record PurchaseQueryParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public Guid? SupplierId { get; set; }
    public PurchaseApprovalStatus? ApprovalStatus { get; set; }
    public bool? IsPaid { get; set; }
    public bool? IsAddedToStock { get; set; }
}

public interface IPurchaseService
{
    Task<PagedResult<PurchaseDto>> GetAllAsync(PurchaseQueryParams query);
    Task<PurchaseDto> GetByIdAsync(Guid id);
    Task<PurchaseDto> CreateAsync(CreatePurchaseRequest request, Guid userId);
    Task<PurchaseDto> ApproveAsync(Guid id, Guid userId);
    Task<PurchaseDto> AddToStockAsync(Guid id, Guid userId);
    Task<PurchaseDto> MarkAsPaidAsync(Guid id, Guid userId);
    Task<PurchaseDto> CancelAsync(Guid id);
}
