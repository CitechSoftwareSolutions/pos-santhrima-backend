using POSSystem.Application.Common.Models;
using POSSystem.Domain.Enums;

namespace POSSystem.Application.Features.Sales;

/// <summary>
/// UnitPrice: null = auto-resolve from the product's price tiers (falling back to
/// RetailPrice); a value = explicit override chosen by the cashier at checkout,
/// e.g. a one-off price break for a particular customer.
/// </summary>
public record CreateSaleItemRequest(Guid ProductId, decimal Quantity, decimal DiscountAmount = 0, decimal? UnitPrice = null);

public record CreateSalePaymentRequest(PaymentMethod Method, decimal Amount, string? ReferenceNumber);

public record CreateSaleRequest(
    Guid? CustomerId,
    List<CreateSaleItemRequest> Items,
    List<CreateSalePaymentRequest> Payments,
    decimal DiscountAmount = 0,
    string? Notes = null);

public record SaleItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal LineTotal,
    Guid? PromotionId,
    string? PromotionName,
    decimal PromotionDiscountAmount);

public record SalePaymentDto(Guid Id, PaymentMethod Method, decimal Amount, string? ReferenceNumber, DateTime PaymentDate);

public record SaleDto(
    Guid Id,
    string SaleNumber,
    Guid? CustomerId,
    string? CustomerName,
    Guid CashierId,
    string CashierName,
    DateTime SaleDate,
    decimal SubTotal,
    decimal TaxAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal AmountPaid,
    decimal ChangeDue,
    SaleStatus Status,
    PaymentStatus PaymentStatus,
    string? Notes,
    List<SaleItemDto> Items,
    List<SalePaymentDto> Payments);

public record SaleListItemDto(
    Guid Id,
    string SaleNumber,
    string? CustomerName,
    string CashierName,
    DateTime SaleDate,
    decimal TotalAmount,
    SaleStatus Status,
    PaymentStatus PaymentStatus);

public record SaleQueryParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? CashierId { get; set; }
    public SaleStatus? Status { get; set; }
    public string? Search { get; set; }
}

public interface ISaleService
{
    Task<PagedResult<SaleListItemDto>> GetAllAsync(SaleQueryParams query);
    Task<SaleDto> GetByIdAsync(Guid id);
    Task<SaleDto> CreateAsync(CreateSaleRequest request, Guid cashierId);
    Task<SaleDto> VoidAsync(Guid id, string reason);
    Task<SaleDto> RefundAsync(Guid id, List<Guid>? saleItemIds, string reason);
}
