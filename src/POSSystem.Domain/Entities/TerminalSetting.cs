using POSSystem.Domain.Common;

namespace POSSystem.Domain.Entities;

public class TerminalSetting : BaseEntity
{
    public int CounterNumber { get; set; } = 1;
    public string TerminalCode { get; set; } = "COUNTER-01";
    public string Name { get; set; } = "Counter 1";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    // Receipt Printer Peripherals
    public string PrinterType { get; set; } = "BrowserDialog"; // BrowserDialog, NetworkEscPos, UsbSerial, Bluetooth
    public string? PrinterAddress { get; set; }
    public string? PaperWidth { get; set; } // Nullable: null inherits StoreSetting.DefaultPaperWidth; "80mm" or "58mm"
    public bool AutoPrintReceipt { get; set; } = true;
    public bool AutoCutPaper { get; set; } = true;
    public bool CashDrawerKick { get; set; } = false;

    // Barcode Scanner Peripherals
    public string BarcodeScannerType { get; set; } = "KeyboardWedgeHid"; // KeyboardWedgeHid, DirectSerial, CameraWebcam
    public string BarcodeDelimiter { get; set; } = "Enter"; // Enter, Tab, None
    public int BarcodeMinLength { get; set; } = 3;
    public bool BarcodeBeepOnScan { get; set; } = true;

    // Navigation
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}
