using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POSSystem.Application.Common.Interfaces;
using POSSystem.Application.Features.Sales;
using POSSystem.Domain.Entities;
using POSSystem.Domain.Enums;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class SalesShiftService : ISalesShiftService
{
    private readonly ApplicationDbContext _normalDb;
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly IDatabaseContextResolver _dbResolver;
    private readonly ILogger<SalesShiftService> _logger;

    public SalesShiftService(
        ApplicationDbContext normalDb,
        IApplicationDbContextFactory dbFactory,
        IDatabaseContextResolver dbResolver,
        ILogger<SalesShiftService> logger)
    {
        _normalDb = normalDb;
        _dbFactory = dbFactory;
        _dbResolver = dbResolver;
        _logger = logger;
    }

    public async Task<List<SalesShiftResult>> CheckAndShiftAllEligibleCashiersAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<SalesShiftResult>();

        if (_dbResolver.IsAdminMode())
        {
            return results;
        }

        var setting = await _normalDb.StoreSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (setting != null && !setting.CashierSalesShiftEnabled)
        {
            return results;
        }

        var threshold = setting?.CashierSalesShiftThreshold > 0 ? setting.CashierSalesShiftThreshold : 60000m;

        var eligibleCashiers = await _normalDb.Sales
            .Where(s => s.Status == SaleStatus.Completed && !s.IsDeleted)
            .GroupBy(s => s.CashierId)
            .Select(g => new { CashierId = g.Key, Total = g.Sum(s => s.TotalAmount) })
            .Where(x => x.Total >= threshold)
            .ToListAsync(cancellationToken);

        foreach (var c in eligibleCashiers)
        {
            _logger.LogInformation("Cashier {CashierId} qualifies for shift with unshifted total Rs. {Total:N2} (threshold Rs. {Threshold:N2}). Shifting...",
                c.CashierId, c.Total, threshold);
            var res = await ExecuteShiftAsync(c.CashierId, cancellationToken);
            results.Add(res);
        }

        return results;
    }

    public async Task<SalesShiftResult> CheckAndShiftCashierSalesAsync(Guid cashierId, CancellationToken cancellationToken = default)
    {
        if (_dbResolver.IsAdminMode())
        {
            return new SalesShiftResult(false, 0, 0, "Current context is Admin database; shifting skipped.");
        }

        var setting = await _normalDb.StoreSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (setting != null && !setting.CashierSalesShiftEnabled)
        {
            return new SalesShiftResult(false, 0, 0, "Cashier sales shifting is disabled in store settings.");
        }

        var threshold = setting?.CashierSalesShiftThreshold > 0 ? setting.CashierSalesShiftThreshold : 60000m;

        var cashierUnshiftedTotal = await _normalDb.Sales
            .Where(s => s.CashierId == cashierId &&
                        s.Status == SaleStatus.Completed &&
                        !s.IsDeleted)
            .SumAsync(s => s.TotalAmount, cancellationToken);

        if (cashierUnshiftedTotal < threshold)
        {
            return new SalesShiftResult(false, 0, 0,
                $"Cashier unshifted sales total Rs. {cashierUnshiftedTotal:N2} is below the threshold of Rs. {threshold:N2}.");
        }

        _logger.LogInformation(
            "Cashier {CashierId} reached threshold Rs. {Threshold:N2} with unshifted total Rs. {Total:N2}. Shifting to Admin database...",
            cashierId, threshold, cashierUnshiftedTotal);

        return await ExecuteShiftAsync(cashierId, cancellationToken);
    }

    private async Task<SalesShiftResult> ExecuteShiftAsync(Guid cashierId, CancellationToken cancellationToken)
    {
        var salesToShift = await _normalDb.Sales
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .Where(s => s.CashierId == cashierId &&
                        s.Status == SaleStatus.Completed &&
                        !s.IsDeleted)
            .OrderBy(s => s.SaleDate)
            .ToListAsync(cancellationToken);

        if (salesToShift.Count == 0)
        {
            return new SalesShiftResult(false, 0, 0, "No completed sales found to shift.");
        }

        var totalAmount = salesToShift.Sum(s => s.TotalAmount);
        var saleNumbers = salesToShift.Select(s => s.SaleNumber).ToList();
        var saleIds = salesToShift.Select(s => s.Id).ToList();

        var stockMovements = await _normalDb.StockMovements
            .Where(sm => sm.Reference != null && saleNumbers.Contains(sm.Reference))
            .ToListAsync(cancellationToken);

        var loyaltyTransactions = await _normalDb.LoyaltyTransactions
            .Where(lt => lt.SaleId.HasValue && saleIds.Contains(lt.SaleId.Value))
            .ToListAsync(cancellationToken);

        using var adminDb = _dbFactory.CreateAdminDbContext();

        await SyncParentEntitiesAsync(_normalDb, adminDb, salesToShift, cashierId, cancellationToken);

        foreach (var sale in salesToShift)
        {
            var exists = await adminDb.Sales.AnyAsync(s => s.Id == sale.Id, cancellationToken);
            if (!exists)
            {
                var adminSale = new Sale
                {
                    Id = sale.Id,
                    SaleNumber = sale.SaleNumber,
                    CustomerId = sale.CustomerId,
                    CashierId = sale.CashierId,
                    TerminalId = sale.TerminalId,
                    SaleDate = sale.SaleDate,
                    SubTotal = sale.SubTotal,
                    TaxAmount = sale.TaxAmount,
                    DiscountAmount = sale.DiscountAmount,
                    TotalAmount = sale.TotalAmount,
                    AmountPaid = sale.AmountPaid,
                    ChangeDue = sale.ChangeDue,
                    LoyaltyPointsRedeemed = sale.LoyaltyPointsRedeemed,
                    LoyaltyPointsEarned = sale.LoyaltyPointsEarned,
                    Status = sale.Status,
                    PaymentStatus = sale.PaymentStatus,
                    Notes = sale.Notes,
                    CreatedAt = sale.CreatedAt,
                    UpdatedAt = DateTime.UtcNow,
                    IsDeleted = false
                };

                foreach (var item in sale.Items)
                {
                    adminSale.Items.Add(new SaleItem
                    {
                        Id = item.Id,
                        SaleId = sale.Id,
                        ProductId = item.ProductId,
                        ProductName = item.ProductName,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        DiscountAmount = item.DiscountAmount,
                        TaxAmount = item.TaxAmount,
                        LineTotal = item.LineTotal,
                        PromotionId = item.PromotionId,
                        PromotionName = item.PromotionName,
                        PromotionDiscountAmount = item.PromotionDiscountAmount
                    });
                }

                foreach (var payment in sale.Payments)
                {
                    adminSale.Payments.Add(new Payment
                    {
                        Id = payment.Id,
                        SaleId = sale.Id,
                        Method = payment.Method,
                        Amount = payment.Amount,
                        PaymentDate = payment.PaymentDate,
                        ReferenceNumber = payment.ReferenceNumber
                    });
                }

                adminDb.Sales.Add(adminSale);
            }
        }

        foreach (var sm in stockMovements)
        {
            var exists = await adminDb.StockMovements.AnyAsync(s => s.Id == sm.Id, cancellationToken);
            if (!exists)
            {
                adminDb.StockMovements.Add(new StockMovement
                {
                    Id = sm.Id,
                    ProductId = sm.ProductId,
                    Type = sm.Type,
                    QuantityChange = sm.QuantityChange,
                    StockAfter = sm.StockAfter,
                    Reference = sm.Reference,
                    Notes = sm.Notes,
                    PerformedByUserId = sm.PerformedByUserId,
                    CreatedAt = sm.CreatedAt
                });
            }
        }

        foreach (var lt in loyaltyTransactions)
        {
            var exists = await adminDb.LoyaltyTransactions.AnyAsync(a => a.Id == lt.Id, cancellationToken);
            if (!exists)
            {
                adminDb.LoyaltyTransactions.Add(new LoyaltyTransaction
                {
                    Id = lt.Id,
                    CustomerId = lt.CustomerId,
                    SaleId = lt.SaleId,
                    Type = lt.Type,
                    Points = lt.Points,
                    PointsRemaining = lt.PointsRemaining,
                    ExpiresAt = lt.ExpiresAt,
                    Notes = lt.Notes,
                    CreatedAt = lt.CreatedAt
                });
            }
        }

        await adminDb.SaveChangesAsync(cancellationToken);

        // In normal DB, decouple loyalty transactions before removing sales to satisfy FK constraints
        foreach (var lt in loyaltyTransactions)
        {
            lt.SaleId = null;
        }
        await _normalDb.SaveChangesAsync(cancellationToken);

        // Now delete sales and stock movements from normal DB
        _normalDb.Sales.RemoveRange(salesToShift);
        if (stockMovements.Count > 0)
        {
            _normalDb.StockMovements.RemoveRange(stockMovements);
        }

        await _normalDb.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Successfully shifted {Count} sales (totaling Rs. {Amount:N2}) from Normal DB to Admin DB for Cashier {CashierId}.",
            salesToShift.Count, totalAmount, cashierId);

        return new SalesShiftResult(
            true,
            salesToShift.Count,
            totalAmount,
            $"Successfully transferred {salesToShift.Count} sales (Rs. {totalAmount:N2}) to Admin database and reset cashier active tally.");
    }

    private static async Task SyncParentEntitiesAsync(
        ApplicationDbContext normalDb,
        ApplicationDbContext adminDb,
        List<Sale> sales,
        Guid cashierId,
        CancellationToken cancellationToken)
    {
        // Sync Cashier User
        var userInAdmin = await adminDb.Users.AnyAsync(u => u.Id == cashierId, cancellationToken);
        if (!userInAdmin)
        {
            var normalUser = await normalDb.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == cashierId, cancellationToken);
            if (normalUser != null)
            {
                adminDb.Users.Add(new Identity.ApplicationUser
                {
                    Id = normalUser.Id,
                    UserName = normalUser.UserName,
                    NormalizedUserName = normalUser.NormalizedUserName,
                    Email = normalUser.Email,
                    NormalizedEmail = normalUser.NormalizedEmail,
                    EmailConfirmed = normalUser.EmailConfirmed,
                    PasswordHash = normalUser.PasswordHash,
                    SecurityStamp = normalUser.SecurityStamp,
                    ConcurrencyStamp = normalUser.ConcurrencyStamp,
                    FullName = normalUser.FullName,
                    IsActive = normalUser.IsActive,
                    CreatedAt = normalUser.CreatedAt
                });
                await adminDb.SaveChangesAsync(cancellationToken);
            }
        }

        // Sync Terminals
        var terminalIds = sales.Where(s => s.TerminalId.HasValue).Select(s => s.TerminalId!.Value).Distinct().ToList();
        foreach (var tid in terminalIds)
        {
            var exists = await adminDb.TerminalSettings.AnyAsync(t => t.Id == tid, cancellationToken);
            if (!exists)
            {
                var normalTerm = await normalDb.TerminalSettings.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, cancellationToken);
                if (normalTerm != null)
                {
                    adminDb.TerminalSettings.Add(new TerminalSetting
                    {
                        Id = normalTerm.Id,
                        CounterNumber = normalTerm.CounterNumber,
                        Name = normalTerm.Name,
                        TerminalCode = normalTerm.TerminalCode,
                        Description = normalTerm.Description,
                        IsActive = normalTerm.IsActive,
                        PrinterType = normalTerm.PrinterType,
                        PrinterAddress = normalTerm.PrinterAddress,
                        PaperWidth = normalTerm.PaperWidth,
                        AutoPrintReceipt = normalTerm.AutoPrintReceipt,
                        AutoCutPaper = normalTerm.AutoCutPaper,
                        CashDrawerKick = normalTerm.CashDrawerKick,
                        BarcodeScannerType = normalTerm.BarcodeScannerType,
                        BarcodeDelimiter = normalTerm.BarcodeDelimiter,
                        BarcodeMinLength = normalTerm.BarcodeMinLength,
                        BarcodeBeepOnScan = normalTerm.BarcodeBeepOnScan,
                        CreatedAt = normalTerm.CreatedAt
                    });
                    await adminDb.SaveChangesAsync(cancellationToken);
                }
            }
        }

        // Sync Customers
        var customerIds = sales.Where(s => s.CustomerId.HasValue).Select(s => s.CustomerId!.Value).Distinct().ToList();
        foreach (var cid in customerIds)
        {
            var exists = await adminDb.Customers.AnyAsync(c => c.Id == cid, cancellationToken);
            if (!exists)
            {
                var normalCust = await normalDb.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, cancellationToken);
                if (normalCust != null)
                {
                    adminDb.Customers.Add(new Customer
                    {
                        Id = normalCust.Id,
                        Name = normalCust.Name,
                        Email = normalCust.Email,
                        Phone = normalCust.Phone,
                        Address = normalCust.Address,
                        LoyaltyPoints = normalCust.LoyaltyPoints,
                        TotalPurchases = normalCust.TotalPurchases,
                        LoyaltyPointsRedeemed = normalCust.LoyaltyPointsRedeemed,
                        MilestoneGiftsClaimed = normalCust.MilestoneGiftsClaimed,
                        CreditBalance = normalCust.CreditBalance,
                        CreatedAt = normalCust.CreatedAt
                    });
                    await adminDb.SaveChangesAsync(cancellationToken);
                }
            }
        }

        // Sync Products and Categories
        var productIds = sales.SelectMany(s => s.Items).Select(i => i.ProductId).Distinct().ToList();
        foreach (var pid in productIds)
        {
            var exists = await adminDb.Products.AnyAsync(p => p.Id == pid, cancellationToken);
            if (!exists)
            {
                var normalProd = await normalDb.Products
                    .Include(p => p.Category)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == pid, cancellationToken);

                if (normalProd != null)
                {
                    var catExists = await adminDb.Categories.AnyAsync(c => c.Id == normalProd.CategoryId, cancellationToken);
                    if (!catExists && normalProd.Category != null)
                    {
                        adminDb.Categories.Add(new Category
                        {
                            Id = normalProd.Category.Id,
                            Name = normalProd.Category.Name,
                            Description = normalProd.Category.Description,
                            IsActive = normalProd.Category.IsActive,
                            CreatedAt = normalProd.Category.CreatedAt
                        });
                        await adminDb.SaveChangesAsync(cancellationToken);
                    }

                    adminDb.Products.Add(new Product
                    {
                        Id = normalProd.Id,
                        Name = normalProd.Name,
                        Sku = normalProd.Sku,
                        Barcode = normalProd.Barcode,
                        Description = normalProd.Description,
                        CategoryId = normalProd.CategoryId,
                        Brand = normalProd.Brand,
                        CostPrice = normalProd.CostPrice,
                        RetailPrice = normalProd.RetailPrice,
                        WholesalePrice = normalProd.WholesalePrice,
                        SpecialPrice = normalProd.SpecialPrice,
                        StockQuantity = normalProd.StockQuantity,
                        ReorderLevel = normalProd.ReorderLevel,
                        IsActive = normalProd.IsActive,
                        TaxRate = normalProd.TaxRate,
                        Unit = normalProd.Unit,
                        AllowDecimalQuantity = normalProd.AllowDecimalQuantity,
                        ImageUrl = normalProd.ImageUrl,
                        CreatedAt = normalProd.CreatedAt
                    });
                    await adminDb.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}
