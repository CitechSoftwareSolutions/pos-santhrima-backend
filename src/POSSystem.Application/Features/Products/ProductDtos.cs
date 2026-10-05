using POSSystem.Application.Common.Models;
using POSSystem.Application.Features.Promotions;

namespace POSSystem.Application.Features.Products;

public record PriceTierDto(
    Guid Id,
    decimal Quantity,
    decimal RetailPrice,
    decimal? WholesalePrice,
    decimal? SpecialPrice);

public record PriceTierRequest(
    decimal Quantity,
    decimal RetailPrice,
    decimal? WholesalePrice,
    decimal? SpecialPrice);

public record ProductDto(
    Guid Id,
    string Sku,
    string? Barcode,
    string Name,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    decimal CostPrice,
    decimal RetailPrice,
    decimal? WholesalePrice,
    decimal? SpecialPrice,
    decimal TaxRate,
    string Unit,
    bool AllowDecimalQuantity,
    decimal StockQuantity,
    decimal ReorderLevel,
    string? ImageUrl,
    bool IsActive,
    bool IsLowStock,
    List<PriceTierDto> PriceTiers,
    PromotionSummaryDto? ActivePromotion);

public record CreateProductRequest(
    string Sku,
    string? Barcode,
    string Name,
    string? Description,
    Guid CategoryId,
    decimal CostPrice,
    decimal RetailPrice,
    decimal? WholesalePrice,
    decimal? SpecialPrice,
    decimal TaxRate,
    string Unit,
    bool AllowDecimalQuantity,
    decimal InitialStock,
    decimal ReorderLevel,
    string? ImageUrl,
    List<PriceTierRequest>? PriceTiers = null);

public record UpdateProductRequest(
    string Sku,
    string? Barcode,
    string Name,
    string? Description,
    Guid CategoryId,
    decimal CostPrice,
    decimal RetailPrice,
    decimal? WholesalePrice,
    decimal? SpecialPrice,
    decimal TaxRate,
    string Unit,
    bool AllowDecimalQuantity,
    decimal ReorderLevel,
    string? ImageUrl,
    bool IsActive,
    List<PriceTierRequest>? PriceTiers = null);

public record AdjustStockRequest(decimal QuantityChange, string? Notes);

public record ProductQueryParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? IsActive { get; set; }
    public bool LowStockOnly { get; set; } = false;
}

public interface IProductService
{
    Task<PagedResult<ProductDto>> GetAllAsync(ProductQueryParams query);
    Task<ProductDto> GetByIdAsync(Guid id);
    Task<ProductDto?> GetBySkuOrBarcodeAsync(string code);
    Task<ProductDto> CreateAsync(CreateProductRequest request);
    Task<ProductDto> UpdateAsync(Guid id, UpdateProductRequest request);
    Task DeleteAsync(Guid id);
    Task<ProductDto> AdjustStockAsync(Guid id, AdjustStockRequest request, Guid? userId);
    Task<List<ProductDto>> GetLowStockAsync();
    Task<string> GenerateNextSkuAsync(Guid categoryId);
}
