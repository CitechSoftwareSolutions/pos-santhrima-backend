using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Common.Interfaces;
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
    private readonly IDatabaseContextResolver _dbResolver;
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly ISalesShiftService? _salesShiftService;
    private readonly ILogger<SaleService>? _logger;

    public SaleService(
        ApplicationDbContext db,
        IDatabaseContextResolver dbResolver,
        IApplicationDbContextFactory dbFactory,
        ISalesShiftService? salesShiftService = null,
        ILogger<SaleService>? logger = null)
    {
        _db = db;
        _dbResolver = dbResolver;
        _dbFactory = dbFactory;
        _salesShiftService = salesShiftService;
        _logger = logger;
    }

    public async Task<PagedResult<SaleListItemDto>> GetAllAsync(SaleQueryParams query)
    {
        IQueryable<Sale> ApplyFilters(IQueryable<Sale> source)
        {
            var q = source
                .Include(s => s.Customer)
                .Include(s => s.Terminal)
                .AsQueryable();

            if (query.DateFrom.HasValue)
            {
                var dateFrom = query.DateFrom.Value.AsUtc();
                q = q.Where(s => s.SaleDate >= dateFrom);
            }

            if (query.DateTo.HasValue)
            {
                var dateTo = query.DateTo.Value.AsUtc();
                q = q.Where(s => s.SaleDate <= dateTo);
            }

            if (query.CustomerId.HasValue)
            {
                q = q.Where(s => s.CustomerId == query.CustomerId.Value);
            }

            if (query.CashierId.HasValue)
            {
                q = q.Where(s => s.CashierId == query.CashierId.Value);
            }

            if (query.TerminalId.HasValue)
            {
                q = q.Where(s => s.TerminalId == query.TerminalId.Value);
            }

            if (query.Status.HasValue)
            {
                q = q.Where(s => s.Status == query.Status.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim().ToLower();
                q = q.Where(s => s.SaleNumber.ToLower().Contains(term));
            }

            return q;
        }

        if (!_dbResolver.IsAdminMode())
        {
            if (_salesShiftService != null)
            {
                try
                {
                    await _salesShiftService.CheckAndShiftAllEligibleCashiersAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Could not check and shift eligible cashiers during GetAllAsync.");
                }
            }

            var sales = ApplyFilters(_db.Sales);
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
                s.TerminalId,
                s.Terminal?.Name ?? (s.Terminal != null ? $"Counter {s.Terminal.CounterNumber}" : (s.TerminalId.HasValue ? "Counter" : null)),
                s.SaleDate,
                s.TotalAmount,
                s.Status,
                s.PaymentStatus,
                s.RefundedAmount)).ToList();

            return new PagedResult<SaleListItemDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize,
            };
        }

        // Admin Mode (Dark Mode): Combine Normal DB and Admin DB so admin sees everything
        using var normalDb = _dbFactory.CreateNormalDbContext();

        var adminSalesList = await ApplyFilters(_db.Sales).ToListAsync();
        var normalSalesList = await ApplyFilters(normalDb.Sales).ToListAsync();

        var combinedSales = adminSalesList
            .Concat(normalSalesList)
            .DistinctBy(s => s.Id)
            .OrderByDescending(s => s.SaleDate)
            .ToList();

        var totalCombinedCount = combinedSales.Count;

        var pageSalesList = combinedSales
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        var pageCashierIds = pageSalesList.Select(s => s.CashierId).Distinct().ToList();

        var usersMap = await _db.Users
            .Where(u => pageCashierIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName);

        var combinedItems = pageSalesList.Select(s => new SaleListItemDto(
            s.Id,
            s.SaleNumber,
            s.Customer?.Name,
            usersMap.GetValueOrDefault(s.CashierId, "Unknown"),
            s.TerminalId,
            s.Terminal?.Name ?? (s.Terminal != null ? $"Counter {s.Terminal.CounterNumber}" : (s.TerminalId.HasValue ? "Counter" : null)),
            s.SaleDate,
            s.TotalAmount,
            s.Status,
            s.PaymentStatus,
            s.RefundedAmount)).ToList();

        return new PagedResult<SaleListItemDto>
        {
            Items = combinedItems,
            TotalCount = totalCombinedCount,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    public async Task<SaleDto> GetByIdAsync(Guid id)
    {
        var sale = await _db.Sales
            .Include(s => s.Customer)
            .Include(s => s.Terminal)
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sale == null && _dbResolver.IsAdminMode())
        {
            using var normalDb = _dbFactory.CreateNormalDbContext();
            sale = await normalDb.Sales
                .Include(s => s.Customer)
                .Include(s => s.Terminal)
                .Include(s => s.Items)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        if (sale == null)
        {
            throw new NotFoundException(nameof(Sale), id);
        }

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
            TerminalId = request.TerminalId,
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
                ?? PriceTierResolver.ResolveUnitPrice(product.Mrp ?? product.RetailPrice, product.PriceTiers, itemRequest.Quantity);

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

        var resultDto = await GetByIdAsync(sale.Id);

        if (_salesShiftService != null)
        {
            try
            {
                await _salesShiftService.CheckAndShiftCashierSalesAsync(cashierId);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to auto-shift cashier sales after sale {SaleNumber}", sale.SaleNumber);
            }
        }

        return resultDto;
    }

    public async Task<SaleDto> VoidAsync(Guid id, string reason)
    {
        var sale = await _db.Sales
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id);

        ApplicationDbContext targetDb = _db;
        ApplicationDbContext? normalDb = null;

        if (sale == null && _dbResolver.IsAdminMode())
        {
            normalDb = _dbFactory.CreateNormalDbContext();
            targetDb = normalDb;
            sale = await targetDb.Sales
                .Include(s => s.Items)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        if (sale == null)
        {
            normalDb?.Dispose();
            throw new NotFoundException(nameof(Sale), id);
        }

        try
        {
            if (sale.Status is SaleStatus.Cancelled or SaleStatus.Refunded)
            {
                throw new BusinessRuleException($"Sale is already {sale.Status}.");
            }

            await using var transaction = await targetDb.Database.BeginTransactionAsync();

            foreach (var item in sale.Items)
            {
                var product = await targetDb.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product is not null)
                {
                    product.StockQuantity += item.Quantity;
                    product.UpdatedAt = DateTime.UtcNow;

                    targetDb.StockMovements.Add(new StockMovement
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
                var customer = await targetDb.Customers.FirstOrDefaultAsync(c => c.Id == sale.CustomerId.Value);
                if (customer is not null)
                {
                    customer.TotalPurchases = Math.Max(0, customer.TotalPurchases - sale.TotalAmount);

                    if (sale.LoyaltyPointsEarned > 0)
                    {
                        targetDb.LoyaltyTransactions.Add(new LoyaltyTransaction
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
                        targetDb.LoyaltyTransactions.Add(new LoyaltyTransaction
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

            await targetDb.SaveChangesAsync();
            await transaction.CommitAsync();

            return await GetByIdAsync(id);
        }
        finally
        {
            normalDb?.Dispose();
        }
    }

    public async Task<SaleDto> RefundAsync(Guid id, List<Guid>? saleItemIds, string reason)
    {
        var sale = await _db.Sales
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sale == null && _dbResolver.IsAdminMode())
        {
            using var normalDb = _dbFactory.CreateNormalDbContext();
            sale = await normalDb.Sales
                .Include(s => s.Items)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        if (sale == null)
        {
            throw new NotFoundException(nameof(Sale), id);
        }

        var itemsToRefund = saleItemIds is { Count: > 0 }
            ? sale.Items.Where(i => saleItemIds.Contains(i.Id)).ToList()
            : sale.Items.ToList();

        var returnItems = itemsToRefund
            .Where(i => i.Quantity - i.ReturnedQuantity > 0)
            .Select(i => new ReturnSaleItemRequest(i.Id, i.Quantity - i.ReturnedQuantity, reason))
            .ToList();

        if (returnItems.Count == 0)
        {
            throw new ValidationAppException("No items available to return for this sale.");
        }

        return await ProcessReturnAsync(id, new ReturnSaleRequest(returnItems, PaymentMethod.Other, reason));
    }

    public async Task<SaleDto> ProcessReturnAsync(Guid id, ReturnSaleRequest request)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            throw new ValidationAppException("Please select at least one item to return.");
        }

        var sale = await _db.Sales
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);

        ApplicationDbContext targetDb = _db;
        ApplicationDbContext? normalDb = null;

        if (sale == null && _dbResolver.IsAdminMode())
        {
            normalDb = _dbFactory.CreateNormalDbContext();
            targetDb = normalDb;
            sale = await targetDb.Sales
                .Include(s => s.Items)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        if (sale == null)
        {
            normalDb?.Dispose();
            throw new NotFoundException(nameof(Sale), id);
        }

        try
        {
            if (sale.Status is SaleStatus.Cancelled or SaleStatus.Refunded)
            {
                throw new BusinessRuleException($"Sale is already {sale.Status}.");
            }

            await using var transaction = await targetDb.Database.BeginTransactionAsync();

            decimal totalRefundAmount = 0;
            var returnNotes = new List<string>();

            foreach (var returnReq in request.Items)
            {
                var item = sale.Items.FirstOrDefault(i => i.Id == returnReq.SaleItemId);
                if (item == null)
                {
                    throw new ValidationAppException($"Sale item with ID {returnReq.SaleItemId} not found in this sale.");
                }

                var returnable = item.Quantity - item.ReturnedQuantity;
                if (returnReq.Quantity <= 0)
                {
                    continue;
                }

                if (returnReq.Quantity > returnable)
                {
                    throw new ValidationAppException(
                        $"Cannot return {returnReq.Quantity} of '{item.ProductName}'. Only {returnable} piece(s) available for return.");
                }

                // Calculate item refund amount
                decimal itemRefund;
                var effectiveUnitRate = item.Quantity > 0 ? (item.LineTotal / item.Quantity) : 0;

                if (item.ReturnedQuantity == 0 && returnReq.Quantity == item.Quantity)
                {
                    itemRefund = item.LineTotal;
                }
                else if (item.ReturnedQuantity + returnReq.Quantity == item.Quantity)
                {
                    // Exact remainder for final piece to avoid any floating round discrepancy
                    var alreadyRefundedApprox = Math.Round(effectiveUnitRate * item.ReturnedQuantity, 2);
                    itemRefund = Math.Max(0, item.LineTotal - alreadyRefundedApprox);
                }
                else
                {
                    itemRefund = Math.Round(effectiveUnitRate * returnReq.Quantity, 2);
                }

                item.ReturnedQuantity += returnReq.Quantity;
                item.UpdatedAt = DateTime.UtcNow;
                totalRefundAmount += itemRefund;

                // Restore product stock
                var product = await targetDb.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product != null)
                {
                    product.StockQuantity += returnReq.Quantity;
                    product.UpdatedAt = DateTime.UtcNow;

                    targetDb.StockMovements.Add(new StockMovement
                    {
                        ProductId = product.Id,
                        Type = StockMovementType.Return,
                        QuantityChange = returnReq.Quantity,
                        StockAfter = product.StockQuantity,
                        Reference = sale.SaleNumber,
                        Notes = $"Return: {returnReq.Quantity}x {item.ProductName} ({request.Reason ?? "Customer Return"})",
                    });
                }

                returnNotes.Add($"{returnReq.Quantity}x {item.ProductName}");
            }

            if (totalRefundAmount <= 0)
            {
                throw new ValidationAppException("Total return quantity must be greater than zero.");
            }

            // Record negative refund payment
            var refundPayment = new Payment
            {
                SaleId = sale.Id,
                Amount = -totalRefundAmount,
                Method = request.RefundPaymentMethod,
                ReferenceNumber = $"RET-{sale.SaleNumber}-{DateTime.UtcNow:yyyyMMddHHmmss}",
                PaymentDate = DateTime.UtcNow,
            };
            targetDb.Payments.Add(refundPayment);

            // Adjust Customer Total Purchases & Loyalty Points
            if (sale.CustomerId.HasValue)
            {
                var customer = await targetDb.Customers.FirstOrDefaultAsync(c => c.Id == sale.CustomerId.Value);
                if (customer != null)
                {
                    customer.TotalPurchases = Math.Max(0, customer.TotalPurchases - totalRefundAmount);

                    if (sale.LoyaltyPointsEarned > 0)
                    {
                        var pointsToReclaim = Math.Round(totalRefundAmount * 0.001m, 2);
                        if (pointsToReclaim > 0)
                        {
                            targetDb.LoyaltyTransactions.Add(new LoyaltyTransaction
                            {
                                CustomerId = customer.Id,
                                SaleId = sale.Id,
                                Type = LoyaltyTransactionType.Adjustment,
                                Points = -pointsToReclaim,
                                PointsRemaining = 0,
                                Notes = $"Reversal of points on returned items for sale {sale.SaleNumber}"
                            });
                            customer.LoyaltyPoints = Math.Max(0, customer.LoyaltyPoints - pointsToReclaim);
                        }
                    }

                    customer.UpdatedAt = DateTime.UtcNow;
                }
            }

            // Update status and refunded amount
            sale.RefundedAmount += totalRefundAmount;
            var allItemsFullyReturned = sale.Items.All(i => i.ReturnedQuantity >= i.Quantity);
            sale.Status = allItemsFullyReturned ? SaleStatus.Refunded : SaleStatus.PartiallyRefunded;
            if (allItemsFullyReturned)
            {
                sale.PaymentStatus = PaymentStatus.Refunded;
                sale.RefundedAmount = sale.TotalAmount; // ensure exact match
            }

            var reasonSummary = string.IsNullOrWhiteSpace(request.Reason) ? "" : $" Reason: {request.Reason}.";
            var appendNote = $"Return: {string.Join(", ", returnNotes)} refunded Rs. {totalRefundAmount:N2} via {request.RefundPaymentMethod}.{reasonSummary}";
            sale.Notes = string.IsNullOrWhiteSpace(sale.Notes) ? appendNote : $"{sale.Notes} | {appendNote}";
            sale.UpdatedAt = DateTime.UtcNow;

            await targetDb.SaveChangesAsync();
            await transaction.CommitAsync();

            return await GetByIdAsync(id);
        }
        finally
        {
            normalDb?.Dispose();
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        var sale = await _db.Sales
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == id);

        ApplicationDbContext targetDb = _db;
        ApplicationDbContext? normalDb = null;

        if (sale == null && _dbResolver.IsAdminMode())
        {
            normalDb = _dbFactory.CreateNormalDbContext();
            targetDb = normalDb;
            sale = await targetDb.Sales
                .Include(s => s.Items)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        if (sale == null)
        {
            normalDb?.Dispose();
            throw new NotFoundException(nameof(Sale), id);
        }

        try
        {
            await using var transaction = await targetDb.Database.BeginTransactionAsync();

            // If the sale was not already voided/cancelled, restore any remaining non-returned inventory
            if (sale.Status != SaleStatus.Cancelled)
            {
                foreach (var item in sale.Items)
                {
                    var qtyToRestore = item.Quantity - item.ReturnedQuantity;
                    if (qtyToRestore > 0)
                    {
                        var product = await targetDb.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                        if (product != null)
                        {
                            product.StockQuantity += qtyToRestore;
                            product.UpdatedAt = DateTime.UtcNow;

                            targetDb.StockMovements.Add(new StockMovement
                            {
                                ProductId = product.Id,
                                Type = StockMovementType.Adjustment,
                                QuantityChange = qtyToRestore,
                                StockAfter = product.StockQuantity,
                                Reference = sale.SaleNumber,
                                Notes = $"Sale record deleted: {sale.SaleNumber}",
                            });
                        }
                    }
                }

                // Adjust customer purchases & loyalty points
                if (sale.CustomerId.HasValue)
                {
                    var customer = await targetDb.Customers.FirstOrDefaultAsync(c => c.Id == sale.CustomerId.Value);
                    if (customer != null)
                    {
                        var totalRefundsAlreadyGiven = sale.Payments.Where(p => p.Amount < 0).Sum(p => -p.Amount);
                        var netCustomerPurchases = Math.Max(0, sale.TotalAmount - totalRefundsAlreadyGiven);
                        customer.TotalPurchases = Math.Max(0, customer.TotalPurchases - netCustomerPurchases);

                        if (sale.LoyaltyPointsEarned > 0)
                        {
                            customer.LoyaltyPoints = Math.Max(0, customer.LoyaltyPoints - sale.LoyaltyPointsEarned);
                            targetDb.LoyaltyTransactions.Add(new LoyaltyTransaction
                            {
                                CustomerId = customer.Id,
                                SaleId = sale.Id,
                                Type = LoyaltyTransactionType.Adjustment,
                                Points = -sale.LoyaltyPointsEarned,
                                PointsRemaining = 0,
                                Notes = $"Reversal of points earned on deleted sale {sale.SaleNumber}"
                            });
                        }

                        if (sale.LoyaltyPointsRedeemed > 0)
                        {
                            customer.LoyaltyPointsRedeemed = Math.Max(0, customer.LoyaltyPointsRedeemed - sale.LoyaltyPointsRedeemed);
                            customer.LoyaltyPoints += sale.LoyaltyPointsRedeemed;
                            targetDb.LoyaltyTransactions.Add(new LoyaltyTransaction
                            {
                                CustomerId = customer.Id,
                                SaleId = sale.Id,
                                Type = LoyaltyTransactionType.Earned,
                                Points = sale.LoyaltyPointsRedeemed,
                                PointsRemaining = sale.LoyaltyPointsRedeemed,
                                ExpiresAt = DateTime.UtcNow.AddMonths(3),
                                Notes = $"Restoration of points redeemed on deleted sale {sale.SaleNumber}"
                            });
                        }

                        customer.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }

            // Soft-delete the sale and items
            sale.IsDeleted = true;
            sale.Status = SaleStatus.Cancelled;
            sale.UpdatedAt = DateTime.UtcNow;

            foreach (var item in sale.Items)
            {
                item.IsDeleted = true;
                item.UpdatedAt = DateTime.UtcNow;
            }

            await targetDb.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            normalDb?.Dispose();
        }
    }

    private async Task<string> GenerateSaleNumberAsync()
    {
        var today = DateTime.UtcNow.Date;
        var prefix = $"SALE-{today:yyyyMMdd}-";

        using var adminDb = _dbFactory.CreateAdminDbContext();

        var normalNumbers = await _db.Sales
            .Where(s => s.SaleNumber.StartsWith(prefix))
            .Select(s => s.SaleNumber)
            .ToListAsync();

        var adminNumbers = await adminDb.Sales
            .Where(s => s.SaleNumber.StartsWith(prefix))
            .Select(s => s.SaleNumber)
            .ToListAsync();

        int maxSeq = 0;
        foreach (var sn in normalNumbers.Concat(adminNumbers))
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

    private async Task<SaleDto> ToDtoAsync(Sale sale)
    {
        var cashier = await _db.Users.FirstOrDefaultAsync(u => u.Id == sale.CashierId);
        var terminalName = sale.Terminal?.Name;
        if (terminalName is null && sale.TerminalId.HasValue)
        {
            terminalName = (await _db.TerminalSettings.FindAsync(sale.TerminalId.Value))?.Name;
        }

        return new SaleDto(
            sale.Id,
            sale.SaleNumber,
            sale.CustomerId,
            sale.Customer?.Name,
            sale.CashierId,
            cashier?.FullName ?? "Unknown",
            sale.TerminalId,
            terminalName,
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
                i.PromotionId, i.PromotionName, i.PromotionDiscountAmount, i.ReturnedQuantity)).ToList(),
            sale.Payments.Select(p => new SalePaymentDto(p.Id, p.Method, p.Amount, p.ReferenceNumber, p.PaymentDate)).ToList(),
            sale.RefundedAmount);
    }
}
