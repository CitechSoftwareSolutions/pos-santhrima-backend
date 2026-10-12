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

    public async Task<List<CashierSalesDto>> GetCashierSalesAsync(DateTime dateFrom, DateTime dateTo)
    {
        (dateFrom, dateTo) = (dateFrom.AsUtc(), dateTo.AsUtc());

        var sales = await _db.Sales
            .Include(s => s.Terminal)
            .Where(s => s.SaleDate >= dateFrom && s.SaleDate <= dateTo && s.Status != SaleStatus.Cancelled)
            .ToListAsync();

        var cashierIds = sales.Select(s => s.CashierId).Distinct().ToList();

        var users = await _db.Users
            .Where(u => cashierIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        return sales
            .GroupBy(s => s.CashierId)
            .Select(g =>
            {
                var user = users.GetValueOrDefault(g.Key);
                var counterNames = g.Where(s => s.Terminal != null).Select(s => s.Terminal!.Name).Distinct().ToList();
                var counterName = counterNames.Count > 0 ? string.Join(", ", counterNames) : null;
                var counterNumber = g.Where(s => s.Terminal != null).Select(s => (int?)s.Terminal!.CounterNumber).FirstOrDefault();
                var totalSales = g.Count();
                var totalRevenue = g.Sum(s => s.TotalAmount);
                var avg = totalSales == 0 ? 0 : totalRevenue / totalSales;
                var totalDiscount = g.Sum(s => s.DiscountAmount);
                var totalTax = g.Sum(s => s.TaxAmount);
                var firstSale = g.Min(s => (DateTime?)s.SaleDate);
                var lastSale = g.Max(s => (DateTime?)s.SaleDate);

                return new CashierSalesDto(
                    g.Key,
                    user?.FullName ?? "Unknown Cashier",
                    user?.Email,
                    counterName,
                    counterNumber,
                    totalSales,
                    totalRevenue,
                    avg,
                    totalDiscount,
                    totalTax,
                    firstSale,
                    lastSale
                );
            })
            .OrderByDescending(c => c.TotalRevenue)
            .ToList();
    }

    public async Task<List<CounterSalesDto>> GetCounterSalesAsync(DateTime dateFrom, DateTime dateTo)
    {
        (dateFrom, dateTo) = (dateFrom.AsUtc(), dateTo.AsUtc());

        var sales = await _db.Sales
            .Include(s => s.Terminal)
            .Where(s => s.SaleDate >= dateFrom && s.SaleDate <= dateTo && s.Status != SaleStatus.Cancelled)
            .ToListAsync();

        var cashierIds = sales.Select(s => s.CashierId).Distinct().ToList();

        var users = await _db.Users
            .Where(u => cashierIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        return sales
            .GroupBy(s => s.TerminalId)
            .Select(g =>
            {
                var firstSaleWithTerminal = g.FirstOrDefault(s => s.Terminal != null);
                var terminal = firstSaleWithTerminal?.Terminal;
                var counterNumber = terminal?.CounterNumber;
                var counterName = terminal?.Name ?? (counterNumber.HasValue ? $"Counter {counterNumber}" : "Main Register");
                var terminalCode = terminal?.TerminalCode;
                var totalSales = g.Count();
                var totalRevenue = g.Sum(s => s.TotalAmount);
                var avg = totalSales == 0 ? 0 : totalRevenue / totalSales;
                var activeCashiers = g.Select(s => s.CashierId)
                    .Distinct()
                    .Select(id => users.TryGetValue(id, out var u) ? u.FullName : "Unknown Cashier")
                    .OrderBy(name => name)
                    .ToList();

                return new CounterSalesDto(
                    g.Key,
                    counterNumber,
                    counterName,
                    terminalCode,
                    totalSales,
                    totalRevenue,
                    avg,
                    activeCashiers
                );
            })
            .OrderBy(c => c.CounterNumber ?? 999)
            .ThenBy(c => c.CounterName)
            .ToList();
    }
}
