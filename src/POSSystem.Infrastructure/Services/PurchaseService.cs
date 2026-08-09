using Microsoft.EntityFrameworkCore;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Common.Interfaces;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Purchases;
using POSSystem.Domain.Entities;
using POSSystem.Domain.Enums;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class PurchaseService : IPurchaseService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public PurchaseService(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<PurchaseDto>> GetAllAsync(PurchaseQueryParams query)
    {
        var purchases = _db.Purchases.Include(p => p.Supplier).Include(p => p.Items).ThenInclude(i => i.Product).AsQueryable();

        if (query.SupplierId.HasValue)
        {
            purchases = purchases.Where(p => p.SupplierId == query.SupplierId.Value);
        }

        if (query.ApprovalStatus.HasValue)
        {
            purchases = purchases.Where(p => p.ApprovalStatus == query.ApprovalStatus.Value);
        }

        if (query.IsPaid.HasValue)
        {
            purchases = purchases.Where(p => p.IsPaid == query.IsPaid.Value);
        }

        if (query.IsAddedToStock.HasValue)
        {
            purchases = purchases.Where(p => p.IsAddedToStock == query.IsAddedToStock.Value);
        }

        var totalCount = await purchases.CountAsync();

        var items = await purchases
            .OrderByDescending(p => p.PurchaseDate)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        var userNames = await ResolveUserNamesAsync(items);

        return new PagedResult<PurchaseDto>
        {
            Items = items.Select(p => ToDto(p, userNames)).ToList(),
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    public async Task<PurchaseDto> GetByIdAsync(Guid id)
    {
        var purchase = await _db.Purchases
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Purchase), id);

        var userNames = await ResolveUserNamesAsync(new[] { purchase });

        return ToDto(purchase, userNames);
    }

    public async Task<PurchaseDto> CreateAsync(CreatePurchaseRequest request, Guid userId)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ValidationAppException("A purchase must contain at least one item.");
        }

        if (request.Items.Any(i => i.QuantityOrdered <= 0))
        {
            throw new ValidationAppException("Quantity must be greater than zero for every item.");
        }

        if (request.Items.Any(i => i.FreeQuantity < 0))
        {
            throw new ValidationAppException("Free quantity cannot be negative.");
        }

        if (request.Items.Any(i => i.UnitCost < 0))
        {
            throw new ValidationAppException("Unit cost cannot be negative.");
        }

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId)
            ?? throw new NotFoundException(nameof(Supplier), request.SupplierId);

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        if (products.Count != productIds.Count)
        {
            throw new ValidationAppException("One or more products in the purchase could not be found.");
        }

        var purchase = new Purchase
        {
            PurchaseNumber = await GeneratePurchaseNumberAsync(),
            SupplierId = supplier.Id,
            CreatedByUserId = userId,
            ExpectedDeliveryDate = request.ExpectedDeliveryDate,
            Notes = request.Notes,
        };

        // Admin/Manager can commit their own invoices without a separate approver;
        // only Cashier-entered invoices need Admin/Manager approval before stock is touched.
        if (_currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Manager))
        {
            purchase.ApprovalStatus = PurchaseApprovalStatus.Approved;
            purchase.ApprovedByUserId = userId;
            purchase.ApprovedAt = DateTime.UtcNow;
        }

        decimal subTotal = 0;

        foreach (var itemRequest in request.Items)
        {
            var lineTotal = itemRequest.UnitCost * itemRequest.QuantityOrdered;
            purchase.Items.Add(new PurchaseItem
            {
                ProductId = itemRequest.ProductId,
                QuantityOrdered = itemRequest.QuantityOrdered,
                FreeQuantity = itemRequest.FreeQuantity,
                UnitCost = itemRequest.UnitCost,
                LineTotal = lineTotal,
            });
            subTotal += lineTotal;
        }

        purchase.SubTotal = subTotal;
        purchase.TaxAmount = 0;
        purchase.TotalAmount = subTotal;

        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync();

        return await GetByIdAsync(purchase.Id);
    }

    public async Task<PurchaseDto> ApproveAsync(Guid id, Guid userId)
    {
        var purchase = await _db.Purchases.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Purchase), id);

        if (purchase.IsCancelled)
        {
            throw new BusinessRuleException("Cannot approve a cancelled purchase.");
        }

        if (purchase.ApprovalStatus == PurchaseApprovalStatus.Approved)
        {
            throw new BusinessRuleException("Purchase is already approved.");
        }

        purchase.ApprovalStatus = PurchaseApprovalStatus.Approved;
        purchase.ApprovedByUserId = userId;
        purchase.ApprovedAt = DateTime.UtcNow;
        purchase.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task<PurchaseDto> AddToStockAsync(Guid id, Guid userId)
    {
        var purchase = await _db.Purchases
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Purchase), id);

        if (purchase.IsCancelled)
        {
            throw new BusinessRuleException("Cannot add a cancelled purchase to stock.");
        }

        if (purchase.ApprovalStatus != PurchaseApprovalStatus.Approved)
        {
            throw new BusinessRuleException("Purchase must be approved before it can be added to stock.");
        }

        if (purchase.IsAddedToStock)
        {
            throw new BusinessRuleException("This purchase has already been added to stock.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();

        foreach (var item in purchase.Items)
        {
            var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
            if (product is null)
            {
                continue;
            }

            var totalQuantity = item.QuantityOrdered + item.FreeQuantity;
            product.StockQuantity += totalQuantity;
            product.CostPrice = item.UnitCost;
            product.UpdatedAt = DateTime.UtcNow;

            _db.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                Type = StockMovementType.Purchase,
                QuantityChange = totalQuantity,
                StockAfter = product.StockQuantity,
                Reference = purchase.PurchaseNumber,
                PerformedByUserId = userId,
            });
        }

        purchase.IsAddedToStock = true;
        purchase.StockAddedByUserId = userId;
        purchase.StockAddedAt = DateTime.UtcNow;
        purchase.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return await GetByIdAsync(id);
    }

    public async Task<PurchaseDto> MarkAsPaidAsync(Guid id, Guid userId)
    {
        var purchase = await _db.Purchases.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Purchase), id);

        if (purchase.IsCancelled)
        {
            throw new BusinessRuleException("Cannot mark a cancelled purchase as paid.");
        }

        if (purchase.IsPaid)
        {
            throw new BusinessRuleException("This purchase is already marked as paid.");
        }

        purchase.IsPaid = true;
        purchase.PaidByUserId = userId;
        purchase.PaidAt = DateTime.UtcNow;
        purchase.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task<PurchaseDto> CancelAsync(Guid id)
    {
        var purchase = await _db.Purchases.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Purchase), id);

        if (purchase.IsAddedToStock)
        {
            throw new BusinessRuleException("Cannot cancel a purchase that has already been added to stock.");
        }

        if (purchase.IsCancelled)
        {
            throw new BusinessRuleException("Purchase is already cancelled.");
        }

        purchase.IsCancelled = true;
        purchase.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    private async Task<string> GeneratePurchaseNumberAsync()
    {
        var today = DateTime.UtcNow.Date;
        var countToday = await _db.Purchases.CountAsync(p => p.PurchaseDate >= today);
        return $"PO-{today:yyyyMMdd}-{(countToday + 1):D4}";
    }

    private async Task<Dictionary<Guid, string>> ResolveUserNamesAsync(IEnumerable<Purchase> purchases)
    {
        var userIds = new HashSet<Guid>();
        foreach (var p in purchases)
        {
            userIds.Add(p.CreatedByUserId);
            if (p.ApprovedByUserId.HasValue) userIds.Add(p.ApprovedByUserId.Value);
            if (p.StockAddedByUserId.HasValue) userIds.Add(p.StockAddedByUserId.Value);
            if (p.PaidByUserId.HasValue) userIds.Add(p.PaidByUserId.Value);
        }

        return await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
    }

    private static PurchaseDto ToDto(Purchase p, Dictionary<Guid, string> userNames) => new(
        p.Id,
        p.PurchaseNumber,
        p.SupplierId,
        p.Supplier.Name,
        p.PurchaseDate,
        p.ExpectedDeliveryDate,
        p.SubTotal,
        p.TaxAmount,
        p.TotalAmount,
        p.Notes,
        userNames.GetValueOrDefault(p.CreatedByUserId, "Unknown"),
        p.ApprovalStatus,
        p.ApprovedByUserId.HasValue ? userNames.GetValueOrDefault(p.ApprovedByUserId.Value, "Unknown") : null,
        p.ApprovedAt,
        p.IsAddedToStock,
        p.StockAddedByUserId.HasValue ? userNames.GetValueOrDefault(p.StockAddedByUserId.Value, "Unknown") : null,
        p.StockAddedAt,
        p.IsPaid,
        p.PaidByUserId.HasValue ? userNames.GetValueOrDefault(p.PaidByUserId.Value, "Unknown") : null,
        p.PaidAt,
        p.IsCancelled,
        p.Items.Select(i => new PurchaseItemDto(i.Id, i.ProductId, i.Product.Name, i.QuantityOrdered, i.FreeQuantity, i.UnitCost, i.LineTotal)).ToList());
}
