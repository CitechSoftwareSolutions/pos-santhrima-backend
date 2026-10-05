using Microsoft.EntityFrameworkCore;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Products;
using POSSystem.Application.Features.Promotions;
using POSSystem.Domain.Entities;
using POSSystem.Domain.Enums;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class ProductService : IProductService
{
    private readonly ApplicationDbContext _db;

    public ProductService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ProductDto>> GetAllAsync(ProductQueryParams query)
    {
        var products = _db.Products
            .Include(p => p.Category)
            .Include(p => p.Promotions)
            .Include(p => p.PriceTiers)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            products = products.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.Sku.ToLower().Contains(term) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)));
        }

        if (query.CategoryId.HasValue)
        {
            products = products.Where(p => p.CategoryId == query.CategoryId.Value);
        }

        if (query.IsActive.HasValue)
        {
            products = products.Where(p => p.IsActive == query.IsActive.Value);
        }

        if (query.LowStockOnly)
        {
            products = products.Where(p => p.StockQuantity <= p.ReorderLevel);
        }

        var totalCount = await products.CountAsync();

        var items = await products
            .OrderBy(p => p.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(p => ToDto(p))
            .ToListAsync();

        return new PagedResult<ProductDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize,
        };
    }

    public async Task<ProductDto> GetByIdAsync(Guid id)
    {
        var product = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Promotions)
            .Include(p => p.PriceTiers)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Product), id);

        return ToDto(product);
    }

    public async Task<ProductDto?> GetBySkuOrBarcodeAsync(string code)
    {
        var product = await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Promotions)
            .Include(p => p.PriceTiers)
            .FirstOrDefaultAsync(p => p.Sku == code || p.Barcode == code);

        return product is null ? null : ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request)
    {
        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == request.CategoryId);
        if (!categoryExists)
        {
            throw new ValidationAppException($"Category '{request.CategoryId}' does not exist.");
        }

        if (string.IsNullOrWhiteSpace(request.Sku))
        {
            request = request with { Sku = await GenerateNextSkuAsync(request.CategoryId) };
        }

        var skuTaken = await _db.Products.AnyAsync(p => p.Sku == request.Sku);
        if (skuTaken)
        {
            throw new ConflictException($"A product with SKU '{request.Sku}' already exists.");
        }

        ValidateBasePrices(request.RetailPrice, request.WholesalePrice, request.SpecialPrice);
        var tiers = ValidateAndBuildTiers(request.PriceTiers);

        var product = new Product
        {
            Sku = request.Sku,
            Barcode = request.Barcode,
            Name = request.Name,
            Description = request.Description,
            CategoryId = request.CategoryId,
            CostPrice = request.CostPrice,
            RetailPrice = request.RetailPrice,
            WholesalePrice = request.WholesalePrice,
            SpecialPrice = request.SpecialPrice,
            TaxRate = request.TaxRate,
            Unit = request.Unit,
            AllowDecimalQuantity = request.AllowDecimalQuantity,
            StockQuantity = request.InitialStock,
            ReorderLevel = request.ReorderLevel,
            ImageUrl = request.ImageUrl,
            PriceTiers = tiers,
        };

        _db.Products.Add(product);

        if (request.InitialStock != 0)
        {
            _db.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                Type = StockMovementType.InitialStock,
                QuantityChange = request.InitialStock,
                StockAfter = request.InitialStock,
                Reference = "Initial stock on product creation",
            });
        }

        await _db.SaveChangesAsync();

        return await GetByIdAsync(product.Id);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, UpdateProductRequest request)
    {
        var product = await _db.Products
            .Include(p => p.PriceTiers)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Product), id);

        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == request.CategoryId);
        if (!categoryExists)
        {
            throw new ValidationAppException($"Category '{request.CategoryId}' does not exist.");
        }

        var skuTaken = await _db.Products.AnyAsync(p => p.Sku == request.Sku && p.Id != id);
        if (skuTaken)
        {
            throw new ConflictException($"A product with SKU '{request.Sku}' already exists.");
        }

        ValidateBasePrices(request.RetailPrice, request.WholesalePrice, request.SpecialPrice);
        var tiers = ValidateAndBuildTiers(request.PriceTiers);

        product.Sku = request.Sku;
        product.Barcode = request.Barcode;
        product.Name = request.Name;
        product.Description = request.Description;
        product.CategoryId = request.CategoryId;
        product.CostPrice = request.CostPrice;
        product.RetailPrice = request.RetailPrice;
        product.WholesalePrice = request.WholesalePrice;
        product.SpecialPrice = request.SpecialPrice;
        product.TaxRate = request.TaxRate;
        product.Unit = request.Unit;
        product.AllowDecimalQuantity = request.AllowDecimalQuantity;
        product.ReorderLevel = request.ReorderLevel;
        product.ImageUrl = request.ImageUrl;
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTime.UtcNow;

        // Replace the tier set wholesale. Deletes are flushed first so a re-used MinQuantity
        // value doesn't collide with the row being replaced under the unique index. New tiers
        // are added directly to the DbSet (not just via the navigation) because Id is
        // client-generated: attaching them only through fixup makes EF's key-presence
        // heuristic treat them as pre-existing rows and emit UPDATE instead of INSERT.
        _db.ProductPriceTiers.RemoveRange(product.PriceTiers);
        await _db.SaveChangesAsync();

        foreach (var tier in tiers)
        {
            tier.ProductId = product.Id;
        }
        _db.ProductPriceTiers.AddRange(tiers);
        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task DeleteAsync(Guid id)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Product), id);

        product.IsDeleted = true;
        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<ProductDto> AdjustStockAsync(Guid id, AdjustStockRequest request, Guid? userId)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException(nameof(Product), id);

        var newStock = product.StockQuantity + request.QuantityChange;
        if (newStock < 0)
        {
            throw new BusinessRuleException("Stock adjustment would result in negative stock.");
        }

        product.StockQuantity = newStock;
        product.UpdatedAt = DateTime.UtcNow;

        _db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            Type = StockMovementType.Adjustment,
            QuantityChange = request.QuantityChange,
            StockAfter = newStock,
            Notes = request.Notes,
            PerformedByUserId = userId,
        });

        await _db.SaveChangesAsync();

        return await GetByIdAsync(id);
    }

    public async Task<List<ProductDto>> GetLowStockAsync()
    {
        return await _db.Products
            .Include(p => p.Category)
            .Include(p => p.Promotions)
            .Include(p => p.PriceTiers)
            .Where(p => p.IsActive && p.StockQuantity <= p.ReorderLevel)
            .OrderBy(p => p.StockQuantity)
            .Select(p => ToDto(p))
            .ToListAsync();
    }

    private static void ValidateBasePrices(decimal retailPrice, decimal? wholesalePrice, decimal? specialPrice)
    {
        if (retailPrice < 0 || wholesalePrice < 0 || specialPrice < 0)
        {
            throw new ValidationAppException("Prices cannot be negative.");
        }
    }

    private static List<ProductPriceTier> ValidateAndBuildTiers(List<PriceTierRequest>? requests)
    {
        if (requests is null || requests.Count == 0)
        {
            return new List<ProductPriceTier>();
        }

        if (requests.Any(t => t.Quantity <= 0))
        {
            throw new ValidationAppException("Price tier quantity must be greater than zero.");
        }

        if (requests.Any(t => t.RetailPrice < 0 || t.WholesalePrice < 0 || t.SpecialPrice < 0))
        {
            throw new ValidationAppException("Price tier prices cannot be negative.");
        }

        if (requests.Select(t => t.Quantity).Distinct().Count() != requests.Count)
        {
            throw new ValidationAppException("Price tiers cannot have duplicate quantities.");
        }

        return requests
            .Select(t => new ProductPriceTier
            {
                Quantity = t.Quantity,
                RetailPrice = t.RetailPrice,
                WholesalePrice = t.WholesalePrice,
                SpecialPrice = t.SpecialPrice,
            })
            .ToList();
    }

    private static ProductDto ToDto(Product p) => new(
        p.Id,
        p.Sku,
        p.Barcode,
        p.Name,
        p.Description,
        p.CategoryId,
        p.Category.Name,
        p.CostPrice,
        p.RetailPrice,
        p.WholesalePrice,
        p.SpecialPrice,
        p.TaxRate,
        p.Unit,
        p.AllowDecimalQuantity,
        p.StockQuantity,
        p.ReorderLevel,
        p.ImageUrl,
        p.IsActive,
        p.StockQuantity <= p.ReorderLevel,
        p.PriceTiers
            .OrderBy(t => t.Quantity)
            .Select(t => new PriceTierDto(t.Id, t.Quantity, t.RetailPrice, t.WholesalePrice, t.SpecialPrice))
            .ToList(),
        PromotionCalculator.GetActivePromotion(p.Promotions));

    public async Task<string> GenerateNextSkuAsync(Guid categoryId)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId);
        if (category is null)
        {
            throw new ValidationAppException($"Category '{categoryId}' does not exist.");
        }

        var rawName = category.Name.Trim();
        var cleanChars = rawName.Where(char.IsLetterOrDigit).ToArray();
        var cleanName = new string(cleanChars);
        var code = (cleanName.Length >= 2 ? cleanName[..2] : cleanName.PadRight(2, 'X')).ToUpperInvariant();

        var prefix = $"SKU-{code}-";
        var existingSkus = await _db.Products
            .Where(p => p.Sku.StartsWith(prefix))
            .Select(p => p.Sku)
            .ToListAsync();

        var maxNumber = 0;
        foreach (var sku in existingSkus)
        {
            var numPart = sku[prefix.Length..];
            if (int.TryParse(numPart, out var n) && n > maxNumber)
            {
                maxNumber = n;
            }
        }

        var nextNumber = maxNumber + 1;
        var candidate = $"SKU-{code}-{nextNumber:D4}";

        while (await _db.Products.AnyAsync(p => p.Sku == candidate))
        {
            nextNumber++;
            candidate = $"SKU-{code}-{nextNumber:D4}";
        }

        return candidate;
    }
}

