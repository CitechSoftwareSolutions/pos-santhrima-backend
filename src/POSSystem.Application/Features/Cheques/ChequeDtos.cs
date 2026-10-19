using POSSystem.Application.Common.Models;
using POSSystem.Domain.Enums;

namespace POSSystem.Application.Features.Cheques;

public record ChequeDto(
    Guid Id,
    string ChequeNumber,
    string Payee,
    decimal Amount,
    DateTime IssueDate,
    DateTime DueDate,
    string? BankName,
    ChequeStatus Status,
    string? Remarks,
    bool IsReminderSent,
    DateTime? ReminderSentAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record ChequeSummaryDto(
    int TotalPendingCount,
    decimal TotalPendingAmount,
    int DueTodayCount,
    decimal DueTodayAmount,
    int OverdueCount,
    decimal OverdueAmount,
    int ClearedCount,
    decimal ClearedAmount);

public record CreateChequeRequest(
    string ChequeNumber,
    string Payee,
    decimal Amount,
    DateTime DueDate,
    DateTime? IssueDate,
    string? BankName,
    string? Remarks);

public record UpdateChequeRequest(
    string ChequeNumber,
    string Payee,
    decimal Amount,
    DateTime DueDate,
    DateTime? IssueDate,
    string? BankName,
    string? Remarks,
    ChequeStatus Status);

public record UpdateChequeStatusRequest(
    ChequeStatus Status,
    string? Remarks);

public class ChequeQueryParams : PaginationParams
{
    public ChequeStatus? Status { get; set; }
    public string? DueFilter { get; set; } // "all", "due_today", "due_week", "overdue"
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public record ChequeNotificationRecipientDto(
    Guid Id,
    string Name,
    string PhoneNumber,
    bool IsActive,
    string? Notes,
    DateTime CreatedAt);

public record CreateRecipientRequest(
    string Name,
    string PhoneNumber,
    bool IsActive = true,
    string? Notes = null);

public record UpdateRecipientRequest(
    string Name,
    string PhoneNumber,
    bool IsActive,
    string? Notes);

public record WhatsAppSettingDto(
    Guid Id,
    bool IsEnabled,
    string Provider,
    string? TwilioAccountSid,
    string? TwilioAuthToken,
    string? TwilioFromNumber,
    string? UltraMsgInstanceId,
    string? UltraMsgToken,
    int ScheduledHour,
    int ScheduledMinute,
    string TimeZoneId,
    bool IsConfiguredViaEnv = false,
    bool HasConfiguredToken = false);

public record UpdateWhatsAppSettingRequest(
    bool IsEnabled,
    string Provider,
    string? TwilioAccountSid,
    string? TwilioAuthToken,
    string? TwilioFromNumber,
    string? UltraMsgInstanceId,
    string? UltraMsgToken,
    int ScheduledHour,
    int ScheduledMinute,
    string TimeZoneId);

public record TestWhatsAppRequest(
    string PhoneNumber,
    string? Message = null);

public record WhatsAppSendResultDto(
    bool Success,
    string Message,
    int RecipientsSentCount,
    int ChequesCount,
    List<string>? Errors = null);

public interface IChequeService
{
    Task<PagedResult<ChequeDto>> GetAllAsync(ChequeQueryParams query, CancellationToken cancellationToken = default);
    Task<ChequeSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<ChequeDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ChequeDto> CreateAsync(CreateChequeRequest request, CancellationToken cancellationToken = default);
    Task<ChequeDto> UpdateAsync(Guid id, UpdateChequeRequest request, CancellationToken cancellationToken = default);
    Task<ChequeDto> UpdateStatusAsync(Guid id, UpdateChequeStatusRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Recipients
    Task<List<ChequeNotificationRecipientDto>> GetRecipientsAsync(CancellationToken cancellationToken = default);
    Task<ChequeNotificationRecipientDto> CreateRecipientAsync(CreateRecipientRequest request, CancellationToken cancellationToken = default);
    Task<ChequeNotificationRecipientDto> UpdateRecipientAsync(Guid id, UpdateRecipientRequest request, CancellationToken cancellationToken = default);
    Task DeleteRecipientAsync(Guid id, CancellationToken cancellationToken = default);

    // WhatsApp Settings & Notification Triggers
    Task<WhatsAppSettingDto> GetWhatsAppSettingsAsync(CancellationToken cancellationToken = default);
    Task<WhatsAppSettingDto> UpdateWhatsAppSettingsAsync(UpdateWhatsAppSettingRequest request, CancellationToken cancellationToken = default);
    Task<WhatsAppSendResultDto> SendDueChequeRemindersAsync(bool forceSendAllDueToday = false, CancellationToken cancellationToken = default);
    Task<WhatsAppSendResultDto> SendTestMessageAsync(TestWhatsAppRequest request, CancellationToken cancellationToken = default);
}

public interface IWhatsAppNotificationService
{
    Task<(bool Success, string? Error)> SendTextMessageAsync(string toPhoneNumber, string messageBody, CancellationToken cancellationToken = default);
    Task<WhatsAppSendResultDto> DispatchDueChequesAsync(IEnumerable<ChequeDto> dueCheques, IEnumerable<ChequeNotificationRecipientDto> recipients, CancellationToken cancellationToken = default);
}
