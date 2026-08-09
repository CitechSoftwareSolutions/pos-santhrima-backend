using Microsoft.EntityFrameworkCore;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Features.Categories;
using POSSystem.Domain.Entities;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class CategoryService : ICategoryService
{
    private readonly ApplicationDbContext _db;

    public CategoryService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<CategoryDto>> GetAllAsync(bool includeInactive = false)
    {
        var query = _db.Categories.Include(c => c.Products).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.IsActive, c.Products.Count(p => !p.IsDeleted)))
            .ToListAsync();
    }

    public async Task<CategoryDto> GetByIdAsync(Guid id)
    {
        var category = await _db.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(nameof(Category), id);

        return ToDto(category);
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request)
    {
        var category = new Category
        {
            Name = request.Name,
            Description = request.Description,
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return ToDto(category);
    }

    public async Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryRequest request)
    {
        var category = await _db.Categories.FindAsync(id)
            ?? throw new NotFoundException(nameof(Category), id);

        category.Name = request.Name;
        category.Description = request.Description;
        category.IsActive = request.IsActive;
        category.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return ToDto(category);
    }

    public async Task DeleteAsync(Guid id)
    {
        var category = await _db.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(nameof(Category), id);

        if (category.Products.Any(p => !p.IsDeleted))
        {
            throw new BusinessRuleException("Cannot delete a category that still has products assigned to it.");
        }

        category.IsDeleted = true;
        category.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static CategoryDto ToDto(Category category) =>
        new(category.Id, category.Name, category.Description, category.IsActive, category.Products.Count(p => !p.IsDeleted));
}
