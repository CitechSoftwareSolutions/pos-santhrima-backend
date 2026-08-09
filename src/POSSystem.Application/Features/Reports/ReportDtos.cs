namespace POSSystem.Application.Features.Reports;

public record SalesSummaryDto(
    DateTime DateFrom,
    DateTime DateTo,
    int TotalSales,
    decimal GrossRevenue,
    decimal TotalDiscount,
    decimal TotalTax,
    decimal NetRevenue,
    decimal AverageSaleValue);

public record DailySalesDto(DateOnly Date, int SalesCount, decimal TotalAmount);

public record TopProductDto(Guid ProductId, string ProductName, decimal QuantitySold, decimal Revenue);

public record LowStockReportItemDto(Guid ProductId, string Sku, string ProductName, decimal StockQuantity, decimal ReorderLevel);

public record InventoryValuationDto(int TotalProducts, decimal TotalUnits, decimal TotalCostValue, decimal TotalRetailValue);

public record PaymentMethodBreakdownDto(string Method, decimal Amount, int Count);

public interface IReportService
{
    Task<SalesSummaryDto> GetSalesSummaryAsync(DateTime dateFrom, DateTime dateTo);
    Task<List<DailySalesDto>> GetDailySalesAsync(DateTime dateFrom, DateTime dateTo);
    Task<List<TopProductDto>> GetTopProductsAsync(DateTime dateFrom, DateTime dateTo, int take = 10);
    Task<List<LowStockReportItemDto>> GetLowStockReportAsync();
    Task<InventoryValuationDto> GetInventoryValuationAsync();
    Task<List<PaymentMethodBreakdownDto>> GetPaymentMethodBreakdownAsync(DateTime dateFrom, DateTime dateTo);
}
