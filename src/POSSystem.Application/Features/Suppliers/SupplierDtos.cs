using POSSystem.Application.Common.Models;

namespace POSSystem.Application.Features.Suppliers;

public record SupplierDto(
    Guid Id,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    bool IsActive);

public record CreateSupplierRequest(string Name, string? ContactPerson, string? Phone, string? Email, string? Address);

public record UpdateSupplierRequest(string Name, string? ContactPerson, string? Phone, string? Email, string? Address, bool IsActive);

public interface ISupplierService
{
    Task<PagedResult<SupplierDto>> GetAllAsync(PaginationParams query);
    Task<SupplierDto> GetByIdAsync(Guid id);
    Task<SupplierDto> CreateAsync(CreateSupplierRequest request);
    Task<SupplierDto> UpdateAsync(Guid id, UpdateSupplierRequest request);
    Task DeleteAsync(Guid id);
}
