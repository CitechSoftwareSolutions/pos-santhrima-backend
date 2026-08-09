namespace POSSystem.Domain.Enums;

public enum SaleStatus
{
    Pending = 0,
    Completed = 1,
    Cancelled = 2,
    Refunded = 3,
    PartiallyRefunded = 4
}

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    MobilePayment = 2,
    BankTransfer = 3,
    StoreCredit = 4,
    Other = 5
}

public enum PaymentStatus
{
    Unpaid = 0,
    PartiallyPaid = 1,
    Paid = 2,
    Refunded = 3
}

public enum PurchaseApprovalStatus
{
    PendingApproval = 0,
    Approved = 1
}

public enum StockMovementType
{
    Sale = 0,
    Purchase = 1,
    Return = 2,
    Adjustment = 3,
    InitialStock = 4,
    Transfer = 5
}

public enum PromotionType
{
    BuyXGetYFree = 0,
    BulkPercentOff = 1,
    FixedPriceBundle = 2
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Cashier = "Cashier";

    public static readonly string[] All = { Admin, Manager, Cashier };
}
