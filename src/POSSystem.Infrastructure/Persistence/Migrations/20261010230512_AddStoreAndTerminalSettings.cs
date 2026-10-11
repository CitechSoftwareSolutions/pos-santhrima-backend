using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreAndTerminalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TerminalId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StoreSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoyaltyPointsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LoyaltyEarningRate = table.Column<decimal>(type: "numeric", nullable: false),
                    LoyaltyMilestoneThreshold = table.Column<decimal>(type: "numeric", nullable: false),
                    LoyaltyPointValue = table.Column<decimal>(type: "numeric", nullable: false),
                    LoyaltyPointsExpiryMonths = table.Column<int>(type: "integer", nullable: false),
                    LoyaltyMilestoneGiftDescription = table.Column<string>(type: "text", nullable: false),
                    StoreName = table.Column<string>(type: "text", nullable: false),
                    StoreAddress = table.Column<string>(type: "text", nullable: true),
                    StorePhone = table.Column<string>(type: "text", nullable: true),
                    ReceiptHeader = table.Column<string>(type: "text", nullable: false),
                    ReceiptFooter = table.Column<string>(type: "text", nullable: false),
                    DefaultPaperWidth = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TerminalSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CounterNumber = table.Column<int>(type: "integer", nullable: false),
                    TerminalCode = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PrinterType = table.Column<string>(type: "text", nullable: false),
                    PrinterAddress = table.Column<string>(type: "text", nullable: true),
                    PaperWidth = table.Column<string>(type: "text", nullable: true),
                    AutoPrintReceipt = table.Column<bool>(type: "boolean", nullable: false),
                    AutoCutPaper = table.Column<bool>(type: "boolean", nullable: false),
                    CashDrawerKick = table.Column<bool>(type: "boolean", nullable: false),
                    BarcodeScannerType = table.Column<string>(type: "text", nullable: false),
                    BarcodeDelimiter = table.Column<string>(type: "text", nullable: false),
                    BarcodeMinLength = table.Column<int>(type: "integer", nullable: false),
                    BarcodeBeepOnScan = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_TerminalId",
                table: "Sales",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalSettings_CounterNumber",
                table: "TerminalSettings",
                column: "CounterNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TerminalSettings_TerminalCode",
                table: "TerminalSettings",
                column: "TerminalCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_TerminalSettings_TerminalId",
                table: "Sales",
                column: "TerminalId",
                principalTable: "TerminalSettings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_TerminalSettings_TerminalId",
                table: "Sales");

            migrationBuilder.DropTable(
                name: "StoreSettings");

            migrationBuilder.DropTable(
                name: "TerminalSettings");

            migrationBuilder.DropIndex(
                name: "IX_Sales_TerminalId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "TerminalId",
                table: "Sales");
        }
    }
}
