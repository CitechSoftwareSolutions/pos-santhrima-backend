using Microsoft.EntityFrameworkCore;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Suppliers;
using POSSystem.Domain.Entities;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class SupplierService : ISupplierService
{
    private readonly ApplicationDbContext _db;

    public SupplierService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<SupplierDto>> GetAllAsync(PaginationParams query)
    {
        var suppliers = _db.Suppliers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            suppliers = suppliers.Where(s =>
                s.Name.ToLower().Contains(term) ||
                (s.Phone != null && s.Phone.Contains(term)) ||
                (s.Email != null && s.Email.ToLower().Contains(term)));
        }

        var totalCount = await suppliers.CountAsync();

        var items = await suppliers
            .OrderBy(s => s.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => ToDto(s))
            .ToListAsync();

        return new PagedResult<SupplierDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    public async Task<SupplierDto> GetByIdAsync(Guid id)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new NotFoundException(nameof(Supplier), id);

        return ToDto(supplier);
    }

    public async Task<SupplierDto> CreateAsync(CreateSupplierRequest request)
    {
        var supplier = new Supplier
        {
            Name = request.Name,
            ContactPerson = request.ContactPerson,
            Phone = request.Phone,
            Email = request.Email,
            Address = request.Address,
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        return ToDto(supplier);
    }

    public async Task<SupplierDto> UpdateAsync(Guid id, UpdateSupplierRequest request)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new NotFoundException(nameof(Supplier), id);

        supplier.Name = request.Name;
        supplier.ContactPerson = request.ContactPerson;
        supplier.Phone = request.Phone;
        supplier.Email = request.Email;
        supplier.Address = request.Address;
        supplier.IsActive = request.IsActive;
        supplier.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return ToDto(supplier);
    }

    public async Task DeleteAsync(Guid id)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id)
            ?? throw new NotFoundException(nameof(Supplier), id);

        supplier.IsDeleted = true;
        supplier.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static SupplierDto ToDto(Supplier s) =>
        new(s.Id, s.Name, s.ContactPerson, s.Phone, s.Email, s.Address, s.IsActive);
}
