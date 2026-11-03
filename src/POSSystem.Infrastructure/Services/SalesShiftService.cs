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
            .Where(s => (s.Status == SaleStatus.Completed || s.Status == SaleStatus.Refunded || s.Status == SaleStatus.PartiallyRefunded) && !s.IsDeleted)
            .GroupBy(s => s.CashierId)
            .Select(g => new { CashierId = g.Key, Total = g.Sum(s => s.TotalAmount - s.RefundedAmount), Count = g.Count() })
            .Where(x => x.Total >= threshold && x.Count > 1)
            .ToListAsync(cancellationToken);

        foreach (var c in eligibleCashiers)
        {
            _logger.LogInformation("Cashier {CashierId} qualifies for shift with unshifted total Rs. {Total:N2} across {Count} sales (threshold Rs. {Threshold:N2}). Shifting previous sales and keeping latest sale...",
                c.CashierId, c.Total, c.Count, threshold);
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

        var cashierSalesQuery = _normalDb.Sales
            .Where(s => s.CashierId == cashierId &&
                        (s.Status == SaleStatus.Completed || s.Status == SaleStatus.Refunded || s.Status == SaleStatus.PartiallyRefunded) &&
                        !s.IsDeleted);

        var cashierUnshiftedTotal = await cashierSalesQuery.SumAsync(s => s.TotalAmount - s.RefundedAmount, cancellationToken);
        var cashierSalesCount = await cashierSalesQuery.CountAsync(cancellationToken);

        if (cashierUnshiftedTotal < threshold)
        {
            return new SalesShiftResult(false, 0, 0,
                $"Cashier unshifted sales total Rs. {cashierUnshiftedTotal:N2} is below the threshold of Rs. {threshold:N2}.");
        }

        if (cashierSalesCount <= 1)
        {
            return new SalesShiftResult(false, 0, 0,
                $"Cashier reached threshold Rs. {threshold:N2} with Rs. {cashierUnshiftedTotal:N2}, but only 1 sale exists. Keeping the current transaction in normal database.");
        }

        _logger.LogInformation(
            "Cashier {CashierId} reached threshold Rs. {Threshold:N2} with unshifted total Rs. {Total:N2} across {Count} sales. Shifting previous sales to Admin database while retaining the latest sale...",
            cashierId, threshold, cashierUnshiftedTotal, cashierSalesCount);

        return await ExecuteShiftAsync(cashierId, cancellationToken);
    }

    public async Task<SalesShiftResult> ShiftAllRemainingSalesAsync(
        Guid? terminalId = null,
        Guid? cashierId = null,
        CancellationToken cancellationToken = default)
    {
        if (_dbResolver.IsAdminMode())
        {
            return new SalesShiftResult(false, 0, 0, "Current context is Admin database; shifting skipped.");
        }

        using var _ = DualDatabaseSyncInterceptor.BeginSuppression();

        var query = _normalDb.Sales
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .Where(s => (s.Status == SaleStatus.Completed || s.Status == SaleStatus.Refunded || s.Status == SaleStatus.PartiallyRefunded) && !s.IsDeleted);

        if (terminalId.HasValue && terminalId.Value != Guid.Empty)
        {
            query = query.Where(s => s.TerminalId == terminalId.Value);
        }
        else if (cashierId.HasValue && cashierId.Value != Guid.Empty)
        {
            query = query.Where(s => s.CashierId == cashierId.Value);
        }

        var allSales = await query
            .OrderBy(s => s.SaleDate)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        if (allSales.Count == 0)
        {
            return new SalesShiftResult(false, 0, 0, "No remaining sales to shift.");
        }

        var targetCashierId = cashierId ?? allSales.First().CashierId;
        _logger.LogInformation(
            "Register close: shifting all {Count} remaining transactions (Rs. {Total:N2}) to Admin DB (terminal {TerminalId}, cashier {CashierId}).",
            allSales.Count, allSales.Sum(s => s.TotalAmount), terminalId, targetCashierId);

        return await MigrateSalesToAdminAsync(allSales, targetCashierId, cancellationToken);
    }

    private async Task<SalesShiftResult> ExecuteShiftAsync(Guid cashierId, CancellationToken cancellationToken)
    {
        using var _ = DualDatabaseSyncInterceptor.BeginSuppression();

        var allSales = await _normalDb.Sales
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .Where(s => s.CashierId == cashierId &&
                        (s.Status == SaleStatus.Completed || s.Status == SaleStatus.Refunded || s.Status == SaleStatus.PartiallyRefunded) &&
                        !s.IsDeleted)
            .OrderBy(s => s.SaleDate)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        if (allSales.Count <= 1)
        {
            return new SalesShiftResult(false, 0, 0, "No previous completed sales to shift (preserving latest transaction).");
        }

        // Leave the very last transaction in the Normal database as active/visible
        var salesToShift = allSales.Take(allSales.Count - 1).ToList();
        var lastSaleKept = allSales.Last();

        return await MigrateSalesToAdminAsync(salesToShift, cashierId, cancellationToken, lastSaleKept);
    }

    private async Task<SalesShiftResult> MigrateSalesToAdminAsync(
        List<Sale> salesToShift,
        Guid cashierId,
        CancellationToken cancellationToken,
        Sale? lastSaleKept = null)
    {
        var totalAmount = salesToShift.Sum(s => s.TotalAmount);
        var saleNumbers = salesToShift.Select(s => s.SaleNumber).ToList();
        var saleIds = salesToShift.Select(s => s.Id).ToList();

        if (lastSaleKept != null)
        {
            _logger.LogInformation(
                "Shifting {Count} previous sales (totaling Rs. {Amount:N2}) to Admin DB, keeping latest sale {LastSaleNumber} (Rs. {LastAmount:N2}) in Normal DB.",
                salesToShift.Count, totalAmount, lastSaleKept.SaleNumber, lastSaleKept.TotalAmount);
        }
        else
        {
            _logger.LogInformation(
                "Shifting all {Count} remaining sales (totaling Rs. {Amount:N2}) to Admin DB for Cashier {CashierId}.",
                salesToShift.Count, totalAmount, cashierId);
        }

        var stockMovements = await _normalDb.StockMovements
            .Where(sm => sm.Reference != null && saleNumbers.Contains(sm.Reference))
            .ToListAsync(cancellationToken);

        var loyaltyTransactions = await _normalDb.LoyaltyTransactions
            .Where(lt => lt.SaleId.HasValue && saleIds.Contains(lt.SaleId.Value))
            .ToListAsync(cancellationToken);

        using var adminDb = _dbFactory.CreateAdminDbContext();

        await SyncParentEntitiesAsync(_normalDb, adminDb, salesToShift, cashierId, cancellationToken);

        // Map to keep track of any re-numbered sales for stock movement references
        var saleNumberMap = new Dictionary<string, string>();

        foreach (var sale in salesToShift)
        {
            var existingSale = await adminDb.Sales.FirstOrDefaultAsync(
                s => s.Id == sale.Id || s.SaleNumber == sale.SaleNumber, cancellationToken);

            if (existingSale != null && existingSale.Id == sale.Id)
            {
                // Synchronize any return updates (refunded amount, status, items returned quantities, refund payments)
                existingSale.Status = sale.Status;
                existingSale.PaymentStatus = sale.PaymentStatus;
                existingSale.RefundedAmount = sale.RefundedAmount;
                existingSale.Notes = sale.Notes;
                existingSale.UpdatedAt = DateTime.UtcNow;

                foreach (var item in sale.Items)
                {
                    var existingItem = existingSale.Items.FirstOrDefault(i => i.Id == item.Id || (i.ProductId == item.ProductId && i.UnitPrice == item.UnitPrice));
                    if (existingItem != null)
                    {
                        existingItem.ReturnedQuantity = item.ReturnedQuantity;
                        existingItem.UpdatedAt = DateTime.UtcNow;
                    }
                }

                foreach (var payment in sale.Payments)
                {
                    if (!existingSale.Payments.Any(p => p.Id == payment.Id || p.ReferenceNumber == payment.ReferenceNumber))
                    {
                        existingSale.Payments.Add(new Payment
                        {
                            Id = payment.Id,
                            SaleId = existingSale.Id,
                            Method = payment.Method,
                            Amount = payment.Amount,
                            PaymentDate = payment.PaymentDate,
                            ReferenceNumber = payment.ReferenceNumber
                        });
                    }
                }

                await adminDb.SaveChangesAsync(cancellationToken);
                saleNumberMap[sale.SaleNumber] = existingSale.SaleNumber;
                continue;
            }

            var saleNumberToUse = sale.SaleNumber;
            if (existingSale != null && existingSale.Id != sale.Id)
            {
                // Collision with existing sale number in Admin DB: dynamically assign next monotonic sequence
                saleNumberToUse = await GetNextAvailableSaleNumberAsync(adminDb, sale.SaleDate, cancellationToken);
                _logger.LogWarning(
                    "Sale {OriginalNumber} (Id {Id}) collided with existing sale in Admin DB. Re-assigned to {NewNumber}.",
                    sale.SaleNumber, sale.Id, saleNumberToUse);
            }

            saleNumberMap[sale.SaleNumber] = saleNumberToUse;

            var adminSale = new Sale
            {
                Id = sale.Id,
                SaleNumber = saleNumberToUse,
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
                RefundedAmount = sale.RefundedAmount,
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
                    PromotionDiscountAmount = item.PromotionDiscountAmount,
                    ReturnedQuantity = item.ReturnedQuantity
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
            await adminDb.SaveChangesAsync(cancellationToken);
        }

        foreach (var sm in stockMovements)
        {
            var exists = await adminDb.StockMovements.AnyAsync(s => s.Id == sm.Id, cancellationToken);
            if (!exists)
            {
                var refToUse = sm.Reference != null && saleNumberMap.TryGetValue(sm.Reference, out var mappedRef)
                    ? mappedRef
                    : sm.Reference;

                adminDb.StockMovements.Add(new StockMovement
                {
                    Id = sm.Id,
                    ProductId = sm.ProductId,
                    Type = sm.Type,
                    QuantityChange = sm.QuantityChange,
                    StockAfter = sm.StockAfter,
                    Reference = refToUse,
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

        // Explicitly remove child collections to ensure clean deletion
        var itemsToRemove = salesToShift.SelectMany(s => s.Items).ToList();
        var paymentsToRemove = salesToShift.SelectMany(s => s.Payments).ToList();
        if (itemsToRemove.Count > 0)
        {
            _normalDb.SaleItems.RemoveRange(itemsToRemove);
        }
        if (paymentsToRemove.Count > 0)
        {
            _normalDb.Payments.RemoveRange(paymentsToRemove);
        }

        // Now delete sales and stock movements from normal DB
        _normalDb.Sales.RemoveRange(salesToShift);
        if (stockMovements.Count > 0)
        {
            _normalDb.StockMovements.RemoveRange(stockMovements);
        }

        await _normalDb.SaveChangesAsync(cancellationToken);

        if (lastSaleKept != null)
        {
            _logger.LogInformation(
                "Successfully shifted {Count} sales (totaling Rs. {Amount:N2}) from Normal DB to Admin DB for Cashier {CashierId}. Latest sale {LastSaleNumber} (Rs. {LastAmount:N2}) remains active in Normal DB.",
                salesToShift.Count, totalAmount, cashierId, lastSaleKept.SaleNumber, lastSaleKept.TotalAmount);

            return new SalesShiftResult(
                true,
                salesToShift.Count,
                totalAmount,
                $"Successfully transferred {salesToShift.Count} previous sales (Rs. {totalAmount:N2}) to Admin database while keeping the latest sale {lastSaleKept.SaleNumber} (Rs. {lastSaleKept.TotalAmount:N2}) active in normal database.");
        }

        _logger.LogInformation(
            "Successfully shifted all {Count} remaining sales (totaling Rs. {Amount:N2}) from Normal DB to Admin DB for Cashier {CashierId}.",
            salesToShift.Count, totalAmount, cashierId);

        return new SalesShiftResult(
            true,
            salesToShift.Count,
            totalAmount,
            $"Successfully transferred all {salesToShift.Count} remaining sales (Rs. {totalAmount:N2}) to Admin database.");
    }

    private static async Task<string> GetNextAvailableSaleNumberAsync(ApplicationDbContext adminDb, DateTime saleDate, CancellationToken cancellationToken)
    {
        var date = saleDate.Date;
        var prefix = $"SALE-{date:yyyyMMdd}-";

        var existingNumbers = await adminDb.Sales
            .Where(s => s.SaleNumber.StartsWith(prefix))
            .Select(s => s.SaleNumber)
            .ToListAsync(cancellationToken);

        int maxSeq = 0;
        foreach (var sn in existingNumbers)
        {
            if (sn.Length > prefix.Length)
            {
                var suffix = sn.Substring(prefix.Length);
                if (int.TryParse(suffix, out var seq) && seq > maxSeq)
                {
                    maxSeq = seq;
                }
            }
        }

        return $"{prefix}{(maxSeq + 1):D4}";
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

        // Sync Promotions
        var promoIds = sales.SelectMany(s => s.Items).Where(i => i.PromotionId.HasValue).Select(i => i.PromotionId!.Value).Distinct().ToList();
        foreach (var pmid in promoIds)
        {
            var promoExists = await adminDb.Promotions.AnyAsync(p => p.Id == pmid, cancellationToken);
            if (!promoExists)
            {
                var normalPromo = await normalDb.Promotions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pmid, cancellationToken);
                if (normalPromo != null)
                {
                    adminDb.Promotions.Add(new Promotion
                    {
                        Id = normalPromo.Id,
                        Name = normalPromo.Name,
                        Description = normalPromo.Description,
                        Type = normalPromo.Type,
                        ProductId = normalPromo.ProductId,
                        BuyQuantity = normalPromo.BuyQuantity,
                        FreeQuantity = normalPromo.FreeQuantity,
                        MinQuantity = normalPromo.MinQuantity,
                        DiscountPercent = normalPromo.DiscountPercent,
                        BundleQuantity = normalPromo.BundleQuantity,
                        BundlePrice = normalPromo.BundlePrice,
                        StartDate = normalPromo.StartDate,
                        EndDate = normalPromo.EndDate,
                        IsActive = normalPromo.IsActive,
                        CreatedAt = normalPromo.CreatedAt
                    });
                    await adminDb.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }
}
