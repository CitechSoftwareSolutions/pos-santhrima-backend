using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using POSSystem.Domain.Enums;
using POSSystem.Application.Common.Exceptions;
using POSSystem.Application.Features.Terminals;
using POSSystem.Domain.Entities;
using POSSystem.Infrastructure.Identity;
using POSSystem.Infrastructure.Persistence;

namespace POSSystem.Infrastructure.Services;

public class TerminalService : ITerminalService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private static bool _initialized = false;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public TerminalService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS ""TerminalSettings"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""CounterNumber"" integer NOT NULL,
                    ""TerminalCode"" text NOT NULL,
                    ""Name"" text NOT NULL,
                    ""Description"" text NULL,
                    ""IsActive"" boolean NOT NULL DEFAULT true,
                    ""PrinterType"" text NOT NULL DEFAULT 'BrowserDialog',
                    ""PrinterAddress"" text NULL,
                    ""PaperWidth"" text NULL,
                    ""AutoPrintReceipt"" boolean NOT NULL DEFAULT true,
                    ""AutoCutPaper"" boolean NOT NULL DEFAULT true,
                    ""CashDrawerKick"" boolean NOT NULL DEFAULT false,
                    ""BarcodeScannerType"" text NOT NULL DEFAULT 'KeyboardWedgeHid',
                    ""BarcodeDelimiter"" text NOT NULL DEFAULT 'Enter',
                    ""BarcodeMinLength"" integer NOT NULL DEFAULT 3,
                    ""BarcodeBeepOnScan"" boolean NOT NULL DEFAULT true,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TerminalSettings_CounterNumber"" ON ""TerminalSettings"" (""CounterNumber"") WHERE ""IsDeleted"" = false;
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TerminalSettings_TerminalCode"" ON ""TerminalSettings"" (""TerminalCode"") WHERE ""IsDeleted"" = false;
                ALTER TABLE ""Sales"" ADD COLUMN IF NOT EXISTS ""TerminalId"" uuid NULL;
            ";

            try
            {
                await _db.Database.ExecuteSqlRawAsync(createTableSql, cancellationToken);
            }
            catch
            {
                // Ignore if DB already managed
            }

            // Seed default Counter 1 if none exists
            var existing = await _db.TerminalSettings.IgnoreQueryFilters().FirstOrDefaultAsync(cancellationToken);
            if (existing is null)
            {
                var defaultCounter = new TerminalSetting
                {
                    CounterNumber = 1,
                    TerminalCode = "COUNTER-01",
                    Name = "Counter 1",
                    Description = "Main Cashier Counter",
                    IsActive = true,
                    PrinterType = "BrowserDialog",
                    PrinterAddress = null,
                    PaperWidth = null, // inherits global default
                    AutoPrintReceipt = true,
                    AutoCutPaper = true,
                    CashDrawerKick = false,
                    BarcodeScannerType = "KeyboardWedgeHid",
                    BarcodeDelimiter = "Enter",
                    BarcodeMinLength = 3,
                    BarcodeBeepOnScan = true,
                    CreatedAt = DateTime.UtcNow
                };

                _db.TerminalSettings.Add(defaultCounter);
                await _db.SaveChangesAsync(cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<List<TerminalDto>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var query = _db.TerminalSettings.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(t => t.IsActive);
        }

        var terminals = await query.OrderBy(t => t.CounterNumber).ToListAsync(cancellationToken);
        return terminals.Select(ToDto).ToList();
    }

    public async Task<TerminalDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var terminal = await _db.TerminalSettings.FindAsync([id], cancellationToken);
        if (terminal is null || terminal.IsDeleted)
        {
            throw new NotFoundException($"Terminal with ID '{id}' was not found.");
        }

        return ToDto(terminal);
    }

    public async Task<TerminalDto> CreateAsync(CreateTerminalRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        int counterNumber = request.CounterNumber ??
            ((await _db.TerminalSettings.MaxAsync(t => (int?)t.CounterNumber, cancellationToken) ?? 0) + 1);

        string terminalCode = string.IsNullOrWhiteSpace(request.TerminalCode)
            ? $"COUNTER-{counterNumber:D2}"
            : request.TerminalCode.Trim().ToUpperInvariant();

        if (await _db.TerminalSettings.AnyAsync(t => t.CounterNumber == counterNumber, cancellationToken))
        {
            throw new ValidationAppException($"A counter with number {counterNumber} already exists.");
        }

        if (await _db.TerminalSettings.AnyAsync(t => t.TerminalCode == terminalCode, cancellationToken))
        {
            throw new ValidationAppException($"A counter with code '{terminalCode}' already exists.");
        }

        var terminal = new TerminalSetting
        {
            CounterNumber = counterNumber,
            TerminalCode = terminalCode,
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"Counter {counterNumber}" : request.Name.Trim(),
            Description = request.Description,
            IsActive = true,
            PrinterType = request.PrinterType ?? "BrowserDialog",
            PrinterAddress = request.PrinterAddress,
            PaperWidth = request.PaperWidth,
            AutoPrintReceipt = request.AutoPrintReceipt,
            AutoCutPaper = request.AutoCutPaper,
            CashDrawerKick = request.CashDrawerKick,
            BarcodeScannerType = request.BarcodeScannerType ?? "KeyboardWedgeHid",
            BarcodeDelimiter = request.BarcodeDelimiter ?? "Enter",
            BarcodeMinLength = request.BarcodeMinLength <= 0 ? 3 : request.BarcodeMinLength,
            BarcodeBeepOnScan = request.BarcodeBeepOnScan,
            CreatedAt = DateTime.UtcNow
        };

        _db.TerminalSettings.Add(terminal);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(terminal);
    }

    public async Task<TerminalDto> UpdateAsync(Guid id, UpdateTerminalRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var terminal = await _db.TerminalSettings.FindAsync([id], cancellationToken);
        if (terminal is null || terminal.IsDeleted)
        {
            throw new NotFoundException($"Terminal with ID '{id}' was not found.");
        }

        terminal.Name = string.IsNullOrWhiteSpace(request.Name) ? terminal.Name : request.Name.Trim();
        terminal.Description = request.Description;
        terminal.IsActive = request.IsActive;
        terminal.PrinterType = request.PrinterType ?? "BrowserDialog";
        terminal.PrinterAddress = request.PrinterAddress;
        terminal.PaperWidth = string.IsNullOrWhiteSpace(request.PaperWidth) ? null : request.PaperWidth;
        terminal.AutoPrintReceipt = request.AutoPrintReceipt;
        terminal.AutoCutPaper = request.AutoCutPaper;
        terminal.CashDrawerKick = request.CashDrawerKick;
        terminal.BarcodeScannerType = request.BarcodeScannerType ?? "KeyboardWedgeHid";
        terminal.BarcodeDelimiter = request.BarcodeDelimiter ?? "Enter";
        terminal.BarcodeMinLength = request.BarcodeMinLength <= 0 ? 3 : request.BarcodeMinLength;
        terminal.BarcodeBeepOnScan = request.BarcodeBeepOnScan;
        terminal.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(terminal);
    }

    public async Task<List<TerminalDto>> SetCountAsync(int targetCount, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (targetCount <= 0 || targetCount > 50)
        {
            throw new ValidationAppException("Total counter count must be between 1 and 50.");
        }

        var allTerminals = await _db.TerminalSettings
            .OrderBy(t => t.CounterNumber)
            .ToListAsync(cancellationToken);

        // Reactivate or provision up to targetCount
        for (int i = 1; i <= targetCount; i++)
        {
            var existing = allTerminals.FirstOrDefault(t => t.CounterNumber == i);
            if (existing is not null)
            {
                if (!existing.IsActive)
                {
                    existing.IsActive = true;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
            }
            else
            {
                var newCounter = new TerminalSetting
                {
                    CounterNumber = i,
                    TerminalCode = $"COUNTER-{i:D2}",
                    Name = $"Counter {i}",
                    Description = $"Cashier Counter {i}",
                    IsActive = true,
                    PrinterType = "BrowserDialog",
                    PaperWidth = null,
                    AutoPrintReceipt = true,
                    AutoCutPaper = true,
                    CashDrawerKick = false,
                    BarcodeScannerType = "KeyboardWedgeHid",
                    BarcodeDelimiter = "Enter",
                    BarcodeMinLength = 3,
                    BarcodeBeepOnScan = true,
                    CreatedAt = DateTime.UtcNow
                };
                _db.TerminalSettings.Add(newCounter);
            }
        }

        // Deactivate counters exceeding targetCount instead of deleting
        foreach (var extra in allTerminals.Where(t => t.CounterNumber > targetCount))
        {
            if (extra.IsActive)
            {
                extra.IsActive = false;
                extra.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var result = await _db.TerminalSettings
            .OrderBy(t => t.CounterNumber)
            .ToListAsync(cancellationToken);

        return result.Select(ToDto).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var terminal = await _db.TerminalSettings.FindAsync([id], cancellationToken);
        if (terminal is null || terminal.IsDeleted)
        {
            throw new NotFoundException($"Terminal with ID '{id}' was not found.");
        }

        // Soft deactivate
        terminal.IsActive = false;
        terminal.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> AuthorizeSwitchAsync(AuthorizeSwitchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ValidationAppException("Admin credentials are required.");
        }

        var user = await _userManager.FindByEmailAsync(request.UsernameOrEmail)
            ?? await _userManager.FindByNameAsync(request.UsernameOrEmail);

        if (user is null || !user.IsActive)
        {
            throw new ValidationAppException("Invalid administrator credentials.");
        }

        var isPasswordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!isPasswordValid)
        {
            throw new ValidationAppException("Invalid administrator password.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(Roles.Admin))
        {
            throw new ValidationAppException("Access denied. Super Admin privileges are required to reassign this counter.");
        }

        return true;
    }

    private static TerminalDto ToDto(TerminalSetting t) => new(
        t.Id,
        t.CounterNumber,
        t.TerminalCode,
        t.Name,
        t.Description,
        t.IsActive,
        t.PrinterType,
        t.PrinterAddress,
        t.PaperWidth,
        t.AutoPrintReceipt,
        t.AutoCutPaper,
        t.CashDrawerKick,
        t.BarcodeScannerType,
        t.BarcodeDelimiter,
        t.BarcodeMinLength,
        t.BarcodeBeepOnScan,
        t.CreatedAt,
        t.UpdatedAt);
}
