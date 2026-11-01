using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Common.Interfaces;
using POSSystem.Application.Features.Registers;
using POSSystem.Application.Features.Sales;
using POSSystem.Domain.Entities;
using POSSystem.Domain.Enums;
using POSSystem.Infrastructure.Identity;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class RegisterService : IRegisterService
{
    private readonly ApplicationDbContext _db;
    private readonly IDatabaseContextResolver _dbResolver;
    private readonly IApplicationDbContextFactory _dbFactory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISalesShiftService _salesShiftService;
    private readonly ILogger<RegisterService> _logger;
    private static bool _initialized;

    public RegisterService(
        ApplicationDbContext db,
        IDatabaseContextResolver dbResolver,
        IApplicationDbContextFactory dbFactory,
        UserManager<ApplicationUser> userManager,
        ISalesShiftService salesShiftService,
        ILogger<RegisterService> logger)
    {
        _db = db;
        _dbResolver = dbResolver;
        _dbFactory = dbFactory;
        _userManager = userManager;
        _salesShiftService = salesShiftService;
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS ""RegisterSessions"" (
                ""Id"" uuid NOT NULL PRIMARY KEY,
                ""TerminalId"" uuid NOT NULL,
                ""CashierId"" uuid NOT NULL,
                ""OpenedAt"" timestamp with time zone NOT NULL,
                ""ClosedAt"" timestamp with time zone NULL,
                ""Status"" text NOT NULL DEFAULT 'Open',
                ""Notes"" text NULL,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                ""UpdatedAt"" timestamp with time zone NULL,
                ""IsDeleted"" boolean NOT NULL DEFAULT false
            );
            CREATE INDEX IF NOT EXISTS ""IX_RegisterSessions_TerminalId_Status"" ON ""RegisterSessions"" (""TerminalId"", ""Status"");
        ";

        try
        {
            await _db.Database.ExecuteSqlRawAsync(createTableSql, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify/create RegisterSessions table on active DB.");
        }

        try
        {
            using var adminDb = _dbFactory.CreateAdminDbContext();
            await adminDb.Database.ExecuteSqlRawAsync(createTableSql, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify/create RegisterSessions table on Admin DB.");
        }

        _initialized = true;
    }

    public async Task<RegisterSessionDto?> GetCurrentSessionAsync(Guid terminalId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var session = await _db.RegisterSessions
            .AsNoTracking()
            .Include(r => r.Terminal)
            .OrderByDescending(r => r.OpenedAt)
            .FirstOrDefaultAsync(r => r.TerminalId == terminalId && r.Status == "Open", cancellationToken);

        if (session == null)
        {
            try
            {
                using var companionDb = _dbResolver.IsAdminMode()
                    ? _dbFactory.CreateNormalDbContext()
                    : _dbFactory.CreateAdminDbContext();

                session = await companionDb.RegisterSessions
                    .AsNoTracking()
                    .Include(r => r.Terminal)
                    .OrderByDescending(r => r.OpenedAt)
                    .FirstOrDefaultAsync(r => r.TerminalId == terminalId && r.Status == "Open", cancellationToken);
            }
            catch { }
        }

        if (session == null)
            return null;

        var cashier = await _userManager.FindByIdAsync(session.CashierId.ToString());
        var cashierName = cashier?.FullName ?? "Unknown Cashier";

        return ToDto(session, cashierName);
    }

    public async Task<RegisterSessionDto> OpenSessionAsync(OpenRegisterRequest request, Guid cashierId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var terminal = await _db.TerminalSettings.FindAsync([request.TerminalId], cancellationToken);
        if (terminal == null || terminal.IsDeleted)
            throw new NotFoundException($"Terminal with ID '{request.TerminalId}' not found.");

        // Check if there is already an open session
        var existingSession = await _db.RegisterSessions
            .Include(r => r.Terminal)
            .FirstOrDefaultAsync(r => r.TerminalId == request.TerminalId && r.Status == "Open", cancellationToken);

        if (existingSession != null)
        {
            var existingCashier = await _userManager.FindByIdAsync(existingSession.CashierId.ToString());
            return ToDto(existingSession, existingCashier?.FullName ?? "Unknown Cashier");
        }

        var newSession = new RegisterSession
        {
            TerminalId = request.TerminalId,
            CashierId = cashierId,
            OpenedAt = DateTime.UtcNow,
            Status = "Open",
            Notes = request.Notes,
            Terminal = terminal
        };

        _db.RegisterSessions.Add(newSession);
        await _db.SaveChangesAsync(cancellationToken);

        var user = await _userManager.FindByIdAsync(cashierId.ToString());
        var cashierName = user?.FullName ?? "Unknown Cashier";

        _logger.LogInformation("Cash register opened for Terminal {TerminalId} ({TerminalCode}) by Cashier {CashierId}",
            terminal.Id, terminal.TerminalCode, cashierId);

        // Mirror to companion DB so session state is consistent in both databases
        try
        {
            using var companionDb = _dbResolver.IsAdminMode()
                ? _dbFactory.CreateNormalDbContext()
                : _dbFactory.CreateAdminDbContext();

            companionDb.RegisterSessions.Add(new RegisterSession
            {
                Id = newSession.Id,
                TerminalId = newSession.TerminalId,
                CashierId = newSession.CashierId,
                OpenedAt = newSession.OpenedAt,
                Status = newSession.Status,
                Notes = newSession.Notes,
                CreatedAt = newSession.CreatedAt
            });
            await companionDb.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not mirror open RegisterSession to companion DB.");
        }

        return ToDto(newSession, cashierName);
    }

    public async Task<CounterDailySummaryDto> CloseSessionAsync(Guid terminalId, CloseRegisterRequest request, Guid cashierId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var openSession = await _db.RegisterSessions
            .Include(r => r.Terminal)
            .OrderByDescending(r => r.OpenedAt)
            .FirstOrDefaultAsync(r => r.TerminalId == terminalId && r.Status == "Open", cancellationToken);

        if (openSession == null)
        {
            try
            {
                using var companionDb = _dbResolver.IsAdminMode()
                    ? _dbFactory.CreateNormalDbContext()
                    : _dbFactory.CreateAdminDbContext();

                openSession = await companionDb.RegisterSessions
                    .Include(r => r.Terminal)
                    .OrderByDescending(r => r.OpenedAt)
                    .FirstOrDefaultAsync(r => r.TerminalId == terminalId && r.Status == "Open", cancellationToken);
            }
            catch { }
        }

        DateTime openedAt;
        DateTime closedAt = DateTime.UtcNow;
        Guid sessionCashierId = cashierId;

        if (openSession != null)
        {
            openSession.Status = "Closed";
            openSession.ClosedAt = closedAt;
            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                openSession.Notes = string.IsNullOrWhiteSpace(openSession.Notes)
                    ? request.Notes
                    : $"{openSession.Notes} | {request.Notes}";
            }
            openSession.UpdatedAt = closedAt;
            openedAt = openSession.OpenedAt;
            sessionCashierId = openSession.CashierId;
            await _db.SaveChangesAsync(cancellationToken);

            // Mirror close to companion DB
            try
            {
                using var companionDb = _dbResolver.IsAdminMode()
                    ? _dbFactory.CreateNormalDbContext()
                    : _dbFactory.CreateAdminDbContext();

                var companionSession = await companionDb.RegisterSessions.FirstOrDefaultAsync(r => r.Id == openSession.Id, cancellationToken);
                if (companionSession != null)
                {
                    companionSession.Status = "Closed";
                    companionSession.ClosedAt = closedAt;
                    companionSession.UpdatedAt = closedAt;
                    companionSession.Notes = openSession.Notes;
                    await companionDb.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not mirror register close to companion DB.");
            }
        }
        else
        {
            openedAt = closedAt;
        }

        // 1. Transfer remaining unshifted transaction data to the Admin database (even though < 60000)
        try
        {
            var shiftResult = await _salesShiftService.ShiftAllRemainingSalesAsync(terminalId, sessionCashierId, cancellationToken);
            _logger.LogInformation("Register close shift completed: {Message}", shiftResult.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to shift remaining sales to Admin DB on register close.");
        }

        // 2. Calculate summary across both Normal and Admin databases strictly between openedAt and closedAt for this counter & cashier
        var summary = await CalculateSummaryAsync(terminalId, openedAt, closedAt, sessionCashierId, cancellationToken);

        return summary;
    }

    public async Task<CounterDailySummaryDto> GetTodayCounterSummaryAsync(Guid terminalId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var currentSession = await _db.RegisterSessions
            .AsNoTracking()
            .OrderByDescending(r => r.OpenedAt)
            .FirstOrDefaultAsync(r => r.TerminalId == terminalId && r.Status == "Open", cancellationToken);

        if (currentSession != null)
        {
            return await CalculateSummaryAsync(terminalId, currentSession.OpenedAt, DateTime.UtcNow, currentSession.CashierId, cancellationToken);
        }

        var latestSession = await _db.RegisterSessions
            .AsNoTracking()
            .OrderByDescending(r => r.OpenedAt)
            .FirstOrDefaultAsync(r => r.TerminalId == terminalId, cancellationToken);

        if (latestSession != null && latestSession.ClosedAt.HasValue)
        {
            return await CalculateSummaryAsync(terminalId, latestSession.OpenedAt, latestSession.ClosedAt.Value, latestSession.CashierId, cancellationToken);
        }

        var slNow = DateTime.UtcNow.AddHours(5.5);
        var slTodayStart = new DateTime(slNow.Year, slNow.Month, slNow.Day, 0, 0, 0, DateTimeKind.Utc).AddHours(-5.5);
        return await CalculateSummaryAsync(terminalId, slTodayStart, DateTime.UtcNow, Guid.Empty, cancellationToken);
    }

    private async Task<CounterDailySummaryDto> CalculateSummaryAsync(
        Guid terminalId,
        DateTime openedAt,
        DateTime closedAt,
        Guid cashierId,
        CancellationToken cancellationToken)
    {
        var terminal = await _db.TerminalSettings.AsNoTracking().FirstOrDefaultAsync(t => t.Id == terminalId, cancellationToken);
        if (terminal == null)
        {
            try
            {
                using var normalDb = _dbFactory.CreateNormalDbContext();
                terminal = await normalDb.TerminalSettings.AsNoTracking().FirstOrDefaultAsync(t => t.Id == terminalId, cancellationToken);
            }
            catch { }
        }

        var counterNumber = terminal?.CounterNumber;
        var counterName = terminal?.Name ?? (counterNumber.HasValue ? $"Counter {counterNumber}" : "Main Register");
        var terminalCode = terminal?.TerminalCode;

        string cashierName = "All Cashiers";
        if (cashierId != Guid.Empty)
        {
            var user = await _userManager.FindByIdAsync(cashierId.ToString());
            if (user != null) cashierName = user.FullName;
        }

        // Query transactions strictly within the session window (between open and close)
        var utcOpenedAt = openedAt.AsUtc();
        var utcClosedAt = closedAt.AsUtc();
        var dateFrom = utcOpenedAt;
        var dateTo = utcClosedAt.AddSeconds(1);

        List<Sale> sales = new();
        HashSet<Guid> seenSaleIds = new();

        IQueryable<Sale> CreateSaleQuery(ApplicationDbContext context) =>
            context.Sales.AsNoTracking()
                .Include(s => s.Payments)
                .Where(s => s.TerminalId == terminalId &&
                            (cashierId == Guid.Empty || s.CashierId == cashierId) &&
                            s.SaleDate >= dateFrom &&
                            s.SaleDate <= dateTo &&
                            s.Status != SaleStatus.Cancelled);

        // 1. Query Normal DB
        try
        {
            using var normalDb = _dbFactory.CreateNormalDbContext();
            var normalSales = await CreateSaleQuery(normalDb).ToListAsync(cancellationToken);
            foreach (var s in normalSales)
            {
                if (seenSaleIds.Add(s.Id))
                {
                    sales.Add(s);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query sales from Normal DB in RegisterService.");
        }

        // 2. Query Admin DB (capturing transactions already shifted to Admin DB)
        try
        {
            using var adminDb = _dbFactory.CreateAdminDbContext();
            var adminSales = await CreateSaleQuery(adminDb).ToListAsync(cancellationToken);
            foreach (var s in adminSales)
            {
                if (seenSaleIds.Add(s.Id))
                {
                    sales.Add(s);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query sales from Admin DB in RegisterService.");
        }

        var totalSalesCount = sales.Count;
        var subTotal = sales.Sum(s => s.SubTotal);
        var totalDiscount = sales.Sum(s => s.DiscountAmount);
        var totalTax = sales.Sum(s => s.TaxAmount);
        var totalRevenue = sales.Sum(s => s.TotalAmount);

        // Aggregate payments by every payment method
        var allPayments = sales.SelectMany(s => s.Payments).Where(p => !p.IsDeleted && p.Amount > 0).ToList();

        var paymentMethodBreakdowns = new List<PaymentMethodSummaryDto>();
        var methods = new[]
        {
            PaymentMethod.Cash,
            PaymentMethod.Card,
            PaymentMethod.MobilePayment,
            PaymentMethod.BankTransfer,
            PaymentMethod.StoreCredit,
            PaymentMethod.Other
        };

        foreach (var m in methods)
        {
            var matching = allPayments.Where(p => p.Method == m).ToList();
            var totalForMethod = matching.Sum(p => p.Amount);
            if (m == PaymentMethod.Cash)
            {
                var changeGiven = sales
                    .Where(s => s.Payments.Any(p => p.Method == PaymentMethod.Cash && !p.IsDeleted))
                    .Sum(s => s.ChangeDue);
                totalForMethod = Math.Max(0, totalForMethod - changeGiven);
            }

            paymentMethodBreakdowns.Add(new PaymentMethodSummaryDto(
                m.ToString(),
                (int)m,
                totalForMethod,
                matching.Count
            ));
        }

        return new CounterDailySummaryDto(
            terminalId,
            counterNumber,
            counterName,
            terminalCode,
            cashierName,
            utcOpenedAt,
            utcClosedAt,
            totalSalesCount,
            subTotal,
            totalDiscount,
            totalTax,
            totalRevenue,
            paymentMethodBreakdowns
        );
    }

    private static RegisterSessionDto ToDto(RegisterSession s, string cashierName) =>
        new(
            s.Id,
            s.TerminalId,
            s.Terminal?.Name,
            s.Terminal?.CounterNumber,
            s.Terminal?.TerminalCode,
            s.CashierId,
            cashierName,
            s.OpenedAt,
            s.ClosedAt,
            s.Status,
            s.Notes
        );
}
