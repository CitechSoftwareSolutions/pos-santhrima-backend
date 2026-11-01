namespace POSSystem.Application.Features.Sales;

public record SalesShiftResult(
    bool Shifted,
    int SalesCount,
    decimal TotalAmountShifted,
    string Message);

public interface ISalesShiftService
{
    /// <summary>
    /// Checks if the cashier's total sales for today in the Normal database has met or exceeded the threshold.
    /// If yes, transfers all completed sales for today into the Admin database and removes them from the Normal database.
    /// </summary>
    Task<SalesShiftResult> CheckAndShiftCashierSalesAsync(Guid cashierId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks all cashiers with today's sales and shifts any cashier whose total >= threshold.
    /// </summary>
    Task<List<SalesShiftResult>> CheckAndShiftAllEligibleCashiersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shifts all remaining unshifted completed sales in the Normal database to the Admin database immediately,
    /// even if below the threshold (e.g. at register close).
    /// </summary>
    Task<SalesShiftResult> ShiftAllRemainingSalesAsync(Guid? terminalId = null, Guid? cashierId = null, CancellationToken cancellationToken = default);
}
