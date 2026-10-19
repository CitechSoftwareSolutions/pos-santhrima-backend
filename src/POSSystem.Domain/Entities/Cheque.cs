using POSSystem.Domain.Common;
using POSSystem.Domain.Enums;

namespace POSSystem.Domain.Entities;

public class Cheque : BaseEntity
{
    public string ChequeNumber { get; set; } = string.Empty;
    public string Payee { get; set; } = string.Empty; // To whom this cheque is issued
    public decimal Amount { get; set; } // Cheque amount / price
    public DateTime IssueDate { get; set; } = DateTime.UtcNow;
    public DateTime DueDate { get; set; }
    public string? BankName { get; set; }
    public ChequeStatus Status { get; set; } = ChequeStatus.Pending;
    public string? Remarks { get; set; }
    public bool IsReminderSent { get; set; } = false;
    public DateTime? ReminderSentAt { get; set; }
}
