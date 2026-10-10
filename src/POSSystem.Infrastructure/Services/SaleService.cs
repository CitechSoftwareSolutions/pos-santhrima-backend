using Microsoft.EntityFrameworkCore;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Products;
using POSSystem.Application.Features.Promotions;
using POSSystem.Application.Features.Sales;
using POSSystem.Domain.Entities;
using POSSystem.Domain.Enums;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class SaleService : ISaleService
{
    private readonly ApplicationDbContext _db;

    public SaleService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<SaleListItemDto>> GetAllAsync(SaleQueryParams query)
    {
        var sales = _db.Sales
            .Include(s => s.Customer)
            .AsQueryable();

        if (query.DateFrom.HasValue)
        {
            var dateFrom = query.DateFrom.Value.AsUtc();
            sales = sales.Where(s => s.SaleDate >= dateFrom);
        }

        if (query.DateTo.HasValue)
        {
            var dateTo = query.DateTo.Value.AsUtc();
            sales = sales.Where(s => s.SaleDate <= dateTo);
        }

        if (query.CustomerId.HasValue)
        {
            sales = sales.Where(s => s.CustomerId == query.CustomerId.Value);
        }

        if (query.CashierId.HasValue)
        {
            sales = sales.Where(s => s.CashierId == query.CashierId.Value);
        }

        if (query.Status.HasValue)
        {
            sales = sales.Where(s => s.Status == query.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            sales = sales.Where(s => s.SaleNumber.ToLower().Contains(term));
        }

        var totalCount = await sales.CountAsync();

        var cashierIds = await sales
            .OrderByDescending(s => s.SaleDate)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => s.CashierId)
            .ToListAsync();

        var pageSales = await sales
            .OrderByDescending(s => s.SaleDate)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        var cashierNames = await _db.Users
            .Where(u => cashierIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        var items = pageSales.Select(s => new SaleListItemDto(
            s.Id,
            s.SaleNumber,
            s.Customer?.Name,
            cashierNames.GetValueOrDefault(s.CashierId, "Unknown"),
            s.SaleDate,
            s.TotalAmount,
            s.Status,
            s.PaymentStatus)).ToList();

        return new PagedResult<SaleListItemDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    public async Task<SaleDto> GetByIdAsync(Guid id)
    {
        var sale = await _db.Sales
            .Include(s => s.Customer)
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new NotFoundException(nameof(Sale), id);

        return await ToDtoAsync(sale);
    }

    public async Task<SaleDto> CreateAsync(CreateSaleRequest request, Guid cashierId)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ValidationAppException("A sale must contain at least one item.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Include(p => p.Promotions)
            .Include(p => p.PriceTiers)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        if (products.Count != productIds.Count)
        {
            throw new ValidationAppException("One or more products in the sale could not be found.");
        }

        Customer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId.Value)
                ?? throw new NotFoundException(nameof(Customer), request.CustomerId.Value);
        }

        var sale = new Sale
        {
            SaleNumber = await GenerateSaleNumberAsync(),
            CustomerId = request.CustomerId,
            CashierId = cashierId,
            SaleDate = DateTime.UtcNow,
            Notes = request.Notes,
        };

        decimal subTotal = 0;
        decimal itemTax = 0;
        decimal itemDiscount = 0;

        foreach (var itemRequest in request.Items)
        {
            var product = products[itemRequest.ProductId];

            if (!product.IsActive)
            {
                throw new BusinessRuleException($"Product '{product.Name}' is not active and cannot be sold.");
            }

            if (product.StockQuantity < itemRequest.Quantity)
            {
                throw new BusinessRuleException($"Insufficient stock for '{product.Name}'. Available: {product.StockQuantity}, requested: {itemRequest.Quantity}.");
            }

            if (itemRequest.UnitPrice.HasValue && itemRequest.UnitPrice.Value < 0)
            {
                throw new ValidationAppException($"Unit price for '{product.Name}' cannot be negative.");
            }

            var unitPrice = itemRequest.UnitPrice
                ?? PriceTierResolver.ResolveUnitPrice(product.RetailPrice, product.PriceTiers, itemRequest.Quantity);

            var activePromotion = PromotionCalculator.GetActivePromotion(product.Promotions);
            var promotionDiscount = activePromotion is null
                ? 0
                : PromotionCalculator.CalculateDiscount(activePromotion, itemRequest.Quantity, unitPrice);

            var baseAmount = unitPrice * itemRequest.Quantity;
            var combinedDiscount = itemRequest.DiscountAmount + promotionDiscount;
            var taxableAmount = baseAmount - combinedDiscount;
            var taxAmount = Math.Round(taxableAmount * (product.TaxRate / 100m), 2);
            var lineTotal = taxableAmount + taxAmount;

            sale.Items.Add(new SaleItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = itemRequest.Quantity,
                UnitPrice = unitPrice,
                DiscountAmount = itemRequest.DiscountAmount,
                TaxAmount = taxAmount,
                LineTotal = lineTotal,
                PromotionId = activePromotion?.Id,
                PromotionName = activePromotion?.Name,
                PromotionDiscountAmount = promotionDiscount,
            });

            product.StockQuantity -= itemRequest.Quantity;
            product.UpdatedAt = DateTime.UtcNow;

            _db.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                Type = StockMovementType.Sale,
                QuantityChange = -itemRequest.Quantity,
                StockAfter = product.StockQuantity,
                Reference = sale.SaleNumber,
                PerformedByUserId = cashierId,
            });

            subTotal += baseAmount;
            itemTax += taxAmount;
            itemDiscount += combinedDiscount;
        }

        decimal pointsRedeemed = 0;
        decimal pointsDiscount = 0;

        if (customer is not null && request.LoyaltyPointsRedeemed > 0)
        {
            const decimal milestoneThreshold = 75000m;
            var tier = (int)Math.Floor(customer.TotalPurchases / milestoneThreshold);
            if (tier <= customer.MilestoneGiftsClaimed)
            {
                var nextMilestone = (customer.MilestoneGiftsClaimed + 1) * milestoneThreshold;
                throw new BusinessRuleException(
                    $"Royalty points can only be redeemed after reaching each Rs. {milestoneThreshold:N0} milestone. Next redeemable milestone: Rs. {nextMilestone:N0}. Current purchases: Rs. {customer.TotalPurchases:N2}.");
            }

            // Expire overdue batches (points older than 3 months)
            var overdueBatches = await _db.LoyaltyTransactions
                .Where(t => t.CustomerId == customer.Id && t.Type == LoyaltyTransactionType.Earned && t.PointsRemaining > 0 && t.ExpiresAt <= DateTime.UtcNow)
                .ToListAsync();

            foreach (var ob in overdueBatches)
            {
                _db.LoyaltyTransactions.Add(new LoyaltyTransaction
                {
                    CustomerId = customer.Id,
                    Type = LoyaltyTransactionType.Expired,
                    Points = -ob.PointsRemaining,
                    PointsRemaining = 0,
                    Notes = $"Expired 3-month old points earned on {ob.CreatedAt:yyyy-MM-dd}"
                });
                ob.PointsRemaining = 0;
                ob.UpdatedAt = DateTime.UtcNow;
            }

            var activeBatches = await _db.LoyaltyTransactions
                .Where(t => t.CustomerId == customer.Id && t.Type == LoyaltyTransactionType.Earned && t.PointsRemaining > 0 && t.ExpiresAt > DateTime.UtcNow)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();

            var availablePoints = activeBatches.Sum(b => b.PointsRemaining);
            if (request.LoyaltyPointsRedeemed > availablePoints)
            {
                throw new ValidationAppException($"Insufficient active loyalty points. Available: {availablePoints:N2}, requested: {request.LoyaltyPointsRedeemed:N2}.");
            }

            pointsRedeemed = request.LoyaltyPointsRedeemed;
            pointsDiscount = pointsRedeemed; // 1 point = Rs. 1

            // Consume points FIFO
            var remainingToDeduct = pointsRedeemed;
            foreach (var batch in activeBatches)
            {
                var take = Math.Min(batch.PointsRemaining, remainingToDeduct);
                batch.PointsRemaining -= take;
                batch.UpdatedAt = DateTime.UtcNow;
                remainingToDeduct -= take;
                if (remainingToDeduct <= 0) break;
            }

            customer.LoyaltyPointsRedeemed += pointsRedeemed;
            customer.MilestoneGiftsClaimed++;
        }

        var totalDiscount = itemDiscount + request.DiscountAmount + pointsDiscount;
        var totalAmount = subTotal - totalDiscount + itemTax;
        if (totalAmount < 0)
        {
            throw new ValidationAppException("Total discount exceeds the sale subtotal.");
        }

        var amountPaid = request.Payments?.Sum(p => p.Amount) ?? 0;

        sale.SubTotal = subTotal;
        sale.TaxAmount = itemTax;
        sale.DiscountAmount = totalDiscount;
        sale.TotalAmount = totalAmount;
        sale.AmountPaid = amountPaid;
        sale.ChangeDue = Math.Max(0, amountPaid - totalAmount);
        sale.LoyaltyPointsRedeemed = pointsRedeemed;
        sale.PaymentStatus = amountPaid <= 0
            ? PaymentStatus.Unpaid
            : (amountPaid >= totalAmount ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid);
        sale.Status = SaleStatus.Completed;

        // Earn loyalty points: 0.001 rate of total bill
        decimal pointsEarned = 0;
        if (customer is not null)
        {
            pointsEarned = Math.Round(totalAmount * 0.001m, 2);
            sale.LoyaltyPointsEarned = pointsEarned;
        }

        foreach (var payment in request.Payments ?? new List<CreateSalePaymentRequest>())
        {
            sale.Payments.Add(new Payment
            {
                Amount = payment.Amount,
                Method = payment.Method,
                ReferenceNumber = payment.ReferenceNumber,
            });
        }

        _db.Sales.Add(sale);

        if (customer is not null)
        {
            customer.TotalPurchases += totalAmount;

            if (pointsRedeemed > 0)
            {
                _db.LoyaltyTransactions.Add(new LoyaltyTransaction
                {
                    CustomerId = customer.Id,
                    Sale = sale,
                    Type = LoyaltyTransactionType.Redeemed,
                    Points = -pointsRedeemed,
                    PointsRemaining = 0,
                    Notes = $"Redeemed {pointsRedeemed:N2} points for Rs. {pointsRedeemed:N2} discount on {sale.SaleNumber}"
                });
            }

            if (pointsEarned > 0)
            {
                _db.LoyaltyTransactions.Add(new LoyaltyTransaction
                {
                    CustomerId = customer.Id,
                    Sale = sale,
                    Type = LoyaltyTransactionType.Earned,
                    Points = pointsEarned,
                    PointsRemaining = pointsEarned,
                    ExpiresAt = DateTime.UtcNow.AddMonths(3),
                    Notes = $"Earned {pointsEarned:N2} points (0.001 rate on Rs. {totalAmount:N2}) on {sale.SaleNumber}"
                });
            }

            customer.LoyaltyPoints = Math.Max(0, customer.LoyaltyPoints - pointsRedeemed + pointsEarned);
            customer.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetByIdAsync(sale.Id);
    }

    public async Task<SaleDto> VoidAsync(Guid id, string reason)
    {
        var sale = await _db.Sales
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new NotFoundException(nameof(Sale), id);

        if (sale.Status is SaleStatus.Cancelled or SaleStatus.Refunded)
        {
            throw new BusinessRuleException($"Sale is already {sale.Status}.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        foreach (var item in sale.Items)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
            if (product is not null)
            {
                product.StockQuantity += item.Quantity;
                product.UpdatedAt = DateTime.UtcNow;

                _db.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    Type = StockMovementType.Return,
                    QuantityChange = item.Quantity,
                    StockAfter = product.StockQuantity,
                    Reference = sale.SaleNumber,
                    Notes = $"Sale voided: {reason}",
                });
            }
        }

        sale.Status = SaleStatus.Cancelled;
        sale.Notes = string.IsNullOrWhiteSpace(sale.Notes) ? $"Voided: {reason}" : $"{sale.Notes} | Voided: {reason}";
        sale.UpdatedAt = DateTime.UtcNow;

        if (sale.CustomerId.HasValue)
        {
            var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == sale.CustomerId.Value);
            if (customer is not null)
            {
                customer.TotalPurchases = Math.Max(0, customer.TotalPurchases - sale.TotalAmount);

                if (sale.LoyaltyPointsEarned > 0)
                {
                    _db.LoyaltyTransactions.Add(new LoyaltyTransaction
                    {
                        CustomerId = customer.Id,
                        SaleId = sale.Id,
                        Type = LoyaltyTransactionType.Adjustment,
                        Points = -sale.LoyaltyPointsEarned,
                        PointsRemaining = 0,
                        Notes = $"Reversal of points earned on voided sale {sale.SaleNumber}"
                    });
                    customer.LoyaltyPoints = Math.Max(0, customer.LoyaltyPoints - sale.LoyaltyPointsEarned);
                }

                if (sale.LoyaltyPointsRedeemed > 0)
                {
                    _db.LoyaltyTransactions.Add(new LoyaltyTransaction
                    {
                        CustomerId = customer.Id,
                        SaleId = sale.Id,
                        Type = LoyaltyTransactionType.Earned,
                        Points = sale.LoyaltyPointsRedeemed,
                        PointsRemaining = sale.LoyaltyPointsRedeemed,
                        ExpiresAt = DateTime.UtcNow.AddMonths(3),
                        Notes = $"Restoration of points redeemed on voided sale {sale.SaleNumber}"
                    });
                    customer.LoyaltyPointsRedeemed = Math.Max(0, customer.LoyaltyPointsRedeemed - sale.LoyaltyPointsRedeemed);
                    customer.LoyaltyPoints += sale.LoyaltyPointsRedeemed;
                }

                customer.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetByIdAsync(id);
    }

    public async Task<SaleDto> RefundAsync(Guid id, List<Guid>? saleItemIds, string reason)
    {
        var sale = await _db.Sales
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new NotFoundException(nameof(Sale), id);

        if (sale.Status is SaleStatus.Cancelled or SaleStatus.Refunded)
        {
            throw new BusinessRuleException($"Sale is already {sale.Status}.");
        }

        var itemsToRefund = saleItemIds is { Count: > 0 }
            ? sale.Items.Where(i => saleItemIds.Contains(i.Id)).ToList()
            : sale.Items.ToList();

        if (itemsToRefund.Count == 0)
        {
            throw new ValidationAppException("No matching sale items found to refund.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        decimal refundAmount = 0;

        foreach (var item in itemsToRefund)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
            if (product is not null)
            {
                product.StockQuantity += item.Quantity;
                product.UpdatedAt = DateTime.UtcNow;

                _db.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    Type = StockMovementType.Return,
                    QuantityChange = item.Quantity,
                    StockAfter = product.StockQuantity,
                    Reference = sale.SaleNumber,
                    Notes = $"Refund: {reason}",
                });
            }

            refundAmount += item.LineTotal;
        }

        sale.Payments.Add(new Payment
        {
            Amount = -refundAmount,
            Method = PaymentMethod.Other,
            ReferenceNumber = $"REFUND-{DateTime.UtcNow:yyyyMMddHHmmss}",
        });

        var isFullRefund = itemsToRefund.Count == sale.Items.Count;
        sale.Status = isFullRefund ? SaleStatus.Refunded : SaleStatus.PartiallyRefunded;
        sale.PaymentStatus = PaymentStatus.Refunded;
        sale.Notes = string.IsNullOrWhiteSpace(sale.Notes) ? $"Refunded: {reason}" : $"{sale.Notes} | Refunded: {reason}";
        sale.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetByIdAsync(id);
    }

    private async Task<string> GenerateSaleNumberAsync()
    {
        var today = DateTime.UtcNow.Date;
        var countToday = await _db.Sales.CountAsync(s => s.SaleDate >= today);
        return $"SALE-{today:yyyyMMdd}-{(countToday + 1):D4}";
    }

    private async Task<SaleDto> ToDtoAsync(Sale sale)
    {
        var cashier = await _db.Users.FirstOrDefaultAsync(u => u.Id == sale.CashierId);

        return new SaleDto(
            sale.Id,
            sale.SaleNumber,
            sale.CustomerId,
            sale.Customer?.Name,
            sale.CashierId,
            cashier?.FullName ?? "Unknown",
            sale.SaleDate,
            sale.SubTotal,
            sale.TaxAmount,
            sale.DiscountAmount,
            sale.TotalAmount,
            sale.AmountPaid,
            sale.ChangeDue,
            sale.LoyaltyPointsRedeemed,
            sale.LoyaltyPointsEarned,
            sale.Status,
            sale.PaymentStatus,
            sale.Notes,
            sale.Items.Select(i => new SaleItemDto(
                i.Id, i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.DiscountAmount, i.TaxAmount, i.LineTotal,
                i.PromotionId, i.PromotionName, i.PromotionDiscountAmount)).ToList(),
            sale.Payments.Select(p => new SalePaymentDto(p.Id, p.Method, p.Amount, p.ReferenceNumber, p.PaymentDate)).ToList());
    }
}
