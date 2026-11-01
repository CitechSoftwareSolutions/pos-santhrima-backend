using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

public class RegisterSession : BaseEntity
{
    public Guid TerminalId { get; set; }
    public TerminalSetting? Terminal { get; set; }

    public Guid CashierId { get; set; }

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    public string Status { get; set; } = "Open"; // "Open" | "Closed"
    public string? Notes { get; set; }
}
