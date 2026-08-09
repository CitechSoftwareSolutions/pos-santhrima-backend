using POSSystem.Domain.Enums;

namespace POSSystem.Application.Features.Promotions;

public record PromotionDto(
    Guid Id,
    string Name,
    string? Description,
    Guid ProductId,
    string ProductName,
    PromotionType Type,
    decimal? BuyQuantity,
    decimal? FreeQuantity,
    decimal? MinQuantity,
    decimal? DiscountPercent,
    decimal? BundleQuantity,
    decimal? BundlePrice,
    bool IsActive,
    DateTime? StartDate,
    DateTime? EndDate);

public record PromotionSummaryDto(
    Guid Id,
    string Name,
    PromotionType Type,
    decimal? BuyQuantity,
    decimal? FreeQuantity,
    decimal? MinQuantity,
    decimal? DiscountPercent,
    decimal? BundleQuantity,
    decimal? BundlePrice);

public record CreatePromotionRequest(
    string Name,
    string? Description,
    Guid ProductId,
    PromotionType Type,
    decimal? BuyQuantity,
    decimal? FreeQuantity,
    decimal? MinQuantity,
    decimal? DiscountPercent,
    decimal? BundleQuantity,
    decimal? BundlePrice,
    DateTime? StartDate,
    DateTime? EndDate);

public record UpdatePromotionRequest(
    string Name,
    string? Description,
    PromotionType Type,
    decimal? BuyQuantity,
    decimal? FreeQuantity,
    decimal? MinQuantity,
    decimal? DiscountPercent,
    decimal? BundleQuantity,
    decimal? BundlePrice,
    bool IsActive,
    DateTime? StartDate,
    DateTime? EndDate);

public interface IPromotionService
{
    Task<List<PromotionDto>> GetAllAsync(Guid? productId = null);
    Task<PromotionDto> GetByIdAsync(Guid id);
    Task<PromotionDto> CreateAsync(CreatePromotionRequest request);
    Task<PromotionDto> UpdateAsync(Guid id, UpdatePromotionRequest request);
    Task DeleteAsync(Guid id);
}
