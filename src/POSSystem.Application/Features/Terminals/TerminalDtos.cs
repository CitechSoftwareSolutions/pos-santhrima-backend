namespace POSSystem.Application.Features.Terminals;

public record TerminalDto(
    Guid Id,
    int CounterNumber,
    string TerminalCode,
    string Name,
    string? Description,
    bool IsActive,
    string PrinterType,
    string? PrinterAddress,
    string? PaperWidth,
    bool AutoPrintReceipt,
    bool AutoCutPaper,
    bool CashDrawerKick,
    string BarcodeScannerType,
    string BarcodeDelimiter,
    int BarcodeMinLength,
    bool BarcodeBeepOnScan,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateTerminalRequest(
    int? CounterNumber,
    string? TerminalCode,
    string Name,
    string? Description,
    string PrinterType = "BrowserDialog",
    string? PrinterAddress = null,
    string? PaperWidth = null,
    bool AutoPrintReceipt = true,
    bool AutoCutPaper = true,
    bool CashDrawerKick = false,
    string BarcodeScannerType = "KeyboardWedgeHid",
    string BarcodeDelimiter = "Enter",
    int BarcodeMinLength = 3,
    bool BarcodeBeepOnScan = true);

public record UpdateTerminalRequest(
    string Name,
    string? Description,
    bool IsActive,
    string PrinterType,
    string? PrinterAddress,
    string? PaperWidth,
    bool AutoPrintReceipt,
    bool AutoCutPaper,
    bool CashDrawerKick,
    string BarcodeScannerType,
    string BarcodeDelimiter,
    int BarcodeMinLength,
    bool BarcodeBeepOnScan);

public record SetTerminalCountRequest(int TargetCount);

public record AuthorizeSwitchRequest(string UsernameOrEmail, string Password);

public interface ITerminalService
{
    Task<List<TerminalDto>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<TerminalDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TerminalDto> CreateAsync(CreateTerminalRequest request, CancellationToken cancellationToken = default);
    Task<TerminalDto> UpdateAsync(Guid id, UpdateTerminalRequest request, CancellationToken cancellationToken = default);
    Task<List<TerminalDto>> SetCountAsync(int targetCount, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> AuthorizeSwitchAsync(AuthorizeSwitchRequest request, CancellationToken cancellationToken = default);
}
