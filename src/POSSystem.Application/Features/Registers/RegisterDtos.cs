namespace POSSystem.Application.Features.Registers;

public record RegisterSessionDto(
    Guid Id,
    Guid TerminalId,
    string? TerminalName,
    int? CounterNumber,
    string? TerminalCode,
    Guid CashierId,
    string CashierName,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    string Status,
    string? Notes
);

public record OpenRegisterRequest(Guid TerminalId, string? Notes);

public record CloseRegisterRequest(string? Notes);

public record PaymentMethodSummaryDto(
    string MethodName,
    int MethodValue,
    decimal TotalAmount,
    int TransactionCount
);

public record CounterDailySummaryDto(
    Guid? TerminalId,
    int? CounterNumber,
    string CounterName,
    string? TerminalCode,
    string CashierName,
    DateTime OpenedAt,
    DateTime ClosedAt,
    int TotalSalesCount,
    decimal SubTotal,
    decimal TotalDiscount,
    decimal TotalTax,
    decimal TotalRevenue,
    List<PaymentMethodSummaryDto> PaymentMethods
);

public interface IRegisterService
{
    Task<RegisterSessionDto?> GetCurrentSessionAsync(Guid terminalId, CancellationToken cancellationToken = default);
    Task<RegisterSessionDto> OpenSessionAsync(OpenRegisterRequest request, Guid cashierId, CancellationToken cancellationToken = default);
    Task<CounterDailySummaryDto> CloseSessionAsync(Guid terminalId, CloseRegisterRequest request, Guid cashierId, CancellationToken cancellationToken = default);
    Task<CounterDailySummaryDto> GetTodayCounterSummaryAsync(Guid terminalId, CancellationToken cancellationToken = default);
}
