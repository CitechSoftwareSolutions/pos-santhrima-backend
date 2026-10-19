using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

public class ChequeNotificationRecipient : BaseEntity
{
    public string Name { get; set; } = string.Empty; // e.g. "Store Owner", "Accountant"
    public string PhoneNumber { get; set; } = string.Empty; // WhatsApp phone number with country code, e.g. "+94771234567"
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
}
