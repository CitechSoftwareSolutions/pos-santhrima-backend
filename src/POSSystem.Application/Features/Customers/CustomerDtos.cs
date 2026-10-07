using POSSystem.Application.Common.Models;
using POSSystem.Domain.Entities;

namespace POSSystem.Application.Features.Customers;

public record CustomerDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    decimal LoyaltyPoints,
    decimal TotalPurchases,
    decimal LoyaltyPointsRedeemed,
    decimal LoyaltyPointsEarned,
    decimal LoyaltyPointsExpired,
    int MilestoneTier,
    int MilestoneGiftsClaimed,
    bool IsEligibleForGift,
    bool CanRedeemPoints,
    decimal NextMilestoneAmount,
    decimal AmountToNextMilestone,
    decimal CreditBalance,
    bool IsActive
);

public record LoyaltyTransactionDto(
    Guid Id,
    LoyaltyTransactionType Type,
    decimal Points,
    decimal PointsRemaining,
    DateTime? ExpiresAt,
    bool IsExpired,
    DateTime CreatedAt,
    Guid? SaleId,
    string? SaleNumber,
    string? Notes
);

public record CreateCustomerRequest(string Name, string? Phone, string? Email, string? Address);

public record UpdateCustomerRequest(string Name, string? Phone, string? Email, string? Address, bool IsActive);

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> GetAllAsync(PaginationParams query);
    Task<CustomerDto> GetByIdAsync(Guid id);
    Task<CustomerDto> CreateAsync(CreateCustomerRequest request);
    Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request);
    Task DeleteAsync(Guid id);
    Task<CustomerDto> ClaimMilestoneGiftAsync(Guid customerId);
    Task<List<LoyaltyTransactionDto>> GetLoyaltyHistoryAsync(Guid customerId);
    Task ProcessExpiredLoyaltyPointsAsync(Guid? customerId = null);
}
