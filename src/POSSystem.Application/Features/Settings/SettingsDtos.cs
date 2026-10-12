namespace POSSystem.Application.Features.Settings;

public record StoreSettingsDto(
    Guid Id,
    bool LoyaltyPointsEnabled,
    decimal LoyaltyEarningRate,
    decimal LoyaltyMilestoneThreshold,
    decimal LoyaltyPointValue,
    int LoyaltyPointsExpiryMonths,
    string LoyaltyMilestoneGiftDescription,
    string StoreName,
    string? StoreAddress,
    string? StorePhone,
    string ReceiptHeader,
    string ReceiptFooter,
    string DefaultPaperWidth,
    string? HotkeyBindingsJson);

public record UpdateStoreSettingsRequest(
    bool LoyaltyPointsEnabled,
    decimal LoyaltyEarningRate,
    decimal LoyaltyMilestoneThreshold,
    decimal LoyaltyPointValue,
    int LoyaltyPointsExpiryMonths,
    string LoyaltyMilestoneGiftDescription,
    string StoreName,
    string? StoreAddress,
    string? StorePhone,
    string ReceiptHeader,
    string ReceiptFooter,
    string DefaultPaperWidth,
    string? HotkeyBindingsJson);

public interface ISettingsService
{
    Task<StoreSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task<StoreSettingsDto> UpdateSettingsAsync(UpdateStoreSettingsRequest request, CancellationToken cancellationToken = default);
}
