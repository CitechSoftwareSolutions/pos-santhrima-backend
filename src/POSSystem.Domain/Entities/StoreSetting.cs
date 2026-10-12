using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

public class StoreSetting : BaseEntity
{
    // Loyalty Program Configurations
    public bool LoyaltyPointsEnabled { get; set; } = true;
    public decimal LoyaltyEarningRate { get; set; } = 0.001m; // 0.1% of purchase amount
    public decimal LoyaltyMilestoneThreshold { get; set; } = 75000m; // Rs. 75,000 milestones
    public decimal LoyaltyPointValue { get; set; } = 1.00m; // Rs. 1 per point
    public int LoyaltyPointsExpiryMonths { get; set; } = 12;
    public string LoyaltyMilestoneGiftDescription { get; set; } = "Loyalty Milestone Reward Voucher";

    // Store Branding & Receipt Details
    public string StoreName { get; set; } = "Santhrima Store";
    public string? StoreAddress { get; set; } = "Main Street, Colombo, Sri Lanka";
    public string? StorePhone { get; set; } = "+94 11 234 5678";
    public string ReceiptHeader { get; set; } = "Welcome to Santhrima Store\nYour trusted grocery partner";
    public string ReceiptFooter { get; set; } = "Thank you for shopping with us!\nExchange possible within 7 days with bill.";
    public string DefaultPaperWidth { get; set; } = "80mm"; // 80mm or 58mm
    public string? HotkeyBindingsJson { get; set; }
}
