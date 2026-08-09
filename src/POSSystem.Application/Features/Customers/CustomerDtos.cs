using POSSystem.Application.Common.Models;

namespace POSSystem.Application.Features.Customers;

public record CustomerDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    int LoyaltyPoints,
    decimal CreditBalance,
    bool IsActive
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
}





