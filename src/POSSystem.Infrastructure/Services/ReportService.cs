using Microsoft.EntityFrameworkCore;
using POSSystem.Application.Features.Reports;
using POSSystem.Domain.Enums;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class ReportService : IReportService
{
    private readonly ApplicationDbContext _db;

    public ReportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<SalesSummaryDto> GetSalesSummaryAsync(DateTime dateFrom, DateTime dateTo)
    {
        (dateFrom, dateTo) = (dateFrom.AsUtc(), dateTo.AsUtc());

        var sales = await _db.Sales
            .Where(s => s.SaleDate >= dateFrom && s.SaleDate <= dateTo && s.Status != SaleStatus.Cancelled)
            .ToListAsync();

        var totalSales = sales.Count;
        var grossRevenue = sales.Sum(s => s.SubTotal);
        var totalDiscount = sales.Sum(s => s.DiscountAmount);
        var totalTax = sales.Sum(s => s.TaxAmount);
        var netRevenue = sales.Sum(s => s.TotalAmount);
        var averageSaleValue = totalSales == 0 ? 0 : netRevenue / totalSales;

        return new SalesSummaryDto(dateFrom, dateTo, totalSales, grossRevenue, totalDiscount, totalTax, netRevenue, averageSaleValue);
    }

    public async Task<List<DailySalesDto>> GetDailySalesAsync(DateTime dateFrom, DateTime dateTo)
    {
        (dateFrom, dateTo) = (dateFrom.AsUtc(), dateTo.AsUtc());

        var sales = await _db.Sales
            .Where(s => s.SaleDate >= dateFrom && s.SaleDate <= dateTo && s.Status != SaleStatus.Cancelled)
            .ToListAsync();

        return sales
            .GroupBy(s => DateOnly.FromDateTime(s.SaleDate))
            .Select(g => new DailySalesDto(g.Key, g.Count(), g.Sum(s => s.TotalAmount)))
            .OrderBy(d => d.Date)
            .ToList();
    }

    public async Task<List<TopProductDto>> GetTopProductsAsync(DateTime dateFrom, DateTime dateTo, int take = 10)
    {
        (dateFrom, dateTo) = (dateFrom.AsUtc(), dateTo.AsUtc());

        var items = await _db.SaleItems
            .Include(i => i.Sale)
            .Where(i => i.Sale.SaleDate >= dateFrom && i.Sale.SaleDate <= dateTo && i.Sale.Status != SaleStatus.Cancelled)
            .ToListAsync();

        return items
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g => new TopProductDto(g.Key.ProductId, g.Key.ProductName, g.Sum(i => i.Quantity), g.Sum(i => i.LineTotal)))
            .OrderByDescending(t => t.QuantitySold)
            .Take(take)
            .ToList();
    }

    public async Task<List<LowStockReportItemDto>> GetLowStockReportAsync()
    {
        return await _db.Products
            .Where(p => p.IsActive && p.StockQuantity <= p.ReorderLevel)
            .OrderBy(p => p.StockQuantity)
            .Select(p => new LowStockReportItemDto(p.Id, p.Sku, p.Name, p.StockQuantity, p.ReorderLevel))
            .ToListAsync();
    }

    public async Task<InventoryValuationDto> GetInventoryValuationAsync()
    {
        var products = await _db.Products.Where(p => p.IsActive).ToListAsync();

        var totalProducts = products.Count;
        var totalUnits = products.Sum(p => p.StockQuantity);
        var totalCostValue = products.Sum(p => p.CostPrice * p.StockQuantity);
        var totalRetailValue = products.Sum(p => p.RetailPrice * p.StockQuantity);

        return new InventoryValuationDto(totalProducts, totalUnits, totalCostValue, totalRetailValue);
    }

    public async Task<List<PaymentMethodBreakdownDto>> GetPaymentMethodBreakdownAsync(DateTime dateFrom, DateTime dateTo)
    {
        (dateFrom, dateTo) = (dateFrom.AsUtc(), dateTo.AsUtc());

        var payments = await _db.Payments
            .Include(p => p.Sale)
            .Where(p => p.Sale.SaleDate >= dateFrom && p.Sale.SaleDate <= dateTo && p.Amount > 0)
            .ToListAsync();

        return payments
            .GroupBy(p => p.Method)
            .Select(g => new PaymentMethodBreakdownDto(g.Key.ToString(), g.Sum(p => p.Amount), g.Count()))
            .OrderByDescending(p => p.Amount)
            .ToList();
    }
}
