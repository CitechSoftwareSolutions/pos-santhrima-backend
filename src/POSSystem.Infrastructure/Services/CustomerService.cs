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

        var items = await customers
            .OrderBy(c => c.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => ToDto(c))
            .ToListAsync();

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
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(nameof(Customer), id);

        return ToDto(customer);
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

        return ToDto(customer);
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

        return ToDto(customer);
    }

    public async Task DeleteAsync(Guid id)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(nameof(Customer), id);

        customer.IsDeleted = true;
        customer.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static CustomerDto ToDto(Customer c) =>
        new(c.Id, c.Name, c.Phone, c.Email, c.Address, c.LoyaltyPoints, c.CreditBalance, c.IsActive);
}
