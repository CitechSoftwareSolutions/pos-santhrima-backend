using Microsoft.EntityFrameworkCore;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Features.Promotions;
using POSSystem.Domain.Entities;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class PromotionService : IPromotionService
{
    private readonly ApplicationDbContext _db;

    public PromotionService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<PromotionDto>> GetAllAsync(Guid? productId = null)
    {
        var query = _db.Promotions.Include(p => p.Product).AsQueryable();

        if (productId.HasValue)
        {
            query = query.Where(p => p.ProductId == productId.Value);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => ToDto(p))
            .ToListAsync();
    }

    public async Task<PromotionDto> GetByIdAsync(Guid id)
    {
        var promotion = await _db.Promotions.Include(p => p.Product).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Promotion), id);

        return ToDto(promotion);
    }

    public async Task<PromotionDto> CreateAsync(CreatePromotionRequest request)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId)
            ?? throw new NotFoundException(nameof(Product), request.ProductId);

        ValidateFields(request.Type, request.BuyQuantity, request.FreeQuantity, request.MinQuantity,
            request.DiscountPercent, request.BundleQuantity, request.BundlePrice);

        var overlapping = await _db.Promotions.AnyAsync(p =>
            p.ProductId == request.ProductId &&
            p.IsActive &&
            (request.EndDate == null || p.StartDate == null || p.StartDate <= request.EndDate) &&
            (request.StartDate == null || p.EndDate == null || p.EndDate >= request.StartDate));

        if (overlapping)
        {
            throw new ConflictException("An active promotion already exists for this product in the given date range.");
        }

        var promotion = new Promotion
        {
            Name = request.Name,
            Description = request.Description,
            ProductId = request.ProductId,
            Type = request.Type,
            BuyQuantity = request.BuyQuantity,
            FreeQuantity = request.FreeQuantity,
            MinQuantity = request.MinQuantity,
            DiscountPercent = request.DiscountPercent,
            BundleQuantity = request.BundleQuantity,
            BundlePrice = request.BundlePrice,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
        };

        _db.Promotions.Add(promotion);
        await _db.SaveChangesAsync();

        promotion.Product = product;
        return ToDto(promotion);
    }

    public async Task<PromotionDto> UpdateAsync(Guid id, UpdatePromotionRequest request)
    {
        var promotion = await _db.Promotions.Include(p => p.Product).FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Promotion), id);

        ValidateFields(request.Type, request.BuyQuantity, request.FreeQuantity, request.MinQuantity,
            request.DiscountPercent, request.BundleQuantity, request.BundlePrice);

        promotion.Name = request.Name;
        promotion.Description = request.Description;
        promotion.Type = request.Type;
        promotion.BuyQuantity = request.BuyQuantity;
        promotion.FreeQuantity = request.FreeQuantity;
        promotion.MinQuantity = request.MinQuantity;
        promotion.DiscountPercent = request.DiscountPercent;
        promotion.BundleQuantity = request.BundleQuantity;
        promotion.BundlePrice = request.BundlePrice;
        promotion.IsActive = request.IsActive;
        promotion.StartDate = request.StartDate;
        promotion.EndDate = request.EndDate;
        promotion.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return ToDto(promotion);
    }

    public async Task DeleteAsync(Guid id)
    {
        var promotion = await _db.Promotions.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Promotion), id);

        promotion.IsDeleted = true;
        promotion.IsActive = false;
        promotion.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private static void ValidateFields(
        Domain.Enums.PromotionType type,
        decimal? buyQuantity,
        decimal? freeQuantity,
        decimal? minQuantity,
        decimal? discountPercent,
        decimal? bundleQuantity,
        decimal? bundlePrice)
    {
        switch (type)
        {
            case Domain.Enums.PromotionType.BuyXGetYFree when buyQuantity is null or <= 0 || freeQuantity is null or <= 0:
                throw new ValidationAppException("Buy X Get Y Free promotions require positive BuyQuantity and FreeQuantity.");
            case Domain.Enums.PromotionType.BulkPercentOff when minQuantity is null or <= 0 || discountPercent is null or <= 0:
                throw new ValidationAppException("Bulk percent-off promotions require a positive MinQuantity and DiscountPercent.");
            case Domain.Enums.PromotionType.FixedPriceBundle when bundleQuantity is null or <= 0 || bundlePrice is null or < 0:
                throw new ValidationAppException("Fixed-price bundle promotions require a positive BundleQuantity and a non-negative BundlePrice.");
        }
    }

    private static PromotionDto ToDto(Promotion p) => new(
        p.Id,
        p.Name,
        p.Description,
        p.ProductId,
        p.Product.Name,
        p.Type,
        p.BuyQuantity,
        p.FreeQuantity,
        p.MinQuantity,
        p.DiscountPercent,
        p.BundleQuantity,
        p.BundlePrice,
        p.IsActive,
        p.StartDate,
        p.EndDate);
}
