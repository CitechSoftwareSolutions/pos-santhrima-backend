using Microsoft.EntityFrameworkCore;
using POSSystem.Application.Features.Settings;
using POSSystem.Domain.Entities;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class SettingsService : ISettingsService
{
    private readonly ApplicationDbContext _db;
    private static bool _initialized = false;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public SettingsService(ApplicationDbContext db)
    {
        _db = db;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            // Ensure table exists via raw SQL fallback if migration hasn't been executed
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS ""StoreSettings"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""LoyaltyPointsEnabled"" boolean NOT NULL DEFAULT true,
                    ""LoyaltyEarningRate"" numeric NOT NULL DEFAULT 0.001,
                    ""LoyaltyMilestoneThreshold"" numeric NOT NULL DEFAULT 75000,
                    ""LoyaltyPointValue"" numeric NOT NULL DEFAULT 1.00,
                    ""LoyaltyPointsExpiryMonths"" integer NOT NULL DEFAULT 12,
                    ""LoyaltyMilestoneGiftDescription"" text NOT NULL DEFAULT 'Loyalty Milestone Reward Voucher',
                    ""StoreName"" text NOT NULL DEFAULT 'Santhrima Store',
                    ""StoreAddress"" text NULL,
                    ""StorePhone"" text NULL,
                    ""ReceiptHeader"" text NOT NULL DEFAULT 'Welcome to Santhrima Store\nYour trusted grocery partner',
                    ""ReceiptFooter"" text NOT NULL DEFAULT 'Thank you for shopping with us!\nExchange possible within 7 days with bill.',
                    ""DefaultPaperWidth"" text NOT NULL DEFAULT '80mm',
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false
                );";

            try
            {
                await _db.Database.ExecuteSqlRawAsync(createTableSql, cancellationToken);
                await _db.Database.ExecuteSqlRawAsync(
                    @"ALTER TABLE ""StoreSettings"" ADD COLUMN IF NOT EXISTS ""HotkeyBindingsJson"" text NULL;",
                    cancellationToken);
            }
            catch
            {
                // Ignore if DB already managed by migrations
            }

            // Seed default settings row if table is empty
            var existing = await _db.StoreSettings.IgnoreQueryFilters().FirstOrDefaultAsync(cancellationToken);
            if (existing is null)
            {
                var defaultSetting = new StoreSetting
                {
                    LoyaltyPointsEnabled = true,
                    LoyaltyEarningRate = 0.001m,
                    LoyaltyMilestoneThreshold = 75000m,
                    LoyaltyPointValue = 1.00m,
                    LoyaltyPointsExpiryMonths = 12,
                    LoyaltyMilestoneGiftDescription = "Loyalty Milestone Reward Voucher",
                    StoreName = "Santhrima Store",
                    StoreAddress = "Main Street, Colombo, Sri Lanka",
                    StorePhone = "+94 11 234 5678",
                    ReceiptHeader = "Welcome to Santhrima Store\nYour trusted grocery partner",
                    ReceiptFooter = "Thank you for shopping with us!\nExchange possible within 7 days with bill.",
                    DefaultPaperWidth = "80mm",
                    CreatedAt = DateTime.UtcNow
                };

                _db.StoreSettings.Add(defaultSetting);
                await _db.SaveChangesAsync(cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<StoreSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var setting = await _db.StoreSettings.FirstOrDefaultAsync(cancellationToken);
        if (setting is null)
        {
            setting = new StoreSetting();
            _db.StoreSettings.Add(setting);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ToDto(setting);
    }

    public async Task<StoreSettingsDto> UpdateSettingsAsync(UpdateStoreSettingsRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var setting = await _db.StoreSettings.FirstOrDefaultAsync(cancellationToken);
        if (setting is null)
        {
            setting = new StoreSetting();
            _db.StoreSettings.Add(setting);
        }

        setting.LoyaltyPointsEnabled = request.LoyaltyPointsEnabled;
        setting.LoyaltyEarningRate = request.LoyaltyEarningRate;
        setting.LoyaltyMilestoneThreshold = request.LoyaltyMilestoneThreshold;
        setting.LoyaltyPointValue = request.LoyaltyPointValue;
        setting.LoyaltyPointsExpiryMonths = request.LoyaltyPointsExpiryMonths;
        setting.LoyaltyMilestoneGiftDescription = request.LoyaltyMilestoneGiftDescription ?? string.Empty;
        setting.StoreName = request.StoreName ?? "Santhrima Store";
        setting.StoreAddress = request.StoreAddress;
        setting.StorePhone = request.StorePhone;
        setting.ReceiptHeader = request.ReceiptHeader ?? string.Empty;
        setting.ReceiptFooter = request.ReceiptFooter ?? string.Empty;
        setting.DefaultPaperWidth = request.DefaultPaperWidth ?? "80mm";
        setting.HotkeyBindingsJson = request.HotkeyBindingsJson;
        setting.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(setting);
    }

    private static StoreSettingsDto ToDto(StoreSetting s) => new(
        s.Id,
        s.LoyaltyPointsEnabled,
        s.LoyaltyEarningRate,
        s.LoyaltyMilestoneThreshold,
        s.LoyaltyPointValue,
        s.LoyaltyPointsExpiryMonths,
        s.LoyaltyMilestoneGiftDescription,
        s.StoreName,
        s.StoreAddress,
        s.StorePhone,
        s.ReceiptHeader,
        s.ReceiptFooter,
        s.DefaultPaperWidth,
        s.HotkeyBindingsJson);
}
