using Microsoft.EntityFrameworkCore;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Customers;
using POSSystem.Domain.Entities;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class CustomerService : ICustomerService
{
    private readonly ApplicationDbContext _db;

    public CustomerService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<CustomerDto>> GetAllAsync(PaginationParams query)
    {
        await ProcessExpiredLoyaltyPointsAsync();

        var customers = _db.Customers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            customers = customers.Where(c =>
                c.Name.ToLower().Contains(term) ||
                (c.Phone != null && c.Phone.Contains(term)) ||
                (c.Email != null && c.Email.ToLower().Contains(term)));
        }

        var totalCount = await customers.CountAsync();

        var pageList = await customers
            .OrderBy(c => c.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync();

        var customerIds = pageList.Select(c => c.Id).ToList();

        var earnedMap = await _db.LoyaltyTransactions
            .Where(t => customerIds.Contains(t.CustomerId) && t.Type == LoyaltyTransactionType.Earned)
            .GroupBy(t => t.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(x => x.Points) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total);

        var expiredMap = await _db.LoyaltyTransactions
            .Where(t => customerIds.Contains(t.CustomerId) && t.Type == LoyaltyTransactionType.Expired)
            .GroupBy(t => t.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(x => Math.Abs(x.Points)) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total);

        var items = pageList.Select(c => ToDto(
            c,
            earnedMap.GetValueOrDefault(c.Id, 0m),
            expiredMap.GetValueOrDefault(c.Id, 0m)
        )).ToList();

        return new PagedResult<CustomerDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    public async Task<CustomerDto> GetByIdAsync(Guid id)
    {
        await ProcessExpiredLoyaltyPointsAsync(id);

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(nameof(Customer), id);

        var earned = await _db.LoyaltyTransactions
            .Where(t => t.CustomerId == id && t.Type == LoyaltyTransactionType.Earned)
            .SumAsync(t => (decimal?)t.Points) ?? 0m;

        var expired = await _db.LoyaltyTransactions
            .Where(t => t.CustomerId == id && t.Type == LoyaltyTransactionType.Expired)
            .SumAsync(t => (decimal?)Math.Abs(t.Points)) ?? 0m;

        return ToDto(customer, earned, expired);
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request)
    {
        var customer = new Customer
        {
            Name = request.Name,
            Phone = request.Phone,
            Email = request.Email,
            Address = request.Address,
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        return ToDto(customer, 0m, 0m);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(nameof(Customer), id);

        customer.Name = request.Name;
        customer.Phone = request.Phone;
        customer.Email = request.Email;
        customer.Address = request.Address;
        customer.IsActive = request.IsActive;
        customer.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task DeleteAsync(Guid id)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(nameof(Customer), id);

        customer.IsDeleted = true;
        customer.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<CustomerDto> ClaimMilestoneGiftAsync(Guid customerId)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId)
            ?? throw new NotFoundException(nameof(Customer), customerId);

        const decimal milestoneThreshold = 75000m;
        var tier = (int)Math.Floor(customer.TotalPurchases / milestoneThreshold);
        if (tier <= customer.MilestoneGiftsClaimed)
        {
            throw new BusinessRuleException(
                $"Customer has not reached an unclaimed gift milestone. Tier: {tier}, gifts claimed: {customer.MilestoneGiftsClaimed}.");
        }

        customer.MilestoneGiftsClaimed++;
        var pointsReset = customer.LoyaltyPoints;
        customer.LoyaltyPoints = 0m;
        customer.UpdatedAt = DateTime.UtcNow;

        // Reset all remaining active earned batches so points cannot be redeemed
        var activeBatches = await _db.LoyaltyTransactions
            .Where(t => t.CustomerId == customer.Id && t.Type == LoyaltyTransactionType.Earned && t.PointsRemaining > 0)
            .ToListAsync();

        foreach (var batch in activeBatches)
        {
            batch.PointsRemaining = 0;
            batch.UpdatedAt = DateTime.UtcNow;
        }

        _db.LoyaltyTransactions.Add(new LoyaltyTransaction
        {
            CustomerId = customer.Id,
            Type = LoyaltyTransactionType.MilestoneGiftClaimed,
            Points = -pointsReset,
            PointsRemaining = 0,
            Notes = $"Milestone gift #{customer.MilestoneGiftsClaimed} claimed for reaching tier Rs. {customer.MilestoneGiftsClaimed * milestoneThreshold:N0} purchases. Royalty points ({pointsReset:N2} pts) reset to zero."
        });

        await _db.SaveChangesAsync();

        return await GetByIdAsync(customerId);
    }

    public async Task<List<LoyaltyTransactionDto>> GetLoyaltyHistoryAsync(Guid customerId)
    {
        await ProcessExpiredLoyaltyPointsAsync(customerId);

        return await _db.LoyaltyTransactions
            .Include(t => t.Sale)
            .Where(t => t.CustomerId == customerId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new LoyaltyTransactionDto(
                t.Id,
                t.Type,
                t.Points,
                t.PointsRemaining,
                t.ExpiresAt,
                t.ExpiresAt.HasValue && t.ExpiresAt.Value <= DateTime.UtcNow,
                t.CreatedAt,
                t.SaleId,
                t.Sale != null ? t.Sale.SaleNumber : null,
                t.Notes
            ))
            .ToListAsync();
    }

    public async Task ProcessExpiredLoyaltyPointsAsync(Guid? customerId = null)
    {
        var now = DateTime.UtcNow;
        var query = _db.LoyaltyTransactions
            .Where(t => t.Type == LoyaltyTransactionType.Earned && t.PointsRemaining > 0 && t.ExpiresAt <= now);

        if (customerId.HasValue)
        {
            query = query.Where(t => t.CustomerId == customerId.Value);
        }

        var expiredBatches = await query.ToListAsync();
        if (expiredBatches.Count == 0) return;

        var customerIds = expiredBatches.Select(b => b.CustomerId).Distinct().ToList();

        foreach (var batch in expiredBatches)
        {
            _db.LoyaltyTransactions.Add(new LoyaltyTransaction
            {
                CustomerId = batch.CustomerId,
                SaleId = batch.SaleId,
                Type = LoyaltyTransactionType.Expired,
                Points = -batch.PointsRemaining,
                PointsRemaining = 0,
                Notes = $"Expired 3-month old points earned on {batch.CreatedAt:yyyy-MM-dd}"
            });
            batch.PointsRemaining = 0;
            batch.UpdatedAt = now;
        }

        var affectedCustomers = await _db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .ToListAsync();

        foreach (var cust in affectedCustomers)
        {
            var activeRemaining = await _db.LoyaltyTransactions
                .Where(t => t.CustomerId == cust.Id && t.Type == LoyaltyTransactionType.Earned && t.PointsRemaining > 0 && t.ExpiresAt > now)
                .SumAsync(t => (decimal?)t.PointsRemaining) ?? 0m;

            cust.LoyaltyPoints = activeRemaining;
            cust.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();
    }

    private static CustomerDto ToDto(Customer c, decimal lifetimeEarned, decimal lifetimeExpired)
    {
        const decimal milestoneThreshold = 75000m;
        var tier = (int)Math.Floor(c.TotalPurchases / milestoneThreshold);
        var isEligible = tier > c.MilestoneGiftsClaimed;
        var nextMilestone = (Math.Max(tier, c.MilestoneGiftsClaimed) + 1) * milestoneThreshold;
        var toNext = Math.Max(0, nextMilestone - c.TotalPurchases);
        var canRedeem = isEligible;
        var isEligibleGift = isEligible;

        return new CustomerDto(
            c.Id,
            c.Name,
            c.Phone,
            c.Email,
            c.Address,
            c.LoyaltyPoints,
            c.TotalPurchases,
            c.LoyaltyPointsRedeemed,
            lifetimeEarned,
            lifetimeExpired,
            tier,
            c.MilestoneGiftsClaimed,
            isEligibleGift,
            canRedeem,
            nextMilestone,
            toNext,
            c.CreditBalance,
            c.IsActive
        );
    }
}
