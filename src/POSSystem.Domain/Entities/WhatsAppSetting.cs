using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

public class WhatsAppSetting : BaseEntity
{
    public bool IsEnabled { get; set; } = true;
    public string Provider { get; set; } = "Twilio"; // "Twilio", "UltraMsg", or "Generic"
    
    // Twilio Settings
    public string? TwilioAccountSid { get; set; }
    public string? TwilioAuthToken { get; set; }
    public string? TwilioFromNumber { get; set; } = "whatsapp:+14155238886";

    // UltraMsg Settings
    public string? UltraMsgInstanceId { get; set; }
    public string? UltraMsgToken { get; set; }

    // Schedule Settings (8:00 AM by default)
    public int ScheduledHour { get; set; } = 8;
    public int ScheduledMinute { get; set; } = 0;
    public string TimeZoneId { get; set; } = "Asia/Colombo";
}
